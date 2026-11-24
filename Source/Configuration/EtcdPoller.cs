using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ROOT.Etcd.Configuration;

/// <summary>
/// Exposes the outcome of background polling so applications and health checks can react to
/// persistent authentication, authorization, or TLS failures.
/// </summary>
public interface IEtcdPollerStatus
{
    /// <summary>
    /// Gets the last exception observed by the poller, or null when no poll has failed.
    /// </summary>
    Exception? LastFailure { get; }

    /// <summary>
    /// Gets when the last failure occurred, or null when no poll has failed.
    /// </summary>
    DateTimeOffset? LastFailureTime { get; }

    /// <summary>
    /// Gets the number of consecutive failures classified as permanent.
    /// </summary>
    int ConsecutivePermanentFailures { get; }

    /// <summary>
    /// Gets whether the poller is healthy. Permanent failures make it unhealthy until a poll succeeds.
    /// </summary>
    bool IsHealthy { get; }
}

/// <summary>
/// Hosted service that periodically loads configuration from etcd, publishes changes, and
/// tracks permanent failures.
/// </summary>
public sealed class EtcdPoller : IHostedService, IEtcdPollerStatus, IDisposable, IAsyncDisposable
{
    private readonly EtcdOptions _options;
    private readonly IEtcdConfigurationLoader _configurationLoader;
    private readonly IEtcdConfigurationUpdater _etcdConfigurationUpdater;
    private readonly ILogger<EtcdPoller> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IHostApplicationLifetime? _applicationLifetime;
    private readonly Lock _timerLock = new();
    private readonly Lock _failureLock = new();
    private ITimer? _timer;
    private CancellationTokenSource? _pollingCancellation;
    private Exception? _lastFailure;
    private DateTimeOffset? _lastFailureTime;
    private int _consecutivePermanentFailures;
    private long _stopping;

    /// <summary>
    /// Initializes a new instance of the <see cref="EtcdPoller"/> class.
    /// </summary>
    /// <param name="configurationLoader">Loader used for each poll.</param>
    /// <param name="etcdConfigurationUpdater">Store that receives changed configuration.</param>
    /// <param name="options">Polling and failure-policy options.</param>
    /// <param name="timeProvider">Time provider used for timers and failure timestamps.</param>
    /// <param name="logger">Logger for poll outcomes.</param>
    /// <param name="applicationLifetime">Optional host lifetime used to stop the host after repeated permanent failures.</param>
    public EtcdPoller(IEtcdConfigurationLoader configurationLoader,
        IEtcdConfigurationUpdater etcdConfigurationUpdater,
        EtcdOptions options,
        TimeProvider timeProvider,
        ILogger<EtcdPoller> logger,
        IHostApplicationLifetime? applicationLifetime = null)
    {
        _options = options;
        _configurationLoader = configurationLoader;
        _etcdConfigurationUpdater = etcdConfigurationUpdater;
        _logger = logger;
        _timeProvider = timeProvider;
        _applicationLifetime = applicationLifetime;
    }

    /// <inheritdoc/>
    public Exception? LastFailure
    {
        get
        {
            lock (_failureLock)
            {
                return _lastFailure;
            }
        }
    }

    /// <inheritdoc/>
    public DateTimeOffset? LastFailureTime
    {
        get
        {
            lock (_failureLock)
            {
                return _lastFailureTime;
            }
        }
    }

    /// <inheritdoc/>
    public int ConsecutivePermanentFailures
    {
        get
        {
            lock (_failureLock)
            {
                return _consecutivePermanentFailures;
            }
        }
    }

    /// <inheritdoc/>
    public bool IsHealthy => ConsecutivePermanentFailures == 0;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.RedactConfigurationValues)
        {
            _logger.LogWarning(
                "RedactConfigurationValues is false. Configuration values that do not look like secrets may be written to logs.");
        }

        if (_options.RefreshInterval == TimeSpan.Zero)
        {
            _logger.LogInformation("ETCD polling is disabled because RefreshInterval is set to TimeSpan.Zero");
            return Task.CompletedTask;
        }

        _logger.LogInformation("Starting ETCD polling");

        lock (_timerLock)
        {
            Interlocked.Exchange(ref _stopping, 0);
            _pollingCancellation = new CancellationTokenSource();
            _timer = _timeProvider.CreateTimer(Runner, this, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _stopping, 1);
        TakeTimer()?.Dispose();

        var pollingCancellation = TakePollingCancellation();
        pollingCancellation?.Cancel();
        pollingCancellation?.Dispose();

        return Task.CompletedTask;
    }

    private void Runner(object? state)
    {
        if (Interlocked.Read(ref _stopping) == 1)
        {
            _logger.LogInformation("Skipping poll because the service is shutting down");
            return;
        }

        try
        {
            var cancellationToken = GetPollingToken();
            var (configuration, changesPresent) = _configurationLoader.LoadConfiguration(cancellationToken);

            ReportSuccess();

            if (!changesPresent)
            {
                return;
            }

            _etcdConfigurationUpdater.PublishConfiguration(configuration);
        }
        catch (OperationCanceledException e)
        {
            if (Interlocked.Read(ref _stopping) != 1)
            {
                ReportFailure(e, permanent: false);
                _logger.LogWarning(e, "Timed out retreving data from etcd");
            }
        }
        catch (Exception e)
        {
            HandlePollingFailure(e);
        }
        finally
        {
            RearmOrStop();
        }
    }

    private void HandlePollingFailure(Exception exception)
    {
        var permanent = EtcdFailureClassifier.IsPermanent(exception);
        var failureCount = ReportFailure(exception, permanent);

        if (!permanent)
        {
            _logger.LogError(exception, "Unexpected error occured while polling for configuration changes");
            return;
        }

        _logger.LogError(exception, "Permanent error occured while polling for configuration changes");

        var threshold = _options.MaximumConsecutivePermanentFailures;
        if (threshold > 0 && failureCount >= threshold)
        {
            _logger.LogCritical(
                "Stopping the host after {FailureCount} consecutive permanent etcd polling failures.",
                failureCount);

            _applicationLifetime?.StopApplication();
        }
    }

    private CancellationToken GetPollingToken()
    {
        lock (_timerLock)
        {
            return _pollingCancellation?.Token ?? CancellationToken.None;
        }
    }

    private int ReportFailure(Exception exception, bool permanent)
    {
        lock (_failureLock)
        {
            _lastFailure = exception;
            _lastFailureTime = _timeProvider.GetUtcNow();
            _consecutivePermanentFailures = permanent ? _consecutivePermanentFailures + 1 : 0;
            return _consecutivePermanentFailures;
        }
    }

    private void ReportSuccess()
    {
        lock (_failureLock)
        {
            _consecutivePermanentFailures = 0;
        }
    }

    private void RearmOrStop()
    {
        lock (_timerLock)
        {
            if (Interlocked.Read(ref _stopping) == 1)
            {
                _logger.LogInformation("Stopping polling because of service shutdown");
                return;
            }

            try
            {
                _timer?.Change(_options.RefreshInterval, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // The timer was disposed concurrently; polling has already stopped.
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Interlocked.Exchange(ref _stopping, 1);
        TakeTimer()?.Dispose();
        TakePollingCancellation()?.Dispose();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _stopping, 1);
        var timer = TakeTimer();
        if (timer != null)
        {
            await timer.DisposeAsync();
        }

        TakePollingCancellation()?.Dispose();
    }

    private ITimer? TakeTimer()
    {
        lock (_timerLock)
        {
            var timer = _timer;
            _timer = null;
            return timer;
        }
    }

    private CancellationTokenSource? TakePollingCancellation()
    {
        lock (_timerLock)
        {
            var cancellation = _pollingCancellation;
            _pollingCancellation = null;
            return cancellation;
        }
    }
}

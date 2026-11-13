using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ROOT.Etcd.Configuration;

public sealed class EtcdPoller : IHostedService, IDisposable, IAsyncDisposable
{
    private readonly EtcdOptions _options;
    private readonly IEtcdConfigurationLoader _configurationLoader;
    private readonly IEtcdConfigurationUpdater _etcdConfigurationUpdater;
    private readonly ILogger<EtcdPoller> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly object _timerLock = new();
    private ITimer? _timer;
    private long _stopping;

    public EtcdPoller(IEtcdConfigurationLoader configurationLoader,
        IEtcdConfigurationUpdater etcdConfigurationUpdater,
        EtcdOptions options,
        TimeProvider timeProvider,
        ILogger<EtcdPoller> logger)
    {
        _options = options;
        _configurationLoader = configurationLoader;
        _etcdConfigurationUpdater = etcdConfigurationUpdater;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if(_options.RefreshInterval == TimeSpan.Zero)
        {
            _logger.LogInformation("ETCD polling is disabled because RefreshInterval is set to TimeSpan.Zero");
            return Task.CompletedTask;
        }

        _logger.LogInformation("Starting ETCD polling");

        lock (_timerLock)
        {
            Interlocked.Exchange(ref _stopping, 0);
            _timer = _timeProvider.CreateTimer(Runner, this, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _stopping, 1);
        TakeTimer()?.Dispose();
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
            var (configuration,changesPresent) = _configurationLoader.LoadConfiguration();

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
                _logger.LogWarning(e, "Timed out retreving data from etcd");
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unexpected error occured while polling for configuration changes");
        }
        finally
        {
            RearmOrStop();
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
}
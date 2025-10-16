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
    private Timer? _timer;
    private long _stopping;


    public EtcdPoller(IEtcdConfigurationLoader configurationLoader, IEtcdConfigurationUpdater etcdConfigurationUpdater, EtcdOptions options, ILogger<EtcdPoller> logger)
    {
        _options = options;
        _configurationLoader = configurationLoader;
        _etcdConfigurationUpdater = etcdConfigurationUpdater;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting ETCD polling");

        _timer = new Timer(Runner, this, TimeSpan.Zero, Timeout.InfiniteTimeSpan);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _stopping, 1);
        return Task.CompletedTask;
    }

    private void Runner(object? state)
    {
        try
        {
            var configuration = _configurationLoader.LoadConfiguration();

            _etcdConfigurationUpdater.PublishChanges(configuration);
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
            if (Interlocked.Read(ref _stopping) != 1)
            {
                _timer?.Change(_options.RefreshInterval, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _logger.LogInformation("Stopping polling because of service shutdown");
            }
        }
    }

    public void Dispose()
    {
        var timer = Interlocked.Exchange(ref _timer, null);
        timer?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        var timer = Interlocked.Exchange(ref _timer, null);
        if (timer != null)
        {
            await timer.DisposeAsync();
        }
    }
}
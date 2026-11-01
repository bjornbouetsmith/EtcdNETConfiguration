using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ROOT.Etcd.Configuration;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdPollerTest
{
    [Fact]
    public async Task StartAsync_CreatesTimerUsingProvidedTimeProvider()
    {
        var timeProvider = CreateTimeProvider();
        var logger = new TestLogger<EtcdPoller>();
        using var poller = new EtcdPoller(
            new FakeConfigurationLoader(_ => CreateData(false)),
            new RecordingUpdater(),
            CreateOptions(),
            timeProvider, logger);

        await poller.StartAsync(CancellationToken.None);

        var timer = timeProvider.Timer;
        Assert.Equal(TimeSpan.Zero, timer.DueTime);
        Assert.Equal(Timeout.InfiniteTimeSpan, timer.Period);
        Assert.Contains(logger.Entries, entry => entry.Message == "Starting ETCD polling");
    }

    [Fact]
    public async Task TimerCallback_WhenConfigurationChanged_PublishesIt()
    {
        var loader = new FakeConfigurationLoader(_ => CreateData(true, ("/configuration:one", "value-one")));
        var updater = new RecordingUpdater();
        var logger = new TestLogger<EtcdPoller>();
        var timeProvider = CreateTimeProvider();
        using var poller = new EtcdPoller(loader, updater, CreateOptions(), timeProvider, logger);

        await poller.StartAsync(CancellationToken.None);
        timeProvider.Timer.Fire();

        Assert.Equal(1, updater.PublishCount);
        Assert.NotNull(updater.LastConfiguration);
        Assert.Equal("value-one", updater.LastConfiguration["/configuration:one"]);
    }

    [Fact]
    public async Task TimerCallback_WhenConfigurationDidNotChange_DoesNotPublishAndRearmsTimer()
    {
        var options = CreateOptions(TimeSpan.FromSeconds(30));
        var loader = new FakeConfigurationLoader(_ => CreateData(false));
        var updater = new RecordingUpdater();
        var timeProvider = CreateTimeProvider();
        using var poller = new EtcdPoller(
            loader,
            updater,
            options,
            timeProvider, new TestLogger<EtcdPoller>());

        await poller.StartAsync(CancellationToken.None);
        var timer = timeProvider.Timer;
        timer.Fire();

        Assert.Equal(0, updater.PublishCount);
        Assert.Equal(1, loader.CallCount);
        Assert.Equal(1, timer.ChangeCount);
        Assert.Equal(options.RefreshInterval, timer.DueTime);
        Assert.Equal(Timeout.InfiniteTimeSpan, timer.Period);
    }

    [Fact]
    public async Task TimerCallback_WhenLoaderThrows_LogsErrorAndKeepsPolling()
    {
        var loader = new FakeConfigurationLoader(call => call == 1
            ? throw new InvalidOperationException("boom")
            : CreateData(true, ("/configuration:one", "value-one")));
        var updater = new RecordingUpdater();
        var logger = new TestLogger<EtcdPoller>();
        var timeProvider = CreateTimeProvider();
        using var poller = new EtcdPoller(
            loader,
            updater,
            CreateOptions(TimeSpan.FromSeconds(30)),
            timeProvider, logger);

        await poller.StartAsync(CancellationToken.None);
        var timer = timeProvider.Timer;
        timer.Fire();

        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Error &&
                     entry.Message == "Unexpected error occured while polling for configuration changes");
        Assert.Equal(0, updater.PublishCount);
        Assert.Equal(1, timer.ChangeCount);

        timer.Fire();

        Assert.Equal(1, updater.PublishCount);
        Assert.Equal(2, timer.ChangeCount);
    }

    [Fact]
    public async Task TimerCallback_WhenLoadIsCancelled_LogsWarningAndKeepsPolling()
    {
        var loader = new FakeConfigurationLoader(call => call == 1
            ? throw new OperationCanceledException()
            : CreateData(true, ("/configuration:one", "value-one")));
        var updater = new RecordingUpdater();
        var logger = new TestLogger<EtcdPoller>();
        var timeProvider = CreateTimeProvider();
        using var poller = new EtcdPoller(
            loader,
            updater,
            CreateOptions(TimeSpan.FromSeconds(30)),
            timeProvider, logger);

        await poller.StartAsync(CancellationToken.None);
        var timer = timeProvider.Timer;
        timer.Fire();

        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                     entry.Message == "Timed out retreving data from etcd");
        Assert.Equal(0, updater.PublishCount);
        Assert.Equal(1, timer.ChangeCount);

        timer.Fire();

        Assert.Equal(1, updater.PublishCount);
        Assert.Equal(2, timer.ChangeCount);
    }

    [Fact]
    public async Task TimerCallback_WhenStoppedDuringLoad_DoesNotLogWarningOrRearmTimer()
    {
        EtcdPoller? poller = null;
        var loader = new FakeConfigurationLoader(_ =>
        {
            poller!.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            throw new OperationCanceledException();
        });
        var updater = new RecordingUpdater();
        var logger = new TestLogger<EtcdPoller>();
        var timeProvider = CreateTimeProvider();
        poller = new EtcdPoller(loader, updater, CreateOptions(), timeProvider, logger);

        using (poller)
        {
            await poller.StartAsync(CancellationToken.None);
            timeProvider.Timer.Fire();
        }

        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(
            logger.Entries,
            entry => entry.Message == "Stopping polling because of service shutdown");
        Assert.Equal(0, timeProvider.Timer.ChangeCount);
        Assert.Equal(0, updater.PublishCount);
    }

    [Fact]
    public async Task StopAsync_WhileLoadIsInProgress_DoesNotRearmTimer()
    {
        EtcdPoller? poller = null;
        var loader = new FakeConfigurationLoader(_ =>
        {
            poller!.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            return CreateData(true, ("/configuration:one", "value-one"));
        });
        var updater = new RecordingUpdater();
        var logger = new TestLogger<EtcdPoller>();
        var timeProvider = CreateTimeProvider();
        poller = new EtcdPoller(loader, updater, CreateOptions(), timeProvider, logger);

        using (poller)
        {
            await poller.StartAsync(CancellationToken.None);
            timeProvider.Timer.Fire();
        }

        Assert.Equal(1, loader.CallCount);
        Assert.Equal(1, updater.PublishCount);
        Assert.Equal(0, timeProvider.Timer.ChangeCount);
        Assert.Contains(
            logger.Entries,
            entry => entry.Message == "Stopping polling because of service shutdown");
    }

    [Fact]
    public async Task StopAsync_DisposesTheTimer()
    {
        var timeProvider = CreateTimeProvider();
        using var poller = new EtcdPoller(
            new FakeConfigurationLoader(_ => CreateData(false)),
            new RecordingUpdater(),
            CreateOptions(),
            timeProvider, new TestLogger<EtcdPoller>());

        await poller.StartAsync(CancellationToken.None);
        var timer = timeProvider.Timer;

        await poller.StopAsync(CancellationToken.None);

        Assert.True(timer.IsDisposed);
    }

    [Fact]
    public async Task StopAsync_WhenCallbackWasAlreadyScheduled_DoesNotLoadOrPublish()
    {
        var loader = new FakeConfigurationLoader(_ => CreateData(true, ("/configuration:one", "value-one")));
        var updater = new RecordingUpdater();
        var timeProvider = CreateTimeProvider();
        using var poller = new EtcdPoller(
            loader,
            updater,
            CreateOptions(),
            timeProvider, new TestLogger<EtcdPoller>());

        await poller.StartAsync(CancellationToken.None);
        var timer = timeProvider.Timer;

        await poller.StopAsync(CancellationToken.None);

        // Simulate a callback that was queued before the timer was disposed.
        timer.FireIgnoringDisposal();

        Assert.Equal(0, loader.CallCount);
        Assert.Equal(0, updater.PublishCount);
        Assert.Equal(0, timer.ChangeCount);
    }

    [Fact]
    public async Task StopAsync_CalledTwice_DoesNotThrow()
    {
        var timeProvider = CreateTimeProvider();
        using var poller = new EtcdPoller(
            new FakeConfigurationLoader(_ => CreateData(false)),
            new RecordingUpdater(),
            CreateOptions(),
            timeProvider, new TestLogger<EtcdPoller>());

        await poller.StartAsync(CancellationToken.None);

        await poller.StopAsync(CancellationToken.None);
        await poller.StopAsync(CancellationToken.None);

        Assert.True(timeProvider.Timer.IsDisposed);
    }

    [Fact]
    public async Task DisposeAsync_WhileLoadIsInProgress_DoesNotRearmTimer()
    {
        EtcdPoller? poller = null;
        var loader = new FakeConfigurationLoader(_ =>
        {
            poller!.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return CreateData(false);
        });
        var updater = new RecordingUpdater();
        var timeProvider = CreateTimeProvider();
        poller = new EtcdPoller(
            loader,
            updater,
            CreateOptions(),
            timeProvider, new TestLogger<EtcdPoller>());

        try
        {
            await poller.StartAsync(CancellationToken.None);
            var timer = timeProvider.Timer;
            timer.Fire();

            Assert.True(timer.IsDisposed);
            Assert.Equal(1, loader.CallCount);
            Assert.Equal(0, timer.ChangeCount);
        }
        finally
        {
            await poller.DisposeAsync();
        }
    }

    [Fact]
    public async Task Dispose_AfterStart_DisposesTimerAndIsIdempotent()
    {
        var timeProvider = CreateTimeProvider();
        var poller = new EtcdPoller(
            new FakeConfigurationLoader(_ => CreateData(false)),
            new RecordingUpdater(),
            CreateOptions(),
            timeProvider, new TestLogger<EtcdPoller>());

        await poller.StartAsync(CancellationToken.None);
        var timer = timeProvider.Timer;

        poller.Dispose();
        poller.Dispose();

        Assert.True(timer.IsDisposed);
    }

    [Fact]
    public async Task DisposeAsync_AfterStart_DisposesTimerAndIsIdempotent()
    {
        var timeProvider = CreateTimeProvider();
        var poller = new EtcdPoller(
            new FakeConfigurationLoader(_ => CreateData(false)),
            new RecordingUpdater(),
            CreateOptions(),
            timeProvider, new TestLogger<EtcdPoller>());

        await poller.StartAsync(CancellationToken.None);
        var timer = timeProvider.Timer;

        await poller.DisposeAsync();
        await poller.DisposeAsync();

        Assert.True(timer.IsDisposed);
    }

    [Fact]
    public void Dispose_WithoutStart_DoesNotThrow()
    {
        var poller = new EtcdPoller(
            new FakeConfigurationLoader(_ => CreateData(false)),
            new RecordingUpdater(),
            CreateOptions(),
            CreateTimeProvider(), new TestLogger<EtcdPoller>());

        Assert.NotNull(poller); // Prevents warning about no asertions in this test.

        poller.Dispose();
        poller.Dispose();
    }

    [Fact]
    public async Task DisposeAsync_WithoutStart_DoesNotThrow()
    {
        var poller = new EtcdPoller(
            new FakeConfigurationLoader(_ => CreateData(false)),
            new RecordingUpdater(),
            CreateOptions(),
            CreateTimeProvider(), new TestLogger<EtcdPoller>());

        Assert.NotNull(poller); // Prevents warning about no asertions in this test.

        await poller.DisposeAsync();
        await poller.DisposeAsync();
    }

    private static TestTimeProvider CreateTimeProvider() =>
        new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));

    private static EtcdOptions CreateOptions(TimeSpan? refreshInterval = null) => new()
    {
        RefreshInterval = refreshInterval ?? TimeSpan.FromHours(1)
    };

    private static ConfigurationData CreateData(bool changesPresent, params (string Key, string? Value)[] values)
    {
        var data = new Dictionary<string, string?>();
        foreach (var (key, value) in values)
        {
            data[key] = value;
        }

        return new ConfigurationData(data, changesPresent);
    }

    private sealed class FakeConfigurationLoader : IEtcdConfigurationLoader
    {
        private readonly Func<int, ConfigurationData> _load;
        private int _callCount;

        public FakeConfigurationLoader(Func<int, ConfigurationData> load)
        {
            _load = load;
        }

        public int CallCount => Volatile.Read(ref _callCount);

        public ConfigurationData LoadConfiguration()
        {
            var call = Interlocked.Increment(ref _callCount);
            return _load(call);
        }
    }

    private sealed class RecordingUpdater : IEtcdConfigurationUpdater
    {
        private int _publishCount;
        private IReadOnlyDictionary<string, string?>? _lastConfiguration;

        public int PublishCount => Volatile.Read(ref _publishCount);

        public IReadOnlyDictionary<string, string?>? LastConfiguration =>
            Volatile.Read(ref _lastConfiguration);

        public void PublishConfiguration(IReadOnlyDictionary<string, string?> configurationData)
        {
            Volatile.Write(ref _lastConfiguration, configurationData);
            Interlocked.Increment(ref _publishCount);
        }
    }
}

using dotnet_etcd.interfaces;
using Etcdserverpb;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Moq;
using Mvccpb;
using ROOT.Etcd.Configuration;
using System;
using System.Collections.Generic;
using System.Threading;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdConfigurationLoaderTest
{
    private readonly Mock<IEtcdClient> _etcdClient = new();
    private readonly RecordingLogger<EtcdConfigurationLoader> _logger = new();
    private readonly Queue<RangeResponse> _responses = new();
    private readonly EtcdOptions _options = new()
    {
        RootKey = "/configuration",
        RedactConfigurationValues = false
    };
    private DateTime? _lastDeadline;
    private CancellationToken _lastCancellationToken;

    public EtcdConfigurationLoaderTest()
    {
        _etcdClient
            .Setup(client => client.GetRange(
                It.IsAny<string>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Metadata, DateTime?, CancellationToken>(
                (_, _, deadline, cancellationToken) =>
                {
                    _lastDeadline = deadline;
                    _lastCancellationToken = cancellationToken;
                })
            .Returns(() => _responses.Dequeue());
    }

    [Fact]
    public void LoadConfiguration_ReturnsAllKeyValuePairs()
    {
        EnqueueResponse(
            ("/configuration:one", "value-one"),
            ("/configuration:two", "value-two"));

        var result = CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Configuration.Count);
        Assert.Equal("value-one", result.Configuration["/configuration:one"]);
        Assert.Equal("value-two", result.Configuration["/configuration:two"]);
        Assert.True(result.ChangesPresent);
    }

    [Fact]
    public void LoadConfiguration_UsesConfiguredRootKey()
    {
        _options.RootKey = "/custom-root";
        EnqueueResponse(("/custom-root:key", "value"));

        CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        _etcdClient.Verify(
            client => client.GetRange(
                "/custom-root",
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void LoadConfiguration_UsesProvidedTimeProviderForDeadline()
    {
        var now = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        _options.ConfigurationTimeout = TimeSpan.FromSeconds(10);
        EnqueueResponse();

        CreateLoader(new TestTimeProvider(now)).LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Equal(now.Add(_options.ConfigurationTimeout).DateTime, _lastDeadline);
    }

    [Fact]
    public void LoadConfiguration_AfterTimeProviderAdvances_UsesUpdatedDeadline()
    {
        var now = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(now);
        _options.ConfigurationTimeout = TimeSpan.FromSeconds(5);
        EnqueueResponse();
        EnqueueResponse();

        var loader = CreateLoader(timeProvider);
        loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Equal(now.Add(_options.ConfigurationTimeout).DateTime, _lastDeadline);

        timeProvider.Advance(TimeSpan.FromMinutes(2));
        loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Equal(
            now.AddMinutes(2).Add(_options.ConfigurationTimeout).DateTime,
            _lastDeadline);
    }

    [Fact]
    public void LoadConfiguration_WhenNoTimeProviderIsGiven_UsesSystemTime()
    {
        _options.ConfigurationTimeout = TimeSpan.FromSeconds(30);
        EnqueueResponse();

        var before = TimeProvider.System.GetUtcNow().Add(_options.ConfigurationTimeout);
        CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);
        var after = TimeProvider.System.GetUtcNow().Add(_options.ConfigurationTimeout);

        Assert.NotNull(_lastDeadline);
        Assert.InRange(_lastDeadline!.Value, before.DateTime, after.DateTime);
    }

    [Fact]
    public void LoadConfiguration_WhenEtcdReturnsNoKeys_ReturnsEmptyDictionary()
    {
        EnqueueResponse();

        var result = CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Empty(result.Configuration);
        Assert.True(result.ChangesPresent);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void LoadConfiguration_WhenEtcdStillReturnsNoKeys_ReportsNoChanges()
    {
        EnqueueResponse();
        EnqueueResponse();

        var loader = CreateLoader();
        loader.LoadConfiguration(TestContext.Current.CancellationToken);
        _logger.Clear();

        var result = loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Empty(result.Configuration);
        Assert.False(result.ChangesPresent);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void LoadConfiguration_DecodesUtf8KeysAndValues()
    {
        EnqueueResponse(("/configuration:n\u00f8kkel", "verd\u00ef"));

        var result = CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Equal("verd\u00ef", result.Configuration["/configuration:n\u00f8kkel"]);
    }

    [Fact]
    public void LoadConfiguration_OnFirstLoad_LogsEveryKey()
    {
        EnqueueResponse(
            ("/configuration:one", "value-one"),
            ("/configuration:two", "value-two"));

        var result = CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.True(result.ChangesPresent);
        Assert.Equal(2, _logger.Entries.Count);
        Assert.Contains(
            _logger.Entries,
            entry => entry.Message == "Configuration key /configuration:one was added with value value-one");
        Assert.Contains(
            _logger.Entries,
            entry => entry.Message == "Configuration key /configuration:two was added with value value-two");
    }

    [Fact]
    public void LoadConfiguration_ByDefault_RedactsValuesInLogs()
    {
        EnqueueResponse(("/configuration:one", "value-one"));
        var loader = new EtcdConfigurationLoader(
            _etcdClient.Object,
            new EtcdOptions { RootKey = "/configuration" },
            _logger);

        var result = loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.True(result.ChangesPresent);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(
            "Configuration key /configuration:one was added with value ***",
            entry.Message);
    }

    [Fact]
    public void LoadConfiguration_WhenRedactionEnabled_RedactsAddedValues()
    {
        _options.RedactConfigurationValues = true;
        EnqueueResponse(("/configuration:one", "value-one"));

        var result = CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.True(result.ChangesPresent);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(
            "Configuration key /configuration:one was added with value ***",
            entry.Message);
        Assert.Equal("value-one", result.Configuration["/configuration:one"]);
    }

    [Fact]
    public void LoadConfiguration_WhenRedactionEnabled_RedactsChangedValues()
    {
        _options.RedactConfigurationValues = true;
        EnqueueResponse(("/configuration:one", "value-one"));
        EnqueueResponse(("/configuration:one", "new-value"));
        var loader = CreateLoader();
        loader.LoadConfiguration(TestContext.Current.CancellationToken);
        _logger.Clear();

        var result = loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.True(result.ChangesPresent);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(
            "Configuration key /configuration:one changed from *** to ***",
            entry.Message);
        Assert.Equal("new-value", result.Configuration["/configuration:one"]);
    }

    [Fact]
    public void LoadConfiguration_WhenRedactionEnabled_RedactsRemovedValues()
    {
        _options.RedactConfigurationValues = true;
        EnqueueResponse(
            ("/configuration:one", "value-one"),
            ("/configuration:two", "value-two"));
        EnqueueResponse(("/configuration:one", "value-one"));
        var loader = CreateLoader();
        loader.LoadConfiguration(TestContext.Current.CancellationToken);
        _logger.Clear();

        var result = loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.True(result.ChangesPresent);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(
            "Configuration key /configuration:two was removed; previous value was ***",
            entry.Message);
    }

    [Fact]
    public void LoadConfiguration_WhenNothingChanged_DoesNotLog()
    {
        EnqueueResponse(
            ("/configuration:one", "value-one"),
            ("/configuration:two", "value-two"));
        EnqueueResponse(
            ("/configuration:two", "value-two"),
            ("/configuration:one", "value-one"));

        var loader = CreateLoader();
        loader.LoadConfiguration(TestContext.Current.CancellationToken);
        _logger.Clear();

        var result = loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.False(result.ChangesPresent);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void LoadConfiguration_WhenKeyAdded_LogsOnlyAddedKey()
    {
        EnqueueResponse(("/configuration:one", "value-one"));
        EnqueueResponse(
            ("/configuration:one", "value-one"),
            ("/configuration:two", "value-two"));

        var loader = CreateLoader();
        loader.LoadConfiguration(TestContext.Current.CancellationToken);
        _logger.Clear();

        var result = loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.True(result.ChangesPresent);
        Assert.True(result.Configuration.ContainsKey("/configuration:two"));
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(
            "Configuration key /configuration:two was added with value value-two",
            entry.Message);
    }

    [Fact]
    public void LoadConfiguration_WhenKeyRemoved_LogsOnlyRemovedKey()
    {
        EnqueueResponse(
            ("/configuration:one", "value-one"),
            ("/configuration:two", "value-two"));
        EnqueueResponse(("/configuration:one", "value-one"));

        var loader = CreateLoader();
        loader.LoadConfiguration(TestContext.Current.CancellationToken);
        _logger.Clear();

        var result = loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.True(result.ChangesPresent);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(
            "Configuration key /configuration:two was removed; previous value was value-two",
            entry.Message);
        Assert.False(result.Configuration.ContainsKey("/configuration:two"));
    }

    [Fact]
    public void LoadConfiguration_WhenValueChanged_LogsOnlyChangedKey()
    {
        EnqueueResponse(
            ("/configuration:one", "value-one"),
            ("/configuration:two", "value-two"));
        EnqueueResponse(
            ("/configuration:one", "new-value"),
            ("/configuration:two", "value-two"));

        var loader = CreateLoader();
        loader.LoadConfiguration(TestContext.Current.CancellationToken);
        _logger.Clear();

        var result = loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.True(result.ChangesPresent);
        Assert.Equal("new-value", result.Configuration["/configuration:one"]);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(
            "Configuration key /configuration:one changed from value-one to new-value",
            entry.Message);
    }

    [Fact]
    public void LoadConfiguration_WhenValueChangesToEmptyString_LogsChange()
    {
        EnqueueResponse(("/configuration:one", "value-one"));
        EnqueueResponse(("/configuration:one", string.Empty));

        var loader = CreateLoader();
        loader.LoadConfiguration(TestContext.Current.CancellationToken);
        _logger.Clear();

        var result = loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.True(result.ChangesPresent);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(
            "Configuration key /configuration:one changed from value-one to ",
            entry.Message);
    }

    [Fact]
    public void LoadConfiguration_WhenMultipleChangesOccur_LogsOnlyChanges()
    {
        EnqueueResponse(
            ("/configuration:one", "value-one"),
            ("/configuration:two", "value-two"),
            ("/configuration:three", "value-three"));
        EnqueueResponse(
            ("/configuration:one", "new-value"),
            ("/configuration:two", "value-two"),
            ("/configuration:four", "value-four"));

        var loader = CreateLoader();
        loader.LoadConfiguration(TestContext.Current.CancellationToken);
        _logger.Clear();

        var result = loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.True(result.ChangesPresent);
        Assert.Equal(3, _logger.Entries.Count);
        Assert.Contains(
            _logger.Entries,
            entry => entry.Message == "Configuration key /configuration:one changed from value-one to new-value");
        Assert.Contains(
            _logger.Entries,
            entry => entry.Message == "Configuration key /configuration:three was removed; previous value was value-three");
        Assert.Contains(
            _logger.Entries,
            entry => entry.Message == "Configuration key /configuration:four was added with value value-four");
        Assert.DoesNotContain(
            _logger.Entries,
            entry => entry.Message.Contains("/configuration:two", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadConfiguration_WhenPreviousLoadWasEmpty_LogsAddedKeys()
    {
        EnqueueResponse();
        EnqueueResponse(("/configuration:one", "value-one"));

        var loader = CreateLoader();
        loader.LoadConfiguration(TestContext.Current.CancellationToken);
        _logger.Clear();

        var result = loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.True(result.ChangesPresent);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(
            "Configuration key /configuration:one was added with value value-one",
            entry.Message);
    }

    [Fact]
    public void LoadConfiguration_ReturnsSnapshotThatIsNotChangedByLaterLoads()
    {
        EnqueueResponse(("/configuration:one", "value-one"));
        EnqueueResponse(("/configuration:one", "value-two"));

        var loader = CreateLoader();
        var firstResult = loader.LoadConfiguration(TestContext.Current.CancellationToken);
        var secondResult = loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.True(firstResult.ChangesPresent);
        Assert.True(secondResult.ChangesPresent);
        Assert.Equal("value-one", firstResult.Configuration["/configuration:one"]);
        Assert.Equal("value-two", secondResult.Configuration["/configuration:one"]);
    }

    [Fact]
    public void LoadConfiguration_PassesTheCancellationTokenToEtcd()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        EnqueueResponse();

        CreateLoader().LoadConfiguration(cancellationTokenSource.Token);

        Assert.Equal(cancellationTokenSource.Token, _lastCancellationToken);
    }

    [Fact]
    public void LoadConfiguration_WhenKeyCountLimitIsExceeded_Throws()
    {
        _options.MaximumConfigurationKeyCount = 1;
        EnqueueResponse(
            ("/configuration:one", "value-one"),
            ("/configuration:two", "value-two"));

        var exception = Assert.ThrowsAny<InvalidOperationException>(
            () => CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken));

        Assert.Contains("MaximumConfigurationKeyCount", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadConfiguration_WhenSizeLimitIsExceeded_Throws()
    {
        _options.MaximumConfigurationSizeBytes = 5;
        EnqueueResponse(("/configuration:one", "value-one"));

        var exception = Assert.ThrowsAny<InvalidOperationException>(
            () => CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken));

        Assert.Contains("MaximumConfigurationSizeBytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadConfiguration_WhenRedactionIsDisabled_StillRedactsSecretLikeKeys()
    {
        _options.RedactConfigurationValues = false;
        EnqueueResponse(
            ("/configuration:database:password", "super-secret"),
            ("/configuration:display-name", "visible"));

        CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Contains(
            _logger.Entries,
            entry => entry.Message == "Configuration key /configuration:database:password was added with value ***");
        Assert.Contains(
            _logger.Entries,
            entry => entry.Message == "Configuration key /configuration:display-name was added with value visible");
    }

    private EtcdConfigurationLoader CreateLoader() => new(_etcdClient.Object, _options, _logger);

    private EtcdConfigurationLoader CreateLoader(TimeProvider timeProvider) =>
        new(_etcdClient.Object, _options, _logger, timeProvider, null);

    private void EnqueueResponse(params (string Key, string Value)[] values)
    {
        var response = new RangeResponse();
        foreach (var (key, value) in values)
        {
            response.Kvs.Add(new KeyValue
            {
                Key = ByteString.CopyFromUtf8(key),
                Value = ByteString.CopyFromUtf8(value)
            });
        }

        _responses.Enqueue(response);
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        public void Clear() => _entries.Clear();
    }
}

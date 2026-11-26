using dotnet_etcd.interfaces;
using Etcdserverpb;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Mvccpb;
using ROOT.Etcd.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdConfigurationLoaderTest
{
    private readonly Mock<IEtcdClient> _etcdClient = new();
    private readonly RecordingLogger<EtcdConfigurationLoader> _logger = new();
    private readonly Queue<RangeResponse> _responses = new();
    private readonly List<RangeCall> _calls = [];
    private readonly EtcdOptions _options = new()
    {
        RootKeys = [new EtcdRootKey { RootKey = "/configuration/", Priority = 0 }],
        ConfigurationPrefix = "/configuration",
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
                (root, _, deadline, cancellationToken) =>
                {
                    _calls.Add(new RangeCall(root, deadline));
                    _lastDeadline = deadline;
                    _lastCancellationToken = cancellationToken;
                })
            .Returns(() => _responses.Dequeue());
    }

    [Fact]
    public void LoadConfiguration_ReturnsAllKeyValuePairs()
    {
        EnqueueResponse(
            ("/configuration/one", "value-one"),
            ("/configuration/two", "value-two"));

        var result = CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Configuration.Count);
        Assert.Equal("value-one", result.Configuration["/configuration:one"]);
        Assert.Equal("value-two", result.Configuration["/configuration:two"]);
        Assert.True(result.ChangesPresent);
    }

    [Fact]
    public void LoadConfiguration_MapsNestedPathsToConfigurationKeys()
    {
        EnqueueResponse(
            ("/configuration/database/host", "localhost"),
            ("/configuration/database/port", "5432"));

        var result = CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Equal("localhost", result.Configuration["/configuration:database:host"]);
        Assert.Equal("5432", result.Configuration["/configuration:database:port"]);
    }

    [Fact]
    public void LoadConfiguration_MapsCollectionIndices()
    {
        EnqueueResponse(
            ("/configuration/connectionstrings:0", "first"),
            ("/configuration/connectionstrings:1", "second"));

        var result = CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Equal("first", result.Configuration["/configuration:connectionstrings:0"]);
        Assert.Equal("second", result.Configuration["/configuration:connectionstrings:1"]);
    }

    [Fact]
    public void LoadConfiguration_MapsNestedCollections()
    {
        EnqueueResponse(("/configuration/services:0/endpoints:1", "endpoint"));

        var result = CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Equal("endpoint", result.Configuration["/configuration:services:0:endpoints:1"]);
    }

    [Fact]
    public void LoadConfiguration_MapsDeepNesting()
    {
        EnqueueResponse(("/configuration/key/subkey/deepkey", "value"));

        var result = CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Equal("value", result.Configuration["/configuration:key:subkey:deepkey"]);
    }

    [Fact]
    public void LoadConfiguration_SkipsOutOfScopeKeys()
    {
        EnqueueResponse(
            ("/configuration/key", "value"),
            ("/configuration-other/key", "other"),
            ("/configurations/key", "other"),
            ("/configuration", "root"),
            ("/configuration-other:key", "other"));

        var result = CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        var value = Assert.Single(result.Configuration);
        Assert.Equal("/configuration:key", value.Key);
        Assert.Equal("value", value.Value);
    }

    [Fact]
    public void LoadConfiguration_WhenKeyUsesOldFormat_ThrowsMigrationError()
    {
        EnqueueResponse(("/configuration:key", "value"));

        var exception = Assert.Throws<EtcdConfigurationKeyFormatException>(
            () => CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken));

        Assert.Contains("removed ':' hierarchy format", exception.Message, StringComparison.Ordinal);
        Assert.Contains("/configuration/key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadConfiguration_WhenCustomRootKeyUsesOldFormat_ThrowsMigrationError()
    {
        _options.RootKeys = [new EtcdRootKey { RootKey = "/app/", Priority = 0 }];
        EnqueueResponse(("/app:Database:Host", "value"));

        var exception = Assert.Throws<EtcdConfigurationKeyFormatException>(
            () => CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken));

        Assert.Contains("/app", exception.Message, StringComparison.Ordinal);
        Assert.Contains("removed ':' hierarchy format", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/configuration/key:foo")]
    [InlineData("/configuration/key:")]
    [InlineData("/configuration/key::0")]
    [InlineData("/configuration/key:-1")]
    [InlineData("/configuration/key:+1")]
    [InlineData("/configuration/key:01")]
    [InlineData("/configuration/key:1.0")]
    public void LoadConfiguration_WhenKeyUsesInvalidColon_ThrowsFormatError(string etcdKey)
    {
        EnqueueResponse((etcdKey, "value"));

        var exception = Assert.Throws<EtcdConfigurationKeyFormatException>(
            () => CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken));

        Assert.Contains("invalid path segment", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/configuration//key")]
    [InlineData("/configuration/key/")]
    public void LoadConfiguration_WhenKeyHasEmptySegment_ThrowsFormatError(string etcdKey)
    {
        EnqueueResponse((etcdKey, "value"));

        var exception = Assert.Throws<EtcdConfigurationKeyFormatException>(
            () => CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken));

        Assert.Contains("empty path segment", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/app/Database:Host")]
    [InlineData("/app//Host")]
    [InlineData("/app/Host/")]
    public void LoadConfiguration_WhenCustomRootKeyHasInvalidRelativePath_ThrowsFormatError(string etcdKey)
    {
        _options.RootKeys = [new EtcdRootKey { RootKey = "/app/", Priority = 0 }];
        EnqueueResponse((etcdKey, "value"));

        Assert.Throws<EtcdConfigurationKeyFormatException>(
            () => CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void LoadConfiguration_WhenKeysDifferOnlyByCase_ThrowsCollision()
    {
        EnqueueResponse(
            ("/configuration/Key", "first"),
            ("/configuration/key", "second"));

        var exception = Assert.Throws<EtcdConfigurationKeyCollisionException>(
            () => CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken));

        Assert.Contains("/configuration/Key", exception.Message, StringComparison.Ordinal);
        Assert.Contains("/configuration/key", exception.Message, StringComparison.Ordinal);
        Assert.Contains("/configuration:key", exception.Message, StringComparison.Ordinal);
        Assert.Contains(":key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadConfiguration_WhenKeysMapToTheSameStructure_ThrowsCollision()
    {
        EnqueueResponse(
            ("/configuration/key:0", "first"),
            ("/configuration/key/0", "second"));

        var exception = Assert.Throws<EtcdConfigurationKeyCollisionException>(
            () => CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken));

        Assert.Contains("/configuration:key:0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadConfiguration_WhenOutOfScopeKeyWouldCollide_DoesNotThrow()
    {
        EnqueueResponse(
            ("/configuration/key", "value"),
            ("/configuration-other/key", "other"));

        var result = CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Equal("value", result.Configuration["/configuration:key"]);
        Assert.Single(result.Configuration);
    }

    [Fact]
    public void LoadConfiguration_UsesStrictPathBoundary()
    {
        _options.RootKeys = [new EtcdRootKey { RootKey = "/app/", Priority = 0 }];
        EnqueueResponse(
            ("/app/key", "value"),
            ("/application/key", "other"),
            ("/app", "root"));

        var result = CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        var value = Assert.Single(result.Configuration);
        Assert.Equal("/configuration:key", value.Key);
        Assert.Equal("value", value.Value);
    }

    [Fact]
    public void LoadConfiguration_WhenOnlyCaseChanges_ReportsNoChanges()
    {
        EnqueueResponse(("/configuration/key", "value"));
        EnqueueResponse(("/configuration/KEY", "value"));

        var loader = CreateLoader();
        loader.LoadConfiguration(TestContext.Current.CancellationToken);
        _logger.Clear();

        var result = loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.False(result.ChangesPresent);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void LoadConfiguration_WhenOnlyCaseChanges_DoesNotReportRemoveAndAdd()
    {
        EnqueueResponse(("/configuration/key", "value"));
        EnqueueResponse(("/configuration/KEY", "value"));

        var loader = CreateLoader();
        loader.LoadConfiguration(TestContext.Current.CancellationToken);
        _logger.Clear();

        loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            _logger.Entries,
            entry => entry.Message.Contains("was removed", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadConfiguration_WhenLookupUsesDifferentCase_IsCaseInsensitive()
    {
        EnqueueResponse(("/configuration/Database/Host", "localhost"));

        var result = CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Equal("localhost", result.Configuration["/configuration:database:host"]);
        Assert.True(result.Configuration.ContainsKey("/CONFIGURATION:DATABASE:HOST"));
    }

    [Fact]
    public void LoadConfiguration_UsesConfiguredRootKeysInPriorityOrder()
    {
        _options.RootKeys =
        [
            new EtcdRootKey { RootKey = "/high/", Priority = 20 },
            new EtcdRootKey { RootKey = "/low/", Priority = 10 }
        ];
        EnqueueResponse();
        EnqueueResponse();

        CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Equal(["/low/", "/high/"], _calls.Select(call => call.Root));
        _etcdClient.Verify(
            client => client.GetRange(
                It.IsAny<string>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public void LoadConfiguration_MergesMultipleRootsAndHighestPriorityWins()
    {
        _options.RootKeys =
        [
            new EtcdRootKey { RootKey = "/domain/", Priority = 0 },
            new EtcdRootKey { RootKey = "/machine/", Priority = 10 }
        ];
        EnqueueResponse(
            ("/domain/Logging/Level", "Debug"),
            ("/domain/Database/Name", "domain-db"));
        EnqueueResponse(
            ("/machine/Logging/Level", "Warning"),
            ("/machine/Kafka/BootStrapServers:0", "host-1"));

        var result = CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Configuration.Count);
        Assert.Equal("Warning", result.Configuration["/configuration:Logging:Level"]);
        Assert.Equal("domain-db", result.Configuration["/configuration:Database:Name"]);
        Assert.Equal("host-1", result.Configuration["/configuration:Kafka:BootStrapServers:0"]);
    }

    [Fact]
    public void LoadConfiguration_MergesCaseVariantRelativeKeysAcrossRootsByPriority()
    {
        _options.RootKeys =
        [
            new EtcdRootKey { RootKey = "/domain/", Priority = 0 },
            new EtcdRootKey { RootKey = "/machine/", Priority = 10 }
        ];
        EnqueueResponse(("/domain/Kafka/Host", "lower"));
        EnqueueResponse(("/machine/kafka/host", "higher"));

        var result = CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        var value = Assert.Single(result.Configuration);
        Assert.Equal("/configuration:kafka:host", value.Key);
        Assert.Equal("higher", value.Value);
    }

    [Fact]
    public void LoadConfiguration_UsesConfigurationPrefixForFinalKeys()
    {
        _options.ConfigurationPrefix = "/app";
        EnqueueResponse(
            ("/configuration/database/host", "localhost"),
            ("/configuration/Kafka/BootStrapServers:0", "host-1"));

        var result = CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Equal("localhost", result.Configuration["/app:database:host"]);
        Assert.Equal("host-1", result.Configuration["/app:Kafka:BootStrapServers:0"]);
        Assert.DoesNotContain(
            result.Configuration,
            kv => kv.Key.StartsWith("/configuration", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadConfiguration_WhenLowerPriorityShadowedValueChanges_ReportsNoChanges()
    {
        ConfigureTwoRoots();
        EnqueueResponse(("/low/Shared", "low"));
        EnqueueResponse(("/high/Shared", "high"));

        var loader = CreateLoader();
        loader.LoadConfiguration(TestContext.Current.CancellationToken);
        _logger.Clear();

        EnqueueResponse(("/low/Shared", "changed-low"));
        EnqueueResponse(("/high/Shared", "high"));

        var result = loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.False(result.ChangesPresent);
        Assert.Equal("high", result.Configuration["/configuration:Shared"]);
        Assert.DoesNotContain(_logger.Entries, entry => entry.Level == LogLevel.Information);
    }

    [Fact]
    public void LoadConfiguration_WhenHigherPriorityValueChanges_ReportsChange()
    {
        ConfigureTwoRoots();
        EnqueueResponse(("/low/Shared", "low"));
        EnqueueResponse(("/high/Shared", "high"));

        var loader = CreateLoader();
        loader.LoadConfiguration(TestContext.Current.CancellationToken);
        _logger.Clear();

        EnqueueResponse(("/low/Shared", "low"));
        EnqueueResponse(("/high/Shared", "changed-high"));

        var result = loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.True(result.ChangesPresent);
        Assert.Equal("changed-high", result.Configuration["/configuration:Shared"]);
    }

    [Fact]
    public void LoadConfiguration_WhenWinningKeyIsRemovedWithADifferentFallback_ReportsChange()
    {
        ConfigureTwoRoots();
        EnqueueResponse(("/low/Shared", "low"));
        EnqueueResponse(("/high/Shared", "high"));

        var loader = CreateLoader();
        loader.LoadConfiguration(TestContext.Current.CancellationToken);
        _logger.Clear();

        EnqueueResponse(("/low/Shared", "low"));
        EnqueueResponse();

        var result = loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.True(result.ChangesPresent);
        Assert.Equal("low", result.Configuration["/configuration:Shared"]);
    }

    [Fact]
    public void LoadConfiguration_WhenWinningKeyIsRemovedWithAnEqualFallback_ReportsNoChanges()
    {
        ConfigureTwoRoots();
        EnqueueResponse(("/low/Shared", "same"));
        EnqueueResponse(("/high/Shared", "same"));

        var loader = CreateLoader();
        loader.LoadConfiguration(TestContext.Current.CancellationToken);
        _logger.Clear();

        EnqueueResponse(("/low/Shared", "same"));
        EnqueueResponse();

        var result = loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.False(result.ChangesPresent);
        Assert.Equal("same", result.Configuration["/configuration:Shared"]);
        Assert.DoesNotContain(_logger.Entries, entry => entry.Level == LogLevel.Information);
    }

    [Fact]
    public void LoadConfiguration_WhenHigherPriorityKeyChangesCase_ReportsNoChanges()
    {
        ConfigureTwoRoots();
        EnqueueResponse(("/low/Shared", "high"));
        EnqueueResponse(("/high/Shared", "high"));

        var loader = CreateLoader();
        loader.LoadConfiguration(TestContext.Current.CancellationToken);
        _logger.Clear();

        EnqueueResponse(("/low/Shared", "high"));
        EnqueueResponse(("/high/SHARED", "high"));

        var result = loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.False(result.ChangesPresent);
    }

    [Fact]
    public void LoadConfiguration_WhenOneRootFails_DoesNotUpdateThePreviousSnapshot()
    {
        _options.RootKeys =
        [
            new EtcdRootKey { RootKey = "/low/", Priority = 0 },
            new EtcdRootKey { RootKey = "/high/", Priority = 10 }
        ];

        var client = new Mock<IEtcdClient>();
        client
            .SetupSequence(GetRangeSetup())
            .Returns(CreateResponse(("/low/one", "value-one")))
            .Returns(CreateResponse(("/high/two", "value-two")))
            .Returns(CreateResponse(("/low/one", "changed")))
            .Throws(new InvalidOperationException("etcd is unavailable"))
            .Returns(CreateResponse(("/low/one", "value-one")))
            .Returns(CreateResponse(("/high/two", "value-two")));

        var loader = new EtcdConfigurationLoader(client.Object, _options, _logger);
        var first = loader.LoadConfiguration(TestContext.Current.CancellationToken);
        Assert.True(first.ChangesPresent);

        Assert.Throws<InvalidOperationException>(
            () => loader.LoadConfiguration(TestContext.Current.CancellationToken));

        var third = loader.LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.False(third.ChangesPresent);
        Assert.Equal("value-one", third.Configuration["/configuration:one"]);
        Assert.Equal("value-two", third.Configuration["/configuration:two"]);
        Assert.DoesNotContain("changed", third.Configuration.Values);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(9, 10)]
    public void LoadConfiguration_WhenPrioritiesAreDuplicatedOrRootsOverlap_FailsBeforeAnyQuery(
        int secondPriority,
        int _)
    {
        _options.RootKeys =
        [
            new EtcdRootKey { RootKey = "/app/", Priority = 10 },
            new EtcdRootKey { RootKey = "/app/service/", Priority = secondPriority }
        ];

        Assert.Throws<OptionsValidationException>(
            () => CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken));

        _etcdClient.Verify(
            client => client.GetRange(
                It.IsAny<string>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void LoadConfiguration_WhenConfigurationPrefixIsInvalid_FailsBeforeAnyQuery()
    {
        _options.ConfigurationPrefix = "/";

        Assert.Throws<OptionsValidationException>(
            () => CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken));

        _etcdClient.Verify(
            client => client.GetRange(
                It.IsAny<string>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void LoadConfiguration_WhenRootKeyIsInvalid_FailsBeforeAnyQuery()
    {
        _options.RootKeys = [new EtcdRootKey { RootKey = "invalid/" }];

        Assert.Throws<OptionsValidationException>(
            () => CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken));

        _etcdClient.Verify(
            client => client.GetRange(
                It.IsAny<string>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
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
    public void LoadConfiguration_UsesOneDeadlineForEveryRoot()
    {
        var now = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        _options.ConfigurationTimeout = TimeSpan.FromSeconds(10);
        ConfigureTwoRoots();
        EnqueueResponse();
        EnqueueResponse();

        CreateLoader(new TestTimeProvider(now)).LoadConfiguration(TestContext.Current.CancellationToken);

        var expected = now.Add(_options.ConfigurationTimeout).DateTime;
        Assert.Equal(2, _calls.Count);
        Assert.All(_calls, call => Assert.Equal(expected, call.Deadline));
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
        EnqueueResponse(("/configuration/n\u00f8kkel", "verd\u00ef"));

        var result = CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Equal("verd\u00ef", result.Configuration["/configuration:n\u00f8kkel"]);
    }

    [Fact]
    public void LoadConfiguration_OnFirstLoad_LogsEveryKey()
    {
        EnqueueResponse(
            ("/configuration/one", "value-one"),
            ("/configuration/two", "value-two"));

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
        EnqueueResponse(("/configuration/one", "value-one"));
        var loader = new EtcdConfigurationLoader(
            _etcdClient.Object,
            new EtcdOptions
            {
                RootKeys = [new EtcdRootKey { RootKey = "/configuration/" }]
            },
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
        EnqueueResponse(("/configuration/one", "value-one"));

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
        EnqueueResponse(("/configuration/one", "value-one"));
        EnqueueResponse(("/configuration/one", "new-value"));
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
            ("/configuration/one", "value-one"),
            ("/configuration/two", "value-two"));
        EnqueueResponse(("/configuration/one", "value-one"));
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
            ("/configuration/one", "value-one"),
            ("/configuration/two", "value-two"));
        EnqueueResponse(
            ("/configuration/two", "value-two"),
            ("/configuration/one", "value-one"));

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
        EnqueueResponse(("/configuration/one", "value-one"));
        EnqueueResponse(
            ("/configuration/one", "value-one"),
            ("/configuration/two", "value-two"));

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
            ("/configuration/one", "value-one"),
            ("/configuration/two", "value-two"));
        EnqueueResponse(("/configuration/one", "value-one"));

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
            ("/configuration/one", "value-one"),
            ("/configuration/two", "value-two"));
        EnqueueResponse(
            ("/configuration/one", "new-value"),
            ("/configuration/two", "value-two"));

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
        EnqueueResponse(("/configuration/one", "value-one"));
        EnqueueResponse(("/configuration/one", string.Empty));

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
            ("/configuration/one", "value-one"),
            ("/configuration/two", "value-two"),
            ("/configuration/three", "value-three"));
        EnqueueResponse(
            ("/configuration/one", "new-value"),
            ("/configuration/two", "value-two"),
            ("/configuration/four", "value-four"));

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
        EnqueueResponse(("/configuration/one", "value-one"));

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
        EnqueueResponse(("/configuration/one", "value-one"));
        EnqueueResponse(("/configuration/one", "value-two"));

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
            ("/configuration/one", "value-one"),
            ("/configuration/two", "value-two"));

        var exception = Assert.ThrowsAny<InvalidOperationException>(
            () => CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken));

        Assert.Contains("MaximumConfigurationKeyCount", exception.Message, StringComparison.Ordinal);
        Assert.Contains("RootKeys", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadConfiguration_WhenKeyCountLimitIsExceededBySkippedKeys_Throws()
    {
        _options.MaximumConfigurationKeyCount = 2;
        EnqueueResponse(
            ("/configuration/one", "value-one"),
            ("/configuration-other/two", "value-two"),
            ("/configuration/three", "value-three"));

        var exception = Assert.Throws<EtcdConfigurationLimitExceededException>(
            () => CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken));

        Assert.Contains("MaximumConfigurationKeyCount", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadConfiguration_WhenKeyCountLimitIsCumulativeAcrossRoots_Throws()
    {
        _options.MaximumConfigurationKeyCount = 3;
        ConfigureTwoRoots();
        EnqueueResponse(
            ("/low/one", "value-one"),
            ("/low-other/skipped", "skipped"));
        EnqueueResponse(
            ("/high/two", "value-two"),
            ("/high/three", "value-three"));

        var exception = Assert.Throws<EtcdConfigurationLimitExceededException>(
            () => CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken));

        Assert.Contains("MaximumConfigurationKeyCount", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadConfiguration_WhenSizeLimitIsExceeded_Throws()
    {
        _options.MaximumConfigurationSizeBytes = 5;
        EnqueueResponse(("/configuration/one", "value-one"));

        var exception = Assert.ThrowsAny<InvalidOperationException>(
            () => CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken));

        Assert.Contains("MaximumConfigurationSizeBytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadConfiguration_WhenSizeLimitIsCumulativeAcrossRoots_Throws()
    {
        _options.MaximumConfigurationSizeBytes = 20;
        ConfigureTwoRoots();
        EnqueueResponse(("/low/one", "value-one"));
        EnqueueResponse(("/high/two", "value-two"));

        var exception = Assert.Throws<EtcdConfigurationLimitExceededException>(
            () => CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken));

        Assert.Contains("MaximumConfigurationSizeBytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadConfiguration_WhenRedactionIsDisabled_StillRedactsSecretLikeKeys()
    {
        _options.RedactConfigurationValues = false;
        EnqueueResponse(
            ("/configuration/database/password", "super-secret"),
            ("/configuration/display-name", "visible"));

        CreateLoader().LoadConfiguration(TestContext.Current.CancellationToken);

        Assert.Contains(
            _logger.Entries,
            entry => entry.Message == "Configuration key /configuration:database:password was added with value ***");
        Assert.Contains(
            _logger.Entries,
            entry => entry.Message == "Configuration key /configuration:display-name was added with value visible");
    }

    private void ConfigureTwoRoots()
    {
        _options.RootKeys =
        [
            new EtcdRootKey { RootKey = "/low/", Priority = 0 },
            new EtcdRootKey { RootKey = "/high/", Priority = 10 }
        ];
    }

    private EtcdConfigurationLoader CreateLoader() => new(_etcdClient.Object, _options, _logger);

    private EtcdConfigurationLoader CreateLoader(TimeProvider timeProvider) =>
        new(_etcdClient.Object, _options, _logger, timeProvider, null);

    private static System.Linq.Expressions.Expression<Func<IEtcdClient, RangeResponse>> GetRangeSetup() =>
        client => client.GetRange(
            It.IsAny<string>(),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>());

    private void EnqueueResponse(params (string Key, string Value)[] values) =>
        _responses.Enqueue(CreateResponse(values));

    private static RangeResponse CreateResponse(params (string Key, string Value)[] values)
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

        return response;
    }

    private sealed record RangeCall(string Root, DateTime? Deadline);

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

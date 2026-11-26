using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using dotnet_etcd.interfaces;
using Etcdserverpb;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Mvccpb;
using ROOT.Etcd.Configuration;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdConfigurationSourceTest
{
    [Fact]
    public void Build_WithInjectedFactory_ReturnsEtcdConfigurationProvider()
    {
        var client = CreateClient(CreateResponse());
        var factory = CreateFactory(client);
        var source = CreateSource(factory.Object);

        var provider = source.Build(new ConfigurationBuilder());

        Assert.IsType<EtcdConfigurationProvider>(provider);
    }

    [Fact]
    public void Build_WithInjectedFactory_LoadsInitialConfiguration()
    {
        var client = CreateClient(CreateResponse(
            ("/configuration/one", "value-one"),
            ("/configuration/two", "value-two")));
        var factory = CreateFactory(client);
        var sharedConfiguration = new EtcdConfiguration();
        var source = CreateSource(sharedConfiguration, factory.Object);

        var provider = source.Build(new ConfigurationBuilder());
        provider.Load();

        Assert.Equal(2, sharedConfiguration.Configuration.Count);
        Assert.Equal("value-one", sharedConfiguration.Configuration["/configuration:one"]);
        Assert.Equal("value-two", sharedConfiguration.Configuration["/configuration:two"]);
        Assert.True(provider.TryGet("/configuration:one", out var firstValue));
        Assert.Equal("value-one", firstValue);
        Assert.True(provider.TryGet("/configuration:two", out var secondValue));
        Assert.Equal("value-two", secondValue);
    }

    [Fact]
    public void Build_WithInjectedFactory_UsesConfiguredRootKeys()
    {
        var options = new EtcdOptions
        {
            RootKeys =
            [
                new EtcdRootKey { RootKey = "/custom-root", Priority = 0 },
                new EtcdRootKey { RootKey = "/other-root", Priority = 10 }
            ]
        };
        var client = new Mock<IEtcdClient>();
        client
            .Setup(GetRangeSetup())
            .Returns(new RangeResponse());
        var factory = CreateFactory(client);
        var source = new EtcdConfigurationSource(options, new EtcdConfiguration(), factory.Object);

        source.Build(new ConfigurationBuilder());

        client.Verify(
            client => client.GetRange(
                "/custom-root",
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        client.Verify(
            client => client.GetRange(
                "/other-root",
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void Build_WithInjectedFactory_ExposesValuesUnderConfigurationPrefix()
    {
        var options = new EtcdOptions
        {
            RootKeys = [new EtcdRootKey { RootKey = "/custom-root" }],
            ConfigurationPrefix = "/app"
        };
        var client = CreateClient(CreateResponse(
            ("/custom-root/one", "value-one")));
        var factory = CreateFactory(client);
        var sharedConfiguration = new EtcdConfiguration();
        var source = new EtcdConfigurationSource(options, sharedConfiguration, factory.Object);

        var provider = source.Build(new ConfigurationBuilder());
        provider.Load();

        Assert.Equal("value-one", sharedConfiguration.Configuration["/app:one"]);
        Assert.True(provider.TryGet("/app:one", out var value));
        Assert.Equal("value-one", value);
        Assert.False(provider.TryGet("/custom-root:one", out _));
    }

    [Fact]
    public void Build_PassesOptionsToClientFactory()
    {
        var options = new EtcdOptions
        {
            EndPoints = "http://etcd:2379",
            RootKeys = [new EtcdRootKey { RootKey = "/custom-root" }]
        };
        var client = CreateClient(CreateResponse());
        var factory = CreateFactory(client);
        var source = new EtcdConfigurationSource(options, new EtcdConfiguration(), factory.Object);

        source.Build(new ConfigurationBuilder());

        factory.Verify(
            factory => factory.CreateClient(It.Is<EtcdOptions>(actual => ReferenceEquals(actual, options))),
            Times.Once);
    }

    [Fact]
    public void Build_WithInjectedFactory_DisposesClientAfterLoading()
    {
        var client = CreateClient(CreateResponse(("/configuration/one", "value-one")));
        var factory = CreateFactory(client);
        var source = CreateSource(factory.Object);

        var provider = source.Build(new ConfigurationBuilder());
        provider.Load();

        Assert.True(provider.TryGet("/configuration:one", out var value));
        Assert.Equal("value-one", value);
        client.Verify(client => client.Dispose(), Times.Once);
    }

    [Fact]
    public void Build_ReturnsProviderThatReceivesLaterConfigurationChanges()
    {
        var client = CreateClient(CreateResponse());
        var factory = CreateFactory(client);
        var sharedConfiguration = new EtcdConfiguration();
        var source = CreateSource(sharedConfiguration, factory.Object);
        var provider = source.Build(new ConfigurationBuilder());

        sharedConfiguration.PublishConfiguration(new Dictionary<string, string?>
        {
            ["/configuration:one"] = "value-one"
        });

        Assert.True(provider.TryGet("/configuration:one", out var value));
        Assert.Equal("value-one", value);
    }

    [Fact]
    public void Build_ReturnsANewProviderForEveryCall()
    {
        var client = CreateClient(CreateResponse());
        var factory = CreateFactory(client);
        var source = CreateSource(factory.Object);
        var builder = new ConfigurationBuilder();

        var first = source.Build(builder);
        var second = source.Build(builder);

        Assert.NotSame(first, second);
        factory.Verify(factory => factory.CreateClient(It.IsAny<EtcdOptions>()), Times.Exactly(2));
        client.Verify(
            client => client.GetRange(
                It.IsAny<string>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        client.Verify(client => client.Dispose(), Times.Exactly(2));
    }

    [Fact]
    public void Build_WhenInjectedClientThrows_PropagatesAndKeepsExistingConfiguration()
    {
        var client = new Mock<IEtcdClient>();
        client
            .Setup(GetRangeSetup())
            .Throws(new InvalidOperationException("etcd is unavailable"));
        var factory = CreateFactory(client);
        var sharedConfiguration = new EtcdConfiguration();
        sharedConfiguration.PublishConfiguration(new Dictionary<string, string?>
        {
            ["/configuration:stale"] = "stale-value"
        });
        var source = CreateSource(sharedConfiguration, factory.Object);

        var exception = Assert.Throws<InvalidOperationException>(
            () => source.Build(new ConfigurationBuilder()));

        Assert.Contains("etcd is unavailable", exception.Message);
        Assert.Equal("stale-value", sharedConfiguration.Configuration["/configuration:stale"]);
        client.Verify(client => client.Dispose(), Times.Once);
    }

    [Fact]
    public void Build_WhenClientFactoryThrows_Propagates()
    {
        var factory = new Mock<IEtcdClientFactory>();
        factory
            .Setup(factory => factory.CreateClient(It.IsAny<EtcdOptions>()))
            .Throws(new InvalidOperationException("client could not be created"));
        var sharedConfiguration = new EtcdConfiguration();
        var source = new EtcdConfigurationSource(new EtcdOptions(), sharedConfiguration, factory.Object);

        var exception = Assert.Throws<InvalidOperationException>(
            () => source.Build(new ConfigurationBuilder()));

        Assert.Contains("client could not be created", exception.Message);
        Assert.Empty(sharedConfiguration.Configuration);
    }
    
    [Fact]
    public void Build_WhenInitialLoadFails_LogsErrorAndRethrows()
    {
        var client = new Mock<IEtcdClient>();
        client
            .Setup(GetRangeSetup())
            .Throws(new InvalidOperationException("etcd is unavailable"));
        var factory = CreateFactory(client);
        var logger = new TestLogger<EtcdConfigurationSource>();
        var source = new EtcdConfigurationSource(
            new EtcdOptions(),
            new EtcdConfiguration(),
            factory.Object,
            logger);

        Assert.Throws<InvalidOperationException>(() => source.Build(new ConfigurationBuilder()));

        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Error &&
                     entry.Message == "Failed to load the initial etcd configuration. The host will not start.");
    }

    private static EtcdConfigurationSource CreateSource(IEtcdClientFactory factory) =>
        CreateSource(new EtcdConfiguration(), factory);

    private static EtcdConfigurationSource CreateSource(
        EtcdConfiguration sharedConfiguration,
        IEtcdClientFactory factory) =>
        new(new EtcdOptions(), sharedConfiguration, factory);

    private static Mock<IEtcdClientFactory> CreateFactory(Mock<IEtcdClient> client)
    {
        var factory = new Mock<IEtcdClientFactory>();
        factory
            .Setup(factory => factory.CreateClient(It.IsAny<EtcdOptions>()))
            .Returns(client.Object);
        return factory;
    }

    private static Mock<IEtcdClient> CreateClient(RangeResponse response)
    {
        var client = new Mock<IEtcdClient>();
        client.Setup(GetRangeSetup()).Returns(response);
        return client;
    }

    private static Expression<Func<IEtcdClient, RangeResponse>> GetRangeSetup() =>
        client => client.GetRange(
            It.IsAny<string>(),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>());

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
}

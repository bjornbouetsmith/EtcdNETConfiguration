using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ROOT.Etcd.Configuration;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdConfigurationTest : IntegrationTestBase
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly string _etcdServer = Environment.GetEnvironmentVariable("ETCD_ENDPOINT") ?? "http://127.0.0.1:2379";

    public EtcdConfigurationTest(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ShouldBeAbleToResolveConfiguration_HostBuilder()
    {
        var containerUp = await StartEtcdContainer(TestContext.Current.CancellationToken);
        Assert.True(containerUp);

        using (var client = CreateClient())
        {
            await client.PutAsync("/configuration/Kafka/BootStrapServers:0", "localhost", cancellationToken: TestContext.Current.CancellationToken);
            await client.PutAsync("/configuration/Kafka/BootStrapServers:1", "otherhost", cancellationToken: TestContext.Current.CancellationToken);
        }
        var builder = Host.CreateDefaultBuilder();
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();

            logging.AddProvider(new XUnitLoggerProvider(_testOutputHelper));
        });

        builder.AddEtcdConfiguration(options =>
        {
            options.EndPoints = _etcdServer;
            options.RefreshInterval = TimeSpan.Zero;
        });
        builder.ConfigureServices((ctx, services) =>
        {
            services.Configure<AppOptions>(ctx.Configuration.GetSection("/configuration"));
            services.AddSingleton<TestService>();
        });

        using var host = builder.Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var options = host.Services.GetRequiredService<IOptions<AppOptions>>();

        _testOutputHelper.WriteLine(JsonSerializer.Serialize(options.Value));
        Assert.NotNull(options.Value.Kafka);
        Assert.NotNull(options.Value.Kafka.BootStrapServers);
        Assert.Equal(2, options.Value.Kafka.BootStrapServers.Count);
        Assert.Equal("localhost", options.Value.Kafka.BootStrapServers[0]);
        Assert.Equal("otherhost", options.Value.Kafka.BootStrapServers[1]);

        var service = host.Services.GetRequiredService<TestService>();
        service.Dump();
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ShouldBeAbleToResolveConfiguration_HostApplicationBuilder()
    {
        var containerUp = await StartEtcdContainer(TestContext.Current.CancellationToken);
        Assert.True(containerUp);

        using (var client = CreateClient())
        {
            await client.PutAsync("/configuration/Kafka/BootStrapServers:0", "localhost", cancellationToken: TestContext.Current.CancellationToken);
            await client.PutAsync("/configuration/Kafka/BootStrapServers:1", "otherhost", cancellationToken: TestContext.Current.CancellationToken);
        }
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new XUnitLoggerProvider(_testOutputHelper));
        

        builder.AddEtcdConfiguration(options =>
        {
            options.EndPoints = _etcdServer;
            options.RefreshInterval = TimeSpan.Zero;
        });

        
        builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("/configuration"));
        builder.Services.AddSingleton<TestService>();
        
        using var host = builder.Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var options = host.Services.GetRequiredService<IOptions<AppOptions>>();

        _testOutputHelper.WriteLine(JsonSerializer.Serialize(options.Value));
        Assert.NotNull(options.Value.Kafka);
        Assert.NotNull(options.Value.Kafka.BootStrapServers);
        Assert.Equal(2, options.Value.Kafka.BootStrapServers.Count);
        Assert.Equal("localhost", options.Value.Kafka.BootStrapServers[0]);
        Assert.Equal("otherhost", options.Value.Kafka.BootStrapServers[1]);

        var service = host.Services.GetRequiredService<TestService>();
        service.Dump();
    }


    [Fact, Trait("Category", "Integration")]
    public async Task InitialConfigurationIsAvailableBeforeStartAndDeletedKeysAreRemoved()
    {
        var containerUp = await StartEtcdContainer(TestContext.Current.CancellationToken);
        Assert.True(containerUp);

        var rootKey = $"/review-{Guid.NewGuid():N}";
        using var client = CreateClient();
        await client.PutAsync($"{rootKey}/one", "value-one", cancellationToken: TestContext.Current.CancellationToken);
        await client.PutAsync($"{rootKey}/two", "value-two", cancellationToken: TestContext.Current.CancellationToken);

        var range1 = await client.GetAsync($"{rootKey}/one", cancellationToken: TestContext.Current.CancellationToken);
        var range2 = await client.GetAsync($"{rootKey}/two", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(range1.Kvs);
        Assert.Single(range2.Kvs);
        try
        {
            var builder = Host.CreateDefaultBuilder();
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new XUnitLoggerProvider(_testOutputHelper));
            });

            builder.AddEtcdConfiguration(options =>
            {
                options.EndPoints = _etcdServer;
                options.RootKeys = [new EtcdRootKey { RootKey = rootKey + "/", Priority = 0 }];
                options.ConfigurationPrefix = rootKey;
                options.RefreshInterval = TimeSpan.FromMilliseconds(100);
            });

            using var host = builder.Build();

            // The shared IEtcdConfiguration must be seeded while the host is built,
            // before any hosted service starts.
            var etcdConfiguration = host.Services.GetRequiredService<IEtcdConfiguration>();
            Assert.Equal("value-one", etcdConfiguration.Configuration[$"{rootKey}:one"]);
            Assert.Equal("value-two", etcdConfiguration.Configuration[$"{rootKey}:two"]);

            await host.StartAsync(TestContext.Current.CancellationToken);

            var configuration = host.Services.GetRequiredService<IConfiguration>();
            Assert.Equal("value-one", configuration[$"{rootKey}:one"]);

            await client.DeleteAsync($"{rootKey}/one", cancellationToken: TestContext.Current.CancellationToken);
            await TestWait.UntilAsync(
                () => configuration[$"{rootKey}:one"] is null,
                "Expected the deleted key to disappear from configuration.");

            Assert.Equal("value-two", configuration[$"{rootKey}:two"]);

            await host.StopAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await client.DeleteRangeAsync(rootKey, cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task DeepNestedOptionsAreBoundCorrectly()
    {
        var containerUp = await StartEtcdContainer(TestContext.Current.CancellationToken);
        Assert.True(containerUp);

        var rootKey = $"/review-{Guid.NewGuid():N}";

        using var client = CreateClient();
        
        await client.PutAsync($"{rootKey}/Kafka/BootStrapServers:0", "localhost", cancellationToken: TestContext.Current.CancellationToken);
        await client.PutAsync($"{rootKey}/Kafka/BootStrapServers:1", "otherhost", cancellationToken: TestContext.Current.CancellationToken);
        await client.PutAsync($"{rootKey}/Kafka/Nested/Value", "NestedValue", cancellationToken: TestContext.Current.CancellationToken);
        await client.PutAsync($"{rootKey}/Kafka/Nested/DeepNested/Value", "DeepNestedValue", cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new XUnitLoggerProvider(_testOutputHelper));


            builder.AddEtcdConfiguration(options =>
            {
                options.EndPoints = _etcdServer;
                options.RootKeys = [new EtcdRootKey { RootKey = rootKey + "/", Priority = 0 }];
                options.ConfigurationPrefix = rootKey;
                options.RefreshInterval = TimeSpan.Zero;
            });
                        
            builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(rootKey));

            using var host = builder.Build();
            
            await host.StartAsync(TestContext.Current.CancellationToken);

            var options = host.Services.GetRequiredService<IOptions<AppOptions>>();

            var appOptions = options.Value;
            Assert.NotNull(appOptions.Kafka);
            Assert.NotNull(appOptions.Kafka.Nested);
            Assert.Equal("NestedValue", appOptions.Kafka.Nested.Value);
            Assert.NotNull(appOptions.Kafka.Nested.DeepNested);
            Assert.Equal("DeepNestedValue", appOptions.Kafka.Nested.DeepNested.Value);

            await host.StopAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await client.DeleteRangeAsync(rootKey, cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task NestedScalarsAndArraysBindThroughIConfiguration()
    {
        var containerUp = await StartEtcdContainer(TestContext.Current.CancellationToken);
        Assert.True(containerUp);

        var rootKey = $"/review-{Guid.NewGuid():N}";

        using var client = CreateClient();
        await client.PutAsync($"{rootKey}/App/Name", "demo", cancellationToken: TestContext.Current.CancellationToken);
        await client.PutAsync($"{rootKey}/App/Servers:0", "one", cancellationToken: TestContext.Current.CancellationToken);
        await client.PutAsync($"{rootKey}/App/Servers:1", "two", cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new XUnitLoggerProvider(_testOutputHelper));

            builder.AddEtcdConfiguration(options =>
            {
                options.EndPoints = _etcdServer;
                options.RootKeys = [new EtcdRootKey { RootKey = rootKey + "/", Priority = 0 }];
                options.ConfigurationPrefix = rootKey;
                options.RefreshInterval = TimeSpan.Zero;
            });

            using var host = builder.Build();

            await host.StartAsync(TestContext.Current.CancellationToken);

            var configuration = host.Services.GetRequiredService<IConfiguration>();
            Assert.Equal("demo", configuration[$"{rootKey}:App:Name"]);
            Assert.Equal("one", configuration[$"{rootKey}:App:Servers:0"]);
            Assert.Equal("two", configuration[$"{rootKey}:App:Servers:1"]);

            await host.StopAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await client.DeleteRangeAsync($"{rootKey}/App/Name", cancellationToken: TestContext.Current.CancellationToken);
            await client.DeleteRangeAsync($"{rootKey}/App/Servers:0", cancellationToken: TestContext.Current.CancellationToken);
            await client.DeleteRangeAsync($"{rootKey}/App/Servers:1", cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task KeysOutsideRootKeyAreNotLoaded()
    {
        var containerUp = await StartEtcdContainer(TestContext.Current.CancellationToken);
        Assert.True(containerUp);

        var rootKey = $"/app-{Guid.NewGuid():N}";
        var siblingRootKey = $"{rootKey}lication";
        var legacyKey = $"{rootKey}:legacy";

        using var client = CreateClient();
        await client.PutAsync($"{rootKey}/included", "included-value", cancellationToken: TestContext.Current.CancellationToken);
        await client.PutAsync($"{siblingRootKey}/excluded", "excluded-value", cancellationToken: TestContext.Current.CancellationToken);
        await client.PutAsync(legacyKey, "legacy-value", cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new XUnitLoggerProvider(_testOutputHelper));

            builder.AddEtcdConfiguration(options =>
            {
                options.EndPoints = _etcdServer;
                options.RootKeys = [new EtcdRootKey { RootKey = rootKey + "/", Priority = 0 }];
                options.ConfigurationPrefix = rootKey;
                options.RefreshInterval = TimeSpan.Zero;
            });

            using var host = builder.Build();

            await host.StartAsync(TestContext.Current.CancellationToken);

            var configuration = host.Services.GetRequiredService<IConfiguration>();
            Assert.Equal("included-value", configuration[$"{rootKey}:included"]);
            Assert.Null(configuration[$"{siblingRootKey}:excluded"]);
            Assert.Null(configuration[$"{rootKey}:legacy"]);

            await host.StopAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await client.DeleteRangeAsync($"{rootKey}/included", cancellationToken: TestContext.Current.CancellationToken);
            await client.DeleteRangeAsync($"{siblingRootKey}/excluded", cancellationToken: TestContext.Current.CancellationToken);
            await client.DeleteRangeAsync(legacyKey, cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task MultipleRootsMergeByPriorityAndFallBackWhenTheWinnerIsDeleted()
    {
        var containerUp = await StartEtcdContainer(TestContext.Current.CancellationToken);
        Assert.True(containerUp);

        var domainRoot = $"/domain-{Guid.NewGuid():N}";
        var machineRoot = $"/machine-{Guid.NewGuid():N}";
        var serviceRoot = $"/service-{Guid.NewGuid():N}";
        var configurationPrefix = $"/merged-{Guid.NewGuid():N}";

        using var client = CreateClient();
        await client.PutAsync($"{domainRoot}/Logging/Level", "Debug", cancellationToken: TestContext.Current.CancellationToken);
        await client.PutAsync($"{domainRoot}/Database/Name", "domain-db", cancellationToken: TestContext.Current.CancellationToken);
        await client.PutAsync($"{machineRoot}/Logging/Level", "Warning", cancellationToken: TestContext.Current.CancellationToken);
        await client.PutAsync($"{machineRoot}/Kafka/BootStrapServers:0", "host-1", cancellationToken: TestContext.Current.CancellationToken);
        await client.PutAsync($"{serviceRoot}/Database/Name", "checkout", cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new XUnitLoggerProvider(_testOutputHelper));

            builder.AddEtcdConfiguration(options =>
            {
                options.EndPoints = _etcdServer;
                options.RootKeys =
                [
                    new EtcdRootKey { RootKey = domainRoot + "/", Priority = 0 },
                    new EtcdRootKey { RootKey = machineRoot + "/", Priority = 10 },
                    new EtcdRootKey { RootKey = serviceRoot + "/", Priority = 20 }
                ];
                options.ConfigurationPrefix = configurationPrefix;
                options.RefreshInterval = TimeSpan.FromMilliseconds(100);
            });

            using var host = builder.Build();
            await host.StartAsync(TestContext.Current.CancellationToken);

            var configuration = host.Services.GetRequiredService<IConfiguration>();

            Assert.Equal("Warning", configuration[$"{configurationPrefix}:Logging:Level"]);
            Assert.Equal("checkout", configuration[$"{configurationPrefix}:Database:Name"]);
            Assert.Equal("host-1", configuration[$"{configurationPrefix}:Kafka:BootStrapServers:0"]);

            // A higher-priority change is picked up by polling.
            await client.PutAsync(
                $"{machineRoot}/Logging/Level",
                "Error",
                cancellationToken: TestContext.Current.CancellationToken);
            await TestWait.UntilAsync(
                () => configuration[$"{configurationPrefix}:Logging:Level"] == "Error",
                "Expected the higher-priority value to win after polling.");

            // Removing the winner falls back to the next priority.
            await client.DeleteAsync(
                $"{serviceRoot}/Database/Name",
                cancellationToken: TestContext.Current.CancellationToken);
            await TestWait.UntilAsync(
                () => configuration[$"{configurationPrefix}:Database:Name"] == "domain-db",
                "Expected the lower-priority value to become effective after deletion.");

            // Deleting every copy removes the effective key.
            await client.DeleteAsync(
                $"{machineRoot}/Kafka/BootStrapServers:0",
                cancellationToken: TestContext.Current.CancellationToken);
            await TestWait.UntilAsync(
                () => configuration[$"{configurationPrefix}:Kafka:BootStrapServers:0"] is null,
                "Expected the key to disappear when no root provides it.");

            await host.StopAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await client.DeleteRangeAsync(domainRoot, cancellationToken: TestContext.Current.CancellationToken);
            await client.DeleteRangeAsync(machineRoot, cancellationToken: TestContext.Current.CancellationToken);
            await client.DeleteRangeAsync(serviceRoot, cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task InvalidKeyFormatInsideARootFailsTheInitialLoad()
    {
        var containerUp = await StartEtcdContainer(TestContext.Current.CancellationToken);
        Assert.True(containerUp);

        var rootKey = $"/invalid-format-{Guid.NewGuid():N}";
        var invalidKey = $"{rootKey}/Database:Host";
        using var client = CreateClient();
        await client.PutAsync(invalidKey, "localhost", cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new XUnitLoggerProvider(_testOutputHelper));

            Assert.Throws<EtcdConfigurationKeyFormatException>(() =>
                builder.AddEtcdConfiguration(options =>
                {
                    options.EndPoints = _etcdServer;
                    options.RootKeys = [new EtcdRootKey { RootKey = rootKey + "/", Priority = 0 }];
                    options.ConfigurationPrefix = rootKey;
                    options.RefreshInterval = TimeSpan.Zero;
                }));
        }
        finally
        {
            await client.DeleteRangeAsync(invalidKey, cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    public class TestService
    {
        private readonly ILogger<TestService> _logger;
        private readonly AppOptions _options;

        public TestService(IOptions<AppOptions> options, ILogger<TestService> logger)
        {
            _logger = logger;
            _options = options.Value;
        }

        public void Dump()
        {
            _logger.LogInformation("Configuration: {@Configuration}", JsonSerializer.Serialize(_options));
        }
    }

    public class AppOptions
    {
        public KafkaOptions? Kafka { get; set; }
    }

    public class KafkaOptions
    {
        public required List<string> BootStrapServers { get; set; }
        
        public NestedOptions? Nested { get; set; }
    }

    public class NestedOptions
    {
        public required string Value { get; set; }
        public DeepNestedOptions? DeepNested { get; set; }
    }
    public class DeepNestedOptions
    {
        public required string Value { get; set; }
    }
}

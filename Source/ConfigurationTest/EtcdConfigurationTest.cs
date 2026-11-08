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
            await client.PutAsync("/configuration:Kafka:BootStrapServers:0", "localhost", cancellationToken: TestContext.Current.CancellationToken);
            await client.PutAsync("/configuration:Kafka:BootStrapServers:1", "otherhost", cancellationToken: TestContext.Current.CancellationToken);
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
            await client.PutAsync("/configuration:Kafka:BootStrapServers:0", "localhost", cancellationToken: TestContext.Current.CancellationToken);
            await client.PutAsync("/configuration:Kafka:BootStrapServers:1", "otherhost", cancellationToken: TestContext.Current.CancellationToken);
        }
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new XUnitLoggerProvider(_testOutputHelper));
        

        builder.AddEtcdConfiguration(options =>
        {
            options.EndPoints = _etcdServer;
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
        await client.PutAsync($"{rootKey}:one", "value-one", cancellationToken: TestContext.Current.CancellationToken);
        await client.PutAsync($"{rootKey}:two", "value-two", cancellationToken: TestContext.Current.CancellationToken);

        var range1 = await client.GetAsync($"{rootKey}:one", cancellationToken: TestContext.Current.CancellationToken);
        var range2 = await client.GetAsync($"{rootKey}:two", cancellationToken: TestContext.Current.CancellationToken);
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
                options.RootKey = rootKey;
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

            await client.DeleteAsync($"{rootKey}:one", cancellationToken: TestContext.Current.CancellationToken);
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
    }
}

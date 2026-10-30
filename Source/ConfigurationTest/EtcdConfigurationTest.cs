using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ROOT.Etcd.Configuration;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdConfigurationTest : TestBase
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly string _etcdServer = Environment.GetEnvironmentVariable("ETCD_ENDPOINT") ?? "http://127.0.0.1:2379";

    public EtcdConfigurationTest(ITestOutputHelper testOutputHelper) 
        : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task ShouldBeAbleToResolveConfiguration() 
    {
        var containerUp = await StartEtcdContainer();
        Assert.True(containerUp);

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
        builder.ConfigureServices((ctx,services) => 
        {
            services.Configure<AppOptions>(ctx.Configuration.GetSection("/configuration"));
            services.AddSingleton<TestClass>();
        });

        using var host = builder.Build();
     
        await host.StartAsync();
                
        var options = host.Services.GetRequiredService<IOptions<AppOptions>>();
        
        _testOutputHelper.WriteLine(JsonSerializer.Serialize(options.Value));

        var service = host.Services.GetRequiredService<TestClass>();
        service.Dump();
    }

    public class TestClass
    {
        private readonly ILogger<TestClass> _logger;
        private readonly AppOptions _options;

        public TestClass(IOptions<AppOptions> options, ILogger<TestClass> logger)
        {
            _logger = logger;
            _options = options.Value;
        }

        public void Dump() 
        {
            _logger.LogInformation(JsonSerializer.Serialize(_options));
        }
    }
}

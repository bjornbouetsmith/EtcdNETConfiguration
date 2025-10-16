using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ROOT.Etcd.Configuration;
using Xunit.Abstractions;

namespace ROOT.Etcd.ConfigurationTest;

public class AppOptions
{
    public KafkaOptions Kafka { get; set; }
}

public class KafkaOptions
{
    public List<string> BootStrapServers { get; set; }

}

public class UnitTest1 : TestBase
{
    private readonly ITestOutputHelper _testOutputHelper;
    
 

    public UnitTest1(ITestOutputHelper testOutputHelper) : base(testOutputHelper) 
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task GetConfiguration_ShouldReadFromEtcs() 
    {
        //Trace.Listeners.Add(new TraceLogger(new XUnitLoggerProvider.XUnitLogger("Trace", _testOutputHelper)));
        var containerUp = await StartEtcdContainer();
        Assert.True(containerUp);
        Trace.WriteLine("Creating client");
        using (var client = CreateClient())
        {
            await client.PutAsync("/configuration:Kafka:BootStrapServers:0", "localhost");
            await client.PutAsync("/configuration:Kafka:BootStrapServers:1", "otherhost");

            Trace.TraceInformation("Test loading configuration from Etcd");
            var range = await client.GetRangeAsync("/configuration");
            Trace.WriteLine($"Keys found  {range.Kvs.Count}");
        }

        var builder = Host.CreateDefaultBuilder();
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();

            logging.AddProvider(new XUnitLoggerProvider(_testOutputHelper));
        });


        builder.AddEtcdConfiguration(options => 
        { 
            options.EndPoints = EtcdServer;
            //options.RootKey = "configuration";
        });

        using var host = builder.Build();


        await host.StartAsync();

        var configuration = host.Services.GetRequiredService<IConfiguration>();


        var section = configuration.GetSection("/configuration");
        var options = new AppOptions();
        configuration.Bind("/configuration", options);
        Assert.NotNull(section);
        Trace.WriteLine(JsonSerializer.Serialize(options));
    }

    [Fact]
    public async Task Test1()
    {
        //Trace.Listeners.Add(new TraceLogger(new XUnitLoggerProvider.XUnitLogger("Trace", _testOutputHelper)));
        var containerUp = await StartEtcdContainer();
        Assert.True(containerUp);
        using var client = CreateClient();
        var builder = Host.CreateDefaultBuilder();
        var options = JsonSerializer.Serialize(new AppOptions { Kafka = new KafkaOptions { BootStrapServers = ["localhost", "otherhost"] } });
        _testOutputHelper.WriteLine(options);



        await client.PutAsync("/configuration/etcd_settings.json", options);

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();

            logging.AddProvider(new XUnitLoggerProvider(_testOutputHelper));
        });


        builder.AddEtcdConfiguration(options =>
        {
            options.EndPoints = EtcdServer;
        });

        using var host = builder.Build();

            


        await host.StartAsync();

    }

    
}
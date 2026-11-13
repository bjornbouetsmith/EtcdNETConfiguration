using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ROOT.Etcd.Configuration;
using System;
using System.Threading.Tasks;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdTlsConfigurationTest : IntegrationTestBase
{
    private readonly ITestOutputHelper _testOutputHelper;

    public EtcdTlsConfigurationTest(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ShouldConnectAndLoadConfigurationOverTls_WhenInsecureChannelsAreDisabled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var containerUp = await StartTlsEtcdContainer(cancellationToken);
        Assert.True(
            containerUp,
            "Could not start the TLS-enabled etcd container. Run 'docker compose -f docker-compose.tls.yml up -d' from the Source directory, or set ETCD_TLS_ENDPOINT and ETCD_TLS_CA_CERT.");

        var rootKey = $"/tls-{Guid.NewGuid():N}";

        using var client = CreateTlsClient();
        await client.PutAsync($"{rootKey}:Kafka:BootStrapServers:0", "tls-host-1", cancellationToken: cancellationToken);
        await client.PutAsync($"{rootKey}:Kafka:BootStrapServers:1", "tls-host-2", cancellationToken: cancellationToken);

        var builder = Host.CreateDefaultBuilder();
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(new XUnitLoggerProvider(_testOutputHelper));
        });

        builder.AddEtcdConfiguration(options =>
        {
            options.EndPoints = TlsEtcdServer;
            options.RootKey = rootKey;
            options.UseInsecureChannels = false;
            options.ServerCertificateAuthorityPath = CaCertificatePath;
            options.RefreshInterval = TimeSpan.Zero;
        });

        using var host = builder.Build();
        await host.StartAsync(cancellationToken);

        try
        {
            var configuration = host.Services.GetRequiredService<IConfiguration>();
            Assert.Equal("tls-host-1", configuration[$"{rootKey}:Kafka:BootStrapServers:0"]);
            Assert.Equal("tls-host-2", configuration[$"{rootKey}:Kafka:BootStrapServers:1"]);

            // Read the values back through the client too, proving the TLS channel
            // can perform both writes and reads when InsecureChannels is disabled.
            var range = await client.GetAsync($"{rootKey}:Kafka:BootStrapServers:0", cancellationToken: cancellationToken);
            var stored = Assert.Single(range.Kvs);
            Assert.Equal("tls-host-1", stored.Value.ToStringUtf8());

            await host.StopAsync(cancellationToken);
        }
        finally
        {
            await client.DeleteRangeAsync(rootKey, cancellationToken: cancellationToken);
        }
    }
}

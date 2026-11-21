using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ROOT.Etcd.Configuration;
using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdMtlsConfigurationTest : IntegrationTestBase
{
    private readonly ITestOutputHelper _testOutputHelper;

    public EtcdMtlsConfigurationTest(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ShouldConnectAndLoadConfigurationOverMtls_WhenClientCertificateFilesAreUsed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var containerUp = await StartMtlsEtcdContainer(cancellationToken);
        Assert.True(
            containerUp,
            "Could not start the mTLS-enabled etcd container. Run 'docker compose -f docker-compose.mtls.yml up -d' from the Source directory, or set ETCD_MTLS_ENDPOINT and the client certificate variables.");

        var rootKey = $"/mtls-{Guid.NewGuid():N}";

        using (var client = CreateMtlsClient())
        {
            await client.PutAsync(
                $"{rootKey}:Kafka:BootStrapServers:0",
                "mtls-host-1",
                cancellationToken: cancellationToken);
            await client.PutAsync(
                $"{rootKey}:Kafka:BootStrapServers:1",
                "mtls-host-2",
                cancellationToken: cancellationToken);
        }

        var builder = Host.CreateDefaultBuilder();
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(new XUnitLoggerProvider(_testOutputHelper));
        });

        builder.AddEtcdConfiguration(options =>
        {
            options.EndPoints = MtlsEtcdServer;
            options.RootKey = rootKey;
            options.SslOptions = new SslOptions
            {
                UseSsl = true,
                ServerCertificateAuthorityPath = CaCertificatePath,
                ClientCertificatePath = ClientCertificatePath,
                ClientCertificateKeyPath = ClientCertificateKeyPath
            };
            options.RefreshInterval = TimeSpan.Zero;
        });

        using var host = builder.Build();
        await host.StartAsync(cancellationToken);

        try
        {
            var configuration = host.Services.GetRequiredService<IConfiguration>();
            Assert.Equal("mtls-host-1", configuration[$"{rootKey}:Kafka:BootStrapServers:0"]);
            Assert.Equal("mtls-host-2", configuration[$"{rootKey}:Kafka:BootStrapServers:1"]);

            await host.StopAsync(cancellationToken);
        }
        finally
        {
            using var client = CreateMtlsClient();
            await client.DeleteRangeAsync(rootKey, cancellationToken: cancellationToken);
        }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ShouldConnectAndLoadConfigurationOverMtls_WhenInMemoryCertificateIsUsed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var containerUp = await StartMtlsEtcdContainer(cancellationToken);
        Assert.True(
            containerUp,
            "Could not start the mTLS-enabled etcd container. Run 'docker compose -f docker-compose.mtls.yml up -d' from the Source directory, or set ETCD_MTLS_ENDPOINT and the client certificate variables.");

        var rootKey = $"/mtls-in-memory-{Guid.NewGuid():N}";

        using var certificate = EtcdTlsCertificateLoader.LoadClientCertificate(new SslOptions
        {
            ClientCertificatePath = ClientCertificatePath,
            ClientCertificateKeyPath = ClientCertificateKeyPath
        });

        using (var client = CreateMtlsClient(certificate))
        {
            await client.PutAsync(
                $"{rootKey}:Kafka:BootStrapServers:0",
                "in-memory-host",
                cancellationToken: cancellationToken);
        }

        var builder = Host.CreateDefaultBuilder();
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(new XUnitLoggerProvider(_testOutputHelper));
        });

        builder.AddEtcdConfiguration(options =>
        {
            options.EndPoints = MtlsEtcdServer;
            options.RootKey = rootKey;
            options.SslOptions = new SslOptions
            {
                UseSsl = true,
                ServerCertificateAuthorityPath = CaCertificatePath,
                ClientCertificate = certificate
            };
            options.RefreshInterval = TimeSpan.Zero;
        });

        using var host = builder.Build();
        await host.StartAsync(cancellationToken);

        try
        {
            var configuration = host.Services.GetRequiredService<IConfiguration>();
            Assert.Equal("in-memory-host", configuration[$"{rootKey}:Kafka:BootStrapServers:0"]);

            await host.StopAsync(cancellationToken);
        }
        finally
        {
            using var client = CreateMtlsClient(certificate);
            await client.DeleteRangeAsync(rootKey, cancellationToken: cancellationToken);
        }
    }
}

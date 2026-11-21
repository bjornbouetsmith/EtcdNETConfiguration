using Microsoft.Extensions.Options;
using ROOT.Etcd.Configuration;
using System;
using System.IO;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdMtlsNegativeTest : IntegrationTestBase
{
    private readonly ITestOutputHelper _testOutputHelper;

    public EtcdMtlsNegativeTest(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ShouldAcceptTrustedClientCertificate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await StartMtlsAsync(cancellationToken);

        using var clientCertificate = LoadClientCertificate(ClientCertificatePath, ClientCertificateKeyPath);
        using var handler = CreateHandler(clientCertificate);
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

        using var response = await client.GetAsync($"{MtlsEtcdServer}/health", cancellationToken);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ShouldRejectConnectionWithoutClientCertificate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await StartMtlsAsync(cancellationToken);

        await AssertClientAuthenticationFailsAsync(null, null, cancellationToken);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ShouldRejectClientCertificateFromUntrustedCa()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await StartMtlsAsync(cancellationToken);

        await AssertClientAuthenticationFailsAsync(
            UntrustedClientCertificatePath,
            UntrustedClientKeyPath,
            cancellationToken);
    }

    [Fact, Trait("Category", "Integration")]
    public void ShouldRejectClientCertificateWithoutClientAuthEkuBeforeConnecting()
    {
        // etcd itself accepts a client certificate without clientAuth EKU, so the
        // library enforces this requirement before opening the channel.
        var options = new EtcdOptions
        {
            EndPoints = MtlsEtcdServer,
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ServerCertificateAuthorityPath = CaCertificatePath,
                ClientCertificatePath = NoClientAuthEkuCertificatePath,
                ClientCertificateKeyPath = NoClientAuthEkuKeyPath
            }
        };

        var exception = Assert.Throws<OptionsValidationException>(
            () => new EtcdClientFactory().CreateClient(options));

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains("clientAuth", StringComparison.Ordinal));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ShouldRejectExpiredClientCertificate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await StartMtlsAsync(cancellationToken);

        await AssertClientAuthenticationFailsAsync(
            ExpiredClientCertificatePath,
            ExpiredClientKeyPath,
            cancellationToken);
    }

    private async Task StartMtlsAsync(CancellationToken cancellationToken)
    {
        Assert.SkipWhen(
            IsExternalMtlsEndpoint,
            "Negative certificate variants depend on the local test CA and are only run against docker-compose.mtls.yml.");

        var containerUp = await StartMtlsEtcdContainer(cancellationToken);
        Assert.True(
            containerUp,
            "Could not start the mTLS-enabled etcd container. Run 'docker compose -f docker-compose.mtls.yml up -d' from the Source directory.");
    }

    private async Task AssertClientAuthenticationFailsAsync(
        string? certificatePath,
        string? keyPath,
        CancellationToken cancellationToken)
    {
        X509Certificate2? clientCertificate = null;
        try
        {
            if (certificatePath is not null)
            {
                clientCertificate = LoadClientCertificate(certificatePath, keyPath);
            }

            using var handler = CreateHandler(clientCertificate);
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

            var exception = await Assert.ThrowsAsync<HttpRequestException>(
                () => client.GetAsync($"{MtlsEtcdServer}/health", cancellationToken));

            _testOutputHelper.WriteLine(exception.ToString());
            Assert.True(
                IsTlsClientAuthenticationFailure(exception),
                $"Expected a TLS client-authentication failure, but got: {exception}");
        }
        finally
        {
            clientCertificate?.Dispose();
        }
    }

    private static SocketsHttpHandler CreateHandler(X509Certificate2? clientCertificate)
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                // These tests verify client authentication, not server trust.
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            }
        };

        if (clientCertificate is not null)
        {
            handler.SslOptions.ClientCertificates = new X509CertificateCollection { clientCertificate };
        }

        return handler;
    }

    private static X509Certificate2 LoadClientCertificate(string certificatePath, string? keyPath)
    {
        return EtcdTlsCertificateLoader.LoadClientCertificate(new SslOptions
        {
            ClientCertificatePath = certificatePath,
            ClientCertificateKeyPath = keyPath
        });
    }

    private static bool IsTlsClientAuthenticationFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException || current is IOException)
            {
                return true;
            }

            if (current.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("TLS", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("handshake", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

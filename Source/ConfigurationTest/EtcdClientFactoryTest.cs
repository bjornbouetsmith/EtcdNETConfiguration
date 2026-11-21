using Grpc.Core;
using System;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using ROOT.Etcd.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdClientFactoryTest
{
    private const string HttpsEndpoint = "https://127.0.0.1:2379";

    [Fact]
    public void ConfigureChannel_WhenSslIsDisabled_UsesInsecureCredentials()
    {
        var channelOptions = new GrpcChannelOptions();

        EtcdClientFactory.ConfigureChannel(
            new EtcdOptions { SslOptions = new SslOptions { UseSsl = false } },
            channelOptions);

        Assert.Same(ChannelCredentials.Insecure, channelOptions.Credentials);
        Assert.Null(channelOptions.HttpHandler);
    }

    [Fact]
    public void ConfigureChannel_WhenSslOptionsAreNotProvided_UsesInsecureCredentials()
    {
        var channelOptions = new GrpcChannelOptions();

        EtcdClientFactory.ConfigureChannel(new EtcdOptions(), channelOptions);

        Assert.Same(ChannelCredentials.Insecure, channelOptions.Credentials);
        Assert.Null(channelOptions.HttpHandler);
    }

    [Fact]
    public void ConfigureChannel_WhenSslIsEnabled_UsesSecureCredentials()
    {
        var channelOptions = new GrpcChannelOptions();

        EtcdClientFactory.ConfigureChannel(
            new EtcdOptions
            {
                EndPoints = "https://127.0.0.1:2379",
                SslOptions = new SslOptions { UseSsl = true }
            },
            channelOptions);

        Assert.Same(ChannelCredentials.SecureSsl, channelOptions.Credentials);
        Assert.NotNull(GetSocketsHandler(channelOptions));
        Assert.True(channelOptions.DisposeHttpClient);
    }

    [Fact]
    public void ConfigureChannel_WhenCustomCaIsProvided_InstallsTlsHttpHandler()
    {
        var channelOptions = new GrpcChannelOptions();

        EtcdClientFactory.ConfigureChannel(
            new EtcdOptions
            {
                EndPoints = "https://127.0.0.1:2379",
                SslOptions = new SslOptions
                {
                    UseSsl = true,
                    ServerCertificateAuthorityPath = GetCaCertificatePath()
                }
            },
            channelOptions);

        Assert.Same(ChannelCredentials.SecureSsl, channelOptions.Credentials);
        Assert.NotNull(GetSocketsHandler(channelOptions));
    }

    [Fact]
    public void ConfigureChannel_WhenSslIsDisabledWithTlsSettings_Throws()
    {
        var channelOptions = new GrpcChannelOptions();

        Assert.Throws<OptionsValidationException>(() =>
            EtcdClientFactory.ConfigureChannel(
                new EtcdOptions
                {
                    EndPoints = "http://127.0.0.1:2379",
                    SslOptions = new SslOptions
                    {
                        UseSsl = false,
                        ServerCertificateAuthorityPath = GetCaCertificatePath()
                    }
                },
                channelOptions));
    }

    [Fact]
    public void CreateClient_WithInvalidTlsOptions_Throws()
    {
        var options = new EtcdOptions
        {
            EndPoints = "http://127.0.0.1:2379",
            SslOptions = new SslOptions { UseSsl = true }
        };

        Assert.Throws<OptionsValidationException>(() => new EtcdClientFactory().CreateClient(options));
    }

    [Fact]
    public void ConfigureChannel_WhenInMemoryClientCertificateIsProvided_InstallsTlsHttpHandler()
    {
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate();
        var channelOptions = new GrpcChannelOptions();

        EtcdClientFactory.ConfigureChannel(
            new EtcdOptions
            {
                EndPoints = HttpsEndpoint,
                SslOptions = new SslOptions
                {
                    UseSsl = true,
                    ClientCertificate = certificate
                }
            },
            channelOptions);

        Assert.Same(ChannelCredentials.SecureSsl, channelOptions.Credentials);
        Assert.True(channelOptions.DisposeHttpClient);

        var handler = GetSocketsHandler(channelOptions);
        var installedCertificate = Assert.Single(
            handler.SslOptions.ClientCertificates!.Cast<X509Certificate2>());
        Assert.Same(certificate, installedCertificate);
    }

    [Fact]
    public void ConfigureChannel_WhenClientCertificateFileIsProvided_InstallsTlsHttpHandler()
    {
        using var files = new TestCertificateFiles();
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate();
        var certificatePath = TestCertificateFactory.WriteCertificatePem(files, "client.crt", certificate);
        var keyPath = TestCertificateFactory.WritePrivateKeyPem(files, "client.key", certificate);
        var channelOptions = new GrpcChannelOptions();

        EtcdClientFactory.ConfigureChannel(
            new EtcdOptions
            {
                EndPoints = HttpsEndpoint,
                SslOptions = new SslOptions
                {
                    UseSsl = true,
                    ClientCertificatePath = certificatePath,
                    ClientCertificateKeyPath = keyPath
                }
            },
            channelOptions);

        var handler = GetSocketsHandler(channelOptions);
        var installedCertificate = Assert.Single(
            handler.SslOptions.ClientCertificates!.Cast<X509Certificate2>());
        Assert.True(installedCertificate.HasPrivateKey);
    }

    [Fact]
    public void ConfigureChannel_WhenCaAndClientCertificateAreProvided_ConfiguresBoth()
    {
        using var files = new TestCertificateFiles();
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate();
        var certificatePath = TestCertificateFactory.WriteCertificatePem(files, "client.crt", certificate);
        var keyPath = TestCertificateFactory.WritePrivateKeyPem(files, "client.key", certificate);
        var channelOptions = new GrpcChannelOptions();

        EtcdClientFactory.ConfigureChannel(
            new EtcdOptions
            {
                EndPoints = HttpsEndpoint,
                SslOptions = new SslOptions
                {
                    UseSsl = true,
                    ServerCertificateAuthorityPath = GetCaCertificatePath(),
                    ClientCertificatePath = certificatePath,
                    ClientCertificateKeyPath = keyPath
                }
            },
            channelOptions);

        var handler = GetSocketsHandler(channelOptions);
        Assert.Single(handler.SslOptions.ClientCertificates!.Cast<X509Certificate2>());
        Assert.NotNull(handler.SslOptions.CertificateChainPolicy);
    }

    [Fact]
    public void ConfigureChannel_WhenCustomCaIsProvided_UsesExclusiveCustomRootTrustAndServerAuth()
    {
        using var caCertificate = X509CertificateLoader.LoadCertificateFromFile(GetCaCertificatePath());
        var channelOptions = new GrpcChannelOptions();

        EtcdClientFactory.ConfigureChannel(
            new EtcdOptions
            {
                EndPoints = HttpsEndpoint,
                SslOptions = new SslOptions
                {
                    UseSsl = true,
                    ServerCertificateAuthorityPath = GetCaCertificatePath(),
                    RevocationMode = X509RevocationMode.Offline
                }
            },
            channelOptions);

        var handler = GetSocketsHandler(channelOptions);

        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);

        var policy = Assert.IsType<X509ChainPolicy>(handler.SslOptions.CertificateChainPolicy);
        Assert.Equal(X509ChainTrustMode.CustomRootTrust, policy.TrustMode);
        Assert.Equal(X509RevocationMode.Offline, policy.RevocationMode);
        Assert.True(policy.DisableCertificateDownloads);

        var trustedCertificate = Assert.Single(policy.CustomTrustStore.Cast<X509Certificate2>());
        Assert.Equal(caCertificate.Thumbprint, trustedCertificate.Thumbprint);

        Assert.Contains(
            policy.ApplicationPolicy.Cast<Oid>(),
            oid => oid.Value == "1.3.6.1.5.5.7.3.1");
    }

    [Fact]
    public void ConfigureChannel_WhenCaBundleIsProvided_TrustsEveryCertificate()
    {
        using var files = new TestCertificateFiles();
        using var firstCertificateAuthority = TestCertificateFactory.CreateCertificateAuthority();
        using var secondCertificateAuthority = TestCertificateFactory.CreateCertificateAuthority();
        var bundlePath = files.PathFor("ca-bundle.crt");
        File.WriteAllText(
            bundlePath,
            firstCertificateAuthority.ExportCertificatePem() +
            Environment.NewLine +
            secondCertificateAuthority.ExportCertificatePem());
        var channelOptions = new GrpcChannelOptions();

        EtcdClientFactory.ConfigureChannel(
            new EtcdOptions
            {
                EndPoints = HttpsEndpoint,
                SslOptions = new SslOptions
                {
                    UseSsl = true,
                    ServerCertificateAuthorityPath = bundlePath
                }
            },
            channelOptions);

        var handler = GetSocketsHandler(channelOptions);
        var policy = Assert.IsType<X509ChainPolicy>(handler.SslOptions.CertificateChainPolicy);
        Assert.Equal(2, policy.CustomTrustStore.Count);
    }

    [Fact]
    public void ConfigureChannel_ByDefault_DisablesTheSystemProxy()
    {
        var channelOptions = new GrpcChannelOptions();

        EtcdClientFactory.ConfigureChannel(
            new EtcdOptions
            {
                EndPoints = HttpsEndpoint,
                SslOptions = new SslOptions
                {
                    UseSsl = true,
                    ServerCertificateAuthorityPath = GetCaCertificatePath()
                }
            },
            channelOptions);

        Assert.False(GetSocketsHandler(channelOptions).UseProxy);
    }

    [Fact]
    public void ConfigureChannel_WhenProxyIsEnabled_UsesTheSystemProxy()
    {
        var channelOptions = new GrpcChannelOptions();

        EtcdClientFactory.ConfigureChannel(
            new EtcdOptions
            {
                EndPoints = HttpsEndpoint,
                SslOptions = new SslOptions
                {
                    UseSsl = true,
                    ServerCertificateAuthorityPath = GetCaCertificatePath(),
                    UseProxy = true
                }
            },
            channelOptions);

        Assert.True(GetSocketsHandler(channelOptions).UseProxy);
    }

    [Fact]
    public void ConfigureChannel_WhenPfxClientCertificateIsProvided_LoadsCertificate()
    {
        using var files = new TestCertificateFiles();
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate();
        var certificatePath = TestCertificateFactory.WritePkcs12(files, "client.pfx", certificate, "password");
        var channelOptions = new GrpcChannelOptions();

        EtcdClientFactory.ConfigureChannel(
            new EtcdOptions
            {
                EndPoints = HttpsEndpoint,
                SslOptions = new SslOptions
                {
                    UseSsl = true,
                    ClientCertificatePath = certificatePath,
                    ClientCertificatePassword = "password"
                }
            },
            channelOptions);

        var handler = GetSocketsHandler(channelOptions);
        var installedCertificate = Assert.Single(
            handler.SslOptions.ClientCertificates!.Cast<X509Certificate2>());
        Assert.True(installedCertificate.HasPrivateKey);
    }

    [Fact]
    public void ConfigureChannel_WhenPfxContainsChain_AddsAllCertificates()
    {
        using var files = new TestCertificateFiles();
        using var certificateAuthority = TestCertificateFactory.CreateCertificateAuthority();
        using var clientCertificate = TestCertificateFactory.CreateSignedClientCertificate(certificateAuthority);
        var certificates = new X509Certificate2Collection { clientCertificate, certificateAuthority };
        var certificatePath = TestCertificateFactory.WritePkcs12Collection(
            files,
            "client-chain.pfx",
            certificates,
            "password");
        var channelOptions = new GrpcChannelOptions();

        EtcdClientFactory.ConfigureChannel(
            new EtcdOptions
            {
                EndPoints = HttpsEndpoint,
                SslOptions = new SslOptions
                {
                    UseSsl = true,
                    ClientCertificatePath = certificatePath,
                    ClientCertificatePassword = "password"
                }
            },
            channelOptions);

        var handler = GetSocketsHandler(channelOptions);
        Assert.Equal(2, handler.SslOptions.ClientCertificates!.Count);
    }

    [Fact]
    public void ConfigureChannel_WhenClientCertificateCannotBeLoaded_ThrowsWithoutLeakingPassword()
    {
        using var files = new TestCertificateFiles();
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate();
        var certificatePath = TestCertificateFactory.WritePkcs12(files, "client.pfx", certificate, "correct-password");
        const string wrongPassword = "wrong-password";

        var exception = Assert.Throws<OptionsValidationException>(() =>
            EtcdClientFactory.ConfigureChannel(
                new EtcdOptions
                {
                    EndPoints = HttpsEndpoint,
                    SslOptions = new SslOptions
                    {
                        UseSsl = true,
                        ClientCertificatePath = certificatePath,
                        ClientCertificatePassword = wrongPassword
                    }
                },
                new GrpcChannelOptions()));

        Assert.DoesNotContain(wrongPassword, string.Join(' ', exception.Failures));
    }

    [Fact]
    public void Dispose_DisposesFileLoadedCertificates()
    {
        using var files = new TestCertificateFiles();
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate();
        var certificatePath = TestCertificateFactory.WriteCertificatePem(files, "client.crt", certificate);
        var keyPath = TestCertificateFactory.WritePrivateKeyPem(files, "client.key", certificate);
        var channelOptions = new GrpcChannelOptions();

        EtcdClientFactory.ConfigureChannel(
            new EtcdOptions
            {
                EndPoints = HttpsEndpoint,
                SslOptions = new SslOptions
                {
                    UseSsl = true,
                    ClientCertificatePath = certificatePath,
                    ClientCertificateKeyPath = keyPath
                }
            },
            channelOptions);

        var handler = GetSocketsHandler(channelOptions);
        var installedCertificate = Assert.Single(
            handler.SslOptions.ClientCertificates!.Cast<X509Certificate2>());

        channelOptions.HttpHandler!.Dispose();

        Assert.Throws<CryptographicException>(() => installedCertificate.GetRawCertData());
    }

    [Fact]
    public void Dispose_DoesNotDisposeCallerSuppliedCertificate()
    {
        using var certificate = TestCertificateFactory.CreateSelfSignedClientCertificate();
        var channelOptions = new GrpcChannelOptions();

        EtcdClientFactory.ConfigureChannel(
            new EtcdOptions
            {
                EndPoints = HttpsEndpoint,
                SslOptions = new SslOptions
                {
                    UseSsl = true,
                    ClientCertificate = certificate
                }
            },
            channelOptions);

        channelOptions.HttpHandler!.Dispose();

        Assert.NotEmpty(certificate.GetRawCertData());
    }

    private static SocketsHttpHandler GetSocketsHandler(GrpcChannelOptions channelOptions)
    {
        var delegatingHandler = Assert.IsType<DelegatingHandler>(
            channelOptions.HttpHandler,
            exactMatch: false);
        return Assert.IsType<SocketsHttpHandler>(delegatingHandler.InnerHandler);
    }

    private static string GetCaCertificatePath()
    {
        var directory = new DirectoryInfo(typeof(EtcdClientFactoryTest).Assembly.Location).Parent;
        while (directory != null && directory.Name != "Source")
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "certs", "ca.crt");
    }
}

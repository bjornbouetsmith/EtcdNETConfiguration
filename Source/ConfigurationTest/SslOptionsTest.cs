using ROOT.Etcd.Configuration;
using System.Security.Cryptography.X509Certificates;

namespace ROOT.Etcd.ConfigurationTest;

public class SslOptionsTest
{
    [Fact]
    public void Defaults_AreExpected()
    {
        var options = new SslOptions();

        Assert.False(options.UseSsl);
        Assert.Null(options.ServerCertificateAuthorityPath);
        Assert.Null(options.ClientCertificate);
        Assert.Null(options.ClientCertificatePath);
        Assert.Null(options.ClientCertificateKeyPath);
        Assert.Null(options.ClientCertificatePassword);
        Assert.Equal(X509RevocationMode.NoCheck, options.RevocationMode);
        Assert.False(options.UseProxy);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        using var certificate = X509CertificateLoader.LoadCertificateFromFile(TestCertificates.CaCertificatePath);

        var options = new SslOptions
        {
            UseSsl = true,
            ServerCertificateAuthorityPath = "/certs/ca.crt",
            ClientCertificate = certificate,
            ClientCertificatePath = "/certs/client.crt",
            ClientCertificateKeyPath = "/certs/client.key",
            ClientCertificatePassword = "not-a-real-password",
            RevocationMode = X509RevocationMode.Offline,
            UseProxy = true
        };

        Assert.True(options.UseSsl);
        Assert.Equal("/certs/ca.crt", options.ServerCertificateAuthorityPath);
        Assert.Same(certificate, options.ClientCertificate);
        Assert.Equal("/certs/client.crt", options.ClientCertificatePath);
        Assert.Equal("/certs/client.key", options.ClientCertificateKeyPath);
        Assert.Equal("not-a-real-password", options.ClientCertificatePassword);
        Assert.Equal(X509RevocationMode.Offline, options.RevocationMode);
        Assert.True(options.UseProxy);
    }

    [Fact]
    public void ToString_RedactsPasswordAndClientCertificate()
    {
        using var certificate = X509CertificateLoader.LoadCertificateFromFile(TestCertificates.CaCertificatePath);
        const string password = "super-secret-password";

        var options = new SslOptions
        {
            UseSsl = true,
            ClientCertificate = certificate,
            ClientCertificatePath = "/certs/client.pfx",
            ClientCertificatePassword = password
        };

        var text = options.ToString();

        Assert.DoesNotContain(password, text);
        Assert.Contains("ClientCertificate = <redacted>", text);
        Assert.Contains("ClientCertificatePassword = <redacted>", text);
    }

    [Fact]
    public void EtcdOptionsToString_DoesNotExposeNestedPassword()
    {
        const string password = "super-secret-password";

        var options = new EtcdOptions
        {
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ClientCertificatePath = "/certs/client.pfx",
                ClientCertificatePassword = password
            }
        };

        Assert.DoesNotContain(password, options.ToString());
    }
}

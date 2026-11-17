using ROOT.Etcd.Configuration;

namespace ROOT.Etcd.ConfigurationTest;

public class SslOptionsTest
{
    [Fact]
    public void Defaults_AreExpected()
    {
        var options = new SslOptions();

        Assert.False(options.UseSsl);
        Assert.Null(options.ServerCertificateAuthorityPath);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var options = new SslOptions
        {
            UseSsl = true,
            ServerCertificateAuthorityPath = "/certs/ca.crt"
        };

        Assert.True(options.UseSsl);
        Assert.Equal("/certs/ca.crt", options.ServerCertificateAuthorityPath);
    }
}

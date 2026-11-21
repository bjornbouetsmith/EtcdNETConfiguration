using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using ROOT.Etcd.Configuration;
using System.IO;
using System.Net.Http;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdClientFactoryTest
{
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
        Assert.Null(channelOptions.HttpHandler);
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
        Assert.IsType<SocketsHttpHandler>(channelOptions.HttpHandler);
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

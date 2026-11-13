using Grpc.Core;
using Grpc.Net.Client;
using ROOT.Etcd.Configuration;
using System.IO;
using System.Net.Http;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdClientFactoryTest
{
    [Fact]
    public void ConfigureChannel_WhenInsecureChannelsAreEnabled_UsesInsecureCredentials()
    {
        var channelOptions = new GrpcChannelOptions();

        EtcdClientFactory.ConfigureChannel(
            new EtcdOptions { UseInsecureChannels = true },
            channelOptions);

        Assert.Same(ChannelCredentials.Insecure, channelOptions.Credentials);
        Assert.Null(channelOptions.HttpHandler);
    }

    [Fact]
    public void ConfigureChannel_WhenInsecureChannelsAreDisabled_UsesSecureCredentials()
    {
        var channelOptions = new GrpcChannelOptions();

        EtcdClientFactory.ConfigureChannel(
            new EtcdOptions { UseInsecureChannels = false },
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
                UseInsecureChannels = false,
                ServerCertificateAuthorityPath = GetCaCertificatePath()
            },
            channelOptions);

        Assert.Same(ChannelCredentials.SecureSsl, channelOptions.Credentials);
        Assert.IsType<SocketsHttpHandler>(channelOptions.HttpHandler);
    }

    [Fact]
    public void ConfigureChannel_WhenInsecureChannelsAreEnabled_DoesNotInstallTlsHandler()
    {
        var channelOptions = new GrpcChannelOptions();

        EtcdClientFactory.ConfigureChannel(
            new EtcdOptions
            {
                UseInsecureChannels = true,
                ServerCertificateAuthorityPath = GetCaCertificatePath()
            },
            channelOptions);

        Assert.Same(ChannelCredentials.Insecure, channelOptions.Credentials);
        Assert.Null(channelOptions.HttpHandler);
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

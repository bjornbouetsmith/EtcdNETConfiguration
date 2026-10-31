using Grpc.Core;
using Grpc.Net.Client;
using ROOT.Etcd.Configuration;

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
    }

    [Fact]
    public void ConfigureChannel_WhenInsecureChannelsAreDisabled_LeavesCredentialsUnset()
    {
        var channelOptions = new GrpcChannelOptions();

        EtcdClientFactory.ConfigureChannel(
            new EtcdOptions { UseInsecureChannels = false },
            channelOptions);

        Assert.Null(channelOptions.Credentials);
    }
}

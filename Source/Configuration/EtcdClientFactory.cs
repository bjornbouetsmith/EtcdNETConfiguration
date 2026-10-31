using dotnet_etcd;
using dotnet_etcd.interfaces;
using Grpc.Core;
using Grpc.Net.Client;

namespace ROOT.Etcd.Configuration;

/// <summary>
/// Factory to create etcd clients
/// </summary>
public interface IEtcdClientFactory
{
    IEtcdClient CreateClient(EtcdOptions options);
}

/// <inheritdoc />
public class EtcdClientFactory : IEtcdClientFactory
{
    /// <summary>
    /// Creates an etcd client with the given options used.
    /// </summary>
    /// <param name="options"></param>
    /// <returns></returns>
    public IEtcdClient CreateClient(EtcdOptions options) 
    {
        return new EtcdClient(options.EndPoints, configureChannelOptions: channelOptions =>
            ConfigureChannel(options, channelOptions));
    }

    internal static void ConfigureChannel(EtcdOptions options, GrpcChannelOptions channelOptions)
    {
        if (options.UseInsecureChannels)
        {
            channelOptions.Credentials = ChannelCredentials.Insecure;
        }
    }
}

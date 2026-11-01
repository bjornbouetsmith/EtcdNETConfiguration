using dotnet_etcd;
using dotnet_etcd.interfaces;
using Grpc.Core;
using Grpc.Net.Client;

namespace ROOT.Etcd.Configuration;

/// <summary>
/// Interface for creating etcd clients.
/// </summary>
public interface IEtcdClientFactory
{
    /// <summary>
    /// Creates an etcd client with the specified options.
    /// </summary>
    /// <param name="options">Etcd connection settings and configuration.</param>
    /// <returns>A new configured etcd client.</returns>
    IEtcdClient CreateClient(EtcdOptions options);
}

/// <inheritdoc />
public class EtcdClientFactory : IEtcdClientFactory
{
    /// <inheritdoc />
    public IEtcdClient CreateClient(EtcdOptions options) 
    {
        return new EtcdClient(options.EndPoints, configureChannelOptions: channelOptions =>
            ConfigureChannel(options, channelOptions));
    }

    /// <summary>
    /// Configures the gRPC channel with credentials based on security settings.
    /// </summary>
    /// <param name="options">Etcd options including the UseInsecureChannels setting.</param>
    /// <param name="channelOptions">The channel options to configure.</param>
    internal static void ConfigureChannel(EtcdOptions options, GrpcChannelOptions channelOptions)
    {
        if (options.UseInsecureChannels)
        {
            channelOptions.Credentials = ChannelCredentials.Insecure;
        }
    }
}

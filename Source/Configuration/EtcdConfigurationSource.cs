using dotnet_etcd;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROOT.Etcd.Configuration;

public class EtcdConfigurationSource : IConfigurationSource
{
    private readonly IEtcdConfiguration _etcdConfiguration;

    public EtcdConfigurationSource(IEtcdConfiguration etcdConfiguration)
    {
        _etcdConfiguration = etcdConfiguration;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder) => new EtcdConfigurationProvider(_etcdConfiguration);
}

public class OneTimeEtcdConfiguration : IConfigurationSource
{
    private readonly EtcdOptions _options;

    public OneTimeEtcdConfiguration(EtcdOptions options)
    {
        _options = options;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        var configuration = LoadInitialConfiguration();
        return new EtcdConfigurationProvider(configuration);
    }

    private EtcdConfiguration LoadInitialConfiguration()
    {
        using var client = new EtcdClient(_options.EndPoints, configureChannelOptions: opts =>
        {
            opts.UnsafeUseInsecureChannelCallCredentials = true;
        });
        EtcdConfigurationLoader loader = new EtcdConfigurationLoader(client, _options, NullLogger<EtcdConfigurationLoader>.Instance);
        var data = loader.LoadConfiguration();
        var configuration = new EtcdConfiguration(data);
        return configuration;
    }
}
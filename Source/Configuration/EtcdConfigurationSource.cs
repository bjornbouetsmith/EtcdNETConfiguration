using dotnet_etcd.interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ROOT.Etcd.Configuration;

public class EtcdConfigurationSource : IConfigurationSource
{
    private readonly EtcdOptions _options;
    private readonly EtcdConfiguration _sharedConfiguration;
    private readonly IEtcdClientFactory _clientFactory;

    internal EtcdConfigurationSource(
        EtcdOptions options,
        EtcdConfiguration sharedConfiguration,
        IEtcdClientFactory clientFactory)
    {
        _options = options;
        _sharedConfiguration = sharedConfiguration;
        _clientFactory = clientFactory;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        var configuration = LoadInitialConfiguration();

        // Seed the same instance that the poller updates so there is only one
        // etcd-backed provider in the configuration chain.
        _sharedConfiguration.PublishConfiguration(configuration.Configuration);
        return new EtcdConfigurationProvider(_sharedConfiguration);
    }

    private EtcdConfiguration LoadInitialConfiguration()
    {
        IEtcdClient? client = null;
        try
        {
            client = _clientFactory.CreateClient(_options);

            EtcdConfigurationLoader loader = new EtcdConfigurationLoader(client, _options, NullLogger<EtcdConfigurationLoader>.Instance);
            var data = loader.LoadConfiguration();
            var configuration = new EtcdConfiguration(data.Configuration);

            return configuration;
        }
        catch (Exception e)
        {
            Trace.WriteLine($"Failed to load initial configuration from etcd because of exception:{e}");
            return new EtcdConfiguration(new Dictionary<string, string?>());
        }
        finally
        {
            client?.Dispose();
        }
    }
}

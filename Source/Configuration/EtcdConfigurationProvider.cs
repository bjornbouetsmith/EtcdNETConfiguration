using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace ROOT.Etcd.Configuration;

public class EtcdConfigurationProvider : ConfigurationProvider
{
    private readonly IEtcdConfiguration _etcdConfiguration;
    
    public EtcdConfigurationProvider(IEtcdConfiguration etcdConfiguration)
    {
        _etcdConfiguration = etcdConfiguration;
        _etcdConfiguration.ConfigurationChanged += ConfigurationChanged;
    }

    private Task ConfigurationChanged(IReadOnlyDictionary<string, string?> changes)
    {
        var data = new Dictionary<string, string?>(changes);
        Data = data;
        OnReload();
        return Task.CompletedTask;
    }

    public override void Load()
    {
        var data = new Dictionary<string,string?>(_etcdConfiguration.Configuration);

        Data = data;
        OnReload();
    }
}
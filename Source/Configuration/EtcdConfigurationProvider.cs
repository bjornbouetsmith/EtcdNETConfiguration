using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace ROOT.Etcd.Configuration;

public sealed class EtcdConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly IEtcdConfiguration _etcdConfiguration;
    private readonly object _configurationLoadLock = new object();

    public EtcdConfigurationProvider(IEtcdConfiguration etcdConfiguration)
    {
        _etcdConfiguration = etcdConfiguration;
        _etcdConfiguration.ConfigurationChanged += ConfigurationChanged;
    }

    private void ConfigurationChanged(IReadOnlyDictionary<string, string?> changes)
    {
        var data = new Dictionary<string, string?>(changes);
        lock (_configurationLoadLock)
        {
            Data = data;
            OnReload();
        }
    }

    public override void Load()
    {
        var data = new Dictionary<string,string?>(_etcdConfiguration.Configuration);
        lock (_configurationLoadLock)
        {
            Data = data;
            OnReload();
        }
    }

    public void Dispose()
    {
        _etcdConfiguration.ConfigurationChanged -= ConfigurationChanged;
    }
}
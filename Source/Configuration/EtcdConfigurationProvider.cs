using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace ROOT.Etcd.Configuration;

/// <summary>
/// Configuration provider that loads and maintains data from an etcd-backed configuration source.
/// When changes are detected, it updates internal Data and invokes OnReload to refresh IConfiguration.
/// </summary>
public sealed class EtcdConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly IEtcdConfiguration _etcdConfiguration;
    private readonly object _configurationLoadLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="EtcdConfigurationProvider"/> class.
    /// </summary>
    /// <param name="etcdConfiguration">The etcd-backed configuration to subscribe to change notifications.</param>
    public EtcdConfigurationProvider(IEtcdConfiguration etcdConfiguration)
    {
        _etcdConfiguration = etcdConfiguration;
        _etcdConfiguration.ConfigurationChanged += ConfigurationChanged;
    }

    /// <summary>
    /// Event handler for changes in the underlying configuration.
    /// </summary>
    /// <param name="changes">Read-only dictionary of changed key-value pairs.</param>
    private void ConfigurationChanged(IReadOnlyDictionary<string, string?> changes)
    {
        var data = new Dictionary<string, string?>(changes);
        lock (_configurationLoadLock)
        {
            Data = data;
            OnReload();
        }
    }

    /// <inheritdoc/>
    public override void Load()
    {
        var data = new Dictionary<string,string?>(_etcdConfiguration.Configuration);
        lock (_configurationLoadLock)
        {
            Data = data;
            OnReload();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _etcdConfiguration.ConfigurationChanged -= ConfigurationChanged;
    }
}
using System.Collections.Generic;
using dotnet_etcd.interfaces;
using Microsoft.Extensions.Logging;

namespace ROOT.Etcd.Configuration;

public interface IEtcdConfigurationLoader
{
    IReadOnlyDictionary<string, string?> LoadConfiguration();
}

public sealed class EtcdConfigurationLoader : IEtcdConfigurationLoader
{
    private readonly IEtcdClient _etcdClient;
    private readonly ILogger<EtcdConfigurationLoader> _logger;
    private readonly EtcdOptions _options;

    public EtcdConfigurationLoader(IEtcdClient etcdClient,EtcdOptions options, ILogger<EtcdConfigurationLoader> logger)
    {
        _etcdClient = etcdClient;
        _logger = logger;
        _options = options;
    }

    public IReadOnlyDictionary<string, string?> LoadConfiguration()
    {
        var data = new Dictionary<string, string?>();

        var response = _etcdClient.GetRange(_options.RootKey);

        foreach (var kv in response.Kvs)
        {
            var key = kv.Key.ToStringUtf8();
            var value = kv.Value.ToStringUtf8();
            _logger.LogInformation("{Key} {Value}", key, value);
            data[key] = value;
        }

        return data;
    }
}
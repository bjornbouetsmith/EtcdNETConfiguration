using dotnet_etcd.interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace ROOT.Etcd.Configuration;

public interface IEtcdConfigurationLoader
{
    ConfigurationData LoadConfiguration();
}

public readonly record struct ConfigurationData(IReadOnlyDictionary<string, string?> Configuration, bool ChangesPresent);

public sealed class EtcdConfigurationLoader : IEtcdConfigurationLoader
{
    private readonly IEtcdClient _etcdClient;
    private readonly ILogger<EtcdConfigurationLoader> _logger;
    private readonly EtcdOptions _options;
    private Dictionary<string, string?>? _previousVersion;
    private readonly TimeProvider _timeProvider;

    private const string RedactedValue = "***";

    public EtcdConfigurationLoader(IEtcdClient etcdClient, EtcdOptions options, ILogger<EtcdConfigurationLoader> logger)
        : this(etcdClient, options, logger, null, null)
    {
    }

    internal EtcdConfigurationLoader(
        IEtcdClient etcdClient,
        EtcdOptions options,
        ILogger<EtcdConfigurationLoader> logger,
        TimeProvider? timeProvider,
        IReadOnlyDictionary<string, string?>? initialValues)
    {
        _etcdClient = etcdClient;
        _logger = logger;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _previousVersion = initialValues is null
            ? null
            : new Dictionary<string, string?>(initialValues);
    }

    public ConfigurationData LoadConfiguration()
    {
        var data = new Dictionary<string, string?>();
        var deadLine = _timeProvider.GetUtcNow().Add(_options.ConfigurationTimeout);
        var response = _etcdClient.GetRange(_options.RootKey, deadline: deadLine.UtcDateTime);

        foreach (var kv in response.Kvs)
        {
            var key = kv.Key.ToStringUtf8();
            var value = kv.Value.ToStringUtf8();

            data[key] = value;
        }
        var changes = LogChangedConfigurationKeys(data);
        _previousVersion = data;
        return new(data, changes);
    }

    /// <summary>
    /// Logs changes and returns whether any changes was found
    /// </summary>
    private bool LogChangedConfigurationKeys(Dictionary<string, string?> newValues)
    {
        if (_previousVersion is null)
        {
            foreach (var kv in newValues)
            {
                _logger.LogInformation(
                    "Configuration key {Key} was added with value {Value}",
                    kv.Key,
                    GetValueForLog(kv.Value));
            }

            return true;
        }
        bool changes = false;
        foreach (var kv in newValues)
        {
            if (!_previousVersion.TryGetValue(kv.Key, out var previousValue))
            {
                _logger.LogInformation(
                    "Configuration key {Key} was added with value {Value}",
                    kv.Key,
                    GetValueForLog(kv.Value));
                changes = true;
            }
            else if (!string.Equals(previousValue, kv.Value, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "Configuration key {Key} changed from {PreviousValue} to {Value}",
                    kv.Key,
                    GetValueForLog(previousValue),
                    GetValueForLog(kv.Value));
                changes = true;
            }
        }

        foreach (var kv in _previousVersion)
        {
            if (!newValues.ContainsKey(kv.Key))
            {
                _logger.LogInformation(
                    "Configuration key {Key} was removed; previous value was {PreviousValue}",
                    kv.Key,
                    GetValueForLog(kv.Value));
                changes = true;
            }
        }

        return changes;
    }

    private string? GetValueForLog(string? value) =>
        _options.RedactConfigurationValues ? RedactedValue : value;
}
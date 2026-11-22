using dotnet_etcd.interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;

namespace ROOT.Etcd.Configuration;

/// <summary>
/// Interface for loading configuration data from etcd.
/// </summary>
public interface IEtcdConfigurationLoader
{
    /// <summary>
    /// Loads the current configuration from etcd and determines if any changes occurred.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the etcd read and shutdown.</param>
    /// <returns>Structure containing the loaded configuration and a flag indicating whether changes were detected.</returns>
    ConfigurationData LoadConfiguration(CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-only record structure representing loaded configuration data.
/// Contains both the current configuration values and a flag indicating if any changes occurred.
/// </summary>
/// <param name="Configuration">Current key-value pairs from etcd.</param>
/// <param name="ChangesPresent">True if any keys were added, changed, or removed since the last load.</param>
public readonly record struct ConfigurationData(IReadOnlyDictionary<string, string?> Configuration, bool ChangesPresent);

/// <summary>
/// Thrown when a single etcd read exceeds the configured key-count or size limit.
/// </summary>
internal sealed class EtcdConfigurationLimitExceededException : InvalidOperationException
{
    internal EtcdConfigurationLimitExceededException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Loads configuration data from etcd and compares it with previous values to detect changes.
/// </summary>
public sealed class EtcdConfigurationLoader : IEtcdConfigurationLoader
{
    private readonly IEtcdClient _etcdClient;
    private readonly ILogger<EtcdConfigurationLoader> _logger;
    private readonly EtcdOptions _options;
    
    private Dictionary<string, string?>? _previousVersion;
    private readonly TimeProvider _timeProvider;

    private const string RedactedValue = "***";

    private static readonly string[] SensitiveKeyTokens =
    [
        "password", "pwd", "secret", "token", "credential", "connectionstring",
        "apikey", "api-key", "privatekey", "private-key"
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="EtcdConfigurationLoader"/> class.
    /// </summary>
    /// <param name="etcdClient">The etcd client used for retrieving configuration values.</param>
    /// <param name="options">Options containing connection settings and behavior configuration.</param>
    /// <param name="logger">Logger instance for writing informational logs.</param>
    public EtcdConfigurationLoader(IEtcdClient etcdClient, EtcdOptions options, ILogger<EtcdConfigurationLoader> logger)
        : this(etcdClient, options, logger, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EtcdConfigurationLoader"/> class with explicit dependencies.
    /// </summary>
    /// <param name="etcdClient">The etcd client used for retrieving configuration values.</param>
    /// <param name="options">Options containing connection settings and behavior configuration.</param>
    /// <param name="logger">Logger instance for writing logs.</param>
    /// <param name="timeProvider">Optional time provider for consistent time access; uses system time if null.</param>
    /// <param name="initialValues">Optional initial values to use as the previous version for change detection.</param>
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

    /// <inheritdoc/>
    public ConfigurationData LoadConfiguration(CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, string?>();
        var deadLine = _timeProvider.GetUtcNow().Add(_options.ConfigurationTimeout);
        var response = _etcdClient.GetRange(
            _options.RootKey,
            deadline: deadLine.UtcDateTime,
            cancellationToken: cancellationToken);

        var totalSize = 0L;
        foreach (var kv in response.Kvs)
        {
            if (data.Count >= _options.MaximumConfigurationKeyCount)
            {
                throw new EtcdConfigurationLimitExceededException(
                    $"The etcd response exceeded MaximumConfigurationKeyCount ({_options.MaximumConfigurationKeyCount}). " +
                    "Narrow RootKey or raise the limit explicitly.");
            }

            totalSize += (long)kv.Key.Length + kv.Value.Length;
            if (totalSize > _options.MaximumConfigurationSizeBytes)
            {
                throw new EtcdConfigurationLimitExceededException(
                    $"The etcd response exceeded MaximumConfigurationSizeBytes ({_options.MaximumConfigurationSizeBytes}). " +
                    "Narrow RootKey or raise the limit explicitly.");
            }

            var key = kv.Key.ToStringUtf8();
            var value = kv.Value.ToStringUtf8();

            data[key] = value;
        }
        var changes = LogChangedConfigurationKeys(data);
        _previousVersion = data;
        return new(data, changes);
    }

    /// <summary>
    /// Logs configuration key changes and returns whether any additions, modifications, or deletions occurred.
    /// </summary>
    /// <param name="newValues">The current values from etcd.</param>
    /// <returns>True if any keys were added, changed, or removed; false otherwise.</returns>
    private bool LogChangedConfigurationKeys(Dictionary<string, string?> newValues)
    {
        if (_previousVersion is null)
        {
            foreach (var kv in newValues)
            {
                _logger.LogInformation(
                    "Configuration key {Key} was added with value {Value}",
                    kv.Key,
                    GetValueForLog(kv.Key, kv.Value));
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
                    GetValueForLog(kv.Key, kv.Value));
                changes = true;
            }
            else if (!string.Equals(previousValue, kv.Value, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "Configuration key {Key} changed from {PreviousValue} to {Value}",
                    kv.Key,
                    GetValueForLog(kv.Key, previousValue),
                    GetValueForLog(kv.Key, kv.Value));
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
                    GetValueForLog(kv.Key, kv.Value));
                changes = true;
            }
        }

        return changes;
    }

    /// <summary>
    /// Redacts or returns the given value for logging. Returns "***" if redact mode is enabled.
    /// </summary>
    /// <param name="value">The value to conditionally redact.</param>
    /// <returns>The original value or "***" if redaction is enabled.</returns>
    private string? GetValueForLog(string key, string? value) =>
        _options.RedactConfigurationValues || IsSensitiveKey(key) ? RedactedValue : value;

    private static bool IsSensitiveKey(string key)
    {
        foreach (var token in SensitiveKeyTokens)
        {
            if (key.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
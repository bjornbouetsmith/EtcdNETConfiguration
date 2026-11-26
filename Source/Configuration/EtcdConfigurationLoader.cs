using dotnet_etcd.interfaces;
using Etcdserverpb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mvccpb;
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
/// <param name="Configuration">
/// Current key-value pairs from etcd, keyed by the mapped configuration key under
/// <see cref="EtcdOptions.ConfigurationPrefix"/> (for example <c>/configuration:key:subkey</c>)
/// rather than the raw etcd path.
/// </param>
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
/// Every configured root is queried within one timeout budget and the results are merged by
/// priority before the final snapshot is compared.
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
            : new Dictionary<string, string?>(initialValues, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public ConfigurationData LoadConfiguration(CancellationToken cancellationToken = default)
    {
        var failures = EtcdRootKeyValidator.Validate(_options);
        if (failures.Count > 0)
        {
            throw new OptionsValidationException(
                nameof(EtcdOptions),
                typeof(EtcdOptions),
                failures);
        }

        var roots = new List<EtcdRootKey>(_options.RootKeys);
        roots.Sort(static (left, right) => left.Priority.CompareTo(right.Priority));

        // One budget per configuration load. Every root query shares the same deadline.
        var deadLine = _timeProvider.GetUtcNow().Add(_options.ConfigurationTimeout);
        var configurationPrefix = _options.ConfigurationPrefix!.Trim();

        // Relative configuration keys are case-insensitive, matching IConfiguration lookups.
        var merged = new Dictionary<string, MergedEntry>(StringComparer.OrdinalIgnoreCase);

        var totalSize = 0L;
        var processedKeyCount = 0;

        foreach (var root in roots)
        {
            var normalizedRootKey = EtcdKeyMapper.NormalizeRootKey(root.RootKey);
            var response = _etcdClient.GetRange(
                normalizedRootKey,
                deadline: deadLine.UtcDateTime,
                cancellationToken: cancellationToken);

            MergeRootResponse(
                response,
                root,
                normalizedRootKey,
                configurationPrefix,
                merged,
                ref totalSize,
                ref processedKeyCount);
        }

        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in merged.Values)
        {
            data[configurationPrefix + entry.RelativeKey] = entry.Value;
        }

        var changes = LogChangedConfigurationKeys(data);
        _previousVersion = data;
        return new(data, changes);
    }

    /// <summary>
    /// Merges the keys returned for one root. Collision detection is scoped to that root; the
    /// same relative key under a different root is resolved by priority instead.
    /// </summary>
    private void MergeRootResponse(
        RangeResponse response,
        EtcdRootKey root,
        string normalizedRootKey,
        string configurationPrefix,
        Dictionary<string, MergedEntry> merged,
        ref long totalSize,
        ref int processedKeyCount)
    {
        var rootKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in response.Kvs)
        {
            TrackResponseEntry(kv, ref totalSize, ref processedKeyCount);

            var etcdKey = kv.Key.ToStringUtf8();
            var value = kv.Value.ToStringUtf8();

            if (!TryMapKey(etcdKey, normalizedRootKey, out var relativeKey))
            {
                continue;
            }

            AddMergedEntry(
                etcdKey,
                value,
                relativeKey,
                root,
                normalizedRootKey,
                configurationPrefix,
                rootKeys,
                merged);
        }
    }

    /// <summary>
    /// Counts every returned key so out-of-scope keys and collisions cannot bypass the limits.
    /// </summary>
    private void TrackResponseEntry(KeyValue kv, ref long totalSize, ref int processedKeyCount)
    {
        if (processedKeyCount >= _options.MaximumConfigurationKeyCount)
        {
            throw new EtcdConfigurationLimitExceededException(
                $"The etcd response exceeded MaximumConfigurationKeyCount ({_options.MaximumConfigurationKeyCount}). " +
                "Narrow RootKeys or raise the limit explicitly.");
        }

        processedKeyCount++;

        totalSize += (long)kv.Key.Length + kv.Value.Length;
        if (totalSize > _options.MaximumConfigurationSizeBytes)
        {
            throw new EtcdConfigurationLimitExceededException(
                $"The etcd response exceeded MaximumConfigurationSizeBytes ({_options.MaximumConfigurationSizeBytes}). " +
                "Narrow RootKeys or raise the limit explicitly.");
        }
    }

    private void AddMergedEntry(
        string etcdKey,
        string value,
        string relativeKey,
        EtcdRootKey root,
        string normalizedRootKey,
        string configurationPrefix,
        Dictionary<string, string> rootKeys,
        Dictionary<string, MergedEntry> merged)
    {
        if (rootKeys.TryGetValue(relativeKey, out var existingEtcdKey))
        {
            throw new EtcdConfigurationKeyCollisionException(
                $"The etcd keys '{existingEtcdKey}' and '{etcdKey}' under root '{normalizedRootKey}' " +
                $"both map to the relative configuration key '{relativeKey}' " +
                $"('{configurationPrefix}{relativeKey}'). Remove or rename one of the keys.");
        }

        rootKeys[relativeKey] = etcdKey;

        if (merged.TryGetValue(relativeKey, out var existing) &&
            existing.Root.Priority < root.Priority)
        {
            _logger.LogDebug(
                "Configuration key {Key} from root {WinningRoot} overrides root {ShadowedRoot}",
                relativeKey,
                normalizedRootKey,
                EtcdKeyMapper.NormalizeRootKey(existing.Root.RootKey));
        }

        merged[relativeKey] = new MergedEntry(relativeKey, value, root);
    }

    /// <summary>
    /// Validates and maps an etcd key to a relative configuration key. Returns false when the
    /// key is outside the configured root scope and should be skipped.
    /// </summary>
    /// <param name="etcdKey">The raw etcd key.</param>
    /// <param name="rootKey">The normalized root key.</param>
    /// <param name="relativeConfigurationKey">The relative configuration key when the method returns true.</param>
    /// <returns>True when the key is in scope and valid; false when it is out of scope.</returns>
    /// <exception cref="EtcdConfigurationKeyFormatException">The key uses the removed format or is invalid.</exception>
    private bool TryMapKey(string etcdKey, string rootKey, out string relativeConfigurationKey)
    {
        if (EtcdKeyMapper.IsOldFormat(etcdKey, rootKey))
        {
            throw new EtcdConfigurationKeyFormatException(
                $"The etcd key '{etcdKey}' uses the removed ':' hierarchy format. " +
                $"Rewrite it using '/' path segments, for example '{rootKey}key/subkey'. " +
                "Configuration keys are still exposed with ':' separators.");
        }

        if (!EtcdKeyMapper.IsInScope(etcdKey, rootKey))
        {
            _logger.LogDebug(
                "Skipping etcd key {Key} because it is not a descendant of RootKey {RootKey}",
                etcdKey,
                rootKey);
            relativeConfigurationKey = string.Empty;
            return false;
        }

        relativeConfigurationKey = EtcdKeyMapper.ToRelativeConfigurationKey(etcdKey, rootKey);
        return true;
    }

    /// <summary>
    /// Logs configuration key changes and returns whether any additions, modifications, or deletions occurred.
    /// </summary>
    /// <param name="newValues">The current merged values from etcd.</param>
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
    /// <param name="key">The configuration key, used to detect secret-like names.</param>
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

    /// <summary>
    /// A merged relative configuration key together with the value and root that won by priority.
    /// </summary>
    private readonly record struct MergedEntry(string RelativeKey, string Value, EtcdRootKey Root);
}

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ROOT.Etcd.Configuration;

/// <summary>
/// Configuration options for connecting to and interacting with an etcd cluster.
/// Contains connection endpoints, polling intervals, root keys, and redaction settings.
/// Validation is performed through DataAnnotations when the options are registered.
/// </summary>
public record EtcdOptions
{
    /// <summary>
    /// The smallest supported positive <see cref="TimeSpan"/> supported by the refresh interval
    /// </summary>
    private const string MinimumRefreshInterval = "00:00:00";

    /// <summary>
    /// The smallest supported positive <see cref="TimeSpan"/> supported 1 second
    /// </summary>
    private const string MinimumConfigurationTimeout = "00:00:01";

    /// <summary>
    /// Maximum refresh interval we want to support.
    /// </summary>
    private const string MaximumRefreshInterval = "1.00:00:00";

    /// <summary>
    /// The largest timeout we want to support, one hour.
    /// </summary>
    private const string MaximumConfigurationTimeout = "01:00:00";

    /// <summary>
    /// Gets or sets a comma-separated list of etcd endpoints (e.g., "https://127.0.0.1:2379,https://127.0.0.1:2380").
    /// Defaults to "http://127.0.0.1:2379", matching the default of <see cref="SslOptions.UseSsl"/>.
    /// </summary>
    [Required(ErrorMessage = "Endpoints must be set to a non empty string")]
    public string? EndPoints { get; set; } = "http://127.0.0.1:2379";

    /// <summary>
    /// Gets or sets the prioritized list of etcd root definitions to load configuration from.
    /// Each root has an etcd prefix and a unique <see cref="EtcdRootKey.Priority"/>.
    /// Higher priorities override lower priorities when two roots contain the same relative
    /// configuration key. Only strict descendants of each root are loaded.
    /// Defaults to a single "/configuration" root with priority 0.
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one root key must be configured")]
    [CustomValidation(typeof(EtcdOptions), nameof(ValidateRootKeys))]
    public IReadOnlyList<EtcdRootKey> RootKeys { get; set; } = [new EtcdRootKey()];

    /// <summary>
    /// Gets or sets the prefix under which the merged configuration is exposed.
    /// This prefix is never queried from etcd; it only determines the final configuration
    /// keys. Must start with '/', must not be '/', and must not end with '/' or contain
    /// ':' or '//'. Defaults to "/configuration", which preserves the historical binding
    /// location when used with the default root key.
    /// </summary>
    [Required(ErrorMessage = "ConfigurationPrefix must be set to a non empty string")]
    [CustomValidation(typeof(EtcdOptions), nameof(ValidateConfigurationPrefix))]
    public string? ConfigurationPrefix { get; set; } = "/configuration";

    /// <summary>
    /// Gets or sets the interval at which configuration is polled for changes from etcd.
    /// Defaults to TimeSpan.FromMinutes(1).
    /// Values must be greater than or equal to <see cref="TimeSpan.Zero"/> and less than or equal to
    /// the maximum refresh interval 24 hours.
    /// Setting this to TimeSpan.Zero disables polling and only loads configuration once at startup.
    /// </summary>
    [Range(typeof(TimeSpan), MinimumRefreshInterval, MaximumRefreshInterval,
        ErrorMessage = "RefreshInterval must be set to a timespan greater than or equal to 0 and must not exceed 24 hours")]
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the timeout budget for one configuration load. Every configured root is
    /// queried within the same deadline; the budget is not reset per root.
    /// A non-zero value indicates the operation should complete within this duration; zero would typically indicate a timeout of zero.
    /// </summary>
    [Range(typeof(TimeSpan), MinimumConfigurationTimeout, MaximumConfigurationTimeout,
        ErrorMessage = "ConfigurationTimeout must be set to a timespan greater than 0 and less than or equal to 1 hour")]
    public TimeSpan ConfigurationTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets whether to replace sensitive configuration values with "***" in log messages.
    /// Defaults to true for security best practices. Values are redacted regardless of this
    /// setting when the key name looks like a secret.
    /// </summary>
    public bool RedactConfigurationValues { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of keys that one configuration load may return across
    /// all configured <see cref="RootKeys"/>. Defaults to 10,000. Broad roots or a large etcd
    /// database cannot exhaust memory or block shutdown indefinitely.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "MaximumConfigurationKeyCount must be at least 1")]
    public int MaximumConfigurationKeyCount { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets the maximum combined key and value size, in bytes, that one configuration
    /// load may return across all configured <see cref="RootKeys"/>. Defaults to 8 MiB.
    /// </summary>
    [Range(typeof(long), "1", "9223372036854775807",
        ErrorMessage = "MaximumConfigurationSizeBytes must be at least 1")]
    public long MaximumConfigurationSizeBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>
    /// Gets or sets how many consecutive permanent polling failures are tolerated before the
    /// host is asked to stop. Defaults to 5. Set to 0 to keep polling indefinitely and only
    /// expose the failure through <see cref="IEtcdPollerStatus"/>.
    /// </summary>
    [Range(0, int.MaxValue,
        ErrorMessage = "MaximumConsecutivePermanentFailures must be zero or greater")]
    public int MaximumConsecutivePermanentFailures { get; set; } = 5;

    /// <summary>
    /// Gets or sets the TLS settings used when communicating with etcd endpoints.
    /// When null or <see cref="SslOptions.UseSsl"/> is false, the connection is unencrypted.
    /// </summary>
    public SslOptions? SslOptions { get; set; }

    /// <summary>
    /// Validates the configured <see cref="RootKeys"/>. The shared validator also enforces
    /// unique priorities and rejects roots that are duplicates or that nest.
    /// </summary>
    /// <param name="rootKeys">The configured root definitions.</param>
    /// <returns>A validation result describing the problems, or success.</returns>
    public static ValidationResult? ValidateRootKeys(IReadOnlyList<EtcdRootKey>? rootKeys)
    {
        // Required and MinLength report missing and empty lists; only surface the remaining
        // rules here so DataAnnotations does not report the same problem twice.
        var failures = new List<string>();
        foreach (var failure in EtcdRootKeyValidator.ValidateRootKeys(rootKeys))
        {
            if (failure is "RootKeys must be set to a non empty list" or
                "At least one root key must be configured")
            {
                continue;
            }

            failures.Add(failure);
        }

        return failures.Count == 0
            ? ValidationResult.Success
            : new ValidationResult(
                string.Join(Environment.NewLine, failures),
                [nameof(RootKeys)]);
    }

    /// <summary>
    /// Validates that <see cref="ConfigurationPrefix"/> is a canonical, non-root prefix.
    /// </summary>
    /// <param name="configurationPrefix">The configured output prefix.</param>
    /// <returns>A validation result describing the problem, or success.</returns>
    public static ValidationResult? ValidateConfigurationPrefix(string? configurationPrefix)
    {
        if (string.IsNullOrWhiteSpace(configurationPrefix))
        {
            // RequiredAttribute reports the missing-value error.
            return ValidationResult.Success;
        }

        var failures = EtcdRootKeyValidator.ValidateConfigurationPrefix(configurationPrefix);
        return failures.Count == 0
            ? ValidationResult.Success
            : new ValidationResult(
                string.Join(Environment.NewLine, failures),
                [nameof(ConfigurationPrefix)]);
    }

    /// <summary>
    /// Compares two options instances, including their root definitions by value.
    /// </summary>
    /// <param name="other">The other options instance.</param>
    /// <returns>True when both instances contain the same values.</returns>
    public virtual bool Equals(EtcdOptions? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return string.Equals(EndPoints, other.EndPoints, StringComparison.Ordinal) &&
               RootKeysEqual(RootKeys, other.RootKeys) &&
               string.Equals(ConfigurationPrefix, other.ConfigurationPrefix, StringComparison.Ordinal) &&
               RefreshInterval == other.RefreshInterval &&
               ConfigurationTimeout == other.ConfigurationTimeout &&
               RedactConfigurationValues == other.RedactConfigurationValues &&
               MaximumConfigurationKeyCount == other.MaximumConfigurationKeyCount &&
               MaximumConfigurationSizeBytes == other.MaximumConfigurationSizeBytes &&
               MaximumConsecutivePermanentFailures == other.MaximumConsecutivePermanentFailures &&
               Equals(SslOptions, other.SslOptions);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(EndPoints, StringComparer.Ordinal);
        AddRootKeysToHash(ref hash, RootKeys);
        hash.Add(ConfigurationPrefix, StringComparer.Ordinal);
        hash.Add(RefreshInterval);
        hash.Add(ConfigurationTimeout);
        hash.Add(RedactConfigurationValues);
        hash.Add(MaximumConfigurationKeyCount);
        hash.Add(MaximumConfigurationSizeBytes);
        hash.Add(MaximumConsecutivePermanentFailures);
        hash.Add(SslOptions);
        return hash.ToHashCode();
    }

    private static bool RootKeysEqual(
        IReadOnlyList<EtcdRootKey>? first,
        IReadOnlyList<EtcdRootKey>? second)
    {
        if (ReferenceEquals(first, second))
        {
            return true;
        }

        if (first is null || second is null || first.Count != second.Count)
        {
            return false;
        }

        for (var index = 0; index < first.Count; index++)
        {
            var firstRoot = first[index];
            var secondRoot = second[index];

            if (ReferenceEquals(firstRoot, secondRoot))
            {
                continue;
            }

            if (firstRoot is null ||
                secondRoot is null ||
                !string.Equals(firstRoot.RootKey, secondRoot.RootKey, StringComparison.OrdinalIgnoreCase) ||
                firstRoot.Priority != secondRoot.Priority)
            {
                return false;
            }
        }

        return true;
    }

    private static void AddRootKeysToHash(ref HashCode hash, IReadOnlyList<EtcdRootKey>? rootKeys)
    {
        if (rootKeys is null)
        {
            hash.Add(0);
            return;
        }

        hash.Add(rootKeys.Count);
        foreach (var root in rootKeys)
        {
            if (root is null)
            {
                hash.Add(0);
                continue;
            }

            hash.Add(root.RootKey, StringComparer.OrdinalIgnoreCase);
            hash.Add(root.Priority);
        }
    }
}

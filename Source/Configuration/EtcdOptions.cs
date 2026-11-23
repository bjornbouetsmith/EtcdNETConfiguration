using System;
using System.ComponentModel.DataAnnotations;

namespace ROOT.Etcd.Configuration;

/// <summary>
/// Configuration options for connecting to and interacting with an etcd cluster.
/// Contains connection endpoints, polling intervals, and redaction settings.
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
    /// The largest timeout we want to support
    /// </summary>
    private const string MaximumConfigurationTimeout = "00:01:00";

    /// <summary>
    /// Gets or sets a comma-separated list of etcd endpoints (e.g., "https://127.0.0.1:2379,https://127.0.0.1:2380").
    /// Defaults to "http://127.0.0.1:2379", matching the default of <see cref="SslOptions.UseSsl"/>.
    /// </summary>
    [Required(ErrorMessage = "Endpoints must be set to a non empty string")]
    public string? EndPoints { get; set; } = "http://127.0.0.1:2379";

    /// <summary>
    /// Gets or sets the root key prefix from which etcd configuration will be loaded.
    /// Only strict descendants of this path are loaded; the key <c>/prefix-other/key</c> is
    /// not in scope of <c>/prefix</c>. Use '/' to separate path segments; the prefix must
    /// start with '/', must not be '/', and must not end with '/' or contain ':' or '//'.
    /// Defaults to "/configuration". The root key "/" is rejected because it would load
    /// every key in etcd; use a prefix scoped to this application instead.
    /// </summary>
    [Required(ErrorMessage = "RootKey must be set to a non empty string")]
    [CustomValidation(typeof(EtcdOptions), nameof(ValidateRootKey))]
    public string? RootKey { get; set; } = "/configuration";

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
    /// Gets or sets the timeout for calls to etcd.
    /// A non-zero value indicates the operation should complete within this duration; zero would typically indicate a timeout of zero.
    /// </summary>
    [Range(typeof(TimeSpan), MinimumConfigurationTimeout, MaximumConfigurationTimeout,
        ErrorMessage = "ConfigurationTimeout must be set to a timespan greater than 0 and less than 60 seconds")]
    public TimeSpan ConfigurationTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets whether to replace sensitive configuration values with "***" in log messages.
    /// Defaults to true for security best practices. Values are redacted regardless of this
    /// setting when the key name looks like a secret.
    /// </summary>
    public bool RedactConfigurationValues { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of keys that a single etcd read may return.
    /// Defaults to 10,000. A broad <see cref="RootKey"/> or a large etcd database
    /// cannot exhaust memory or block shutdown indefinitely.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "MaximumConfigurationKeyCount must be at least 1")]
    public int MaximumConfigurationKeyCount { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets the maximum combined key and value size, in bytes, that a single etcd read
    /// may return. Defaults to 8 MiB.
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
    /// Validates that <see cref="RootKey"/> is a usable etcd path prefix. Loading the entire
    /// keyspace is almost never intended and can expose unrelated configuration, so '/' is
    /// rejected. The prefix must also be a canonical path: it must start with '/', must not
    /// end with '/', and must not contain empty segments or ':'.
    /// </summary>
    /// <param name="rootKey">The configured root key.</param>
    /// <returns>A validation result describing the problem, or success.</returns>
    public static ValidationResult? ValidateRootKey(string? rootKey)
    {
        if (string.IsNullOrWhiteSpace(rootKey))
        {
            // RequiredAttribute reports the missing-value error.
            return ValidationResult.Success;
        }

        var normalized = rootKey.Trim();

        if (string.Equals(normalized, "/", StringComparison.Ordinal))
        {
            return new ValidationResult(
                "RootKey must be a non-root prefix; '/' would load every key in etcd.");
        }

        if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            return new ValidationResult("RootKey must start with '/'.");
        }

        if (normalized.EndsWith("/", StringComparison.Ordinal))
        {
            return new ValidationResult("RootKey must not end with '/'.");
        }

        if (normalized.Contains("//", StringComparison.Ordinal))
        {
            return new ValidationResult("RootKey must not contain empty path segments ('//').");
        }

        if (normalized.Contains(':'))
        {
            return new ValidationResult(
                "RootKey must not contain ':'; use '/' to separate etcd path segments.");
        }

        return ValidationResult.Success;
    }
}

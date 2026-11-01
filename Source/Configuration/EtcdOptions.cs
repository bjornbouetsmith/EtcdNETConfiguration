using System;
using System.Collections.Generic;

namespace ROOT.Etcd.Configuration;

/// <summary>
/// Configuration options for connecting to and interacting with an etcd cluster.
/// Contains connection endpoints, polling intervals, redaction settings, and validation logic.
/// </summary>
public record EtcdOptions 
{
    /// <summary>
    /// Maximum refresh interval supported by .NET timers (~49.7 days). Used as upper bound for RefreshInterval.
    /// </summary>
    private static readonly TimeSpan MaxRefreshInterval = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>
    /// Gets or sets a comma-separated list of etcd endpoints (e.g., "https://127.0.0.1:2379,https://127.0.0.1:2380").
    /// Defaults to "https://127.0.0.1:2379".
    /// </summary>
    public string? EndPoints { get; set; } = "https://127.0.0.1:2379";

    /// <summary>
    /// Gets or sets the root key prefix from which etcd configuration will be loaded.
    /// All keys starting with this value are retrieved.
    /// Defaults to "/configuration".
    /// </summary>
    public string? RootKey { get; set; } = "/configuration";

    /// <summary>
    /// Gets or sets the interval at which configuration is polled for changes from etcd.
    /// Defaults to TimeSpan.FromMinutes(1).
    /// Values must be greater than <see cref="TimeSpan.Zero"/> and less than or equal to <see cref="MaxRefreshInterval"/> (~49.7 days).
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the timeout for calls to etcd.
    /// A non-zero value indicates the operation should complete within this duration; zero would typically indicate a timeout of zero.
    /// </summary>
    public TimeSpan ConfigurationTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets whether to replace sensitive configuration values with "***" in log messages.
    /// Defaults to true for security best practices.
    /// </summary>
    public bool RedactConfigurationValues { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to use insecure HTTP (instead of HTTPS) when communicating with etcd endpoints.
    /// Defaults to true, enabling unencrypted connections.
    /// </summary>
    public bool UseInsecureChannels { get; set; } = true;

    /// <summary>
    /// Validates that all required properties are set and values are within acceptable ranges.
    /// Throws an <see cref="InvalidOperationException"/> if validation fails.
    /// </summary>
    public void Validate()
    {
        List<string>? errors = null;
        if (string.IsNullOrWhiteSpace(EndPoints)) 
        {
            errors ??= new List<string>();
            errors.Add("Endpoints must be set to a non empty string");
        }

        if (string.IsNullOrEmpty(RootKey)) 
        {
            errors ??= new List<string>();
            errors.Add("RootKey must be set to a non empty string");
        }

        if (RefreshInterval <= TimeSpan.Zero) 
        {
            errors ??= new List<string>();
            errors.Add("RefreshInterval must be set to a timespan greater than 0");
        }
        else if (RefreshInterval > MaxRefreshInterval)
        {
            errors ??= new List<string>();
            errors.Add(
                $"RefreshInterval must not exceed {MaxRefreshInterval} (the maximum supported by the timer)");
        }

        if (ConfigurationTimeout <= TimeSpan.Zero)
        {
            errors ??= new List<string>();
            errors.Add("ConfigurationTimeout must be set to a timespan greater than 0");
        }

        if (errors is not { Count: > 0 })
        {
            return;
        }

        var error = $"Etcd configuration invalid\r\n{string.Join(Environment.NewLine, errors)}";

        throw new InvalidOperationException(error);
    }
}
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
    /// The smallest supported positive <see cref="TimeSpan"/> (one tick).
    /// </summary>
    private const string PositiveTimeSpanMinimum = "00:00:00.0000001";

    /// <summary>
    /// Maximum refresh interval supported by .NET timers (~49.7 days).
    /// </summary>
    private const string MaxRefreshInterval = "49.17:02:47.2940000";

    /// <summary>
    /// The largest supported <see cref="TimeSpan"/> value.
    /// </summary>
    private const string MaxTimeSpan = "10675199.02:48:05.4775807";

    /// <summary>
    /// Gets or sets a comma-separated list of etcd endpoints (e.g., "https://127.0.0.1:2379,https://127.0.0.1:2380").
    /// Defaults to "https://127.0.0.1:2379".
    /// </summary>
    [Required(ErrorMessage = "Endpoints must be set to a non empty string")]
    public string? EndPoints { get; set; } = "https://127.0.0.1:2379";

    /// <summary>
    /// Gets or sets the root key prefix from which etcd configuration will be loaded.
    /// All keys starting with this value are retrieved.
    /// Defaults to "/configuration".
    /// </summary>
    [Required(ErrorMessage = "RootKey must be set to a non empty string")]
    public string? RootKey { get; set; } = "/configuration";

    /// <summary>
    /// Gets or sets the interval at which configuration is polled for changes from etcd.
    /// Defaults to TimeSpan.FromMinutes(1).
    /// Values must be greater than <see cref="TimeSpan.Zero"/> and less than or equal to
    /// the maximum refresh interval supported by .NET timers (~49.7 days).
    /// </summary>
    [Range(typeof(TimeSpan), PositiveTimeSpanMinimum, MaxRefreshInterval,
        ErrorMessage = "RefreshInterval must be set to a timespan greater than 0 and must not exceed the maximum supported by the timer")]
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the timeout for calls to etcd.
    /// A non-zero value indicates the operation should complete within this duration; zero would typically indicate a timeout of zero.
    /// </summary>
    [Range(typeof(TimeSpan), PositiveTimeSpanMinimum, MaxTimeSpan,
        ErrorMessage = "ConfigurationTimeout must be set to a timespan greater than 0")]
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
}

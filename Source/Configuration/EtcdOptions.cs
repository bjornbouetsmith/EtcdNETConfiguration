using System;
using System.Collections.Generic;

namespace ROOT.Etcd.Configuration;

public record EtcdOptions 
{
    // .NET timers reject due times above 0xFFFFFFFE milliseconds (~49.7 days).
    private static readonly TimeSpan MaxRefreshInterval = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>
    /// Gets or sets the Etcd endpoints, comma separated., i.e.
    /// https://localhost:2379,https://localhost:2380
    /// Defaults to https://localhost:2379
    /// </summary>
    public string? EndPoints { get; set; } = "https://localhost:2379";

    /// <summary>
    /// Key where configuration should be parsed from
    /// Will load all keys that starts with <see cref="RootKey"/>
    /// Defaults to <b>/configuration</b>
    /// </summary>
    public string? RootKey { get; set; } = "/configuration";

    /// <summary>
    /// Gets or sets how often the configuration should be refreshed from etcd
    /// Defaults to 1 minute
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Timeout for calls to etcd.
    /// </summary>
    public TimeSpan ConfigurationTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether to replace configuration values with <c>***</c> in log messages.
    /// Defaults to true.
    /// </summary>
    public bool RedactConfigurationValues { get; set; } = true;

    /// <summary>
    /// Whether to use insecure (http) to communicate with etcd.
    /// Defaults to true
    /// </summary>
    public bool UseInsecureChannels { get; set; } = true;

    /// <summary>
    /// Validates that all required items are set and that values are within acceptable ranges
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
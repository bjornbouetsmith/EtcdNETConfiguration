using System;

namespace ROOT.Etcd.Configuration;

public record EtcdOptions 
{
    /// <summary>
    /// Gets or sets the Etcd endpoints, comma separated., i.e.
    /// https://localhost:2379,https://localhost:2380
    /// </summary>
    public string? EndPoints { get; set; }

    /// <summary>
    /// Key where configuration should be parsed from
    /// Will load all keys that starts with <see cref="RootKey"/>
    /// Defaults to <b>/configuration</b>
    /// </summary>
    public string? RootKey { get; set; } = "/configuration";

    /// <summary>
    /// Gets or sets how often the configuration should be refreshed from etcd
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);
}
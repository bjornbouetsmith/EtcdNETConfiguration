namespace ROOT.Etcd.Configuration;

/// <summary>
/// Defines an etcd root prefix from which configuration is loaded and the priority used when
/// the same relative configuration key is present under more than one root.
/// </summary>
public sealed record EtcdRootKey
{
    /// <summary>
    /// Gets or sets the etcd prefix to load configuration from.
    /// Only strict descendants of the prefix are loaded; the key <c>/prefix-other/key</c> is
    /// not in scope of <c>/prefix</c>.
    /// The prefix must start with '/', must not be '/', must not end with '/', and must not
    /// contain empty path segments or ':'. Defaults to "/configuration".
    /// </summary>
    public string? RootKey { get; set; } = "/configuration";

    /// <summary>
    /// Gets or sets the merge priority. Higher values override lower values when the same
    /// relative configuration key is loaded from more than one root. Each priority may only
    /// be used once. Defaults to 0.
    /// </summary>
    public int Priority { get; set; }
}

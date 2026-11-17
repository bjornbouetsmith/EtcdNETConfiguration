namespace ROOT.Etcd.Configuration;

/// <summary>
/// Configuration options for securing the connection to etcd with TLS.
/// </summary>
public record SslOptions
{
    /// <summary>
    /// Gets or sets whether to use TLS when communicating with etcd endpoints.
    /// Defaults to false, enabling unencrypted connections.
    /// </summary>
    public bool UseSsl { get; set; }

    /// <summary>
    /// Gets or sets the path to a PEM-encoded CA certificate used to validate the etcd server certificate.
    /// Only used when <see cref="UseSsl"/> is true. When null or empty, the operating
    /// system certificate trust store is used.
    /// </summary>
    public string? ServerCertificateAuthorityPath { get; set; }
}

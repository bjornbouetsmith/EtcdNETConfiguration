using System.Security.Cryptography.X509Certificates;
using System.Text;

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

    /// <summary>
    /// Gets or sets an already-loaded client certificate used for mutual TLS.
    /// The caller owns the certificate and must keep it alive for the lifetime of the host.
    /// The library never disposes this certificate.
    /// Mutually exclusive with the file-based client-certificate properties below.
    /// </summary>
    public X509Certificate2? ClientCertificate { get; set; }

    /// <summary>
    /// Gets or sets the path to the client certificate.
    /// PEM when <see cref="ClientCertificateKeyPath"/> is set; otherwise PKCS#12 (.pfx/.p12).
    /// Mutually exclusive with <see cref="ClientCertificate"/>.
    /// </summary>
    public string? ClientCertificatePath { get; set; }

    /// <summary>
    /// Gets or sets the path to the PEM private key for the client certificate.
    /// Null when using a PKCS#12 file.
    /// </summary>
    public string? ClientCertificateKeyPath { get; set; }

    /// <summary>
    /// Gets or sets the password for the PKCS#12 file or encrypted PEM private key.
    /// Null when unencrypted. This value is never written to logs or exception messages.
    /// </summary>
    public string? ClientCertificatePassword { get; set; }

    /// <summary>
    /// Gets or sets the revocation mode used when validating the etcd server certificate chain.
    /// Defaults to <see cref="X509RevocationMode.NoCheck"/> so private CAs without CRL or OCSP
    /// endpoints keep working. Set to <see cref="X509RevocationMode.Online"/> or
    /// <see cref="X509RevocationMode.Offline"/> when revocation information is available.
    /// </summary>
    public X509RevocationMode RevocationMode { get; set; } = X509RevocationMode.NoCheck;

    /// <summary>
    /// Formats the record for diagnostics without exposing the client certificate or its password.
    /// </summary>
    /// <param name="builder">The builder receiving the formatted values.</param>
    /// <returns>Always true so the record prints its members.</returns>
    protected virtual bool PrintMembers(StringBuilder builder)
    {
        builder.Append("UseSsl = ").Append(UseSsl);
        builder.Append(", ServerCertificateAuthorityPath = ").Append(ServerCertificateAuthorityPath);
        builder.Append(", ClientCertificate = ").Append(ClientCertificate is null ? "null" : "<redacted>");
        builder.Append(", ClientCertificatePath = ").Append(ClientCertificatePath);
        builder.Append(", ClientCertificateKeyPath = ").Append(ClientCertificateKeyPath);
        builder.Append(", ClientCertificatePassword = ").Append(ClientCertificatePassword is null ? "null" : "<redacted>");
        builder.Append(", RevocationMode = ").Append(RevocationMode);
        return true;
    }
}

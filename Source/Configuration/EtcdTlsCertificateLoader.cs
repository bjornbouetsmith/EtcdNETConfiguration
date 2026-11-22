using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ROOT.Etcd.Configuration;

/// <summary>
/// Loads TLS certificate material. Callers own the returned certificates and must dispose them.
/// </summary>
internal static class EtcdTlsCertificateLoader
{
    /// <summary>
    /// Loads every certificate in a PEM CA bundle, or a single DER certificate.
    /// </summary>
    internal static X509Certificate2Collection LoadCertificateAuthorities(string path)
    {
        var certificates = new X509Certificate2Collection();

        try
        {
            certificates.ImportFromPemFile(path);
        }
        catch (CryptographicException)
        {
            // Not PEM (or a malformed PEM bundle). Fall back to DER so a single
            // binary certificate file keeps working.
            DisposeCertificates(certificates);
            certificates.Add(X509CertificateLoader.LoadCertificateFromFile(path));
        }

        if (certificates.Count == 0)
        {
            throw new CryptographicException("The CA bundle does not contain any certificates.");
        }

        return certificates;
    }

    /// <summary>
    /// Loads the client certificate collection for a PEM certificate and key or a PKCS#12 file.
    /// </summary>
    internal static X509Certificate2Collection LoadClientCertificates(SslOptions sslOptions)
    {
        var certificatePath = sslOptions.ClientCertificatePath
            ?? throw new InvalidOperationException("ClientCertificatePath is required to load a client certificate.");

        if (!string.IsNullOrWhiteSpace(sslOptions.ClientCertificateKeyPath))
        {
            var certificate = string.IsNullOrWhiteSpace(sslOptions.ClientCertificatePassword)
                ? X509Certificate2.CreateFromPemFile(certificatePath, sslOptions.ClientCertificateKeyPath)
                : X509Certificate2.CreateFromEncryptedPemFile(
                    certificatePath,
                    sslOptions.ClientCertificatePassword,
                    sslOptions.ClientCertificateKeyPath);

            certificate = MakePemCertificateUsableOnWindows(certificate);
            return new X509Certificate2Collection { certificate };
        }

        // Schannel cannot use ephemeral private keys. Use a temporary user key
        // container on Windows instead; without PersistKeySet the key is removed
        // when the certificate is disposed, which the owning handler guarantees.
        var keyStorageFlags = OperatingSystem.IsWindows()
            ? X509KeyStorageFlags.UserKeySet
            : X509KeyStorageFlags.EphemeralKeySet;

        return X509CertificateLoader.LoadPkcs12CollectionFromFile(
            certificatePath,
            sslOptions.ClientCertificatePassword,
            keyStorageFlags);
    }

    /// <summary>
    /// Loads the client certificate that carries the private key and disposes any
    /// additional chain certificates. The caller owns and must dispose the result.
    /// </summary>
    internal static X509Certificate2 LoadClientCertificate(SslOptions sslOptions)
    {
        var certificates = LoadClientCertificates(sslOptions);
        var certificate = FindCertificateWithPrivateKey(certificates);
        if (certificate is null)
        {
            DisposeCertificates(certificates);
            throw new InvalidOperationException("The client certificate does not contain a private key.");
        }

        foreach (var otherCertificate in certificates)
        {
            if (!ReferenceEquals(otherCertificate, certificate))
            {
                otherCertificate.Dispose();
            }
        }

        return certificate;
    }

    /// <summary>
    /// Finds the certificate that carries the private key. PKCS#12 collections may also
    /// contain intermediate certificates without a private key.
    /// </summary>
    internal static X509Certificate2? FindCertificateWithPrivateKey(X509Certificate2Collection certificates)
    {
        foreach (var certificate in certificates)
        {
            if (certificate.HasPrivateKey)
            {
                return certificate;
            }
        }

        return null;
    }

    /// <summary>
    /// Disposes every certificate in a collection loaded by this class.
    /// </summary>
    internal static void DisposeCertificates(IEnumerable<X509Certificate2> certificates)
    {
        foreach (var certificate in certificates)
        {
            certificate.Dispose();
        }
    }

    /// <summary>
    /// PEM-loaded certificates use ephemeral keys, which Schannel cannot use. Re-import
    /// through PKCS#12 on Windows and clear the exported buffer afterwards. The temporary
    /// user key container is removed when the certificate is disposed because PersistKeySet
    /// is not set.
    /// </summary>
    private static X509Certificate2 MakePemCertificateUsableOnWindows(X509Certificate2 certificate)
    {
        if (!OperatingSystem.IsWindows())
        {
            return certificate;
        }

        byte[]? pfx = null;
        try
        {
            pfx = certificate.Export(X509ContentType.Pkcs12);
            return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet);
        }
        finally
        {
            certificate.Dispose();

            if (pfx is not null)
            {
                CryptographicOperations.ZeroMemory(pfx);
            }
        }
    }
}

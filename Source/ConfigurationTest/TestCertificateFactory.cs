using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ROOT.Etcd.ConfigurationTest;

/// <summary>
/// Creates a temporary directory that is removed when the test finishes.
/// </summary>
internal sealed class TestCertificateFiles : IDisposable
{
    public TestCertificateFiles()
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "etcd-net-configuration-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public string PathFor(string fileName) => Path.Combine(DirectoryPath, fileName);

    public void Dispose()
    {
        try
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// Creates self-signed and CA-signed certificates for hermetic TLS unit tests.
/// </summary>
internal static class TestCertificateFactory
{
    internal const string ClientAuthOid = "1.3.6.1.5.5.7.3.2";

    internal static X509Certificate2 CreateSelfSignedClientCertificate(
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null,
        bool includeClientAuthEku = true)
    {
        using var rsa = RSA.Create(2048);
        var request = CreateClientRequest(rsa, "CN=etcd-test-client", includeClientAuthEku);
        return request.CreateSelfSigned(
            notBefore ?? DateTimeOffset.UtcNow.AddMinutes(-5),
            notAfter ?? DateTimeOffset.UtcNow.AddDays(1));
    }

    internal static X509Certificate2 CreateCertificateAuthority(
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=etcd-test-ca",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                critical: true));

        return request.CreateSelfSigned(
            notBefore ?? DateTimeOffset.UtcNow.AddMinutes(-5),
            notAfter ?? DateTimeOffset.UtcNow.AddDays(1));
    }

    internal static X509Certificate2 CreateSignedClientCertificate(
        X509Certificate2 issuer,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null,
        bool includeClientAuthEku = true)
    {
        using var rsa = RSA.Create(2048);
        var request = CreateClientRequest(rsa, "CN=etcd-test-signed-client", includeClientAuthEku);

        using var issuerKey = issuer.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("The issuer certificate does not contain an RSA private key.");
        var generator = X509SignatureGenerator.CreateForRSA(issuerKey, RSASignaturePadding.Pkcs1);

        using var publicCertificate = request.Create(
            issuer.SubjectName,
            generator,
            notBefore ?? DateTimeOffset.UtcNow.AddMinutes(-5),
            notAfter ?? DateTimeOffset.UtcNow.AddDays(1),
            RandomNumberGenerator.GetBytes(16));

        return publicCertificate.CopyWithPrivateKey(rsa);
    }

    internal static X509Certificate2 WithoutPrivateKey(X509Certificate2 certificate) =>
        X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));

    internal static string WriteCertificatePem(
        TestCertificateFiles files,
        string fileName,
        X509Certificate2 certificate)
    {
        var path = files.PathFor(fileName);
        File.WriteAllText(path, certificate.ExportCertificatePem());
        return path;
    }

    internal static string WritePrivateKeyPem(
        TestCertificateFiles files,
        string fileName,
        X509Certificate2 certificate)
    {
        using var key = certificate.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("The certificate does not contain an RSA private key.");
        var path = files.PathFor(fileName);
        File.WriteAllText(path, key.ExportPkcs8PrivateKeyPem());
        return path;
    }

    internal static string WritePkcs12(
        TestCertificateFiles files,
        string fileName,
        X509Certificate2 certificate,
        string? password)
    {
        var path = files.PathFor(fileName);
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, password));
        return path;
    }

    internal static string WritePkcs12Collection(
        TestCertificateFiles files,
        string fileName,
        X509Certificate2Collection certificates,
        string? password)
    {
        var path = files.PathFor(fileName);
        var bytes = certificates.Export(X509ContentType.Pkcs12, password)
            ?? throw new InvalidOperationException("The certificate collection could not be exported.");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static CertificateRequest CreateClientRequest(
        RSA rsa,
        string subject,
        bool includeClientAuthEku)
    {
        var request = new CertificateRequest(
            subject,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        if (includeClientAuthEku)
        {
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid(ClientAuthOid) },
                    critical: false));
        }

        return request;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

namespace ROOT.Etcd.Configuration;

/// <summary>
/// Fail-closed validation for TLS-related etcd options. The same validation is used by the
/// options pipeline, the public client factory, and the initial configuration source so none
/// of those paths can bypass a TLS misconfiguration.
/// </summary>
internal sealed class EtcdTlsOptionsValidator : IValidateOptions<EtcdOptions>
{
    private const string ClientAuthOid = "1.3.6.1.5.5.7.3.2";
    private const string AnyExtendedKeyUsageOid = "2.5.29.37.0";

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, EtcdOptions options)
    {
        var failures = Validate(options);
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Validates the TLS settings and returns all failures. Never includes the options
    /// object, certificate contents, or the client certificate password in a failure.
    /// </summary>
    internal static IReadOnlyList<string> Validate(EtcdOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        var sslOptions = options.SslOptions ?? new SslOptions();
        var endpoints = ParseEndpoints(options.EndPoints, failures);

        var hasTlsSettings = HasAnyTlsSetting(sslOptions);

        ValidateBasicSettings(options, failures, endpoints);

        if (!sslOptions.UseSsl)
        {
            ValidateNonSsl(hasTlsSettings, failures, endpoints);
            return failures;
        }

        foreach (var endpoint in endpoints)
        {
            if (!IsHttps(endpoint))
            {
                failures.Add($"The etcd endpoint '{endpoint}' uses http:// but SslOptions.UseSsl is true.");
            }
        }

        ValidateSslSettings(sslOptions, failures);
        return failures;
    }

    private static void ValidateBasicSettings(EtcdOptions options, List<string> failures, List<Uri> endpoints)
    {
        if (!string.IsNullOrWhiteSpace(options.RootKey) &&
            string.Equals(options.RootKey.Trim(), "/", StringComparison.Ordinal))
        {
            failures.Add("RootKey must be a non-root prefix; '/' would load every key in etcd.");
        }

        if (endpoints.Count > 1 &&
            endpoints.Select(endpoint => endpoint.Scheme).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            failures.Add("All etcd endpoints must use the same URI scheme; mixed http:// and https:// endpoints are not supported.");
        }
    }

    private static void ValidateNonSsl(bool hasTlsSettings, List<string> failures, List<Uri> endpoints)
    {
        if (hasTlsSettings)
        {
            failures.Add("TLS settings are configured but SslOptions.UseSsl is false. Set UseSsl to true or remove the TLS settings; the library will not silently fall back to plaintext.");
        }

        foreach (var endpoint in endpoints)
        {
            if (IsHttps(endpoint))
            {
                failures.Add($"The etcd endpoint '{endpoint}' uses https:// but SslOptions.UseSsl is false.");
            }
        }
    }

    /// <summary>
    /// Throws <see cref="OptionsValidationException"/> when the TLS options are invalid.
    /// </summary>
    internal static void ValidateAndThrow(EtcdOptions options)
    {
        var failures = Validate(options);
        if (failures.Count > 0)
        {
            throw new OptionsValidationException(nameof(EtcdOptions), typeof(EtcdOptions), failures);
        }
    }

    private static bool HasAnyTlsSetting(SslOptions sslOptions) =>
        !string.IsNullOrWhiteSpace(sslOptions.ServerCertificateAuthorityPath) ||
        sslOptions.ClientCertificate is not null ||
        !string.IsNullOrWhiteSpace(sslOptions.ClientCertificatePath) ||
        !string.IsNullOrWhiteSpace(sslOptions.ClientCertificateKeyPath) ||
        !string.IsNullOrWhiteSpace(sslOptions.ClientCertificatePassword);

    private static bool IsHttps(Uri endpoint) =>
        string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static List<Uri> ParseEndpoints(string? endPoints, List<string> failures)
    {
        var result = new List<Uri>();
        if (string.IsNullOrWhiteSpace(endPoints))
        {
            return result;
        }

        var values = endPoints.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (values.Length == 0)
        {
            failures.Add("EndPoints must contain at least one etcd endpoint.");
            return result;
        }

        foreach (var value in values)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
                !(string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
                string.IsNullOrWhiteSpace(endpoint.Host))
            {
                failures.Add($"The etcd endpoint '{value}' is not a valid absolute http:// or https:// URL.");
                continue;
            }

            result.Add(endpoint);
        }

        return result;
    }

    private static void ValidateSslSettings(SslOptions sslOptions, List<string> failures)
    {
        var hasInMemoryCertificate = sslOptions.ClientCertificate is not null;
        var hasCertificatePath = !string.IsNullOrWhiteSpace(sslOptions.ClientCertificatePath);
        var hasKeyPath = !string.IsNullOrWhiteSpace(sslOptions.ClientCertificateKeyPath);
        var hasPassword = !string.IsNullOrWhiteSpace(sslOptions.ClientCertificatePassword);

        if (hasInMemoryCertificate && (hasCertificatePath || hasKeyPath || hasPassword))
        {
            failures.Add("ClientCertificate cannot be combined with ClientCertificatePath, ClientCertificateKeyPath, or ClientCertificatePassword.");
        }

        if (hasKeyPath && !hasCertificatePath)
        {
            failures.Add("ClientCertificateKeyPath requires ClientCertificatePath.");
        }

        if (hasPassword && !hasCertificatePath)
        {
            failures.Add("ClientCertificatePassword requires ClientCertificatePath.");
        }

        if (!string.IsNullOrWhiteSpace(sslOptions.ServerCertificateAuthorityPath))
        {
            ValidateCertificateAuthority(sslOptions.ServerCertificateAuthorityPath, failures);
        }

        if (hasInMemoryCertificate)
        {
            ValidateInMemoryClientCertificate(sslOptions.ClientCertificate!, failures);
            return;
        }

        if (hasCertificatePath)
        {
            ValidateFileClientCertificate(sslOptions, hasKeyPath, failures);
        }
    }

    private static void ValidateCertificateAuthority(string path, List<string> failures)
    {
        if (!File.Exists(path))
        {
            failures.Add($"The etcd server CA certificate file '{path}' does not exist.");
            return;
        }

        X509Certificate2Collection? certificates = null;
        try
        {
            certificates = EtcdTlsCertificateLoader.LoadCertificateAuthorities(path);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            failures.Add($"The etcd server CA certificate file '{path}' could not be loaded as a certificate.");
        }
        finally
        {
            if (certificates is not null)
            {
                EtcdTlsCertificateLoader.DisposeCertificates(certificates);
            }
        }
    }

    private static void ValidateInMemoryClientCertificate(X509Certificate2 certificate, List<string> failures)
    {
        if (!certificate.HasPrivateKey)
        {
            failures.Add("The configured in-memory etcd client certificate does not contain a private key.");
        }

        if (!HasClientAuthExtendedKeyUsage(certificate))
        {
            failures.Add("The configured in-memory etcd client certificate does not include the clientAuth extended key usage.");
        }

        ValidateCertificateValidity(certificate, "The configured in-memory etcd client certificate", failures);
    }

    private static void ValidateFileClientCertificate(SslOptions sslOptions, bool hasKeyPath, List<string> failures)
    {
        var certificatePath = sslOptions.ClientCertificatePath!;

        if (!File.Exists(certificatePath))
        {
            failures.Add($"The etcd client certificate file '{certificatePath}' does not exist.");
            return;
        }

        if (hasKeyPath && !File.Exists(sslOptions.ClientCertificateKeyPath!))
        {
            failures.Add($"The etcd client certificate key file '{sslOptions.ClientCertificateKeyPath}' does not exist.");
            return;
        }

        X509Certificate2Collection? certificates = null;
        try
        {
            certificates = EtcdTlsCertificateLoader.LoadClientCertificates(sslOptions);
            var certificate = EtcdTlsCertificateLoader.FindCertificateWithPrivateKey(certificates);
            if (certificate is null)
            {
                failures.Add($"The etcd client certificate loaded from '{certificatePath}' does not contain a private key.");
                return;
            }

            if (!HasClientAuthExtendedKeyUsage(certificate))
            {
                failures.Add($"The etcd client certificate loaded from '{certificatePath}' does not include the clientAuth extended key usage.");
            }

            ValidateCertificateValidity(certificate, "The etcd client certificate", failures);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            failures.Add($"The etcd client certificate could not be loaded from '{certificatePath}'. Verify the certificate, key path, and password.");
        }
        finally
        {
            if (certificates is not null)
            {
                EtcdTlsCertificateLoader.DisposeCertificates(certificates);
            }
        }
    }

    private static bool HasClientAuthExtendedKeyUsage(X509Certificate2 certificate)
    {
        foreach (var extension in certificate.Extensions)
        {
            if (extension is not X509EnhancedKeyUsageExtension enhancedKeyUsage)
            {
                continue;
            }

            foreach (var oid in enhancedKeyUsage.EnhancedKeyUsages)
            {
                if (oid.Value == ClientAuthOid || oid.Value == AnyExtendedKeyUsageOid)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void ValidateCertificateValidity(
        X509Certificate2 certificate,
        string description,
        List<string> failures)
    {
        var now = DateTime.UtcNow;

        if (certificate.NotAfter.ToUniversalTime() <= now)
        {
            failures.Add($"{description} is expired.");
        }
        else if (certificate.NotBefore.ToUniversalTime() > now)
        {
            failures.Add($"{description} is not yet valid.");
        }
    }
}

using dotnet_etcd;
using dotnet_etcd.interfaces;
using Grpc.Core;
using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace ROOT.Etcd.Configuration;

/// <summary>
/// Interface for creating etcd clients.
/// </summary>
public interface IEtcdClientFactory
{
    /// <summary>
    /// Creates an etcd client with the specified options.
    /// </summary>
    /// <param name="options">Etcd connection settings and configuration.</param>
    /// <returns>A new configured etcd client.</returns>
    IEtcdClient CreateClient(EtcdOptions options);
}

/// <inheritdoc />
public class EtcdClientFactory : IEtcdClientFactory
{
    /// <inheritdoc />
    public IEtcdClient CreateClient(EtcdOptions options) 
    {
        EtcdTlsOptionsValidator.ValidateAndThrow(options);

        return new EtcdClient(options.EndPoints, configureChannelOptions: channelOptions =>
            ConfigureChannel(options, channelOptions));
    }

    /// <summary>
    /// Configures the gRPC channel with credentials based on security settings.
    /// </summary>
    /// <param name="options">Etcd options including the SslOptions settings.</param>
    /// <param name="channelOptions">The channel options to configure.</param>
    internal static void ConfigureChannel(EtcdOptions options, GrpcChannelOptions channelOptions)
    {
        // Keep validation here as well: the DI path and internal callers must not be
        // able to create a channel that bypasses the public factory's validation.
        EtcdTlsOptionsValidator.ValidateAndThrow(options);

        var sslOptions = options.SslOptions;

        if (sslOptions is null || !sslOptions.UseSsl)
        {
            channelOptions.Credentials = ChannelCredentials.Insecure;
            return;
        }

        channelOptions.Credentials = ChannelCredentials.SecureSsl;

        if (!EtcdTlsOptionsValidator.HasCustomTlsMaterial(sslOptions))
        {
            return;
        }

        channelOptions.HttpHandler = CreateTlsHttpHandler(sslOptions);
        channelOptions.DisposeHttpClient = true;
    }

    /// <summary>
    /// Creates an HTTP handler that carries the custom CA trust and/or client certificate.
    /// Certificates loaded from disk are owned by the handler and disposed with it.
    /// </summary>
    private static HttpMessageHandler CreateTlsHttpHandler(SslOptions sslOptions)
    {
        var ownedCertificates = new List<X509Certificate2>();
        SocketsHttpHandler? socketsHandler = null;

        try
        {
            socketsHandler = new SocketsHttpHandler
            {
                KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                SslOptions = new SslClientAuthenticationOptions
                {
                    // gRPC always runs over HTTP/2, and setting ALPN explicitly is required
                    // on some platforms (notably Windows/Schannel).
                    ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http2 }
                }
            };

            X509Certificate2? caCertificate = null;
            if (!string.IsNullOrWhiteSpace(sslOptions.ServerCertificateAuthorityPath))
            {
                caCertificate = EtcdTlsCertificateLoader.LoadCertificateAuthority(
                    sslOptions.ServerCertificateAuthorityPath);
                ownedCertificates.Add(caCertificate);

                socketsHandler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, sslPolicyErrors) =>
                    ValidateServerCertificate(certificate, caCertificate, sslPolicyErrors);
            }

            if (sslOptions.ClientCertificate is not null)
            {
                // Caller-owned: never track or dispose this certificate.
                socketsHandler.SslOptions.ClientCertificates = new X509CertificateCollection
                {
                    sslOptions.ClientCertificate
                };
            }
            else if (!string.IsNullOrWhiteSpace(sslOptions.ClientCertificatePath))
            {
                var clientCertificates = EtcdTlsCertificateLoader.LoadClientCertificates(sslOptions);
                foreach (var certificate in clientCertificates)
                {
                    ownedCertificates.Add(certificate);
                }

                socketsHandler.SslOptions.ClientCertificates = new X509CertificateCollection(clientCertificates);
            }

            return new CertificateOwningHandler(socketsHandler, ownedCertificates);
        }
        catch
        {
            socketsHandler?.Dispose();

            foreach (var certificate in ownedCertificates)
            {
                certificate.Dispose();
            }

            throw;
        }
    }

    /// <summary>
    /// Validates the etcd server certificate against a custom root CA.
    /// </summary>
    private static bool ValidateServerCertificate(
        X509Certificate? certificate,
        X509Certificate2 caCertificate,
        SslPolicyErrors sslPolicyErrors)
    {
        if (sslPolicyErrors == SslPolicyErrors.None)
        {
            return true;
        }

        if (certificate is null ||
            (sslPolicyErrors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0 ||
            (sslPolicyErrors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
        {
            return false;
        }

        using var serverCertificate = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(caCertificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;

        return chain.Build(serverCertificate);
    }

    /// <summary>
    /// Disposes certificates loaded by the library when the handler is disposed.
    /// Caller-supplied certificates are not included in this list.
    /// </summary>
    private sealed class CertificateOwningHandler(
        HttpMessageHandler innerHandler,
        IReadOnlyList<X509Certificate2> ownedCertificates) : DelegatingHandler(innerHandler)
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var certificate in ownedCertificates)
                {
                    certificate.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}

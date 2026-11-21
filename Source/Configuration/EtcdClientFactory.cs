using dotnet_etcd;
using dotnet_etcd.interfaces;
using Grpc.Core;
using Grpc.Net.Client;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
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

        // Always install the handler for TLS so the explicit connect timeout,
        // proxy policy, and ALPN settings apply even when only the OS trust
        // store is used.
        channelOptions.HttpHandler = CreateTlsHttpHandler(sslOptions);
        channelOptions.DisposeHttpClient = true;
    }

    /// <summary>
    /// Creates an HTTP handler that carries the custom CA trust and/or client certificate.
    /// Certificates loaded from disk are owned by the handler and disposed with it.
    /// </summary>
    private static CertificateOwningHandler CreateTlsHttpHandler(SslOptions sslOptions)
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
                UseProxy = sslOptions.UseProxy,
                SslOptions = new SslClientAuthenticationOptions
                {
                    // gRPC always runs over HTTP/2, and setting ALPN explicitly is required
                    // on some platforms (notably Windows/Schannel).
                    ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http2 }
                }
            };

            if (!string.IsNullOrWhiteSpace(sslOptions.ServerCertificateAuthorityPath))
            {
                var caCertificates = EtcdTlsCertificateLoader.LoadCertificateAuthorities(
                    sslOptions.ServerCertificateAuthorityPath);

                // CustomRootTrust makes the configured CAs exclusive instead of additive,
                // and serverAuth prevents a certificate issued for another purpose from
                // being accepted for the etcd server.
                var chainPolicy = new X509ChainPolicy
                {
                    TrustMode = X509ChainTrustMode.CustomRootTrust,
                    RevocationMode = sslOptions.RevocationMode,
                    DisableCertificateDownloads = true
                };

                foreach (var caCertificate in caCertificates)
                {
                    ownedCertificates.Add(caCertificate);
                    chainPolicy.CustomTrustStore.Add(caCertificate);
                }

                chainPolicy.ApplicationPolicy.Add(new Oid(ServerAuthOid));
                socketsHandler.SslOptions.CertificateChainPolicy = chainPolicy;
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
    /// Server authentication extended key usage: 1.3.6.1.5.5.7.3.1.
    /// </summary>
    private const string ServerAuthOid = "1.3.6.1.5.5.7.3.1";

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

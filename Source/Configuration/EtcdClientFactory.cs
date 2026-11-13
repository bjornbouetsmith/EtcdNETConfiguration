using dotnet_etcd;
using dotnet_etcd.interfaces;
using Grpc.Core;
using Grpc.Net.Client;
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
        return new EtcdClient(options.EndPoints, configureChannelOptions: channelOptions =>
            ConfigureChannel(options, channelOptions));
    }

    /// <summary>
    /// Configures the gRPC channel with credentials based on security settings.
    /// </summary>
    /// <param name="options">Etcd options including the UseInsecureChannels setting.</param>
    /// <param name="channelOptions">The channel options to configure.</param>
    internal static void ConfigureChannel(EtcdOptions options, GrpcChannelOptions channelOptions)
    {
        if (options.UseInsecureChannels)
        {
            channelOptions.Credentials = ChannelCredentials.Insecure;
            return;
        }

        channelOptions.Credentials = ChannelCredentials.SecureSsl;

        if (!string.IsNullOrWhiteSpace(options.ServerCertificateAuthorityPath))
        {
            channelOptions.HttpHandler = CreateTlsHttpHandler(options.ServerCertificateAuthorityPath);
        }
    }

    /// <summary>
    /// Creates an HTTP handler that trusts the CA certificate at <paramref name="caCertificatePath"/>
    /// when validating the etcd server certificate.
    /// </summary>
    /// <param name="caCertificatePath">Path to a PEM-encoded CA certificate.</param>
    /// <returns>A handler configured with custom server certificate validation.</returns>
    private static SocketsHttpHandler CreateTlsHttpHandler(string caCertificatePath)
    {
        var caCertificate = X509CertificateLoader.LoadCertificateFromFile(caCertificatePath);

        return new SocketsHttpHandler
        {
            KeepAlivePingDelay = System.TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = System.TimeSpan.FromSeconds(30),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            SslOptions = new SslClientAuthenticationOptions
            {
                // gRPC always runs over HTTP/2, and setting ALPN explicitly is required
                // on some platforms (notably Windows/Schannel).
                ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http2 },
                RemoteCertificateValidationCallback = (_, certificate, _, sslPolicyErrors) =>
                    ValidateServerCertificate(certificate, caCertificate, sslPolicyErrors)
            }
        };
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
}

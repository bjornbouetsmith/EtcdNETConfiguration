# Mutual TLS (mTLS)

Mutual TLS requires `UseSsl = true`, an `https://` endpoint, and a client certificate. The etcd server must also be configured to require client certificates.

## etcd server configuration

The server must require and validate client certificates:

```text
--client-cert-auth=true
--trusted-ca-file=/certs/ca.crt
--cert-file=/certs/server.crt
--key-file=/certs/server.key
```

## PEM certificate and key files

```csharp
builder.AddEtcdConfiguration(options =>
{
    options.EndPoints = "https://etcd:2379";
    options.SslOptions = new SslOptions
    {
        UseSsl = true,
        ServerCertificateAuthorityPath = "/certs/ca.crt",
        ClientCertificatePath = "/certs/client.crt",
        ClientCertificateKeyPath = "/certs/client.key"
    };
});
```

## PKCS#12 file

```csharp
options.SslOptions = new SslOptions
{
    UseSsl = true,
    ServerCertificateAuthorityPath = "/certs/ca.crt",
    ClientCertificatePath = "/certs/client.pfx",
    ClientCertificatePassword = configuration["Etcd:ClientCertificatePassword"]
};
```

## In-memory certificate

Use an in-memory certificate when the certificate comes from the Windows certificate store, Key Vault, an HSM, or a non-exportable key:

```csharp
using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
store.Open(OpenFlags.ReadOnly);

var certificate = store.Certificates
    .Find(X509FindType.FindByThumbprint, thumbprint, validOnly: true)
    .OfType<X509Certificate2>()
    .Single();

options.SslOptions = new SslOptions
{
    UseSsl = true,
    ServerCertificateAuthorityPath = "/certs/ca.crt",
    ClientCertificate = certificate
};

// Keep the certificate alive until the host stops. The library never disposes it.
```

`ClientCertificate` is mutually exclusive with the file-based client-certificate settings.

## Certificate requirements and limitations

- The client certificate must include `extendedKeyUsage=clientAuth`. The library enforces this before connecting. etcd itself may accept a certificate without that EKU, so do not rely on the server to reject it.
- Keep the private key readable only by the application, for example `chmod 600`.
- The library owns certificates loaded from `ClientCertificatePath` and disposes them with the channel handler, including on failure paths. Caller-supplied `ClientCertificate` instances are never disposed.
- PKCS#12 collections are supported, so intermediate certificates are presented to the server. PEM loading uses the leaf certificate only; use PKCS#12 when the client certificate depends on an intermediate chain.
- `ServerCertificateAuthorityPath` accepts a single DER certificate or a PEM bundle with multiple trusted roots. Every certificate in the bundle is added to the custom trust store.
- Certificate rotation requires an application restart because the channel is created at startup.
- On Windows, Schannel cannot use ephemeral private keys. PEM and PKCS#12 material is re-imported into a temporary user key container without `PersistKeySet`; the key is released when the library disposes the certificate.
- mTLS authenticates the client, but etcd RBAC still decides which keys the client may read. A valid client certificate with an overly broad role can read unrelated configuration. Scope the etcd role to the configuration prefix.
- `RedactConfigurationValues` must remain `true` in production so configuration values are not written to logs.

## Related pages

- [TLS](TLS)
- [Configuration Options](Configuration-Options)
- [Validation and Startup](Validation-and-Startup)
- [Test Certificates](Test-Certificates)

# TLS

Use TLS when the connection between the application and etcd must be encrypted and the etcd server certificate must be validated.

## Enable TLS

Set `SslOptions.UseSsl` to `true` and point `EndPoints` at an `https://` URL.

If the server certificate is signed by a private CA, also set `SslOptions.ServerCertificateAuthorityPath` to the PEM file containing that CA:

```csharp
builder.AddEtcdConfiguration(options =>
{
    options.EndPoints = "https://127.0.0.1:2379";
    options.SslOptions = new SslOptions
    {
        UseSsl = true,
        ServerCertificateAuthorityPath = "/certs/ca.crt"
    };
});
```

When `SslOptions` is `null` or `SslOptions.UseSsl` is `false`, the gRPC channel is configured with `ChannelCredentials.Insecure`. When `UseSsl` is `true`, the channel uses `ChannelCredentials.SecureSsl`.

## Trust store behavior

When `ServerCertificateAuthorityPath` is not set, the operating system trust store is used.

The CA file may contain a single DER certificate or a PEM bundle with multiple roots. Every certificate in a PEM bundle is trusted.

When a custom CA is configured:

- Only that CA is trusted (`CustomRootTrust`), not an additive trust store.
- The server certificate must be valid for `serverAuth`.
- Certificate downloads are disabled during validation.
- `RevocationMode` controls revocation checking.

The default `RevocationMode` is `NoCheck`, which keeps private CAs without CRL or OCSP endpoints working. Use `Online` or `Offline` when revocation information is available.

The gRPC handler uses an explicit 10-second connect timeout. The system proxy is disabled by default so TLS traffic cannot be silently routed through an unexpected proxy; set `SslOptions.UseProxy = true` when a proxy is required.

## Configuration reference

| Option | Description |
|-|-|
| `SslOptions.UseSsl` | The real TLS switch. The endpoint scheme alone does not enable TLS. |
| `SslOptions.ServerCertificateAuthorityPath` | PEM CA certificate used to validate the etcd server certificate. |
| `SslOptions.RevocationMode` | Revocation mode for the etcd server certificate chain. Defaults to `NoCheck`. |
| `SslOptions.UseProxy` | Whether to use the system proxy. Defaults to `false`. |

See [Configuration Options](Configuration-Options) for the full options table and validation rules.

## TLS failure behavior

TLS validation is fail-closed. Startup fails when:

- `UseSsl` is `false` and any TLS setting is present.
- `UseSsl` is `false` and any endpoint is `https://`, or `UseSsl` is `true` and any endpoint is `http://`.
- Endpoints mix `http://` and `https://`.
- The CA file is missing, corrupt, or cannot be parsed.
- The server certificate is not valid for `serverAuth`.

See [Validation and Startup](Validation-and-Startup) for the complete list of startup failures.

## Mutual TLS

If the etcd server requires a client certificate, see [Mutual TLS](Mutual-TLS).

# Validation and Startup

The library is designed to fail closed. Invalid options, TLS problems, or an unreachable etcd cluster prevent the host from starting rather than allowing the application to run with empty or insecure configuration.

## Options validation

`EtcdOptions` uses DataAnnotations attributes. `AddEtcdConfiguration` registers them with:

```csharp
services.AddOptions<EtcdOptions>()
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

Validation therefore runs:

- When the options are first resolved.
- When the host starts, through `ValidateOnStart()`.
- Before the first etcd connection.

Invalid options throw an `OptionsValidationException`.

## TLS validation

A dedicated TLS validator runs through the options pipeline, the public `EtcdClientFactory`, and the initial configuration source. It is also invoked before creating the client, so a custom client factory cannot bypass the TLS checks.

TLS startup fails when any of the following is true:

- `UseSsl` is `false` and any TLS setting is present.
- `UseSsl` is `false` and any endpoint is `https://`, or `UseSsl` is `true` and any endpoint is `http://`.
- Endpoints mix `http://` and `https://`.
- `ClientCertificate` is combined with any file-based client-certificate setting.
- A key path or password is set without a certificate path.
- CA, certificate, or key files are missing, corrupt, or cannot be parsed.
- A PKCS#12 password is wrong, a PEM key does not match its certificate, or a client certificate has no private key, is expired, or lacks the `clientAuth` EKU.

## Initial configuration load

The initial configuration load also fails fast. If etcd cannot be reached, or TLS or authentication fails, the host does not start with empty configuration.

The initial load happens synchronously while the host is being built, so configuration is available before hosted services start. See [How It Works](How-It-Works).

## `IEtcdClient` registration ordering

`AddEtcdConfiguration` registers `IEtcdClient` with `TryAddSingleton`. When TLS is configured, a pre-existing `IEtcdClient` registration fails startup because it would silently bypass the TLS-configured client.

Register `AddEtcdConfiguration` before any other `IEtcdClient` registration:

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

// Register other etcd-related services after AddEtcdConfiguration.
```

A registration added after this call can still replace the client, so keep the TLS configuration authoritative.

## Verifying startup

If the host fails to start, check the exception message first:

- `OptionsValidationException` usually means an invalid endpoint, root key, interval, or timeout.
- TLS validation errors point to a certificate, CA, key, or scheme mismatch.
- Connection errors mean etcd is unreachable or the endpoint/port is wrong.

See [Troubleshooting](Troubleshooting) and [Configuration Options](Configuration-Options).

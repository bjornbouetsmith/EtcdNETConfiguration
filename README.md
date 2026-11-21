[![Build and Test](https://github.com/bjornbouetsmith/EtcdNETConfiguration/actions/workflows/ci.yml/badge.svg)](https://github.com/bjornbouetsmith/EtcdNETConfiguration/actions/workflows/ci.yml)'
[![Quality gate status](https://sonarcloud.io/api/project_badges/measure?project=ETCDConfiguration&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=ETCDConfiguration)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=ETCDConfiguration&metric=coverage)](https://sonarcloud.io/summary/new_code?id=ETCDConfiguration)

# EtcdNETConfiguration — Overview

`EtcdNETConfiguration` is a **C# (.NET) library that integrates `etcd` as a backing store for the standard `Microsoft.Extensions.Configuration` system**. 
It lets a .NET application load and continuously refresh its configuration from an etcd cluster, so config changes made in etcd propagate live into the app's `IConfiguration` (and via the `IOptions` machinery).

It is published as a NuGet package (`ROOT.Etcd.Configuration`) under the namespace `ROOT.Etcd.Configuration`.

## How it works end-to-end

1. `AddEtcdConfiguration(...)` registers options, a shared `EtcdConfiguration`, the loader, and the `EtcdPoller` hosted service, and adds an `EtcdConfigurationSource` to the host configuration.
2. The **options** are validated using `System.ComponentModel.DataAnnotations` and a fail-closed TLS validator when they are first resolved, through `ValidateOnStart()` when the host starts, and before the first etcd connection. Invalid options throw an `OptionsValidationException` and prevent the host from starting.
3. At startup the **source** synchronously loads all keys under `RootKey` (default `/configuration`) into the shared instance — so config is available immediately even before the poller runs. If the initial load fails, the host does not start with empty configuration.
4. The **poller** then re-reads etcd every `RefreshInterval`, compares against the last snapshot, and only publishes/logs when something actually changed. Setting `RefreshInterval` to `TimeSpan.Zero` disables background polling, leaving only the initial startup load.
5. Publishing raises `ConfigurationChanged`, which the **provider** converts into `OnReload()`, updating `IConfiguration` and any `IOptions`-bound settings in place.

## Notable design points

- **Live updates** without app restart (poll-based, not etcd watch-based).
- **Change auditing**: logs added/changed/removed keys; can redact values (`***`) to avoid leaking secrets.
- **Options validation** through DataAnnotations, with `ValidateOnStart()` so misconfiguration is caught before the application begins serving traffic.

## In short

A drop-in etcd-backed configuration provider for .NET's dependency-injection/hosting configuration system, with background polling, change logging/redaction.

## How to use it

Install nuget package.

Update your start up class.

```csharp
var builder = Host.CreateDefaultBuilder();

builder.AddEtcdConfiguration(options =>
{
    options.EndPoints = "http://127.0.0.1:2379";
    options.RootKey = "/myroot";
    options.RefreshInterval = TimeSpan.FromMinutes(5);
});
```

Thats it.

Library will look for keys with the prefix `/myroot` in the example above.

And below that the configuration is defined in the same way as you would define a dictionary and load it into the [configuration](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration) system.

So if you add the following key/values to etcd:

|Key|Value|
|-|-|
|"/mykey/connectionstrings:0"| databaserver1|
|"/mykey/connectionstrings:1"| databaserver2|

It could be bound to a class 
```csharp
public class Configuration
{
    public List<string> ConnectionStrings { get; set; }
}
```

With the simple code:

```csharp
builder.ConfigureServices((ctx, services) =>
{
    services.Configure<Configuration>(ctx.Configuration.GetSection("/mykey"));
});

var options = host.Services.GetRequiredService<IOptions<Configuration>>();
```

And you would be able to depend on `IOptions<Configuration>`

## Options and validation

`EtcdOptions` uses DataAnnotations attributes. `AddEtcdConfiguration` registers them with:

```csharp
services.AddOptions<EtcdOptions>()
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

In addition, a dedicated TLS validator runs through the options pipeline, the
public `EtcdClientFactory`, and the initial configuration source. TLS validation
is fail-closed: a misconfiguration throws an `OptionsValidationException` and
the host does not start.

| Option | Default | Validation |
|-|-|-|
| `EndPoints` | `http://127.0.0.1:2379` | Required; comma-separated absolute `http://` or `https://` URLs. The scheme must match `UseSsl`; mixed schemes fail. |
| `RootKey` | `/configuration` | Required; must be a non-empty string. |
| `RefreshInterval` | 1 minute | Must be between `TimeSpan.Zero` and 24 hours, inclusive. `TimeSpan.Zero` disables background polling. |
| `ConfigurationTimeout` | 1 second | Must be between 1 second and 60 seconds, inclusive. |
| `RedactConfigurationValues` | `true` | No validation. Keep `true` in production. |
| `SslOptions` | `null` | Null means plaintext, but only when every endpoint is `http://`. |
| `SslOptions.UseSsl` | `false` | The real TLS switch. The endpoint scheme alone does not enable TLS. |
| `SslOptions.ServerCertificateAuthorityPath` | `null` | Path to a PEM CA. The file must exist and load as a certificate. |
| `SslOptions.ClientCertificate` | `null` | Caller-owned in-memory client certificate. Must contain a private key and `clientAuth` EKU. |
| `SslOptions.ClientCertificatePath` | `null` | PEM certificate when a key path is set; otherwise PKCS#12. Must exist and load. |
| `SslOptions.ClientCertificateKeyPath` | `null` | PEM private key. Requires `ClientCertificatePath`; must exist and match the certificate. |
| `SslOptions.ClientCertificatePassword` | `null` | Password for a PKCS#12 file or encrypted PEM key. Requires `ClientCertificatePath`. Never logged or included in exceptions. |
| `SslOptions.RevocationMode` | `NoCheck` | Revocation mode used for the etcd server certificate chain. |

TLS startup fails when any of the following is true:

- `UseSsl` is `false` and any TLS setting is present.
- `UseSsl` is `false` and any endpoint is `https://`, or `UseSsl` is `true` and
  any endpoint is `http://`.
- Endpoints mix `http://` and `https://`.
- `ClientCertificate` is combined with any file-based client-certificate setting.
- A key path or password is set without a certificate path.
- CA, certificate, or key files are missing, corrupt, or cannot be parsed.
- A PKCS#12 password is wrong, a PEM key does not match its certificate, or a
  client certificate has no private key, is expired, or lacks the `clientAuth`
  EKU.

The initial configuration load also fails fast. If etcd cannot be reached or TLS
or authentication fails, the host does not start with empty configuration.

### Connecting over TLS

Set `SslOptions.UseSsl` to `true` and point `EndPoints` at an `https://` URL.
If the server certificate is signed by a private CA, also set
`SslOptions.ServerCertificateAuthorityPath` to the PEM file containing that CA:

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

When `SslOptions` is `null` or `SslOptions.UseSsl` is `false`, the gRPC channel
is configured with `ChannelCredentials.Insecure`. When `UseSsl` is `true`, the
channel uses `ChannelCredentials.SecureSsl`. When
`ServerCertificateAuthorityPath` is not set, the operating system trust store
is used.

When a custom CA is configured, only that CA is trusted (`CustomRootTrust`, not
an additive trust store), the server certificate must be valid for `serverAuth`,
certificate downloads are disabled during validation, and `RevocationMode`
controls revocation checking. The default `NoCheck` keeps private CAs without CRL
or OCSP endpoints working; use `Online` or `Offline` when revocation information
is available.

### Connecting with mutual TLS (mTLS)

Mutual TLS requires `UseSsl = true`, an `https://` endpoint, and a client
certificate. The etcd server must be configured to require client certificates:

```text
--client-cert-auth=true
--trusted-ca-file=/certs/ca.crt
--cert-file=/certs/server.crt
--key-file=/certs/server.key
```

PEM certificate and key files:

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

PKCS#12 file:

```csharp
options.SslOptions = new SslOptions
{
    UseSsl = true,
    ServerCertificateAuthorityPath = "/certs/ca.crt",
    ClientCertificatePath = "/certs/client.pfx",
    ClientCertificatePassword = configuration["Etcd:ClientCertificatePassword"]
};
```

In-memory certificate, for example from the Windows certificate store, Key Vault,
an HSM, or a non-exportable key:

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

Certificate requirements and limitations:

- The client certificate must include `extendedKeyUsage=clientAuth`. The library
  enforces this before connecting. etcd itself may accept a certificate without
  that EKU, so do not rely on the server to reject it.
- Keep the private key readable only by the application, for example `chmod 600`.
- The library owns certificates loaded from `ClientCertificatePath` and disposes
  them with the channel handler, including on failure paths. Caller-supplied
  `ClientCertificate` instances are never disposed.
- PKCS#12 collections are supported, so intermediate certificates are presented
  to the server. PEM loading uses the leaf certificate only; use PKCS#12 when
  the client certificate depends on an intermediate chain.
- Certificate rotation requires an application restart because the channel is
  created at startup.
- On Windows, Schannel cannot use ephemeral private keys. PEM and PKCS#12
  material is re-imported into a temporary user key container without
  `PersistKeySet`; the key is released when the library disposes the certificate.
- mTLS authenticates the client, but etcd RBAC still decides which keys the
  client may read. A valid client certificate with an overly broad role can read
  unrelated configuration. Scope the etcd role to the configuration prefix.
- `RedactConfigurationValues` must remain `true` in production so configuration
  values are not written to logs.

### Supported TimeSpan ranges

**`RefreshInterval`** controls how often the background poller checks etcd:

- **Minimum:** `TimeSpan.Zero` (`00:00:00`).
- **Maximum:** `1.00:00:00` (24 hours).
- Any positive value up to 24 hours is supported; the smallest positive value is `00:00:00.0000001` (one tick).

**`ConfigurationTimeout`** limits individual etcd calls:

- **Minimum:** `00:00:01` (1 second).
- **Maximum:** `00:01:00` (60 seconds).

### RefreshInterval and TimeSpan.Zero

Setting `RefreshInterval` to `TimeSpan.Zero` disables background polling. The initial configuration is still loaded once at startup by the configuration source, but `EtcdPoller` does not create a timer and does not poll for changes afterward. It logs:

```text
ETCD polling is disabled because RefreshInterval is set to TimeSpan.Zero
```

Because polling is disabled, changes made in etcd after startup will not be reflected in the application until it is restarted.

## Source code testing

Tests are a mix of unit tests and integration tests that uses a real etcd server.

Tests will auto start the etcd docker container, but it requires that a docker runtime is available.

TLS integration tests additionally start `Source/docker-compose.tls.yml`, and mTLS
tests start `Source/docker-compose.mtls.yml`. The self-signed test certificates are
generated automatically by the test assembly when they are missing or older than one
day. On Linux/macOS `openssl` is used directly; on Windows the generation runs in an
Alpine Docker container because `bash` and `openssl` are usually not installed there.
The container image can be overridden with `ETCD_TLS_CERT_DOCKER_IMAGE`. The generated
files are git-ignored.

The TLS tests run against `https://127.0.0.1:12379` and can be pointed at an external
TLS-enabled etcd by setting:

```text
ETCD_TLS_ENDPOINT=https://my-etcd:2379
ETCD_TLS_CA_CERT=/path/to/ca.crt
```

The mTLS tests run against `https://127.0.0.1:12389` and can be pointed at an external
mTLS-enabled etcd by setting:

```text
ETCD_MTLS_ENDPOINT=https://my-etcd:2379
ETCD_MTLS_CLIENT_CERT=/path/to/client.crt
ETCD_MTLS_CLIENT_KEY=/path/to/client.key
ETCD_TLS_CA_CERT=/path/to/ca.crt
```

The negative mTLS tests use certificates generated for the local test CA and are
skipped when `ETCD_MTLS_ENDPOINT` points at an external server.

## Warning
When using this library do not use `localhost` as the hostname for the etcd service. This can cause extreme delays because the DNS resolution returns an IP address family that the service is potentially not listening on.

Windows defaults `localhost` to return an IPV6 address, and if your etcd service is not listening on IPV6 then the client will time out in connecting and then retry with IPv4.

This is not a problem in this library or the underlying service, but simply a DNS resolution problem.

To overcome this problem, don't use `localhost` and instead use `127.0.0.1` or a hostname that resolves to the actual IP address and family that the service listens on.

See [this](https://github.com/dotnet/runtime/issues/65375) for reference.

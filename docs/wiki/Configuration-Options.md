# Configuration Options

`AddEtcdConfiguration` accepts an `EtcdOptions` instance. `EtcdOptions` uses DataAnnotations attributes, and the library registers them with `ValidateDataAnnotations()` and `ValidateOnStart()`:

```csharp
services.AddOptions<EtcdOptions>()
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

In addition, a dedicated TLS validator runs through the options pipeline, the public `EtcdClientFactory`, and the initial configuration source. TLS validation is fail-closed: a misconfiguration throws an `OptionsValidationException` and the host does not start. See [Validation and Startup](Validation-and-Startup).

## Example

```csharp
builder.AddEtcdConfiguration(options =>
{
    options.EndPoints = "http://127.0.0.1:2379,http://127.0.0.1:2380";
    options.RootKey = "/myroot";
    options.RefreshInterval = TimeSpan.FromMinutes(1);
    options.ConfigurationTimeout = TimeSpan.FromSeconds(1);
    options.RedactConfigurationValues = true;
});
```

## Options reference

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

## Endpoints

`EndPoints` is a comma-separated list of absolute URLs:

```csharp
options.EndPoints = "http://127.0.0.1:2379,http://127.0.0.1:2380";
```

The endpoint scheme and `SslOptions.UseSsl` must agree:

- Use `UseSsl = false` with `http://` endpoints.
- Use `UseSsl = true` with `https://` endpoints.
- Do not mix `http://` and `https://` in the same list.

See [TLS](TLS) and [Mutual TLS](Mutual-TLS).

## Root key

`RootKey` is the etcd key prefix that is loaded into configuration. All keys starting with this value are retrieved.

```csharp
options.RootKey = "/myroot";
```

Keys use `:` as the configuration hierarchy separator:

| Key | Value |
|-|-|
| `/myroot:connectionstrings:0` | `databaserver1` |
| `/myroot:connectionstrings:1` | `databaserver2` |

The prefix is not stripped from the loaded configuration keys, so bind with `GetSection("/myroot")`. See [Getting Started](Getting-Started).

## Supported TimeSpan ranges

### `RefreshInterval`

Controls how often the background poller checks etcd:

- **Minimum:** `TimeSpan.Zero` (`00:00:00`).
- **Maximum:** `1.00:00:00` (24 hours).
- Any positive value up to 24 hours is supported; the smallest positive value is `00:00:00.0000001` (one tick).

Setting `RefreshInterval` to `TimeSpan.Zero` disables background polling. See [Refresh and Polling](Refresh-and-Polling).

### `ConfigurationTimeout`

Limits individual etcd calls:

- **Minimum:** `00:00:01` (1 second).
- **Maximum:** `00:01:00` (60 seconds).

## Redaction

`RedactConfigurationValues` defaults to `true`. When enabled, the loader replaces values in log messages with `***`:

```text
Configuration key /myroot:password was added with value ***
```

Keep redaction enabled in production so configuration values are not written to logs.

## Related pages

- [Validation and Startup](Validation-and-Startup)
- [TLS](TLS)
- [Mutual TLS](Mutual-TLS)
- [Refresh and Polling](Refresh-and-Polling)

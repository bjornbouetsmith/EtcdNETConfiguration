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
    options.RootKeys =
    [
        new EtcdRootKey { RootKey = "/myroot", Priority = 0 }
    ];
    options.ConfigurationPrefix = "/myroot";
    options.RefreshInterval = TimeSpan.FromMinutes(1);
    options.ConfigurationTimeout = TimeSpan.FromSeconds(1);
    options.RedactConfigurationValues = true;
});
```

## Options reference

| Option | Default | Validation |
|-|-|-|
| `EndPoints` | `http://127.0.0.1:2379` | Required; comma-separated absolute `http://` or `https://` URLs. The scheme must match `UseSsl`; mixed schemes fail. |
| `RootKeys` | one root: `/configuration`, priority `0` | Required; at least one root. Each root key must start with `/`, must not be `/` or end with `/`, and must not contain `//` or `:`. Priorities must be unique. Roots must not be duplicates, overlap, or nest, compared case-insensitively. |
| `ConfigurationPrefix` | `/configuration` | Required; must start with `/`, must not be `/` or end with `/`, and must not contain `//` or `:`. The prefix is output-only; it is never queried from etcd. |
| `RefreshInterval` | 1 minute | Must be between `TimeSpan.Zero` and 24 hours, inclusive. `TimeSpan.Zero` disables background polling. |
| `ConfigurationTimeout` | 1 second | One budget per configuration load, shared by every root query. Must be between 1 second and 60 seconds, inclusive. |
| `RedactConfigurationValues` | `true` | No validation. Keep `true` in production. Secret-like keys are redacted even when this is `false`. |
| `MaximumConfigurationKeyCount` | 10,000 | At least 1. A read that returns more keys fails. |
| `MaximumConfigurationSizeBytes` | 8 MiB (8,388,608) | At least 1. A read whose combined key and value size exceeds this fails. |
| `MaximumConsecutivePermanentFailures` | 5 | Zero or greater. After this many consecutive permanent poll failures the host is asked to stop. `0` keeps polling and only updates `IEtcdPollerStatus`. |
| `SslOptions` | `null` | Null means plaintext, but only when every endpoint is `http://`. |
| `SslOptions.UseSsl` | `false` | The real TLS switch. The endpoint scheme alone does not enable TLS. |
| `SslOptions.ServerCertificateAuthorityPath` | `null` | Path to a PEM CA. The file must exist and load as a certificate. |
| `SslOptions.ClientCertificate` | `null` | Caller-owned in-memory client certificate. Must contain a private key and `clientAuth` EKU. |
| `SslOptions.ClientCertificatePath` | `null` | PEM certificate when a key path is set; otherwise PKCS#12. Must exist and load. |
| `SslOptions.ClientCertificateKeyPath` | `null` | PEM private key. Requires `ClientCertificatePath`; must exist and match the certificate. |
| `SslOptions.ClientCertificatePassword` | `null` | Password for a PKCS#12 file or encrypted PEM key. Requires `ClientCertificatePath`. Never logged or included in exceptions. |
| `SslOptions.RevocationMode` | `NoCheck` | Revocation mode used for the etcd server certificate chain. |
| `SslOptions.UseProxy` | `false` | Whether the gRPC HTTP handler uses the system proxy. |

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

## Root keys and ConfigurationPrefix

`RootKeys` is a prioritized list of etcd prefixes to load. `ConfigurationPrefix` is the single output prefix under which the merged values are exposed. Only strict descendants of each root are loaded: with a root `/myroot`, `/myroot/key` is loaded and `/myroot-other/key` is ignored. The exact key `/myroot` is not loaded; store a root-level scalar as `/myroot/value`.

```csharp
options.RootKeys =
[
    new EtcdRootKey { RootKey = "/myroot", Priority = 0 }
];
options.ConfigurationPrefix = "/myroot";
```

Each root key must be a canonical etcd path:

- It must start with `/`.
- It must not be `/`, because that would load every key in etcd.
- It must not end with `/`.
- It must not contain empty segments (`//`).
- It must not contain `:`. Use `/` to separate etcd path segments.

Surrounding whitespace is trimmed for validation. Use a prefix scoped to this application. mTLS and TLS authenticate the client, but etcd RBAC still decides which keys the client may read, so scope the etcd role to the configured roots and grant only the permissions the application needs.

`ConfigurationPrefix` follows the same canonical rules, but it is never queried from etcd. It may equal one of the roots, or differ from every root.

### Multiple roots and priority

When more than one root is configured, the prefix of each returned key is stripped and the remaining `/` segments become a relative configuration key such as `:database:host`. Relative keys are merged case-insensitively, and a higher `Priority` overrides a lower one:

```csharp
options.ConfigurationPrefix = "/configuration";

options.RootKeys =
[
    new EtcdRootKey { RootKey = "/domains/example.com", Priority = 0 },
    new EtcdRootKey { RootKey = "/machines/web-01",     Priority = 10 },
    new EtcdRootKey { RootKey = "/services/checkout",   Priority = 20 }
];
```

| etcd key | Relative configuration key | Effective value |
|-|-|-|
| `/domains/example.com/Logging/Level` | `:Logging:Level` | `Debug` |
| `/machines/web-01/Logging/Level` | `:Logging:Level` | `Warning` |
| `/services/checkout/Database/Name` | `:Database:Name` | `checkout` |
| `/machines/web-01/Kafka/BootStrapServers:0` | `:Kafka:BootStrapServers:0` | `host-1` |

Final configuration:

```text
/configuration:Logging:Level = Warning
/configuration:Database:Name = checkout
/configuration:Kafka:BootStrapServers:0 = host-1
```

Priorities must be unique. Roots must not be duplicates, overlap, or nest, and those comparisons are case-insensitive (`/app` and `/APP/service` are rejected). etcd key scope matching itself stays case-sensitive, because etcd keys are case-sensitive.

etcd keys use `/` as the path hierarchy. The library maps each `/` to the `:` separator used by `IConfiguration`. Collection indices stay attached to their segment with `:`. With a root `/myroot` and `ConfigurationPrefix = "/myroot"`:

| etcd key | IConfiguration key | Value |
|-|-|-|
| `/myroot/connectionstrings:0` | `/myroot:connectionstrings:0` | `databaserver1` |
| `/myroot/connectionstrings:1` | `/myroot:connectionstrings:1` | `databaserver2` |
| `/myroot/logging/loglevel/default` | `/myroot:logging:loglevel:default` | `Information` |

Bind with `GetSection(options.ConfigurationPrefix)`. See [Getting Started](Getting-Started) and [Key Format and Case Sensitivity](Key-Format-and-Case-Sensitivity).

### Read limits and timeout

`MaximumConfigurationKeyCount` and `MaximumConfigurationSizeBytes` are cumulative for one configuration load across every root. Every key returned by etcd counts toward the limits, including keys outside a root's scope and keys that are later rejected. Overlapping sibling prefixes such as `/app` and `/application` can cause a key to be counted twice.

`ConfigurationTimeout` is one budget per configuration load. Every root query shares the same deadline; the budget is not reset per root.

### Migrating from a single root

`RootKey` was removed in 2.0. To preserve the previous binding location, configure one root and set `ConfigurationPrefix` to the old root:

```csharp
// before
options.RootKey = "/myroot";
// configuration.GetSection("/myroot")

// after
options.RootKeys = [new EtcdRootKey { RootKey = "/myroot", Priority = 0 }];
options.ConfigurationPrefix = "/myroot";
// configuration.GetSection("/myroot")
```

With multiple roots there is one output prefix, so bind against `ConfigurationPrefix` rather than each root.

## Supported TimeSpan ranges

### `RefreshInterval`

Controls how often the background poller checks etcd:

- **Minimum:** `TimeSpan.Zero` (`00:00:00`).
- **Maximum:** `1.00:00:00` (24 hours).
- Any positive value up to 24 hours is supported; the smallest positive value is `00:00:00.0000001` (one tick).

Setting `RefreshInterval` to `TimeSpan.Zero` disables background polling. See [Refresh and Polling](Refresh-and-Polling).

### `ConfigurationTimeout`

Limits one configuration load. Every root query shares the same deadline:

- **Minimum:** `00:00:01` (1 second).
- **Maximum:** `00:01:00` (60 seconds).

## Redaction

`RedactConfigurationValues` defaults to `true`. When enabled, the loader replaces values in log messages with `***`:

```text
Configuration key /myroot:password was added with value ***
```

The key in this log is the mapped configuration key. The etcd key would have been `/myroot/password`. Keep redaction enabled in production so configuration values are not written to logs. When `RedactConfigurationValues` is `false`, the library logs a startup warning and still redacts keys whose names look like secrets (`password`, `pwd`, `secret`, `token`, `credential`, `connectionstring`, `apikey`, `privatekey`).

## Related pages

- [Validation and Startup](Validation-and-Startup)
- [Key Format and Case Sensitivity](Key-Format-and-Case-Sensitivity)
- [TLS](TLS)
- [Mutual TLS](Mutual-TLS)
- [Refresh and Polling](Refresh-and-Polling)

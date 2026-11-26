# How It Works

`EtcdNETConfiguration` plugs into the standard .NET configuration and hosting pipeline. It uses a shared etcd-backed store so the initial configuration and background refreshes stay consistent.

## End-to-end lifecycle

1. `AddEtcdConfiguration(...)` registers the options, a shared `EtcdConfiguration` instance, the configuration loader, and the `EtcdPoller` hosted service. It also adds an `EtcdConfigurationSource` to the host configuration.
2. The options are validated using `System.ComponentModel.DataAnnotations` and a fail-closed TLS validator. Validation runs when the options are first resolved, through `ValidateOnStart()` when the host starts, and before the first etcd connection. Invalid options throw an `OptionsValidationException` and prevent the host from starting.
3. At startup the configuration source synchronously loads all keys under every configured root (by default a single `/configuration` root) into the shared instance. Roots are queried in ascending priority order within one timeout budget. Each raw etcd key is scope-checked, validated, mapped from `/` path segments to the `:` configuration key format, and merged by priority. The final keys are exposed under `ConfigurationPrefix`. Configuration is therefore available immediately, even before the poller runs. If the initial load fails, the host does not start with empty configuration.
4. The poller then re-reads etcd every `RefreshInterval`, compares the result with the last snapshot, and only publishes and logs when something actually changed. Setting `RefreshInterval` to `TimeSpan.Zero` disables background polling, leaving only the initial startup load.
5. Publishing raises `ConfigurationChanged`, which the provider converts into `OnReload()`. This updates `IConfiguration` and notifies options consumers that use `IOptionsMonitor<T>`. `IOptions<T>` itself is a cached singleton and does not refresh automatically.

## Components

| Component | Responsibility |
|-|-|
| `EtcdConfigurationProviderExtensions` | `AddEtcdConfiguration` registration for `IHostBuilder` and `IHostApplicationBuilder`. |
| `EtcdOptions` / `SslOptions` | Connection, root, output-prefix, polling, redaction, and TLS settings. |
| `EtcdTlsOptionsValidator` | Fail-closed TLS validation that runs through the options pipeline, the client factory, and the initial source. |
| `EtcdClientFactory` | Creates the etcd client and configures the gRPC channel credentials. |
| `EtcdConfigurationSource` | Performs the synchronous initial load while the host is built. |
| `EtcdConfiguration` | Shared, thread-safe store for the current values and the `ConfigurationChanged` event. |
| `EtcdConfigurationLoader` | Reads keys from every root, validates and maps them, merges by priority, diffs the merged snapshot against the previous one, and logs changes. |
| `EtcdKeyMapper` | Applies a root's path boundary, validates each path segment, and maps `/` hierarchy to relative `:` configuration keys. |
| `EtcdPoller` | Hosted service that periodically calls the loader, publishes changes, and exposes failure status through `IEtcdPollerStatus`. |
| `EtcdConfigurationProvider` | Exposes the shared store to `Microsoft.Extensions.Configuration` and triggers `OnReload()`. |

## Design points

- **Poll-based updates.** The library does not use etcd watches. It periodically reads all keys under every configured root and compares merged snapshots.
- **Prioritized merge.** Each root contributes relative keys. A higher `Priority` overrides a lower one, and priorities must be unique.
- **Single shared store.** The initial source and the poller update the same `EtcdConfiguration` instance, so there is only one etcd-backed provider in the configuration chain.
- **Change auditing.** The loader logs added, changed, and removed keys. Values can be redacted with `***` to avoid leaking secrets; secret-like keys stay redacted even when full logging is enabled.
- **Bounded reads.** Each read has a configurable key-count and byte-size limit, and it is cancelled when the host stops.
- **Key validation and mapping.** etcd keys use `/` path segments, which are mapped to the `:` keys used by `IConfiguration`. Invalid keys, old `:`-hierarchy keys, empty segments, and collisions fail closed.
- **Case-insensitive keys.** `IConfiguration` lookups are case-insensitive, and the loader, shared store, and provider use `StringComparer.OrdinalIgnoreCase`. Two etcd keys that differ only by case under the same root are a collision and fail closed. Relative keys from different roots are merged by priority instead.
- **Failure reporting.** The poller distinguishes transient from permanent failures and exposes the last failure and its timestamp for health checks.
- **Fail-closed validation.** Misconfiguration is caught before the application begins serving traffic. TLS validation is deliberately strict and does not silently downgrade to plaintext.
- **Options validation on start.** `ValidateDataAnnotations()` and `ValidateOnStart()` ensure the host cannot start with invalid options.

## Related pages

- [Configuration Options](Configuration-Options)
- [Key Format and Case Sensitivity](Key-Format-and-Case-Sensitivity)
- [Validation and Startup](Validation-and-Startup)
- [Refresh and Polling](Refresh-and-Polling)
- [TLS](TLS)

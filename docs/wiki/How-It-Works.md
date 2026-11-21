# How It Works

`EtcdNETConfiguration` plugs into the standard .NET configuration and hosting pipeline. It uses a shared etcd-backed store so the initial configuration and background refreshes stay consistent.

## End-to-end lifecycle

1. `AddEtcdConfiguration(...)` registers the options, a shared `EtcdConfiguration` instance, the configuration loader, and the `EtcdPoller` hosted service. It also adds an `EtcdConfigurationSource` to the host configuration.
2. The options are validated using `System.ComponentModel.DataAnnotations` and a fail-closed TLS validator. Validation runs when the options are first resolved, through `ValidateOnStart()` when the host starts, and before the first etcd connection. Invalid options throw an `OptionsValidationException` and prevent the host from starting.
3. At startup the configuration source synchronously loads all keys under `RootKey` (default `/configuration`) into the shared instance. Configuration is therefore available immediately, even before the poller runs. If the initial load fails, the host does not start with empty configuration.
4. The poller then re-reads etcd every `RefreshInterval`, compares the result with the last snapshot, and only publishes and logs when something actually changed. Setting `RefreshInterval` to `TimeSpan.Zero` disables background polling, leaving only the initial startup load.
5. Publishing raises `ConfigurationChanged`, which the provider converts into `OnReload()`. This updates `IConfiguration` and notifies options consumers that use `IOptionsMonitor<T>`. `IOptions<T>` itself is a cached singleton and does not refresh automatically.

## Components

| Component | Responsibility |
|-|-|
| `EtcdConfigurationProviderExtensions` | `AddEtcdConfiguration` registration for `IHostBuilder` and `IHostApplicationBuilder`. |
| `EtcdOptions` / `SslOptions` | Connection, polling, redaction, and TLS settings. |
| `EtcdTlsOptionsValidator` | Fail-closed TLS validation that runs through the options pipeline, the client factory, and the initial source. |
| `EtcdClientFactory` | Creates the etcd client and configures the gRPC channel credentials. |
| `EtcdConfigurationSource` | Performs the synchronous initial load while the host is built. |
| `EtcdConfiguration` | Shared, thread-safe store for the current values and the `ConfigurationChanged` event. |
| `EtcdConfigurationLoader` | Reads keys from etcd, diffs them against the previous snapshot, and logs changes. |
| `EtcdPoller` | Hosted service that periodically calls the loader, publishes changes, and exposes failure status through `IEtcdPollerStatus`. |
| `EtcdConfigurationProvider` | Exposes the shared store to `Microsoft.Extensions.Configuration` and triggers `OnReload()`. |

## Design points

- **Poll-based updates.** The library does not use etcd watches. It periodically reads all keys under `RootKey` and compares snapshots.
- **Single shared store.** The initial source and the poller update the same `EtcdConfiguration` instance, so there is only one etcd-backed provider in the configuration chain.
- **Change auditing.** The loader logs added, changed, and removed keys. Values can be redacted with `***` to avoid leaking secrets; secret-like keys stay redacted even when full logging is enabled.
- **Bounded reads.** Each read has a configurable key-count and byte-size limit, and it is cancelled when the host stops.
- **Failure reporting.** The poller distinguishes transient from permanent failures and exposes the last failure and its timestamp for health checks.
- **Fail-closed validation.** Misconfiguration is caught before the application begins serving traffic. TLS validation is deliberately strict and does not silently downgrade to plaintext.
- **Options validation on start.** `ValidateDataAnnotations()` and `ValidateOnStart()` ensure the host cannot start with invalid options.

## Related pages

- [Configuration Options](Configuration-Options)
- [Validation and Startup](Validation-and-Startup)
- [Refresh and Polling](Refresh-and-Polling)
- [TLS](TLS)

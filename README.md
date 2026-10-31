# EtcdNETConfiguration — Overview

`EtcdNETConfiguration` is a **C# (.NET) library that integrates `etcd` as a backing store for the standard `Microsoft.Extensions.Configuration` system**. 
It lets a .NET application load and continuously refresh its configuration from an etcd cluster, so config changes made in etcd propagate live into the app's `IConfiguration` (and via the `IOptions` machinery).

It is published as a NuGet package (`ROOT.Etcd.Configuration`) under the namespace `ROOT.Etcd.Configuration`.

## How it works end-to-end

1. `AddEtcdConfiguration(...)` registers options, a shared `EtcdConfiguration`, the loader, and the `EtcdPoller` hosted service, and adds an `EtcdConfigurationSource` to the host configuration.
2. At startup the **source** synchronously loads all keys under `RootKey` (default `/configuration`) into the shared instance — so config is available immediately even before the poller runs.
3. The **poller** then re-reads etcd every `RefreshInterval`, compares against the last snapshot, and only publishes/logs when something actually changed.
4. Publishing raises `ConfigurationChanged`, which the **provider** converts into `OnReload()`, updating `IConfiguration` and any `IOptions`-bound settings in place.

## Notable design points

- **Live updates** without app restart (poll-based, not etcd watch-based).
- **Change auditing**: logs added/changed/removed keys; can redact values (`***`) to avoid leaking secrets.

## In short

A drop-in etcd-backed configuration provider for .NET's dependency-injection/hosting configuration system, with background polling, change logging/redaction.


## Source code testing

Tests are a mix of unit tests and integration tests that uses a real etcd server.

Tests will auto start the etcd docker container, but it requires that a docker runtime is available.
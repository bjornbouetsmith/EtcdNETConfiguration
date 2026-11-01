[![Build and Test](https://github.com/bjornbouetsmith/EtcdNETConfiguration/actions/workflows/ci.yml/badge.svg)](https://github.com/bjornbouetsmith/EtcdNETConfiguration/actions/workflows/ci.yml)'
[![Quality gate status](https://sonarcloud.io/api/project_badges/measure?project=ETCDConfiguration&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=ETCDConfiguration)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=ETCDConfiguration&metric=coverage)](https://sonarcloud.io/summary/new_code?id=ETCDConfiguration)

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

## Source code testing

Tests are a mix of unit tests and integration tests that uses a real etcd server.

Tests will auto start the etcd docker container, but it requires that a docker runtime is available.

## Warning
When using this library do not use `localhost` as the hostname for the etcd service. This can cause extreme delays because the DNS resolution returns an IP address family that the service is potentially not listening on.

Windows defaults `localhost` to return an IPV6 address, and if your etcd service is not listening on IPV6 then the client will time out in connecting and then retry with IPv4.

This is not a problem in this library or the underlying service, but simply a DNS resolution problem.

To overcome this problem, don't use `localhost` and instead use `127.0.0.1` or a hostname that resolves to the actual IP address and family that the service listens on.

See [this](https://github.com/dotnet/runtime/issues/65375) for reference.

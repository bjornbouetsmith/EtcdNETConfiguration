# Getting Started

This page walks through installing `EtcdNETConfiguration`, registering it with a .NET host, storing configuration in etcd, and binding those values to a strongly typed class.

## Requirements

- A .NET application targeting `net10.0` or later.
- An etcd cluster reachable from the application.
- The `ROOT.Etcd.Configuration` NuGet package.

## Install the package

```bash
dotnet add package ROOT.Etcd.Configuration
```

## Register the configuration source

`AddEtcdConfiguration` is available for both `IHostBuilder` and `IHostApplicationBuilder`.

### `Host.CreateDefaultBuilder`

```csharp
using Microsoft.Extensions.Hosting;

var builder = Host.CreateDefaultBuilder();

builder.AddEtcdConfiguration(options =>
{
    options.EndPoints = "http://127.0.0.1:2379";
    options.RootKey = "/myroot";
    options.RefreshInterval = TimeSpan.FromMinutes(5);
});

using var host = builder.Build();
await host.RunAsync();
```

### `Host.CreateApplicationBuilder`

```csharp
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.AddEtcdConfiguration(options =>
{
    options.EndPoints = "http://127.0.0.1:2379";
    options.RootKey = "/myroot";
    options.RefreshInterval = TimeSpan.FromMinutes(5);
});

using var host = builder.Build();
await host.RunAsync();
```

At this point the library:

1. Registers the etcd options and validates them.
2. Loads the initial configuration from etcd synchronously while the host is being built.
3. Registers the background poller that refreshes configuration at the configured interval.

If the initial load fails, the host does not start with empty configuration. See [Validation and Startup](Validation-and-Startup).

## Store configuration in etcd

The `RootKey` option defines the etcd key prefix. Only strict descendants of that prefix are loaded into `IConfiguration`, so `/myroot-other/key` is ignored when `RootKey` is `/myroot`.

etcd keys use the native `/` path hierarchy. The library maps `/` to the `:` separator used by `IConfiguration`. With `RootKey = "/myroot"`:

| etcd key | IConfiguration key | Value |
|-|-|-|
| `/myroot/connectionstrings:0` | `/myroot:connectionstrings:0` | `databaserver1` |
| `/myroot/connectionstrings:1` | `/myroot:connectionstrings:1` | `databaserver2` |
| `/myroot/logging/loglevel/default` | `/myroot:logging:loglevel:default` | `Information` |
| `/myroot/services:0/name` | `/myroot:services:0:name` | `api` |

Collection indices stay attached to their segment with `:`. Do not use `:` as a hierarchy separator. Empty segments, `//`, and trailing `/` are rejected.

The `RootKey` prefix is not stripped from the configuration keys. Use `GetSection("/myroot")` to bind the values under that prefix.

You can add the keys with any etcd client. For example, using `etcdctl`:

```bash
etcdctl put "/myroot/connectionstrings:0" "databaserver1"
etcdctl put "/myroot/connectionstrings:1" "databaserver2"
etcdctl put "/myroot/logging/loglevel/default" "Information"
```

The old `:`-hierarchy format is no longer supported. A key such as `/myroot:connectionstrings:0` fails the load with a migration error; rewrite it as `/myroot/connectionstrings:0` before upgrading. See [Key Format and Case Sensitivity](Key-Format-and-Case-Sensitivity) for the full rules and migration steps.

## Bind to a strongly typed class

Define a class that matches the key hierarchy below `RootKey`:

```csharp
public class Configuration
{
    public List<string> ConnectionStrings { get; set; } = [];
}
```

Register it with the options system and the section that represents `RootKey`:

```csharp
builder.ConfigureServices((ctx, services) =>
{
    services.Configure<Configuration>(ctx.Configuration.GetSection("/myroot"));
});
```

Resolve it through `IOptions<T>`:

```csharp
var options = host.Services.GetRequiredService<IOptions<Configuration>>();
var connectionStrings = options.Value.ConnectionStrings;
```

Because the library reloads `IConfiguration` when etcd changes, consumers that use `IOptionsMonitor<Configuration>` observe the updates. `IOptions<Configuration>` is a cached singleton and does not refresh automatically, so use `IOptionsMonitor<T>` when you need the latest values at runtime.

## Verify the setup

Before starting the host, confirm that etcd is reachable and that the keys exist:

```bash
etcdctl get --prefix "/myroot"
```

When the host starts:

- Invalid options or TLS settings cause an `OptionsValidationException` and the host does not start.
- The initial configuration is available before hosted services start.
- With the default `RefreshInterval`, the poller refreshes configuration once per minute.

## Next steps

- [Configuration Options](Configuration-Options) — every option, default, and validation rule.
- [Key Format and Case Sensitivity](Key-Format-and-Case-Sensitivity) — how etcd keys map to configuration keys.
- [Validation and Startup](Validation-and-Startup) — fail-closed behavior and registration ordering.
- [TLS](TLS) and [Mutual TLS](Mutual-TLS) — secure connections to etcd.
- [Refresh and Polling](Refresh-and-Polling) — how live updates work.
- [Troubleshooting](Troubleshooting) — common startup and connectivity problems.

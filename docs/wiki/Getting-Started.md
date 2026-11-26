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
    options.RootKeys =
    [
        new EtcdRootKey { RootKey = "/myroot/", Priority = 0 }
    ];
    options.ConfigurationPrefix = "/myroot";
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
    options.RootKeys =
    [
        new EtcdRootKey { RootKey = "/myroot/", Priority = 0 }
    ];
    options.ConfigurationPrefix = "/myroot";
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

The `RootKeys` option defines the etcd key prefixes. Each root key must end with `/`; the trailing slash scopes the range query, so `/app/` cannot read keys under `/application/`. Only strict descendants of a root are loaded into `IConfiguration`, so `/myroot-other/key` is ignored when a root is `/myroot/`. The `ConfigurationPrefix` option defines the output prefix under which the merged values are exposed.

etcd keys use the native `/` path hierarchy. The library strips each root prefix, merges the remaining relative keys by priority, and maps `/` to the `:` separator used by `IConfiguration`. With a root `/myroot/` and `ConfigurationPrefix = "/myroot"`:

| etcd key | IConfiguration key | Value |
|-|-|-|
| `/myroot/connectionstrings:0` | `/myroot:connectionstrings:0` | `databaserver1` |
| `/myroot/connectionstrings:1` | `/myroot:connectionstrings:1` | `databaserver2` |
| `/myroot/logging/loglevel/default` | `/myroot:logging:loglevel:default` | `Information` |
| `/myroot/services:0/name` | `/myroot:services:0:name` | `api` |

Collection indices stay attached to their segment with `:`. Do not use `:` as a hierarchy separator. Returned etcd keys with empty segments or a trailing `/` are rejected.

The root prefix is not retained separately in the configuration keys. Use `GetSection("/myroot")` (or your configured `ConfigurationPrefix`) to bind the values.

When several roots are configured, a higher `Priority` overrides a lower one for the same relative key. Priorities must be unique, each root must end with `/`, and roots must not be duplicates, overlap, or nest:

```csharp
builder.AddEtcdConfiguration(options =>
{
    options.EndPoints = "http://127.0.0.1:2379";
    options.ConfigurationPrefix = "/configuration";
    options.RootKeys =
    [
        new EtcdRootKey { RootKey = "/domains/example.com/", Priority = 0 },
        new EtcdRootKey { RootKey = "/machines/web-01/",     Priority = 10 },
        new EtcdRootKey { RootKey = "/services/checkout/",   Priority = 20 }
    ];
});
```

This merges all three prefixes into the `/configuration` section. See [Configuration Options](Configuration-Options) for the merge example.

You can add the keys with any etcd client. For example, using `etcdctl`:

```bash
etcdctl put "/myroot/connectionstrings:0" "databaserver1"
etcdctl put "/myroot/connectionstrings:1" "databaserver2"
etcdctl put "/myroot/logging/loglevel/default" "Information"
```

The old `:`-hierarchy format is no longer supported. Rewrite a key such as `/myroot:connectionstrings:0` as `/myroot/connectionstrings:0` before upgrading; with the root `/myroot/`, the legacy key is outside the root and is not loaded. See [Key Format and Case Sensitivity](Key-Format-and-Case-Sensitivity) for the full rules and migration steps.

## Bind to a strongly typed class

Define a class that matches the key hierarchy below a root:

```csharp
public class Configuration
{
    public List<string> ConnectionStrings { get; set; } = [];
}
```

Register it with the options system and the section named by `ConfigurationPrefix`:

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

# EtcdNETConfiguration

`EtcdNETConfiguration` is a C# (.NET) library that integrates [etcd](https://etcd.io/) as a backing store for the standard [`Microsoft.Extensions.Configuration`](https://learn.microsoft.com/dotnet/core/extensions/configuration) system.

It lets a .NET application load and continuously refresh its configuration from an etcd cluster, so configuration changes made in etcd propagate live into the application's `IConfiguration` and any `IOptionsMonitor`-bound settings.

The library is published as the NuGet package `ROOT.Etcd.Configuration` under the namespace `ROOT.Etcd.Configuration`.

## Why use it

- **Prioritized roots**: merge configuration from several etcd prefixes, with a higher-priority root overriding lower ones.
- **Live updates** without restarting the application. Refresh is poll-based, not etcd watch-based.
- **Change auditing** that logs added, changed, and removed keys, with optional value redaction (`***`).
- **Fail-closed startup**: invalid options, TLS problems, or an unreachable etcd cluster prevent the host from starting.
- **TLS and mutual TLS** support, including private CAs and file-based or in-memory client certificates.

## Install

```bash
dotnet add package ROOT.Etcd.Configuration
```

## Quick start

```csharp
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
```

Keys in etcd use the native `/` path hierarchy. The library strips each root prefix, merges the remaining relative keys by priority, and maps `/` to the `:` separator used by `IConfiguration`. Root keys must end with `/`; with a root `/myroot/` and `ConfigurationPrefix = "/myroot"`, store values like this:

| etcd key | IConfiguration key | Value |
|-|-|-|
| `/myroot/connectionstrings:0` | `/myroot:connectionstrings:0` | `databaserver1` |
| `/myroot/connectionstrings:1` | `/myroot:connectionstrings:1` | `databaserver2` |

Bind the values to a strongly typed class:

```csharp
public class Configuration
{
    public List<string> ConnectionStrings { get; set; } = [];
}
```

```csharp
builder.ConfigureServices((ctx, services) =>
{
    services.Configure<Configuration>(ctx.Configuration.GetSection("/myroot"));
});
```

See [Getting Started](Getting-Started) for the full walkthrough.

## Documentation

| Topic | Page |
|-|-|
| Full setup walkthrough | [Getting Started](Getting-Started) |
| Startup lifecycle and design | [How It Works](How-It-Works) |
| All options and defaults | [Configuration Options](Configuration-Options) |
| Key format, validation, and case sensitivity | [Key Format and Case Sensitivity](Key-Format-and-Case-Sensitivity) |
| Validation and fail-closed startup | [Validation and Startup](Validation-and-Startup) |
| TLS | [TLS](TLS) |
| Mutual TLS | [Mutual TLS](Mutual-TLS) |
| Background polling | [Refresh and Polling](Refresh-and-Polling) |
| Common problems | [Troubleshooting](Troubleshooting) |
| Tests and local development | [Testing and Development](Testing-and-Development) |
| Test certificates | [Test Certificates](Test-Certificates) |

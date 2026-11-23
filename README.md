[![Build and Test](https://github.com/bjornbouetsmith/EtcdNETConfiguration/actions/workflows/ci.yml/badge.svg)](https://github.com/bjornbouetsmith/EtcdNETConfiguration/actions/workflows/ci.yml)
[![Quality gate status](https://sonarcloud.io/api/project_badges/measure?project=ETCDConfiguration&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=ETCDConfiguration)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=ETCDConfiguration&metric=coverage)](https://sonarcloud.io/summary/new_code?id=ETCDConfiguration)

# EtcdNETConfiguration

`EtcdNETConfiguration` is a C# (.NET) library that integrates [etcd](https://etcd.io/) as a backing store for the standard [`Microsoft.Extensions.Configuration`](https://learn.microsoft.com/dotnet/core/extensions/configuration) system.

It lets a .NET application load and continuously refresh its configuration from an etcd cluster, so configuration changes made in etcd propagate live into the application's `IConfiguration` and any `IOptionsMonitor`-bound settings.

It is published as the NuGet package `ROOT.Etcd.Configuration` under the namespace `ROOT.Etcd.Configuration`.

Full documentation is maintained in the [GitHub Wiki](https://github.com/bjornbouetsmith/EtcdNETConfiguration/wiki).

## Overview

- **Live updates** without restarting the application. Refresh is poll-based, not etcd watch-based.
- **Change auditing** that logs added, changed, and removed keys, with optional value redaction (`***`).
- **Fail-closed startup**: invalid options, TLS problems, or an unreachable etcd cluster prevent the host from starting.
- **Bounded reads and failure reporting**: configurable key-count and size limits, cancellation on shutdown, and `IEtcdPollerStatus` for health checks.
- **TLS and mutual TLS** support, including private CAs, PEM CA bundles, and file-based or in-memory client certificates.

## How to use it

### Install

```bash
dotnet add package ROOT.Etcd.Configuration
```

### Register the configuration source

```csharp
var builder = Host.CreateDefaultBuilder();

builder.AddEtcdConfiguration(options =>
{
    options.EndPoints = "http://127.0.0.1:2379";
    options.RootKey = "/myroot";
    options.RefreshInterval = TimeSpan.FromMinutes(5);
});
```

The library loads all keys under `RootKey` at startup and refreshes them in the background.

### Store configuration in etcd

Keys use etcd's native `/` path hierarchy. The library maps `/` to the `:` separator used by `IConfiguration`. With `RootKey` set to `/myroot`:

| etcd key | IConfiguration key | Value |
|-|-|-|
| `/myroot/connectionstrings:0` | `/myroot:connectionstrings:0` | `databaserver1` |
| `/myroot/connectionstrings:1` | `/myroot:connectionstrings:1` | `databaserver2` |
| `/myroot/logging/loglevel/default` | `/myroot:logging:loglevel:default` | `Information` |

Collection indices stay attached to their segment with `:` (`key:0`, `key:1`). `:` is not allowed anywhere else. Empty segments, `//`, and trailing `/` are rejected. Only strict descendants of `RootKey` are loaded, so `/myroot-other/key` is ignored when `RootKey` is `/myroot`.

The old `:`-hierarchy key format is no longer supported. A key such as `/myroot:key` fails the load with a migration error; rewrite it as `/myroot/key` before upgrading.

```bash
etcdctl put "/myroot/connectionstrings:0" "databaserver1"
etcdctl put "/myroot/connectionstrings:1" "databaserver2"
```

### Bind to a strongly typed class

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

You can then depend on `IOptions<Configuration>` for the initial values, or `IOptionsMonitor<Configuration>` if you need to observe live updates.

<!-- Legacy README anchors retained for existing external links. -->
<a id="how-it-works-end-to-end"></a>
<a id="notable-design-points"></a>
<a id="options-and-validation"></a>
<a id="connecting-over-tls"></a>
<a id="connecting-with-mutual-tls-mtls"></a>
<a id="supported-timespan-ranges"></a>
<a id="refreshinterval-and-timespanzero"></a>
<a id="source-code-testing"></a>

## Documentation

| Topic | Link |
|-|-|
| Full setup walkthrough | [Getting Started](https://github.com/bjornbouetsmith/EtcdNETConfiguration/wiki/Getting-Started) |
| Startup lifecycle and design | [How It Works](https://github.com/bjornbouetsmith/EtcdNETConfiguration/wiki/How-It-Works) |
| All options and defaults | [Configuration Options](https://github.com/bjornbouetsmith/EtcdNETConfiguration/wiki/Configuration-Options) |
| Key format, validation, and case sensitivity | [Key Format and Case Sensitivity](https://github.com/bjornbouetsmith/EtcdNETConfiguration/wiki/Key-Format-and-Case-Sensitivity) |
| Validation and fail-closed startup | [Validation and Startup](https://github.com/bjornbouetsmith/EtcdNETConfiguration/wiki/Validation-and-Startup) |
| TLS | [TLS](https://github.com/bjornbouetsmith/EtcdNETConfiguration/wiki/TLS) |
| Mutual TLS | [Mutual TLS](https://github.com/bjornbouetsmith/EtcdNETConfiguration/wiki/Mutual-TLS) |
| Background polling | [Refresh and Polling](https://github.com/bjornbouetsmith/EtcdNETConfiguration/wiki/Refresh-and-Polling) |
| Common problems | [Troubleshooting](https://github.com/bjornbouetsmith/EtcdNETConfiguration/wiki/Troubleshooting) |
| Tests and local development | [Testing and Development](https://github.com/bjornbouetsmith/EtcdNETConfiguration/wiki/Testing-and-Development) |
| Test certificates | [Test Certificates](https://github.com/bjornbouetsmith/EtcdNETConfiguration/wiki/Test-Certificates) |

## License

MIT. See [LICENSE](https://github.com/bjornbouetsmith/EtcdNETConfiguration/blob/main/LICENSE).

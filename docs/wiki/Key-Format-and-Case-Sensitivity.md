# Key Format and Case Sensitivity

etcd is a flat key-value store. A `/` in a key is only a naming convention, but this library uses it as the path hierarchy and maps it to the `:` separator that `IConfiguration` uses. This page explains the mapping, the validation rules, case sensitivity, and how to migrate from the old format.

## How etcd keys map to IConfiguration

The library reads every strict descendant of each configured root, strips the root prefix, merges the remaining relative keys by priority, and converts `/` path separators to `:`:

| etcd key | Relative key | IConfiguration key (prefix `/prefix`) | Binding path |
|-|-|-|-|
| `/prefix/key/subkey/deepkey` | `:key:subkey:deepkey` | `/prefix:key:subkey:deepkey` | `GetSection("/prefix").GetSection("key:subkey:deepkey")` |
| `/prefix/key:0` | `:key:0` | `/prefix:key:0` | array element `0` under `key` |
| `/prefix/key:1` | `:key:1` | `/prefix:key:1` | array element `1` under `key` |
| `/prefix/services:0/endpoints:1` | `:services:0:endpoints:1` | `/prefix:services:0:endpoints:1` | element `0` of `services`, then element `1` of `endpoints` |

Collections do not change: an array index stays attached to its segment with `:`. Only the hierarchy separator changes from `:` to `/`.

The root prefix itself is not part of the relative key. The final configuration key is always `ConfigurationPrefix` plus the relative key, for example `/configuration` + `:key:subkey` = `/configuration:key:subkey`.

## Creating keys

Use `etcdctl` or any etcd client. The examples below assume a root `/myroot/` and `ConfigurationPrefix = "/myroot"`, which preserves the historical layout.

### Scalars

```bash
etcdctl put "/myroot/logging/loglevel/default" "Information"
```

Mapped configuration key: `/myroot:logging:loglevel:default`.

### Nested objects

```bash
etcdctl put "/myroot/database/host" "db.example.com"
etcdctl put "/myroot/database/port" "5432"
```

### Arrays and lists

```bash
etcdctl put "/myroot/connectionstrings:0" "databaserver1"
etcdctl put "/myroot/connectionstrings:1" "databaserver2"
```

### Arrays of objects

```bash
etcdctl put "/myroot/services:0/name" "api"
etcdctl put "/myroot/services:0/endpoints:1" "https://api.example.com"
```

### Dictionaries

```bash
etcdctl put "/myroot/dictionary/key1" "value1"
etcdctl put "/myroot/dictionary/key2" "value2"
```

A dictionary key is just another path segment; there is no special syntax.

## Validation rules

Every key under a configured root must satisfy these rules.

### `/` is the hierarchy separator

Use `/` between path segments. The library maps each `/` to `:`.

### `:` is allowed only for collection indices

A segment is either a name or a name followed by one or more canonical indices:

```text
key
key:0
key:1
key:10
key:0:1
```

The following are rejected:

```text
prefix:key      old hierarchy separator
prefix:key:0    old hierarchy separator
key:foo         index is not a number
key:            empty index
key::0          empty index
key:-1          negative index
key:+1          signed index
key:01          leading zero
key:1.0         decimal index
```

The recommended pattern for a segment is `^[^:]+(?::(0|[1-9][0-9]*))*$`. It allows Unicode, spaces, hyphens, and dots in names, while rejecting every other use of `:`.

### No empty segments

These keys are rejected:

```text
/myroot//key
/myroot/key/
```

### No escaping

There is no escape syntax for a literal `/` or `:` inside a name. Use a different character in the name, or restructure the key.

### `RootKeys` and `ConfigurationPrefix` rules

Each configured root key must be a canonical path:

- It must start with `/`.
- It must not be `/`.
- It must end with `/`. The trailing slash scopes the etcd range query, so `/app/` cannot read keys under `/application/`.
- It must not contain empty segments (`//`).
- It must not contain `:`.

Surrounding whitespace is trimmed for validation.

Every root must have a unique `Priority`. Roots must not be duplicates, overlap, or nest, and those comparisons are case-insensitive. For example, `/app/` together with `/APP/`, `/app/service/`, or `/APP/service/` is rejected, while `/app/` together with `/application/` is allowed.

`ConfigurationPrefix` follows the same canonical path rules, except that it also must not be `/`. It is never queried from etcd; it only determines the final configuration key.

### Scope

Only strict descendants of a root are loaded. With roots `/prefix/` and `/other/`:

| etcd key | Result |
|-|-|
| `/prefix/key` | loaded |
| `/prefix/key/subkey` | loaded |
| `/prefix/key:0` | loaded |
| `/prefix-other/key` | ignored |
| `/application/key` | ignored when the root is `/app/` |
| `/prefix` | ignored |
| `/prefix:key` | ignored (legacy sibling outside `/prefix/`) |
| `/prefix/legacy:key` | rejected (invalid `:` usage) |
| `/prefix//key` | rejected |
| `/prefix/key/` | rejected |

Scope uses the path boundary, not a raw string prefix. `/prefix-other/key` is never in the scope of `/prefix/`, and `/application/key` is never in the scope of `/app/`.

## Case sensitivity

etcd keys are case-sensitive. Root prefix matching is case-sensitive, so `/Prefix/key` is not in scope of `/prefix/`. Root *validation* compares roots case-insensitively to reject duplicates and nesting.

`IConfiguration` lookups are case-insensitive. The library uses `StringComparer.OrdinalIgnoreCase` for the loader snapshots, the shared store, and the configuration provider, so `configuration["/prefix:KEY"]` and `configuration["/prefix:key"]` return the same value.

Because configuration keys are case-insensitive, two etcd keys that differ only by case under the same root are a collision and fail closed:

```text
/prefix/Key
/prefix/key
```

Structural collisions within one root are also rejected because different etcd paths can map to the same configuration key:

```text
/prefix/key:0      -> /prefix:key:0
/prefix/key/0      -> /prefix:key:0
```

Collision detection is scoped to one root. These do not collide:

```text
/prefix/key
/prefix-other/key
```

Relative keys from **different** roots are merged, not treated as collisions. When two roots provide the same relative key (compared case-insensitively), the root with the higher `Priority` wins:

```text
/domains/example.com/Logging/Level  (priority 0)
/machines/web-01/Logging/Level      (priority 10)
```

The effective value comes from `/machines/web-01`, exposed as `ConfigurationPrefix + ":Logging:Level"`.

Pick one casing convention and use it consistently. Mixed casing works for a single key, but it becomes fragile as soon as another key with different casing is added under the same root.

## Migrating from the old `:` format

Old `:`-hierarchy keys are no longer supported. Root keys end with `/`, so a legacy sibling such as `/myroot:key` is outside `/myroot/` and is not read. Returned keys that do not match the required path format are rejected with an error that names the key. Inventory and rewrite those legacy keys before upgrading; otherwise they are silently out of scope. This is a breaking change.

| Old key | New key | IConfiguration key |
|-|-|-|
| `/myroot:connectionstrings:0` | `/myroot/connectionstrings:0` | `/myroot:connectionstrings:0` |
| `/myroot:logging:loglevel:default` | `/myroot/logging/loglevel/default` | `/myroot:logging:loglevel:default` |
| `/myroot:Kafka:BootStrapServers:0` | `/myroot/Kafka/BootStrapServers:0` | `/myroot:Kafka:BootStrapServers:0` |

Application code usually does not change if `ConfigurationPrefix` is set to the old root key. The mapped configuration keys are the same, so `GetSection` calls stay as they are. With multiple roots, relative keys are merged under the single `ConfigurationPrefix`.

To inventory and rewrite keys with `etcdctl`:

```bash
# 1. List the old keys.
etcdctl get --prefix "/myroot:" --keys-only

# 2. Copy each value to its new key. For example:
etcdctl get "/myroot:connectionstrings:0" --print-value-only
etcdctl put "/myroot/connectionstrings:0" "databaserver1"

# 3. Verify the new keys.
etcdctl get --prefix "/myroot/" --keys-only

# 4. Delete the old keys before restarting the application.
etcdctl del --prefix "/myroot:"
```

Delete the old keys before restarting, because the library rejects the whole load when it encounters an old-format key under any configured root.

## Troubleshooting

### A key is not found in configuration

- Confirm the key is under a configured root and uses the path boundary. `/prefix-other/key` is ignored when the root is `/prefix/`.
- Confirm the key uses `/`, not `:`.
- Confirm the mapped key. `/prefix/key` becomes `/prefix:key` when `ConfigurationPrefix` is `/prefix`, not `key`.
- Remember that array indices are `key:0`, not `key/0`.

### Old-format key error

The message contains `removed ':' hierarchy format`. Rewrite the key with `/` separators and delete the old key. See [Migrating from the old `:` format](#migrating-from-the-old--format).

### Invalid colon or empty segment error

The message names the invalid segment. `:` is allowed only as a canonical collection index. Remove duplicate slashes and trailing slashes.

### Duplicate or collision error

The message names both etcd keys and the configuration key they map to. Remove or rename one of the keys. Case-only duplicates and the `key:0` versus `key/0` form are the common causes. A key from a different root is not a collision; check that priorities and roots are configured as intended.

### An array or list does not bind

Use `key:0`, `key:1`, and so on. The index must be a canonical non-negative integer without leading zeros. If both `key:0` and `key/0` exist, the load fails with a collision.

## Related pages

- [Getting Started](Getting-Started)
- [Configuration Options](Configuration-Options)
- [Validation and Startup](Validation-and-Startup)
- [Troubleshooting](Troubleshooting)

# Key Format and Case Sensitivity

etcd is a flat key-value store. A `/` in a key is only a naming convention, but this library uses it as the path hierarchy and maps it to the `:` separator that `IConfiguration` uses. This page explains the mapping, the validation rules, case sensitivity, and how to migrate from the old format.

## How etcd keys map to IConfiguration

The library reads every strict descendant of `RootKey`, validates the key, and converts `/` path separators to `:`:

| etcd key | IConfiguration key | Binding path |
|-|-|-|
| `/prefix/key/subkey/deepkey` | `/prefix:key:subkey:deepkey` | `GetSection("/prefix").GetSection("key:subkey:deepkey")` |
| `/prefix/key:0` | `/prefix:key:0` | array element `0` under `key` |
| `/prefix/key:1` | `/prefix:key:1` | array element `1` under `key` |
| `/prefix/services:0/endpoints:1` | `/prefix:services:0:endpoints:1` | element `0` of `services`, then element `1` of `endpoints` |

Collections do not change: an array index stays attached to its segment with `:`. Only the hierarchy separator changes from `:` to `/`.

The `RootKey` prefix is retained in the mapped key, so binding remains `GetSection("/prefix")`.

## Creating keys

Use `etcdctl` or any etcd client. The examples below assume `RootKey = "/myroot"`.

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

Every key under `RootKey` must satisfy these rules.

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

### `RootKey` rules

`RootKey` must be a canonical path:

- It must start with `/`.
- It must not be `/`.
- It must not end with `/`.
- It must not contain empty segments (`//`).
- It must not contain `:`.

Surrounding whitespace is trimmed for validation.

### Scope

Only strict descendants of `RootKey` are loaded. With `RootKey = "/prefix"`:

| etcd key | Result |
|-|-|
| `/prefix/key` | loaded |
| `/prefix/key/subkey` | loaded |
| `/prefix/key:0` | loaded |
| `/prefix-other/key` | ignored |
| `/prefix` | ignored |
| `/prefix:key` | rejected with a migration error |
| `/prefix//key` | rejected |
| `/prefix/key/` | rejected |

Scope uses the path boundary, not a raw string prefix. `/prefix-other/key` is never in the scope of `/prefix`.

## Case sensitivity

etcd keys are case-sensitive. `RootKey` prefix matching is case-sensitive, so `/Prefix/key` is not in scope of `/prefix`.

`IConfiguration` lookups are case-insensitive. The library uses `StringComparer.OrdinalIgnoreCase` for the loader snapshots, the shared store, and the configuration provider, so `configuration["/prefix:KEY"]` and `configuration["/prefix:key"]` return the same value.

Because configuration keys are case-insensitive, two etcd keys that differ only by case under the same `RootKey` are a collision and fail closed:

```text
/prefix/Key
/prefix/key
```

Structural collisions are also rejected because different etcd paths can map to the same configuration key:

```text
/prefix/key:0      -> /prefix:key:0
/prefix/key/0      -> /prefix:key:0
```

Collision detection is scoped to the same `RootKey`. These do not collide:

```text
/prefix/key
/prefix-other/key
```

Pick one casing convention and use it consistently. Mixed casing works for a single key, but it becomes fragile as soon as another key with different casing is added.

## Migrating from the old `:` format

Old `:`-hierarchy keys are no longer supported. They are rejected with an error that names the key and points to the new format. This is a breaking change.

| Old key | New key | IConfiguration key |
|-|-|-|
| `/myroot:connectionstrings:0` | `/myroot/connectionstrings:0` | `/myroot:connectionstrings:0` |
| `/myroot:logging:loglevel:default` | `/myroot/logging/loglevel/default` | `/myroot:logging:loglevel:default` |
| `/myroot:Kafka:BootStrapServers:0` | `/myroot/Kafka/BootStrapServers:0` | `/myroot:Kafka:BootStrapServers:0` |

Application code usually does not change: the mapped configuration keys are the same, so `RootKey` and `GetSection` calls stay as they are.

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

Delete the old keys before restarting, because the library rejects the whole load when it encounters an old-format key under `RootKey`.

## Troubleshooting

### A key is not found in configuration

- Confirm the key is under `RootKey` and uses the path boundary. `/prefix-other/key` is ignored when `RootKey` is `/prefix`.
- Confirm the key uses `/`, not `:`.
- Confirm the mapped key. `/prefix/key` becomes `/prefix:key`, not `key`.
- Remember that array indices are `key:0`, not `key/0`.

### Old-format key error

The message contains `removed ':' hierarchy format`. Rewrite the key with `/` separators and delete the old key. See [Migrating from the old `:` format](#migrating-from-the-old--format).

### Invalid colon or empty segment error

The message names the invalid segment. `:` is allowed only as a canonical collection index. Remove duplicate slashes and trailing slashes.

### Duplicate or collision error

The message names both etcd keys and the configuration key they map to. Remove or rename one of the keys. Case-only duplicates and the `key:0` versus `key/0` form are the common causes.

### An array or list does not bind

Use `key:0`, `key:1`, and so on. The index must be a canonical non-negative integer without leading zeros. If both `key:0` and `key/0` exist, the load fails with a collision.

## Related pages

- [Getting Started](Getting-Started)
- [Configuration Options](Configuration-Options)
- [Validation and Startup](Validation-and-Startup)
- [Troubleshooting](Troubleshooting)

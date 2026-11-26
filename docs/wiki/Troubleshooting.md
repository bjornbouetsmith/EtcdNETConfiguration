# Troubleshooting

## Do not use `localhost` as the etcd hostname

When using this library, do not use `localhost` as the hostname for the etcd service. This can cause extreme delays because DNS resolution may return an IP address family that the service is not listening on.

Windows defaults `localhost` to an IPv6 address. If the etcd service is not listening on IPv6, the client times out connecting and then retries with IPv4.

This is not a problem in the library or in etcd itself; it is a DNS resolution issue. Use `127.0.0.1` or a hostname that resolves to the actual IP address and family the service listens on.

See [dotnet/runtime#65375](https://github.com/dotnet/runtime/issues/65375) for reference.

## The host fails to start

Startup is fail-closed. Check the exception first:

- `OptionsValidationException` means an option is invalid. Check `EndPoints`, `RootKeys`, `ConfigurationPrefix`, `RefreshInterval`, `ConfigurationTimeout`, the read limits, and the TLS settings. Each root key must start with `/`, must not be `/` or end with `/`, and must not contain `//` or `:`. Priorities must be unique, roots must not duplicate, overlap, or nest, and `ConfigurationPrefix` must be a canonical non-root prefix.
- TLS validation errors usually mean the endpoint scheme and `UseSsl` disagree, a certificate file is missing or invalid, or the client certificate lacks the `clientAuth` EKU.
- Connection errors mean etcd is unreachable, the endpoint or port is wrong, or the network blocks the connection.
- Key format errors mean etcd contains a key that cannot be mapped. See [Key format errors](#key-format-errors) below.

See [Validation and Startup](Validation-and-Startup) and [Configuration Options](Configuration-Options).

## Key format errors

The initial load fails closed when etcd contains a key that cannot be mapped, and the same error is classified as permanent during polling.

- **Old `:`-hierarchy key.** The message contains `removed ':' hierarchy format`. Rewrite a key such as `/myroot:key` as `/myroot/key`. The mapped configuration key is unchanged.
- **Invalid colon.** `:` is allowed only as a collection index suffix such as `key:0`. `key:foo`, `key:`, `key::0`, `key:-1`, `key:+1`, `key:01`, and `key:1.0` are rejected.
- **Empty segment.** `//` and a trailing `/` are rejected.
- **Collision.** Two keys under the same root map to the same configuration key. This includes case-only differences (`/myroot/Key` and `/myroot/key`) and structural differences (`/myroot/key:0` and `/myroot/key/0`). Remove or rename one of the keys.
- **Root overlap.** Two configured roots are duplicates, overlap, or nest case-insensitively, or two roots share a priority. Fix `RootKeys`; this is an options error, not an etcd key error.

Keys outside every root are ignored, not errors. With a root `/myroot`, `/myroot-other/key` is not loaded and cannot collide with `/myroot/key`. The same relative key from different roots is merged by priority, not reported as a collision. See [Key Format and Case Sensitivity](Key-Format-and-Case-Sensitivity).

## Array or list does not bind

Collection indices stay attached to their path segment with `:`:

```text
/myroot/connectionstrings:0 = databaserver1
/myroot/connectionstrings:1 = databaserver2
```

The mapped configuration keys are `/myroot:connectionstrings:0` and `/myroot:connectionstrings:1`. Do not write `/myroot/connectionstrings/0`; that maps to the same configuration key and is reported as a collision if both forms exist.

## Configuration does not update

1. Confirm that `RefreshInterval` is not `TimeSpan.Zero`.
2. Check the application logs for `ETCD polling is disabled because RefreshInterval is set to TimeSpan.Zero`.
3. Confirm that changes are made under one of the configured `RootKeys` prefixes.
4. Remember that etcd keys use `/` path segments and are mapped to `:` configuration keys. Keys outside every root's path boundary are ignored.
5. If a key exists under several roots, check which one has the highest `Priority`; lower-priority copies are shadowed until the winner is removed.
6. Check for polling timeout warnings or permanent-failure messages in the logs.
7. Resolve `IEtcdPollerStatus` to inspect `LastFailure`, `LastFailureTime`, `ConsecutivePermanentFailures`, and `IsHealthy`.

A permanent failure such as `Unauthenticated`, `PermissionDenied`, or an invalid certificate is counted. After `MaximumConsecutivePermanentFailures`, the host stops. Fix the credential, role, or certificate, or raise the threshold if you want more retries.

See [Refresh and Polling](Refresh-and-Polling).

## TLS connection fails

Check the following:

- `UseSsl` is `true` and every endpoint is `https://`.
- The CA file path is correct and the file is a valid PEM certificate.
- The server certificate is valid for `serverAuth`.
- If the server uses a private CA, `ServerCertificateAuthorityPath` points to that CA.
- For mutual TLS, the client certificate has a private key and `extendedKeyUsage=clientAuth`.

See [TLS](TLS) and [Mutual TLS](Mutual-TLS).

## Configuration values appear in logs

`RedactConfigurationValues` defaults to `true`. If it is disabled, the library logs a startup warning, and it still redacts keys whose names look like secrets. Do not disable redaction in production. See [Configuration Options](Configuration-Options).

## Tests cannot start etcd

The integration tests start etcd through Docker Compose by default. Confirm that a Docker runtime is available, or point the tests at an existing etcd instance with `ETCD_ENDPOINT`.

See [Testing and Development](Testing-and-Development).

## Related pages

- [Getting Started](Getting-Started)
- [Configuration Options](Configuration-Options)
- [Key Format and Case Sensitivity](Key-Format-and-Case-Sensitivity)
- [Validation and Startup](Validation-and-Startup)
- [Refresh and Polling](Refresh-and-Polling)

# Testing and Development

The repository contains a mix of unit tests and integration tests. Integration tests use a real etcd server.

## Test project

| Path | Purpose |
|-|-|
| `Source/Configuration` | The library. |
| `Source/ConfigurationTest` | Unit and integration tests. |
| `Source/ETCDConfiguration.slnx` | Solution file used by the build and test workflows. |
| `Source/docker-compose.yml` | Plaintext etcd used by integration tests. |
| `Source/docker-compose.tls.yml` | TLS-enabled etcd used by TLS integration tests. |
| `Source/docker-compose.mtls.yml` | mTLS-enabled etcd used by mTLS integration tests. |

## Run the tests

Run the full solution:

```bash
dotnet test Source/ETCDConfiguration.slnx
```

Run only tests that do not require an etcd server:

```bash
dotnet test Source/ETCDConfiguration.slnx --filter "Category!=Integration"
```

The integration tests start the etcd Docker container automatically, but a Docker runtime must be available. If an etcd instance is already running, point the tests at it instead:

```bash
ETCD_ENDPOINT=http://127.0.0.1:2379 dotnet test Source/ETCDConfiguration.slnx
```

## TLS and mTLS tests

The TLS integration tests additionally start `Source/docker-compose.tls.yml`, and the mTLS tests start `Source/docker-compose.mtls.yml`.

The self-signed test certificates are generated automatically by the test assembly when they are missing or older than one day:

- On Linux and macOS, `openssl` is used directly.
- On Windows, generation runs in an Alpine Docker container because `bash` and `openssl` are usually not installed there.

The container image can be overridden with `ETCD_TLS_CERT_DOCKER_IMAGE`. Generated certificate files are git-ignored.

## Point tests at an external TLS endpoint

The TLS tests run against `https://127.0.0.1:12379` by default. Point them at an external TLS-enabled etcd with:

```text
ETCD_TLS_ENDPOINT=https://my-etcd:2379
ETCD_TLS_CA_CERT=/path/to/ca.crt
```

The mTLS tests run against `https://127.0.0.1:12389` by default. Point them at an external mTLS-enabled etcd with:

```text
ETCD_MTLS_ENDPOINT=https://my-etcd:2379
ETCD_MTLS_CLIENT_CERT=/path/to/client.crt
ETCD_MTLS_CLIENT_KEY=/path/to/client.key
ETCD_TLS_CA_CERT=/path/to/ca.crt
```

The negative mTLS tests use certificates generated for the local test CA and are skipped when `ETCD_MTLS_ENDPOINT` points at an external server.

## Test certificates

See [Test Certificates](Test-Certificates) for the certificate file reference, generation instructions, and permission notes.

## CI

The `Build and Test` workflow runs the solution against a plaintext etcd service container, uploads test results, and reports to SonarCloud. The `Release nuget package` workflow packs and publishes the NuGet package.

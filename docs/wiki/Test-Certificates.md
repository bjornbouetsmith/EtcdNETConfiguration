# Test Certificates

These certificates are used by `docker-compose.tls.yml`, `docker-compose.mtls.yml`, and the TLS/mTLS integration tests. **They are for local development and testing only — never use them in production.** The CA private key is intentionally unencrypted and the certificates are disposable; regenerate them at any time with the script below.

All generated files are git-ignored. The integration test assembly generates them automatically when they are missing or older than one day. On Windows the generation runs in an Alpine container because `openssl` and `bash` are usually not available there; on Linux and macOS the script runs directly.

## Certificate files

| File | Purpose |
|-|-|
| `ca.crt` | Self-signed test CA that signs the etcd server and client certificates. |
| `ca.key` | CA private key, only needed to re-issue certificates. |
| `server.crt` | etcd server certificate. SANs: `etcd`, `localhost`, `127.0.0.1`, `::1`. |
| `server.key` | etcd server private key (test-only). |
| `client.crt` | Client certificate with `extendedKeyUsage=clientAuth`, used by the mTLS tests. |
| `client.key` | Client private key (test-only). |
| `other-ca.crt` | Untrusted CA used by the negative mTLS tests. |
| `other-ca.key` | Untrusted CA private key (test-only). |
| `untrusted-client.crt` | Client certificate signed by `other-ca`, rejected by etcd. |
| `untrusted-client.key` | Private key for the untrusted client certificate. |
| `no-eku-client.crt` | Client certificate signed by the trusted CA but without `clientAuth` EKU. |
| `no-eku-client.key` | Private key for the no-EKU client certificate. |
| `expired-client.crt` | Client certificate signed by the trusted CA with validity in the past. |
| `expired-client.key` | Private key for the expired client certificate. |

## Generate the certificates

From `Source/certs/`, generate them manually with [generate-certs.sh](https://github.com/bjornbouetsmith/EtcdNETConfiguration/blob/main/Source/certs/generate-certs.sh):

```bash
./generate-certs.sh
```

On Windows, use:

```powershell
docker run --rm -v "${PWD}:/certs" -w /certs alpine:3.20 `
  sh -c "apk add --no-cache bash openssl >/dev/null && bash generate-certs.sh"
```

The generated certificates are valid for 3650 days, except `expired-client.crt`, which is deliberately expired.

After regenerating, recreate the compose stacks so etcd reads the new certificates:

```bash
docker compose -f ../docker-compose.tls.yml up -d --force-recreate
docker compose -f ../docker-compose.mtls.yml up -d --force-recreate
```

## Permissions

Private keys are written with mode `600`. On Windows the test process reads them through the Windows filesystem; WSL users may need to adjust permissions before running tools such as `curl` inside WSL.

The Windows test path uses a digest-pinned Alpine image; override it for offline or mirrored environments with the `ETCD_TLS_CERT_DOCKER_IMAGE` environment variable. Pin any override to a digest for reproducible runs.

## Related pages

- [Testing and Development](Testing-and-Development)
- [TLS](TLS)
- [Mutual TLS](Mutual-TLS)

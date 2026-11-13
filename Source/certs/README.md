# Test TLS certificates

These certificates are used by `docker-compose.tls.yml` and the TLS integration
tests. **They are for local development and testing only - never use them in
production.**

All generated files are git-ignored. The integration test assembly generates
them automatically when they are missing or older than one day. On Windows the
generation runs in an Alpine container because `openssl` and `bash` are usually
not available there; on Linux/macOS the script runs directly.

| File | Purpose |
|-|-|
| `ca.crt` | Self-signed test CA that signs the etcd server certificate. |
| `ca.key` | CA private key, only needed to re-issue certificates. |
| `server.crt` | etcd server certificate. SANs: `etcd`, `localhost`, `127.0.0.1`, `::1`. |
| `server.key` | etcd server private key (test-only). |

Generate them manually with:

```bash
./generate-certs.sh
```

On Windows, use:

```powershell
docker run --rm -v "${PWD}:/certs" -w /certs alpine:3.20 `
  sh -c "apk add --no-cache bash openssl >/dev/null && bash generate-certs.sh"
```

The generated certificates are valid for 3650 days. After regenerating, recreate
the TLS compose stack so etcd reads the new certificate:

```bash
docker compose -f ../docker-compose.tls.yml up -d --force-recreate
```

The Windows test path uses `alpine:3.20`; override it for offline or mirrored
environments with the `ETCD_TLS_CERT_DOCKER_IMAGE` environment variable.

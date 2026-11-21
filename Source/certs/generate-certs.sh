#!/usr/bin/env bash
#
# Generates the self-signed CA, etcd server certificate, and client
# certificate used by docker-compose.tls.yml, docker-compose.mtls.yml, and the
# TLS/mTLS integration tests.
#
# These certificates are for local development and tests only. Never use them
# in production.
#
# Usage: ./generate-certs.sh
#
set -euo pipefail

cd "$(dirname "$0")"

DAYS=3650

echo "Generating test CA..."
openssl genrsa -out ca.key 2048
openssl req -x509 -new -nodes -key ca.key -sha256 -days "$DAYS" \
  -subj "/CN=Etcd Test CA" \
  -addext "basicConstraints=critical,CA:TRUE" \
  -addext "keyUsage=critical,keyCertSign,cRLSign" \
  -out ca.crt

echo "Generating etcd server certificate..."
openssl genrsa -out server.key 2048
openssl req -new -key server.key -subj "/CN=etcd" -out server.csr

cat > server.ext <<'EOF'
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=DNS:etcd,DNS:localhost,IP:127.0.0.1,IP:::1
EOF

openssl x509 -req -in server.csr -CA ca.crt -CAkey ca.key -CAcreateserial \
  -out server.crt -days "$DAYS" -sha256 -extfile server.ext

echo "Generating etcd client certificate..."
openssl genrsa -out client.key 2048
openssl req -new -key client.key -subj "/CN=etcd-client" -out client.csr

cat > client.ext <<'EOF'
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=clientAuth
subjectAltName=DNS:etcd-client
EOF

openssl x509 -req -in client.csr -CA ca.crt -CAkey ca.key -CAcreateserial \
  -out client.crt -days "$DAYS" -sha256 -extfile client.ext

rm -f server.csr server.ext client.csr client.ext ca.srl
chmod 600 ca.key server.key client.key

echo "Done. Generated ca.crt, server.crt, server.key, client.crt and client.key."

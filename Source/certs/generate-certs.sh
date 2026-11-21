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

echo "Generating negative-case client certificates..."

# Client certificate signed by a CA that etcd does not trust.
openssl genrsa -out other-ca.key 2048
openssl req -x509 -new -nodes -key other-ca.key -sha256 -days "$DAYS" \
  -subj "/CN=Etcd Untrusted Test CA" \
  -addext "basicConstraints=critical,CA:TRUE" \
  -addext "keyUsage=critical,keyCertSign,cRLSign" \
  -out other-ca.crt

openssl genrsa -out untrusted-client.key 2048
openssl req -new -key untrusted-client.key -subj "/CN=etcd-untrusted-client" -out untrusted-client.csr

cat > untrusted-client.ext <<'EOF'
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=clientAuth
EOF

openssl x509 -req -in untrusted-client.csr -CA other-ca.crt -CAkey other-ca.key -CAcreateserial \
  -out untrusted-client.crt -days "$DAYS" -sha256 -extfile untrusted-client.ext

# Client certificate without the clientAuth extended key usage.
openssl genrsa -out no-eku-client.key 2048
openssl req -new -key no-eku-client.key -subj "/CN=etcd-no-eku-client" -out no-eku-client.csr

cat > no-eku-client.ext <<'EOF'
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
EOF

openssl x509 -req -in no-eku-client.csr -CA ca.crt -CAkey ca.key -CAcreateserial \
  -out no-eku-client.crt -days "$DAYS" -sha256 -extfile no-eku-client.ext

# Client certificate with a validity period in the past. openssl ca is used
# because the widely available 3.3 image does not support -not_before/-not_after.
mkdir -p .expired-ca
: > .expired-ca/index.txt
echo 1000 > .expired-ca/serial

cat > .expired-ca/ca.cnf <<'EOF'
[ ca ]
default_ca = CA_default

[ CA_default ]
dir = .
database = ./.expired-ca/index.txt
new_certs_dir = ./.expired-ca
certificate = ./ca.crt
private_key = ./ca.key
serial = ./.expired-ca/serial
default_md = sha256
policy = policy_any
unique_subject = no

[ policy_any ]
commonName = supplied

[ client_ext ]
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=clientAuth
EOF

openssl genrsa -out expired-client.key 2048
openssl req -new -key expired-client.key -subj "/CN=etcd-expired-client" -out expired-client.csr
openssl ca -batch -config .expired-ca/ca.cnf -in expired-client.csr -out expired-client.crt \
  -startdate 20200101000000Z -enddate 20200102000000Z -extensions client_ext

rm -rf .expired-ca
rm -f server.csr server.ext client.csr client.ext ca.srl \
  untrusted-client.csr untrusted-client.ext other-ca.srl \
  no-eku-client.csr no-eku-client.ext expired-client.csr
chmod 600 ca.key server.key client.key other-ca.key untrusted-client.key no-eku-client.key expired-client.key

echo "Done. Generated CA, server, client, untrusted-CA, no-EKU and expired client certificates."

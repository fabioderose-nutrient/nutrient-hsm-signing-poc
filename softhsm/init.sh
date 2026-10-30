#!/bin/bash
set -euo pipefail

: "${PKCS11_TOKEN_LABEL:?}" "${PKCS11_KEY_LABEL:?}" "${PKCS11_PIN:?}" "${PKCS11_SO_PIN:?}" "${SIGNING_P12_PASSWORD:?}"

token_exists() {
  softhsm2-util --show-slots | grep -Eq "^[[:space:]]*Label:[[:space:]]+${PKCS11_TOKEN_LABEL}[[:space:]]*$"
}

if token_exists && [ -f /pki/signer.crt ] && [ -f /pki/root-ca.crt ] && [ -f /p12/poc-signer.p12 ]; then
  echo "Token '${PKCS11_TOKEN_LABEL}', certificates and test .p12 already present, nothing to do"
  exit 0
fi

if token_exists; then
  echo "Certificates or test .p12 missing, deleting token '${PKCS11_TOKEN_LABEL}' to start over"
  softhsm2-util --delete-token --token "${PKCS11_TOKEN_LABEL}"
fi

work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT

cat > "${work}/openssl.cnf" <<'EOF'
[req]
distinguished_name = dn
prompt = no

[dn]

[root_ext]
basicConstraints = critical, CA:TRUE
keyUsage = critical, keyCertSign, cRLSign
subjectKeyIdentifier = hash

[signer_ext]
basicConstraints = CA:FALSE
keyUsage = critical, digitalSignature, nonRepudiation
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid, issuer
EOF

echo "Generating test root CA"
openssl genpkey -quiet -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out "${work}/root-ca.key"
openssl req -x509 -new -config "${work}/openssl.cnf" -extensions root_ext \
  -key "${work}/root-ca.key" -sha256 -days 3650 \
  -subj "/O=Nutrient PoC/CN=Nutrient PoC Test Root CA" \
  -out "${work}/root-ca.crt"

echo "Generating signer certificate"
openssl genpkey -quiet -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "${work}/signer.key"
openssl req -new -config "${work}/openssl.cnf" -key "${work}/signer.key" \
  -subj "/O=Nutrient PoC/CN=PoC Approval Signer" \
  -out "${work}/signer.csr"
openssl x509 -req -in "${work}/signer.csr" \
  -CA "${work}/root-ca.crt" -CAkey "${work}/root-ca.key" \
  -set_serial "0x$(openssl rand -hex 16)" -days 730 -sha256 \
  -extfile "${work}/openssl.cnf" -extensions signer_ext \
  -out "${work}/signer.crt"

echo "Initializing token '${PKCS11_TOKEN_LABEL}'"
softhsm2-util --init-token --free --label "${PKCS11_TOKEN_LABEL}" \
  --pin "${PKCS11_PIN}" --so-pin "${PKCS11_SO_PIN}"

echo "Importing private key '${PKCS11_KEY_LABEL}' into the token"
softhsm2-util --import "${work}/signer.key" --token "${PKCS11_TOKEN_LABEL}" \
  --label "${PKCS11_KEY_LABEL}" --id 01 --pin "${PKCS11_PIN}"

echo "Exporting the same key and certificate chain as /p12/poc-signer.p12"
openssl pkcs12 -export -inkey "${work}/signer.key" -in "${work}/signer.crt" \
  -certfile "${work}/root-ca.crt" -name "PoC Approval Signer" \
  -passout env:SIGNING_P12_PASSWORD -out /p12/poc-signer.p12

rm -f "${work}/signer.key" "${work}/root-ca.key"

cp "${work}/root-ca.crt" "${work}/signer.crt" /pki/
echo "Certificates written to /pki, private keys deleted"
openssl x509 -in /pki/signer.crt -noout -subject -issuer -dates

#!/usr/bin/env bash
# Creates .env from .env.example and fills every empty local secret with a
# random value. License keys stay empty (evaluation mode). Does nothing if
# .env already exists.
set -euo pipefail
cd "$(dirname "$0")/.."

if [ -f .env ]; then
  echo ".env already exists, nothing to do"
  exit 0
fi

random() { openssl rand -hex "$1"; }

while IFS= read -r line || [ -n "$line" ]; do
  case "$line" in
    PG_PASSWORD=|DE_API_TOKEN=|OWNER_PASSWORD=|PKCS11_PIN=|PKCS11_SO_PIN=|SIGNING_P12_PASSWORD=)
      echo "${line}$(random 12)" ;;
    DE_SECRET_KEY_BASE=)
      echo "${line}$(random 48)" ;;
    *)
      echo "$line" ;;
  esac
done < .env.example > .env

chmod 600 .env
echo "Created .env with random local secrets (license keys left empty: evaluation mode)"

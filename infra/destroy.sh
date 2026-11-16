#!/usr/bin/env bash
# Deletes the whole resource group created by deploy.sh.
# Usage: ./destroy.sh
set -euo pipefail

RESOURCE_GROUP="${RESOURCE_GROUP:-rg-rpa-to-system-settlement}"

echo "This will permanently delete the resource group '$RESOURCE_GROUP' and everything in it."
read -r -p "Type the resource group name to confirm: " CONFIRM
if [ "$CONFIRM" != "$RESOURCE_GROUP" ]; then
  echo "Confirmation did not match. Aborting."
  exit 1
fi

az group delete --name "$RESOURCE_GROUP" --yes --no-wait
echo "Deletion started (--no-wait). Check progress with: az group show --name $RESOURCE_GROUP"

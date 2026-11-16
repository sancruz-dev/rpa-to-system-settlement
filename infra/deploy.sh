#!/usr/bin/env bash
# Creates (or updates) the whole project in a single resource group.
# Usage: ./deploy.sh
set -euo pipefail

RESOURCE_GROUP="${RESOURCE_GROUP:-rg-rpa-to-system-settlement}"
LOCATION="${LOCATION:-brazilsouth}"

CLIENT_IP="$(curl -s https://api.ipify.org)"
SQL_ADMIN_PASSWORD="${SQL_ADMIN_PASSWORD:-$(openssl rand -base64 24)}"

echo "Resource group: $RESOURCE_GROUP ($LOCATION)"
echo "Client IP for the SQL firewall: $CLIENT_IP"

az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none

az deployment group create \
  --resource-group "$RESOURCE_GROUP" \
  --template-file "$(dirname "$0")/main.bicep" \
  --parameters clientIpAddress="$CLIENT_IP" sqlAdminPassword="$SQL_ADMIN_PASSWORD" \
  --output json > "$(dirname "$0")/.last-deploy-output.json"

echo "Deployment finished. Outputs saved to infra/.last-deploy-output.json"
echo "SQL admin password (save it somewhere safe, it is not stored anywhere else): $SQL_ADMIN_PASSWORD"

#!/usr/bin/env bash
set -euo pipefail

ROOT="/Users/nuttapon/GitHub/BillingService"
LOG="/tmp/billingservice_swagger.log"
URL="http://localhost:5293"

pkill -f BillingService_API || true

cd "$ROOT"
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$URL" \
  nohup dotnet run --project BillingService_API/BillingService_API.csproj --no-launch-profile > "$LOG" 2>&1 &

for i in {1..60}; do
  if curl -fsS "$URL/" >/dev/null 2>&1; then
    echo "BillingService started at $URL"
    if command -v open >/dev/null 2>&1; then
      open "$URL/"
    fi
    echo "Swagger URL: $URL/"
    exit 0
  fi
  sleep 1
done

echo "Failed to start BillingService within timeout."
echo "Last logs:"
tail -n 120 "$LOG" || true
exit 1

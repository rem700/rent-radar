#!/usr/bin/env bash
# End-to-end check of the docker compose deployment: API → RabbitMQ → worker → ingestion → PostgreSQL → nginx.
# Uses a country with no real sources, so only the demo source runs and no real site is contacted.
set -euo pipefail
API=${API:-http://localhost:8000}
WEB=${WEB:-http://localhost:8080}

wait_for() {
  for _ in $(seq 1 60); do
    if curl -fsS "$1" >/dev/null 2>&1; then return 0; fi
    sleep 3
  done
  echo "timeout waiting for $1" >&2
  return 1
}

wait_for "$API/health/ready"
echo "API ready"

id=$(curl -fsS -X POST "$API/api/searches" -H 'Content-Type: application/json' \
  -d '{"country":"XX","city":"Smoketown","filters":{},"sources":null,"max_pages":1,"auto_update":false,"interval_minutes":60}' \
  | python3 -c 'import sys,json; print(json.load(sys.stdin)["id"])')
echo "search $id created"

count=0
for _ in $(seq 1 40); do
  count=$(curl -fsS "$API/api/searches/$id/properties" | python3 -c 'import sys,json; print(len(json.load(sys.stdin)))')
  [ "$count" -gt 0 ] && break
  sleep 3
done
echo "properties: $count"
[ "$count" -eq 30 ]

curl -fsS "$API/api/searches/$id/runs" | python3 -c '
import sys, json
runs = json.load(sys.stdin)
assert len(runs) == 1 and runs[0]["source"] == "demo" and runs[0]["ok"], runs
print("run:", runs[0]["source"], runs[0]["found"])'

wait_for "$WEB/"
curl -fsS "$WEB/" | grep -q '<div id="root">'
curl -fsS "$WEB/api/meta" | python3 -c 'import sys,json; m=json.load(sys.stdin); assert {"HU","RO","SK"} <= set(m["countries"]), m; print("web → api proxy ok")'
echo "smoke test passed"

#!/usr/bin/env bash
# Interactive happy path: PRD → SD → IA_PLAN → IA_CODEGEN → IA_REVIEW → IA_PUSH_DEPLOY → finalize.
set -euo pipefail

BGX_URL="${BGX_URL:-http://127.0.0.1:43181}"
OUT_DIR="${OUT_DIR:-/tmp/bgx-poc-interactive}"
mkdir -p "$OUT_DIR"

echo "=== Starting calculator-add interactive run ==="
START=$(curl -fsS -X POST "$BGX_URL/api/poc/pipeline/start" \
  -H "Content-Type: application/json" \
  -H "X-User-Id: dummy-user" \
  -d '{
    "userRequest": "Create a calculator app that adds two numbers",
    "stackProfile": "fe-be",
    "projectMode": "New",
    "projectName": "Calculator"
  }')
echo "$START" | tee "$OUT_DIR/start.json"
WI=$(python3 -c "import json,sys; print(json.load(open('$OUT_DIR/start.json'))['workItemId'])")

wait_gate() {
  local expected="$1"
  local i
  for i in $(seq 1 180); do
    STATUS=$(curl -fsS "$BGX_URL/api/poc/pipeline/$WI/status")
    echo "$STATUS" > "$OUT_DIR/status.json"
    STAGE=$(python3 -c "import json; d=json.load(open('$OUT_DIR/status.json')); print(d.get('pendingGate') or '')")
    STATE=$(python3 -c "import json; d=json.load(open('$OUT_DIR/status.json')); print(d['status'])")
    FAILED=$(python3 -c "import json; d=json.load(open('$OUT_DIR/status.json')); print(d.get('failed'))")
    if [ "$FAILED" = "True" ] || [ "$FAILED" = "true" ]; then
      python3 -c "import json; d=json.load(open('$OUT_DIR/status.json')); print(d.get('errorMessage'))"
      exit 1
    fi
    if [ "$STATE" = "Completed" ]; then
      echo "Completed early while waiting for $expected"
      return 0
    fi
    if [ "$STAGE" = "$expected" ]; then
      echo "Gate ready: $expected"
      return 0
    fi
    sleep 2
  done
  echo "Timed out waiting for gate $expected"
  cat "$OUT_DIR/status.json"
  exit 1
}

approve() {
  echo "Approving..."
  curl -fsS -X POST "$BGX_URL/api/poc/pipeline/$WI/approve" \
    -H "Content-Type: application/json" \
    -H "X-User-Id: dummy-user" \
    -d '{}'
  echo
}

wait_gate PRD
curl -fsS "$BGX_URL/api/poc/pipeline/$WI/artifacts" | tee "$OUT_DIR/artifacts-prd.json"
approve

wait_gate SYSTEM_DESIGN
approve

wait_gate IA_PLAN
approve

wait_gate IA_REVIEW
approve

wait_gate IA_PUSH_DEPLOY
curl -fsS "$BGX_URL/api/poc/pipeline/$WI/artifacts" | tee "$OUT_DIR/artifacts-deploy.json"
curl -fsS "$BGX_URL/api/poc/pipeline/$WI/logs" | tee "$OUT_DIR/logs.json" >/dev/null
approve

for i in $(seq 1 60); do
  STATUS=$(curl -fsS "$BGX_URL/api/poc/pipeline/$WI/status")
  echo "$STATUS" > "$OUT_DIR/final.json"
  STATE=$(python3 -c "import json; print(json.load(open('$OUT_DIR/final.json'))['status'])")
  if [ "$STATE" = "Completed" ]; then
    URL=$(python3 -c "import json; print(json.load(open('$OUT_DIR/final.json')).get('deployedUrl') or '')")
    echo "Completed. URL=$URL"
    test -n "$URL"
    curl -fsS "$URL/health" | tee "$OUT_DIR/health.json"
    curl -fsS "$URL/api/add?a=2&b=3" | tee "$OUT_DIR/add.json"
    python3 -c "import json; assert json.load(open('$OUT_DIR/add.json'))['result']==5"
    echo "Interactive happy path OK"
    exit 0
  fi
  FAILED=$(python3 -c "import json; print(json.load(open('$OUT_DIR/final.json')).get('failed'))")
  if [ "$FAILED" = "True" ] || [ "$FAILED" = "true" ]; then
    cat "$OUT_DIR/final.json"
    exit 1
  fi
  sleep 2
done

echo "Timed out waiting for Completed"
cat "$OUT_DIR/final.json"
exit 1

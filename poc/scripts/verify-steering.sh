#!/usr/bin/env bash
# Steering smoke: start → wait PRD gate → PAUSE note → STOP → CONTINUE → approve path to Completed.
set -euo pipefail

BGX_URL="${BGX_URL:-http://127.0.0.1:43181}"
OUT_DIR="${OUT_DIR:-/tmp/bgx-poc-steer}"
mkdir -p "$OUT_DIR"

echo "=== Steering verify ==="
START=$(curl -fsS -X POST "$BGX_URL/api/poc/pipeline/start" \
  -H "Content-Type: application/json" \
  -H "X-User-Id: dummy-user" \
  -d '{
    "userRequest": "Create a calculator app that adds two numbers",
    "stackProfile": "fe-be",
    "projectMode": "New",
    "projectName": "Steer Calc"
  }')
echo "$START" | tee "$OUT_DIR/start.json"
WI=$(python3 -c "import json; print(json.load(open('$OUT_DIR/start.json'))['workItemId'])")

wait_status() {
  local want="$1"
  local field="${2:-pendingGate}"
  for i in $(seq 1 120); do
    curl -fsS "$BGX_URL/api/poc/pipeline/$WI/status" > "$OUT_DIR/status.json"
    val=$(python3 -c "import json; d=json.load(open('$OUT_DIR/status.json')); print(d.get('$field') or '')")
    failed=$(python3 -c "import json; print(json.load(open('$OUT_DIR/status.json')).get('failed'))")
    if [ "$failed" = "True" ] || [ "$failed" = "true" ]; then
      cat "$OUT_DIR/status.json"
      exit 1
    fi
    if [ "$val" = "$want" ]; then
      echo "OK $field=$want"
      return 0
    fi
    sleep 1
  done
  echo "timeout waiting $field=$want"
  cat "$OUT_DIR/status.json"
  exit 1
}

wait_status PRD pendingGate

echo "=== PAUSE while at gate ==="
curl -fsS -X POST "$BGX_URL/api/poc/pipeline/$WI/steer" \
  -H "Content-Type: application/json" -H "X-User-Id: dummy-user" \
  -d '{"command":"PAUSE"}' | tee "$OUT_DIR/pause.json"
echo

echo "=== STOP at PRD gate (revert to INTAKE) ==="
curl -fsS -X POST "$BGX_URL/api/poc/pipeline/$WI/steer" \
  -H "Content-Type: application/json" -H "X-User-Id: dummy-user" \
  -d '{"command":"STOP"}' | tee "$OUT_DIR/stop.json"
echo
wait_status Paused status
python3 -c "import json; d=json.load(open('$OUT_DIR/status.json')); assert d.get('lastApprovedStage') in ('INTAKE', None) or d['currentStage']=='INTAKE'; print('reverted stage', d['currentStage'], 'lastApproved', d.get('lastApprovedStage'))"

echo "=== CONTINUE (resume from PRD) ==="
curl -fsS -X POST "$BGX_URL/api/poc/pipeline/$WI/steer" \
  -H "Content-Type: application/json" -H "X-User-Id: dummy-user" \
  -d '{"command":"CONTINUE"}' | tee "$OUT_DIR/continue.json"
echo
python3 -c "import json; d=json.load(open('$OUT_DIR/continue.json')); assert d.get('message'), d; print('CONTINUE message:', d['message'])"
wait_status PRD pendingGate

echo "=== Approve PRD, then ABORT before SD ==="
curl -fsS -X POST "$BGX_URL/api/poc/pipeline/$WI/approve" \
  -H "Content-Type: application/json" -H "X-User-Id: dummy-user" -d '{}'
echo
# wait until either SYSTEM_DESIGN gate or Running
for i in $(seq 1 90); do
  curl -fsS "$BGX_URL/api/poc/pipeline/$WI/status" > "$OUT_DIR/status.json"
  gate=$(python3 -c "import json; print(json.load(open('$OUT_DIR/status.json')).get('pendingGate') or '')")
  st=$(python3 -c "import json; print(json.load(open('$OUT_DIR/status.json'))['status'])")
  if [ "$gate" = "SYSTEM_DESIGN" ] || [ "$st" = "Running" ]; then
    break
  fi
  sleep 1
done

curl -fsS -X POST "$BGX_URL/api/poc/pipeline/$WI/steer" \
  -H "Content-Type: application/json" -H "X-User-Id: dummy-user" \
  -d '{"command":"ABORT"}' | tee "$OUT_DIR/abort.json"
echo
wait_status Aborted status

echo "=== Fresh run ABORT early (idle chat path) ==="
START2=$(curl -fsS -X POST "$BGX_URL/api/poc/pipeline/start" \
  -H "Content-Type: application/json" -H "X-User-Id: dummy-user" \
  -d '{"userRequest":"Create a calculator app that adds two numbers","stackProfile":"fe-be","projectMode":"New","projectName":"Steer Abort2"}')
WI2=$(python3 -c "import json,sys; print(json.loads(sys.argv[1])['workItemId'])" "$START2")
sleep 2
curl -fsS -X POST "$BGX_URL/api/poc/pipeline/$WI2/steer" \
  -H "Content-Type: application/json" -H "X-User-Id: dummy-user" \
  -d '{"command":"ABORT"}' | tee "$OUT_DIR/abort2.json"
echo
curl -fsS "$BGX_URL/api/poc/pipeline/$WI2/status" | tee "$OUT_DIR/abort2-status.json"
python3 -c "import json; assert json.load(open('$OUT_DIR/abort2-status.json'))['status']=='Aborted'"

echo "Steering verification OK"

#!/usr/bin/env bash
# End-to-end Implementation Agent path: PRD → SD → IA_PLAN → IA_CODEGEN → IA_REVIEW → IA_PUSH_DEPLOY
set -euo pipefail

BGX_URL="${BGX_URL:-http://127.0.0.1:43181}"
OUT_DIR="${OUT_DIR:-/tmp/bgx-poc-verify-ia}"
mkdir -p "$OUT_DIR"

echo "=== IA Calculator (auto-approve) ==="
ADD_JSON=$(curl -fsS -X POST "$BGX_URL/api/poc/pipeline/run" \
  -H "Content-Type: application/json" \
  -H "X-User-Id: dummy-user" \
  -d '{
    "userRequest": "Create a calculator app that adds two numbers",
    "stackProfile": "fe-be",
    "projectMode": "New",
    "projectName": "Calculator"
  }')
echo "$ADD_JSON" | tee "$OUT_DIR/ia-add.json"
STATUS=$(python3 -c "import json,sys; print(json.loads(sys.argv[1])['status'])" "$ADD_JSON")
ADD_URL=$(python3 -c "import json,sys; print(json.loads(sys.argv[1]).get('deployedUrl') or '')" "$ADD_JSON")
WI=$(python3 -c "import json,sys; print(json.loads(sys.argv[1])['workItemId'])" "$ADD_JSON")
PID=$(python3 -c "import json,sys; print(json.loads(sys.argv[1])['projectId'])" "$ADD_JSON")
test "$STATUS" = "Completed"
test -n "$ADD_URL"

echo "Deployed: $ADD_URL"
curl -fsS "$ADD_URL/health" | tee "$OUT_DIR/ia-health.json"
curl -fsS "$ADD_URL/api/add?a=2&b=3" | tee "$OUT_DIR/ia-add-result.json"
python3 -c "import json; d=json.load(open('$OUT_DIR/ia-add-result.json')); assert d.get('result')==5, d"

MONO="project-${PID}-work-item-${WI}"
FOUND=$(find /tmp/bgx-poc -maxdepth 2 -type d -name "$MONO" 2>/dev/null | head -n1 || true)
if [[ -z "$FOUND" ]]; then
  echo "Mono-repo folder $MONO not found under /tmp/bgx-poc" >&2
  find /tmp/bgx-poc -maxdepth 3 -type d 2>/dev/null | head -40 || true
  exit 1
fi
test -d "$FOUND/frontend"
test -d "$FOUND/backend"
echo "Mono folder OK: $FOUND"

# Prefer pull-request artifact via status/events if exposed; else check local file from agent blobs
PR_BLOB=$(find /tmp/bgx-poc-blobs -name 'pull-request.json' 2>/dev/null | head -n1 || true)
if [[ -n "$PR_BLOB" ]]; then
  echo "PR artifact: $PR_BLOB"
  cat "$PR_BLOB" | tee "$OUT_DIR/ia-pr.json"
fi

echo "IA e2e Calculator path completed."

#!/usr/bin/env bash
set -euo pipefail

BGX_URL="${BGX_URL:-http://127.0.0.1:43181}"
OUT_DIR="${OUT_DIR:-/tmp/bgx-poc-verify}"
mkdir -p "$OUT_DIR"

post_run() {
  local name="$1"
  local body="$2"
  echo "=== Running: $name ==="
  curl -fsS -X POST "$BGX_URL/api/poc/pipeline/run" \
    -H "Content-Type: application/json" \
    -H "X-User-Id: dummy-user" \
    -d "$body" | tee "$OUT_DIR/$name.json"
  echo
  local url
  url=$(python3 -c "import json,sys; print(json.load(open('$OUT_DIR/$name.json')).get('deployedUrl') or '')")
  local status
  status=$(python3 -c "import json; print(json.load(open('$OUT_DIR/$name.json'))['status'])")
  test "$status" = "Completed"
  test -n "$url"
  echo "Deployed URL: $url"
  curl -fsS "$url/health" | tee "$OUT_DIR/$name-health.json"
  echo
  echo "$url"
}

HELLO_URL=$(post_run hello '{
  "userRequest": "Build a Hello World app: Angular FE + .NET 10 BE + an LLM giving a thought of the day",
  "stackProfile": "fe-be-llm",
  "projectMode": "New",
  "projectName": "Hello World"
}' | tail -n1)

curl -fsS "$HELLO_URL/api/thought" | tee "$OUT_DIR/hello-thought.json"
echo

ADD_JSON=$(curl -fsS -X POST "$BGX_URL/api/poc/pipeline/run" \
  -H "Content-Type: application/json" \
  -H "X-User-Id: dummy-user" \
  -d '{
    "userRequest": "Create a calculator app that adds two numbers",
    "stackProfile": "fe-be",
    "projectMode": "New",
    "projectName": "Calculator"
  }')
echo "$ADD_JSON" | tee "$OUT_DIR/add.json"
ADD_URL=$(python3 -c "import json,sys; print(json.loads(sys.argv[1])['deployedUrl'])" "$ADD_JSON")
ADD_PROJECT=$(python3 -c "import json,sys; print(json.loads(sys.argv[1])['projectId'])" "$ADD_JSON")
curl -fsS "$ADD_URL/api/add?a=2&b=3" | tee "$OUT_DIR/add-result.json"
echo

SUB_JSON=$(curl -fsS -X POST "$BGX_URL/api/poc/pipeline/run" \
  -H "Content-Type: application/json" \
  -H "X-User-Id: dummy-user" \
  -d "{
    \"userRequest\": \"Update the calculator app to subtract two numbers\",
    \"stackProfile\": \"fe-be\",
    \"projectMode\": \"Existing\",
    \"projectId\": \"$ADD_PROJECT\",
    \"projectName\": \"Calculator\",
    \"baselineArtifacts\": { \"prd\": \"baseline/prd.json\", \"techSpec\": \"baseline/techspec.json\" }
  }")
echo "$SUB_JSON" | tee "$OUT_DIR/subtract.json"
SUB_URL=$(python3 -c "import json,sys; print(json.loads(sys.argv[1])['deployedUrl'])" "$SUB_JSON")
curl -fsS "$SUB_URL/api/add?a=2&b=3" | tee "$OUT_DIR/subtract-still-adds.json"
echo
curl -fsS "$SUB_URL/api/subtract?a=5&b=2" | tee "$OUT_DIR/subtract-result.json"
echo

echo "All three happy paths completed."
echo "Audit logs under /tmp/bgx-poc-audit"
ls -la /tmp/bgx-poc-audit | head

#!/usr/bin/env bash
# Greenfield rewrite + multi-WI feature-increment e2e (auto-approve).
set -euo pipefail

BGX_URL="${BGX_URL:-http://127.0.0.1:43181}"
OUT_DIR="${OUT_DIR:-/tmp/bgx-poc-verify-greenfield}"
mkdir -p "$OUT_DIR"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
POC_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
FIXTURE="$POC_ROOT/fixtures/legacy-net8-calculator"
test -d "$FIXTURE"

echo "=== 1) Rewrite legacy net8 → net10 (greenfield) ==="
REWRITE_JSON=$(curl -fsS -X POST "$BGX_URL/api/poc/pipeline/run" \
  -H "Content-Type: application/json" \
  -H "X-User-Id: dummy-user" \
  -d "{
    \"userRequest\": \"Rewrite the existing net8 calculator repo as a greenfield .NET 10 app keeping /api/add\",
    \"stackProfile\": \"fe-be\",
    \"projectMode\": \"Rewrite\",
    \"projectName\": \"CalculatorRewrite\",
    \"baselineArtifacts\": { \"baselineSourcePath\": \"$FIXTURE\" }
  }")
echo "$REWRITE_JSON" | tee "$OUT_DIR/rewrite.json"
python3 - <<PY
import json
d=json.load(open("$OUT_DIR/rewrite.json"))
assert d["status"]=="Completed", d
url=d["deployedUrl"]
print("rewrite url", url)
import urllib.request
print(urllib.request.urlopen(url+"/health").read().decode())
body=urllib.request.urlopen(url+"/api/add?a=2&b=3").read().decode()
assert '"result":5' in body.replace(" ", "") or '"result": 5' in body, body
pid,wid=d["projectId"],d["workItemId"]
cs=json.load(open(f"/tmp/bgx-poc-blobs/{pid}/{wid}/v1/code-state.json",encoding="utf-8-sig"))
assert cs["command"]=="graphify extract . --code-only"
assert cs["mode"]=="request-driven"
print("rewrite code-state OK")
PY

echo "=== 2) Multi-WI: New calculator then Existing subtract ==="
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
ADD_PROJECT=$(python3 -c "import json; print(json.load(open('$OUT_DIR/add.json'))['projectId'])")
ADD_URL=$(python3 -c "import json; print(json.load(open('$OUT_DIR/add.json'))['deployedUrl'])")
curl -fsS "$ADD_URL/api/add?a=2&b=3" | tee "$OUT_DIR/add-result.json"
python3 -c "import json; d=json.load(open('$OUT_DIR/add-result.json')); assert d.get('result')==5, d"

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
SUB_URL=$(python3 -c "import json; print(json.load(open('$OUT_DIR/subtract.json'))['deployedUrl'])")
curl -fsS "$SUB_URL/api/add?a=2&b=3" | tee "$OUT_DIR/subtract-still-adds.json"
curl -fsS "$SUB_URL/api/subtract?a=5&b=2" | tee "$OUT_DIR/subtract-result.json"
python3 -c "import json; d=json.load(open('$OUT_DIR/subtract-result.json')); assert d.get('result')==3, d"
python3 - <<PY
import json
d=json.load(open("$OUT_DIR/subtract.json"))
assert d["status"]=="Completed", d
pid,wid=d["projectId"],d["workItemId"]
cs=json.load(open(f"/tmp/bgx-poc-blobs/{pid}/{wid}/v1/code-state.json",encoding="utf-8-sig"))
assert cs["command"]=="graphify extract . --code-only"
print("feature-increment code-state OK")
# intake/prd should mention delivery for rewrite run
import pathlib
rewrite=json.load(open("$OUT_DIR/rewrite.json"))
rpid,rwid=rewrite["projectId"],rewrite["workItemId"]
prd=json.load(open(f"/tmp/bgx-poc-blobs/{rpid}/{rwid}/v1/prd.json",encoding="utf-8-sig"))
assert prd.get("deliveryMode")=="Rewrite", prd
intake=json.load(open(f"/tmp/bgx-poc-blobs/{rpid}/{rwid}/v1/intake-summary.json",encoding="utf-8-sig"))
assert intake.get("deliveryMode")=="Rewrite", intake
print("prd+intake deliveryMode Rewrite OK")
PY

echo "Greenfield rewrite + multi-WI feature-increment e2e completed."

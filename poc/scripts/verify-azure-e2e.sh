#!/usr/bin/env bash
# Gate D — full Calculator IA+TA against Azure DevOps + App Service.
# Requires Gate C preflight env. AgentHub must be running with the same env.
set -euo pipefail

BGX_URL="${BGX_URL:-http://127.0.0.1:43181}"
AH_URL="${AH_URL:-http://127.0.0.1:43180}"
OUT_DIR="${OUT_DIR:-/tmp/bgx-poc-verify-azure-e2e}"
mkdir -p "$OUT_DIR"

fail() { echo "FAIL: $*" >&2; exit 1; }
ok() { echo "OK: $*"; }

[[ "${IA_GIT_PROVIDER:-}" == "azuredevops" || "${IA_GIT_PROVIDER:-}" == "azdo" ]] \
  || fail "Export IA_GIT_PROVIDER=azuredevops and restart AgentHub with that env"
[[ "${IA_DEPLOY_PROVIDER:-}" == "appservice" || "${IA_DEPLOY_PROVIDER:-}" == "azure" ]] \
  || fail "Export IA_DEPLOY_PROVIDER=appservice and restart AgentHub with that env"
: "${IA_GIT_TOKEN:?IA_GIT_TOKEN required}"
: "${IA_DEPLOY_PASSWORD:?IA_DEPLOY_PASSWORD required}"

echo "=== Preflight (embedded) ==="
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
"$SCRIPT_DIR/verify-azure-preflight.sh"

echo "=== Health APIs ==="
curl -fsS "$AH_URL/" | tee "$OUT_DIR/ah.json" >/dev/null
curl -fsS "$BGX_URL/" | tee "$OUT_DIR/bg.json" >/dev/null
ok "AgentHub + BuildGenix up"

echo "=== IA+TA Calculator against Azure ==="
JSON=$(curl -fsS -X POST "$BGX_URL/api/poc/pipeline/run" \
  -H "Content-Type: application/json" \
  -H "X-User-Id: dummy-user" \
  -d '{
    "userRequest": "Create a calculator app that adds two numbers",
    "stackProfile": "fe-be",
    "projectMode": "New",
    "projectName": "Calculator"
  }')
echo "$JSON" | tee "$OUT_DIR/run.json"

python3 - <<PY
import json,sys
d=json.load(open("$OUT_DIR/run.json"))
assert d.get("status")=="Completed", d
assert d.get("deployedUrl"), d
url=d["deployedUrl"]
assert url.startswith("https://"), ("expected App Service https URL", url)
trail=" ".join(d.get("transitionTrail") or [])
for n in ["IA_PUSH_DEPLOY","TA_GENERATE","TA_VERIFY","TA_REPORT","TA_MERGE"]:
    assert n in trail, (n, trail)
open("$OUT_DIR/url.txt","w").write(url)
open("$OUT_DIR/wi.txt","w").write(d["workItemId"])
open("$OUT_DIR/pid.txt","w").write(d["projectId"])
print("deployedUrl", url)
print("trail", trail)
PY

URL=$(cat "$OUT_DIR/url.txt")
WI=$(cat "$OUT_DIR/wi.txt")
PID=$(cat "$OUT_DIR/pid.txt")

echo "Smoke $URL"
curl -fsS "$URL/health" | tee "$OUT_DIR/health.json"
curl -fsS "$URL/api/add?a=2&b=3" | tee "$OUT_DIR/add.json"
python3 -c "import json; assert json.load(open('$OUT_DIR/add.json'))['result']==5"

BLOB="/tmp/bgx-poc-blobs/${PID}/${WI}/v1"
# If using Azure blobs, operator should also check Portal; local File fallback still used unless BlobStorage__Provider=Azure
if [[ -f "$BLOB/merge.json" ]]; then
  cp "$BLOB/merge.json" "$OUT_DIR/merge.json"
  cp "$BLOB/pull-request.json" "$OUT_DIR/pull-request.json"
  cp "$BLOB/ta-results.json" "$OUT_DIR/ta-results.json"
  python3 - <<'PY'
import json
m=json.load(open("/tmp/bgx-poc-verify-azure-e2e/merge.json",encoding="utf-8-sig"))
assert m.get("provider")=="azuredevops", m
r=json.load(open("/tmp/bgx-poc-verify-azure-e2e/ta-results.json",encoding="utf-8-sig"))
assert r.get("status")=="green", r
print("merge provider", m.get("provider"), "pr", m.get("prNumber"))
PY
  ok "Artifacts provider=azuredevops and ta-results green"
else
  echo "WARN: local blob path missing $BLOB — if BlobStorage is Azure, confirm merge.json in Portal container"
  echo "WARN: still requiring HTTPS smoke which passed"
fi

ok "Azure e2e PASSED"
echo "Record: URL=$URL WI=$WI PID=$PID"
echo "Next: Gate E — unset Azure env, restart AgentHub, ./scripts/verify-ta.sh"

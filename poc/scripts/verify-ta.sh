#!/usr/bin/env bash
# Full IA + TA e2e: deploy → generate live tests → verify URL → report → merge PR
set -euo pipefail

BGX_URL="${BGX_URL:-http://127.0.0.1:43181}"
GITEA_URL="${GITEA_URL:-http://127.0.0.1:43185}"
OUT_DIR="${OUT_DIR:-/tmp/bgx-poc-verify-ta}"
mkdir -p "$OUT_DIR"

echo "=== IA+TA Calculator (auto-approve) ==="
JSON=$(curl -fsS -X POST "$BGX_URL/api/poc/pipeline/run" \
  -H "Content-Type: application/json" \
  -H "X-User-Id: dummy-user" \
  -d '{
    "userRequest": "Create a calculator app that adds two numbers",
    "stackProfile": "fe-be",
    "projectMode": "New",
    "projectName": "Calculator"
  }')
echo "$JSON" | tee "$OUT_DIR/ta-run.json"

python3 - <<PY
import json
d=json.load(open("$OUT_DIR/ta-run.json"))
assert d["status"]=="Completed", d
assert d.get("deployedUrl"), d
trail=d.get("transitionTrail") or []
need=["IA_PUSH_DEPLOY","TA_GENERATE","TA_VERIFY","TA_REPORT","TA_MERGE"]
text=" ".join(trail)
for n in need:
    assert n in text, (n, trail)
open("$OUT_DIR/url.txt","w").write(d["deployedUrl"])
open("$OUT_DIR/wi.txt","w").write(d["workItemId"])
open("$OUT_DIR/pid.txt","w").write(d["projectId"])
print("trail ok:", trail)
PY

URL=$(cat "$OUT_DIR/url.txt")
WI=$(cat "$OUT_DIR/wi.txt")
PID=$(cat "$OUT_DIR/pid.txt")

echo "Deployed: $URL"
curl -fsS "$URL/health" | tee "$OUT_DIR/health.json"
curl -fsS "$URL/api/add?a=2&b=3" | tee "$OUT_DIR/add.json"
python3 -c "import json; assert json.load(open('$OUT_DIR/add.json'))['result']==5"

MONO="project-${PID}-work-item-${WI}"
FOUND=$(find /tmp/bgx-poc -maxdepth 2 -type d -name "$MONO" 2>/dev/null | head -n1 || true)
test -n "$FOUND"
test -f "$FOUND/tests/e2e/deployed-smoke.sh"
test -f "$FOUND/tests/e2e/cases.json"
echo "E2E suite OK under $FOUND/tests/e2e"

BLOB_DIR="/tmp/bgx-poc-blobs/${PID}/${WI}/v1"
for name in ta-results.json ta-report.md ta-status.json merge.json pull-request.json; do
  hit="$BLOB_DIR/$name"
  test -f "$hit" || { echo "missing artifact $hit"; exit 1; }
  echo "artifact $name -> $hit"
  cp "$hit" "$OUT_DIR/$name"
done

python3 - <<PY
import json
def load(path):
    return json.load(open(path, encoding="utf-8-sig"))
r=load("$OUT_DIR/ta-results.json")
assert r.get("status")=="green", r
m=load("$OUT_DIR/merge.json")
assert m.get("detail"), m
assert m.get("provider") == "gitea", m
assert m.get("prNumber"), m
print("merge provider:", m.get("provider"), "pr:", m.get("prNumber"), "commit:", m.get("mergeCommit"), "detail:", m.get("detail"))
open("$OUT_DIR/prnum.txt","w").write(str(m.get("prNumber") or ""))
PY

PR=$(cat "$OUT_DIR/prnum.txt")
STATE=$(curl -fsS "$GITEA_URL/api/v1/repos/buildgenix/app-monorepo/pulls/$PR" | python3 -c "import json,sys; d=json.load(sys.stdin); print('merged='+str(d.get('merged'))+' state='+str(d.get('state')))")
echo "Gitea PR #$PR $STATE"
echo "$STATE" | grep -q 'merged=True'


echo "TA e2e requirements path completed."

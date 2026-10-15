#!/usr/bin/env bash
# Gate C — prove Azure/AzDO credentials and endpoints before full e2e.
# Requires env from QA_Runbook Gate C. Does not run the pipeline.
set -euo pipefail

fail() { echo "FAIL: $*" >&2; exit 1; }
ok() { echo "OK: $*"; }

command -v curl >/dev/null || fail "curl required"
command -v python3 >/dev/null || fail "python3 required"

: "${IA_GIT_PROVIDER:=azuredevops}"
: "${IA_DEPLOY_PROVIDER:=appservice}"

[[ "$IA_GIT_PROVIDER" == "azuredevops" || "$IA_GIT_PROVIDER" == "azdo" ]] \
  || fail "IA_GIT_PROVIDER must be azuredevops for this preflight (got $IA_GIT_PROVIDER)"
[[ "$IA_DEPLOY_PROVIDER" == "appservice" || "$IA_DEPLOY_PROVIDER" == "azure" ]] \
  || fail "IA_DEPLOY_PROVIDER must be appservice for this preflight (got $IA_DEPLOY_PROVIDER)"

: "${IA_GIT_ORGANIZATION:?set IA_GIT_ORGANIZATION}"
: "${IA_GIT_PROJECT:?set IA_GIT_PROJECT}"
: "${IA_GIT_REPO:?set IA_GIT_REPO}"
: "${IA_GIT_TOKEN:?set IA_GIT_TOKEN (AzDO PAT)}"
: "${IA_DEPLOY_APP_NAME:?set IA_DEPLOY_APP_NAME}"
: "${IA_DEPLOY_USER:?set IA_DEPLOY_USER (publish profile)}"
: "${IA_DEPLOY_PASSWORD:?set IA_DEPLOY_PASSWORD (publish profile)}"

ORG="$IA_GIT_ORGANIZATION"
PROJECT="$IA_GIT_PROJECT"
REPO="$IA_GIT_REPO"
PAT="$IA_GIT_TOKEN"
APP="$IA_DEPLOY_APP_NAME"
PUBLIC_URL="${IA_DEPLOY_PUBLIC_URL:-https://${APP}.azurewebsites.net}"
SCM_URL="${IA_DEPLOY_SCM_URL:-https://${APP}.scm.azurewebsites.net}"

echo "=== Azure preflight ==="
echo "org=$ORG project=$PROJECT repo=$REPO app=$APP"

# --- AzDO: list repository with PAT (Basic :PAT) ---
AUTH=$(python3 - <<'PY' "$PAT"
import base64,sys
print(base64.b64encode((':'+sys.argv[1]).encode()).decode())
PY
)
PROJ_ENC=$(python3 - <<'PY' "$PROJECT"
import urllib.parse,sys
print(urllib.parse.quote(sys.argv[1], safe=''))
PY
)
REPO_ENC=$(python3 - <<'PY' "$REPO"
import urllib.parse,sys
print(urllib.parse.quote(sys.argv[1], safe=''))
PY
)

AZDO_URL="https://dev.azure.com/${ORG}/${PROJ_ENC}/_apis/git/repositories/${REPO_ENC}?api-version=7.1"
HTTP=$(curl -sS -o /tmp/azdo-repo.json -w "%{http_code}" \
  -H "Authorization: Basic ${AUTH}" \
  "$AZDO_URL" || true)
[[ "$HTTP" == "200" ]] || fail "AzDO repo GET HTTP $HTTP — check org/project/repo/PAT scopes (Code Read). Body: $(head -c 300 /tmp/azdo-repo.json)"
python3 - <<'PY'
import json
d=json.load(open("/tmp/azdo-repo.json"))
assert d.get("name") or d.get("id"), d
print("repo name:", d.get("name"), "id:", d.get("id"))
PY
ok "AzDO repository reachable with PAT"

# --- App Service: public host resolves ---
curl -sS -o /dev/null --connect-timeout 10 "$PUBLIC_URL" || fail "Cannot connect to $PUBLIC_URL"
ok "Public App Service URL reachable (any HTTP status)"

# --- SCM: basic auth probe (zipdeploy without body may 4xx but not 401) ---
SCM_AUTH=$(python3 - <<'PY' "$IA_DEPLOY_USER" "$IA_DEPLOY_PASSWORD"
import base64,sys
print(base64.b64encode((sys.argv[1]+':'+sys.argv[2]).encode()).decode())
PY
)
SCM_HTTP=$(curl -sS -o /tmp/scm-probe.txt -w "%{http_code}" \
  -X POST \
  -H "Authorization: Basic ${SCM_AUTH}" \
  -H "Content-Type: application/octet-stream" \
  --data-binary '' \
  "${SCM_URL}/api/zipdeploy" || true)
if [[ "$SCM_HTTP" == "401" || "$SCM_HTTP" == "403" ]]; then
  fail "SCM zipdeploy auth HTTP $SCM_HTTP — enable SCM Basic Auth Publishing Credentials and re-copy publish profile"
fi
ok "SCM zipdeploy endpoint accepted auth (HTTP $SCM_HTTP — empty body probe is fine)"

# Optional: az account
if command -v az >/dev/null 2>&1; then
  az account show --query "{name:name,id:id}" -o tsv 2>/dev/null && ok "az account show" \
    || echo "WARN: az present but not logged in (optional for this preflight)"
else
  echo "WARN: az CLI not installed (optional if PAT + publish profile already set)"
fi

echo "=== Preflight PASSED — proceed to verify-azure-e2e.sh ==="

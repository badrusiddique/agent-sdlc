#!/usr/bin/env bash
# Seed local Gitea (AzDo stand-in) with poc user + buildgenix/app-monorepo.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BASE_URL="${GITEA_URL:-http://127.0.0.1:43185}"
USER="${GITEA_USER:-poc}"
PASS="${GITEA_PASS:-pocpocpoc}"
EMAIL="${GITEA_EMAIL:-poc@buildgenix.local}"
ORG="${GITEA_ORG:-buildgenix}"
REPO="${GITEA_REPO:-app-monorepo}"

echo "Starting Gitea (and Azurite)…"
cd "$ROOT/agenthub-api"
docker compose up -d azurite gitea

echo "Waiting for Gitea at $BASE_URL …"
for i in $(seq 1 60); do
  if curl -fsS "$BASE_URL/api/v1/version" >/dev/null 2>&1; then
    break
  fi
  sleep 2
  if [[ "$i" -eq 60 ]]; then
    echo "Gitea did not become ready." >&2
    exit 1
  fi
done

echo "Ensuring admin user $USER …"
# gitea CLI must not run as root inside the container
if ! docker exec -u git agenthub-gitea gitea admin user list 2>/dev/null | grep -q "$USER"; then
  docker exec -u git agenthub-gitea gitea admin user create \
    --username "$USER" \
    --password "$PASS" \
    --email "$EMAIL" \
    --admin \
    --must-change-password=false || true
fi
# Give Gitea a moment after first user create
sleep 2

TOKEN_NAME="ia-seed-$(date +%s)"
TOKEN_JSON=$(curl -fsS -X POST "$BASE_URL/api/v1/users/$USER/tokens" \
  -u "$USER:$PASS" \
  -H "Content-Type: application/json" \
  -d "{\"name\":\"$TOKEN_NAME\",\"scopes\":[\"write:repository\",\"write:organization\",\"write:user\"]}")
TOKEN=$(python3 -c "import json,sys; print(json.load(sys.stdin).get('sha1',''))" <<<"$TOKEN_JSON")
if [[ -z "$TOKEN" ]]; then
  echo "Failed to mint Gitea token: $TOKEN_JSON" >&2
  exit 1
fi

curl -fsS -X POST "$BASE_URL/api/v1/orgs" \
  -H "Authorization: token $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"username\":\"$ORG\",\"visibility\":\"public\"}" >/dev/null 2>&1 || true

curl -fsS -X POST "$BASE_URL/api/v1/orgs/$ORG/repos" \
  -H "Authorization: token $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"name\":\"$REPO\",\"description\":\"BuildGenix POC mono-repo\",\"private\":false,\"auto_init\":true,\"default_branch\":\"main\"}" \
  >/dev/null 2>&1 || true

echo "Gitea ready."
echo "  URL:    $BASE_URL"
echo "  User:   $USER / $PASS"
echo "  Repo:   $ORG/$REPO"
echo "  Token:  $TOKEN"
echo "export GITEA_TOKEN=$TOKEN"

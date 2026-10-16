#!/usr/bin/env bash
# Reclaim Docker networks/containers from prior POC work-item deploys.
# Keeps agenthub-gitea and agenthub-azurite. Run before long Manual QA matrices.
set -euo pipefail
docker ps -a --format '{{.Names}}' | grep -E '^[0-9a-f]{20,}-api-1$' | while read -r n; do
  docker rm -f "$n" >/dev/null 2>&1 || true
done
docker network ls --format '{{.Name}}' | grep -E '^[0-9a-f]{20,}_default$' | while read -r n; do
  docker network rm "$n" >/dev/null 2>&1 || true
done
docker network prune -f
echo "Pruned POC compose leftovers. Remaining:"
docker ps --format '{{.Names}} {{.Status}}' | head -20

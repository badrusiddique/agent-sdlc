#!/usr/bin/env bash
# Build the IA/TA toolchain image used by BuilderShell.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOCKER_DIR="$ROOT/agenthub-api/docker/ia-builder"
DATE_TAG="${DATE_TAG:-$(date -u +%Y%m%d)}"
LOCAL_TAG="${LOCAL_TAG:-agenthub-ia-builder:local}"
DATED_TAG="${DATED_TAG:-agenthub-ia-builder:${DATE_TAG}}"

# Pin linux/amd64 so Apple Silicon / mixed hosts still produce an ACR-friendly image.
PLATFORM="${DOCKER_PLATFORM:-linux/amd64}"

echo "Building $LOCAL_TAG and $DATED_TAG from $DOCKER_DIR (platform=$PLATFORM)"
docker build \
  --platform "$PLATFORM" \
  -t "$LOCAL_TAG" \
  -t "$DATED_TAG" \
  -f "$DOCKER_DIR/Dockerfile" \
  "$DOCKER_DIR"

echo "Built:"
docker image inspect "$LOCAL_TAG" --format '{{.Id}} {{.RepoTags}} {{.Size}}'
echo "Verify toolchain inside image:"
docker run --rm "$LOCAL_TAG" bash -lc 'git --version; dotnet --list-sdks; node -v; ng version --skip-git 2>/dev/null | head -5; python3 --version; az version --output tsv 2>/dev/null | head -3 || az --version | head -3'
echo "OK. Next: ./poc/scripts/save-ia-builder.sh"

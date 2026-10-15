#!/usr/bin/env bash
# Export agenthub-ia-builder image as a tar.gz for manual ACR / blob upload.
# Does NOT commit the tar (too large). Writes under poc/docker-images/ (gitignored)
# and /opt/cursor/artifacts/ when present.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DATE_TAG="${DATE_TAG:-$(date -u +%Y%m%d)}"
LOCAL_TAG="${LOCAL_TAG:-agenthub-ia-builder:local}"
OUT_DIR="${OUT_DIR:-$ROOT/docker-images}"
mkdir -p "$OUT_DIR"

if ! docker image inspect "$LOCAL_TAG" >/dev/null 2>&1; then
  echo "Image $LOCAL_TAG missing — running build first."
  "$ROOT/scripts/build-ia-builder.sh"
fi

TAR="$OUT_DIR/agenthub-ia-builder-${DATE_TAG}.tar"
GZ="${TAR}.gz"
MANIFEST="$OUT_DIR/agenthub-ia-builder-${DATE_TAG}.manifest.txt"

echo "Saving $LOCAL_TAG → $GZ"
docker save "$LOCAL_TAG" "agenthub-ia-builder:${DATE_TAG}" -o "$TAR"
gzip -f "$TAR"

{
  echo "image=$LOCAL_TAG"
  echo "dated=agenthub-ia-builder:${DATE_TAG}"
  echo "createdUtc=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "file=$(basename "$GZ")"
  echo "sha256=$(sha256sum "$GZ" | awk '{print $1}')"
  echo "bytes=$(wc -c < "$GZ" | tr -d ' ')"
  docker image inspect "$LOCAL_TAG" --format 'id={{.Id}} size={{.Size}}'
} | tee "$MANIFEST"

# Keep a walkthrough / handoff copy when the artifacts mount exists
if [ -d /opt/cursor/artifacts ]; then
  cp -f "$GZ" "/opt/cursor/artifacts/$(basename "$GZ")"
  cp -f "$MANIFEST" "/opt/cursor/artifacts/$(basename "$MANIFEST")"
  echo "Also copied to /opt/cursor/artifacts/"
fi

echo "Done. Upload $GZ to ACR (docker load + push) or to a storage blob."
echo "Manifest: $MANIFEST"

# Exported image handoff (2026-10-15)

The runnable image is **not** stored in git (≈626 MB compressed / ≈2.9 GB uncompressed). Build or load it locally, then push to ACR or a blob yourself.

## Tags built in this session

- `agenthub-ia-builder:local`
- `agenthub-ia-builder:20261015`

## Export produced by `./poc/scripts/save-ia-builder.sh`

| Field | Value |
|---|---|
| File | `agenthub-ia-builder-20261015.tar.gz` |
| Location (agent VM) | `poc/docker-images/` (gitignored) and `/opt/cursor/artifacts/` |
| sha256 | `4b2966508a8d86ed5c055a31f9223e117f4b8a920f71504e6452932c56c1ac2f` |
| bytes | `655481991` |

Re-run `save-ia-builder.sh` after a rebuild to refresh the checksum.

## Manual ACR

```bash
gunzip -c agenthub-ia-builder-20261015.tar.gz | docker load
az acr login --name <youracr>
docker tag agenthub-ia-builder:20261015 <youracr>.azurecr.io/agenthub-ia-builder:20261015
docker push <youracr>.azurecr.io/agenthub-ia-builder:20261015
export IA_BUILDER_IMAGE=<youracr>.azurecr.io/agenthub-ia-builder:20261015
```

## Manual blob

Upload the `.tar.gz` to a storage container (e.g. `build-artifacts`). On a builder VM:

```bash
az storage blob download ... -f agenthub-ia-builder-20261015.tar.gz
gunzip -c agenthub-ia-builder-20261015.tar.gz | docker load
```

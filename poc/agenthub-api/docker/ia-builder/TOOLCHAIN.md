# agenthub-ia-builder toolchain

Image tags used in this repo:

| Tag | Purpose |
|---|---|
| `agenthub-ia-builder:local` | What `BuilderShell` looks for on a laptop / Cloud Agent |
| `agenthub-ia-builder:YYYYMMDD` | Dated export for ACR / blob handoff |

## Included

- git
- .NET 10 SDK (`mcr.microsoft.com/dotnet/sdk:10.0`)
- Node.js 22 + npm (official `nodejs.org` binary tarball; not NodeSource apt)
- Angular CLI (`@angular/cli@19`)
- Python 3 + pip
- Azure CLI (`az`)
- zip / unzip / jq / curl

## Build / save / load

From repo root:

```bash
./poc/scripts/build-ia-builder.sh
./poc/scripts/save-ia-builder.sh
# → writes agenthub-ia-builder-<date>.tar.gz under poc/docker-images/ (gitignored)
#    and a copy under /opt/cursor/artifacts/ when that path exists
```

Load on another machine:

```bash
gunzip -c agenthub-ia-builder-YYYYMMDD.tar.gz | docker load
docker tag agenthub-ia-builder:local youracr.azurecr.io/agenthub-ia-builder:YYYYMMDD
az acr login --name youracr
docker push youracr.azurecr.io/agenthub-ia-builder:YYYYMMDD
```

Or upload the `.tar.gz` to a storage blob and `docker load` from a downloaded copy.

## Point IA at an ACR image later

Set env (when you wire pull-from-ACR):

```bash
export IA_BUILDER_IMAGE=youracr.azurecr.io/agenthub-ia-builder:YYYYMMDD
```

Until that env is wired, `BuilderShell` uses `agenthub-ia-builder:local`.

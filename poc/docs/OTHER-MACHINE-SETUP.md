# Other machine setup — from GitHub zip only

**You start with:** a zip of this repo (or a clone). Nothing else installed yet.  
**Goal:** (1) Local Docker path green, then (2) Azure DevOps + Azure App Service integration.  
**Branch to use:** `main-azure` (includes IA/TA, dual-mode Azure providers, key-local / MI-cloud auth, builder image, QA runbook).  
**Tip when this guide was written:** re-check `git rev-parse HEAD` (or the commit in the zip folder name) after you unpack.

Related docs:

| Doc | When |
|---|---|
| This file | First boot on a clean machine |
| [`azure-clickops-integration.md`](azure-clickops-integration.md) | Portal clicks for Azure resources + auth modes |
| [`azure-devops-provision-brief.md`](azure-devops-provision-brief.md) | Pass to DevOps: `abc-sg` vs `abc-agent-sg`, ACR placement, RBAC |
| [`qa/QA_Runbook-azure-dual-mode.md`](qa/QA_Runbook-azure-dual-mode.md) | Gates A–E after wiring Azure |
| [`../agenthub-api/docker/ia-builder/EXPORT-NOTE.md`](../agenthub-api/docker/ia-builder/EXPORT-NOTE.md) | Optional builder image → ACR |

### Auth rule (read once)

| Where AgentHub runs | Storage | Git (`IA_GIT_AUTH`) | Deploy (`IA_DEPLOY_AUTH`) |
|---|---|---|---|
| **This laptop** (Docker / `dotnet run`) pointing at Azure | Connection string / access key | `pat` | `publishProfile` |
| **Cloud** (AgentHub on App Service / ACA) | `ServiceUri` + managed identity | `managedIdentity` | `managedIdentity` |

On a clean machine you only need the **laptop** row. Switch to managed identity when you later host AgentHub itself in Azure.

---

## Part 0 — Unpack the zip

```bash
# Example: you downloaded agent-sdlc-main-azure.zip from GitHub
unzip agent-sdlc-main-azure.zip -d ~/src
cd ~/src/agent-sdlc-*    # or whatever folder the zip created

# Prefer downloading the main-azure branch zip from GitHub:
#   Code → Switch to branch main-azure → Download ZIP
```

Confirm you see:

```text
poc/
  agenthub-api/
  buildgenix-api/
  buildgenix-web/
  scripts/
  docs/
```

---

## Part 1 — Install prerequisites (clean machine)

### 1.1 Required

| Tool | Why | Install (typical) |
|---|---|---|
| **Docker Desktop** (or Docker Engine + Compose) | Azurite, Gitea, app Compose deploys | https://docs.docker.com/get-docker/ |
| **.NET 10 SDK** | AgentHub + BuildGenix + generated apps | https://dotnet.microsoft.com/download |
| **Git** | Clone/push inside IA (and useful locally) | OS package / Xcode CLT / Git for Windows |
| **curl** + **python3** | Verify scripts | Usually preinstalled on macOS/Linux |

Check:

```bash
docker version
dotnet --list-sdks    # need a 10.x line
git --version
python3 --version
curl --version
```

### 1.2 Optional but useful

| Tool | Why |
|---|---|
| **Node 22 + npm** | BuildGenix studio UI (`ng serve` on `:43182`) |
| **Azure CLI (`az`)** | Login + later ACR/App Service helpers |
| **Ollama** | Real local LLM (Fixture works for first green path) |

```bash
# Node (if you want the studio UI)
node -v   # aim for 22.x

# Azure CLI (for Part 3)
az version
```

### 1.3 Ports that must be free

`43180` (AgentHub), `43181` (BuildGenix), `43182` (studio, optional), `43185` (Gitea), `10000–10002` (Azurite).

---

## Part 2 — Local with Docker (do this first)

### 2.1 Seed Azurite + Gitea

```bash
cd poc
chmod +x scripts/*.sh
./scripts/seed-gitea.sh
```

Expect: Gitea at `http://127.0.0.1:43185`, user `poc` / `pocpocpoc`, repo `buildgenix/app-monorepo`.

### 2.2 (Optional) Build the IA/TA toolchain image

Host SDK fallback works without this. For full parity:

```bash
./scripts/build-ia-builder.sh
```

### 2.3 Start AgentHub (terminal A) — simplest (Fixture LLM)

Development defaults already use **Gitea + Compose + File/Memory/Fixture**. Easiest first green:

```bash
cd poc/agenthub-api
ASPNETCORE_URLS=http://127.0.0.1:43180 \
ASPNETCORE_ENVIRONMENT=Development \
dotnet run --project src/AgentHub.WebApi --no-launch-profile
```

Check: `curl -s http://127.0.0.1:43180/` → JSON with `"status":"ok"`.

### 2.4 Start BuildGenix (terminal B)

```bash
cd poc/buildgenix-api
ASPNETCORE_URLS=http://127.0.0.1:43181 \
ASPNETCORE_ENVIRONMENT=Development \
PocPipeline__AgentHubBaseUrl=http://127.0.0.1:43180 \
dotnet run --project src/BuildGenix.WebApi --no-launch-profile
```

Check: `curl -s http://127.0.0.1:43181/` → ok.

### 2.5 Verify IA + TA end-to-end (terminal C)

```bash
cd poc
./scripts/verify-ta.sh
```

**Pass:** exit 0, trail through `TA_MERGE`, Gitea PR merged, `/api/add?a=2&b=3` → `5`.

Contract tests (no Azure needed):

```bash
cd poc/agenthub-api
dotnet test tests/AgentHub.Agents.Pipeline.Tests
```

Expect PAT Basic + managed-identity Bearer contract tests both green.

### 2.6 Optional studio UI

```bash
cd poc/buildgenix-web
npm ci
npx ng serve --host 127.0.0.1 --port 43182
# open http://127.0.0.1:43182 — Calculator add → approve gates
```

### 2.7 Local baseline = Gate A

When `./scripts/verify-ta.sh` is green, local Docker is done. Do **not** start Azure until this passes.

---

## Part 3 — Azure DevOps + Azure code integration

Follow portal steps in detail: [`azure-clickops-integration.md`](azure-clickops-integration.md). Summary for a **laptop pointing at Azure** (keys / PAT / publish profile):

### 3.1 Create resources (ClickOps once)

1. **Resource group** (e.g. `rg-buildgenix-poc`)
2. **Storage account** + container `agenthub-artifacts` (optional for first AzDO/App Service proof; recommended next)
3. **Azure DevOps** project + git repo with an initialized `main` (README commit)
4. **PAT** — Code (Read & Write) + Pull Request (Read & Write) — copy once
5. **App Service plan + Web App** (Linux) for the generated backend
6. Download **publish profile**; enable **SCM Basic Auth Publishing Credentials** if zipdeploy returns 401

### 3.2 Export env on the other machine (local → Azure via keys)

```bash
# Git → Azure DevOps (PAT / key path)
export IA_GIT_PROVIDER=azuredevops
export IA_GIT_AUTH=pat
export IA_GIT_ORGANIZATION=<your-org>          # e.g. wcbbc
export IA_GIT_PROJECT=<your-project>          # e.g. AI-Operations
export IA_GIT_REPO=<your-repo>                # e.g. inno-tech-aisdlc-apps or app-monorepo
export IA_GIT_TOKEN='<pat>'

# Deploy → App Service (publish profile / key path)
export IA_DEPLOY_PROVIDER=appservice
export IA_DEPLOY_AUTH=publishProfile
export IA_DEPLOY_APP_NAME=<webapp-name>
export IA_DEPLOY_PUBLIC_URL=https://<webapp-name>.azurewebsites.net
export IA_DEPLOY_USER='<publish-profile-user>'      # often app\$app
export IA_DEPLOY_PASSWORD='<publish-profile-password>'

# Optional: real Azure Storage via access key (not MI yet)
# export BlobStorage__Provider=Azure
# export BlobStorage__ConnectionString='DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net'
# export BlobStorage__ContainerName=agenthub-artifacts
# export TableStorage__Provider=Azure
# export TableStorage__ConnectionString="$BlobStorage__ConnectionString"
```

**Important:** Restart **AgentHub** (terminal A) in a shell that has these exports. BuildGenix can stay as in Part 2.

### 3.3 Preflight (Gate C)

```bash
cd poc
./scripts/verify-azure-preflight.sh
```

Fixes common failures: bad PAT scopes, empty AzDO repo, SCM basic auth off.

### 3.4 Azure e2e (Gate D)

```bash
cd poc
./scripts/verify-azure-e2e.sh
```

**Pass:** Calculator Completes; AzDO PR opened then completed; `deployedUrl` is `https://…azurewebsites.net`; `/health` + add→5; `merge.json` provider `azuredevops`.

### 3.5 Prove dual-mode still works (Gate E)

```bash
unset IA_GIT_PROVIDER IA_GIT_AUTH IA_GIT_TOKEN IA_DEPLOY_PROVIDER IA_DEPLOY_AUTH IA_DEPLOY_USER IA_DEPLOY_PASSWORD
export IA_GIT_PROVIDER=gitea
export IA_DEPLOY_PROVIDER=compose
# Restart AgentHub with Development defaults (Part 2.3)
cd poc && ./scripts/verify-ta.sh
```

### 3.6 Later — when AgentHub itself is hosted in Azure

Do **not** put PATs or publish-profile passwords in App Settings. Instead:

```bash
export IA_GIT_PROVIDER=azuredevops
export IA_GIT_AUTH=managedIdentity
export IA_GIT_ORGANIZATION=<org>
export IA_GIT_PROJECT=<project>
export IA_GIT_REPO=<repo>

export IA_DEPLOY_PROVIDER=appservice
export IA_DEPLOY_AUTH=managedIdentity
export IA_DEPLOY_APP_NAME=<webapp>
export IA_DEPLOY_PUBLIC_URL=https://<webapp>.azurewebsites.net

export BlobStorage__Provider=Azure
export BlobStorage__ServiceUri=https://<account>.blob.core.windows.net
export TableStorage__Provider=Azure
export TableStorage__ServiceUri=https://<account>.table.core.windows.net
```

Then assign roles (person / ClickOps): Storage Blob/Table Data Contributor; AzDO org membership for the identity; Website Contributor on the target DEV app. Details: [`azure-clickops-integration.md`](azure-clickops-integration.md).

---

## Part 4 — Suggested order (do not skip)

```text
1. Install Docker + .NET 10 (+ Node if you want UI)
2. Unzip main-azure
3. seed-gitea.sh
4. Start AgentHub + BuildGenix (Fixture)
5. verify-ta.sh                    ← must be green
6. ClickOps Azure (storage → AzDO → App Service)
7. Export IA_GIT_* (Auth=pat) / IA_DEPLOY_* (Auth=publishProfile)
8. Restart AgentHub with those env vars
9. verify-azure-preflight.sh
10. verify-azure-e2e.sh
11. Flip back to gitea/compose; verify-ta.sh again
12. (Later) Host AgentHub in Azure → flip Auth to managedIdentity + ServiceUri
```

Full checklist tables: [`qa/QA_Runbook-azure-dual-mode.md`](qa/QA_Runbook-azure-dual-mode.md).

---

## Part 5 — Troubleshooting (clean machine)

| Problem | Fix |
|---|---|
| `dotnet` not found | Install .NET 10 SDK; open a new terminal |
| `docker: permission denied` | Add user to `docker` group / start Docker Desktop |
| Gitea seed hangs | `docker ps` — start engine; ports `43185` / `10000` free |
| verify-ta fails on PR | Re-run `seed-gitea.sh`; ensure AgentHub still running |
| AzDO 401 | New PAT with Code + PR write; correct org/project/repo; `IA_GIT_AUTH=pat` |
| zipdeploy 401 | Portal → Web App → Configuration → enable SCM Basic Auth; new publish profile; `IA_DEPLOY_AUTH=publishProfile` |
| Empty MI token locally | Expected without App Service identity — use PAT/publishProfile on the laptop |
| App Service health timeout | Portal Log stream; runtime must match published .NET app |
| Only have `main` zip, not `main-azure` | On GitHub switch branch to `main-azure` → Download ZIP again |

---

## Part 6 — What you do **not** need on day one

- ACR (builder tar is optional; host SDK works)
- Real Ollama/Gateway (Fixture is enough for first local + Azure plumbing proof)
- Managed identity (only when AgentHub is hosted in Azure)
- Service Bus emulator (POC uses InProcessOnly)
- Merging `main-azure` into `main` (use `main-azure` until you choose to merge)

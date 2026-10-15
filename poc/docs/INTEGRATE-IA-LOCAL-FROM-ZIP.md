# Get IA running from GitHub zip (local Docker) — then integrate into your agenthub

**You have:** a zip of branch **`main-azure`** from GitHub. Nothing Azure required for this guide.  
**Goal:** (1) Run this reference POC green locally with Docker stand-ins, (2) copy the IA pieces into your similar project (same agent structure / AgentHub background workers).  
**Tip to prefer:** latest `main-azure` (includes IA stages, Gitea + Compose, dual-mode Azure code you can ignore for now).

Longer Azure path later: [`OTHER-MACHINE-SETUP.md`](OTHER-MACHINE-SETUP.md).

---

## Part A — Run the reference POC locally (do this first)

Treat `poc/` as a working lab. Do not merge into your production tree until Part A is green.

### A0. Download and unpack

1. GitHub → repo → branch **`main-azure`** → **Code** → **Download ZIP**.
2. Unzip and enter the folder:

```bash
unzip agent-sdlc-main-azure.zip -d ~/src
cd ~/src/agent-sdlc-*   # adjust to the folder name the zip created
```

3. Confirm layout:

```text
poc/
  agenthub-api/     ← AgentHub + Pipeline agent (IA/TA live here)
  buildgenix-api/   ← orchestrator that drives stages + gates
  buildgenix-web/   ← optional studio UI
  scripts/          ← seed + verify
  docs/
```

> Root `agenthub-api` / `buildgenix-api` outside `poc/` are older trees — **use `poc/` only** for this reference.

### A1. Install tools

| Required | Why |
|---|---|
| Docker Desktop (or Engine + Compose) | Gitea, Azurite, Compose deploy of generated apps |
| .NET 10 SDK | AgentHub, BuildGenix, generated backends |
| Git, curl, python3 | IA push + verify scripts |

| Optional | Why |
|---|---|
| Node 22 | Studio UI only |
| Ollama | Real LLM; **Fixture** is enough for first green |

```bash
docker version
dotnet --list-sdks    # need 10.x
git --version
```

Free ports: `43180` (AgentHub), `43181` (BuildGenix), `43185` (Gitea), `10000–10002` (Azurite). Optional: `43182` (UI).

### A2. Seed local stand-ins (Gitea + Azurite)

```bash
cd poc
chmod +x scripts/*.sh
./scripts/seed-gitea.sh
```

Expect: Gitea at `http://127.0.0.1:43185`, user `poc` / `pocpocpoc`, repo `buildgenix/app-monorepo`.

(Optional builder image — host SDK works without it:)

```bash
./scripts/build-ia-builder.sh
```

### A3. Start AgentHub (terminal 1)

Development defaults already wire **Gitea + Compose + File blobs + Memory tables + Fixture LLM** — that is the local IA path.

```bash
cd poc/agenthub-api
ASPNETCORE_URLS=http://127.0.0.1:43180 \
ASPNETCORE_ENVIRONMENT=Development \
dotnet run --project src/AgentHub.WebApi --no-launch-profile
```

Check: `curl -s http://127.0.0.1:43180/` → `"status":"ok"`.

### A4. Start BuildGenix (terminal 2)

```bash
cd poc/buildgenix-api
ASPNETCORE_URLS=http://127.0.0.1:43181 \
ASPNETCORE_ENVIRONMENT=Development \
PocPipeline__AgentHubBaseUrl=http://127.0.0.1:43180 \
dotnet run --project src/BuildGenix.WebApi --no-launch-profile
```

Check: `curl -s http://127.0.0.1:43181/` → ok.

### A5. Prove IA only (terminal 3)

```bash
cd poc
./scripts/verify-ia.sh
```

**Pass:** exit 0; trail through `IA_PUSH_DEPLOY`; Gitea has a PR; Compose app `/health` ok; mono folder under `/tmp/bgx-poc/...` with `frontend/` + `backend/`.

Then full IA+TA (optional but recommended):

```bash
./scripts/verify-ta.sh
```

**Pass:** through `TA_MERGE`; PR merged; `/api/add?a=2&b=3` → `5`.

Contract tests (no Docker needed beyond build):

```bash
cd poc/agenthub-api
dotnet test tests/AgentHub.Agents.Pipeline.Tests
```

### A6. Optional studio UI

```bash
cd poc/buildgenix-web
npm ci
npx ng serve --host 127.0.0.1 --port 43182
# open http://127.0.0.1:43182 — Calculator → approve IA_PLAN / IA_REVIEW / deploy gate
```

**Stop here until Part A is green.** That is your reference behavior for integration.

---

## Part B — What “Implementation Agent” is in this zip

IA is **not** a separate process. It is stages inside the existing **Pipeline** worker already registered like your Healthy / background workers.

| Stage | What it does locally |
|---|---|
| `IA_PLAN` | Plan from PRD + system design → `implementation-plan.md` (gate) |
| `IA_CODEGEN` | Write mono folder `project-…-work-item-…/{frontend,backend}`; TDD via builder or host SDK |
| `IA_REVIEW` | Review artifact (gate) |
| `IA_PUSH_DEPLOY` | Push branch + open PR (**Gitea**), deploy via **Docker Compose**, then gate |

Seams (swap later for Azure without rewriting the agent):

| Concern | Local default | Config |
|---|---|---|
| Git / PR | Gitea `:43185` | `Ia:Git:Provider=gitea` |
| Deploy | Compose | `Ia:Deploy:Provider=compose` |
| Builder | Host SDK or `agenthub-ia-builder` image | `BuilderShell` / `IA_BUILDER_IMAGE` |
| LLM | Fixture | `Llm:Provider=Fixture` |

Design: [`ia-local-docker-design.md`](ia-local-docker-design.md).

---

## Part C — Integrate IA into your similar project

Assumption: your tree already has AgentHub-shaped host, table/blob stores, LLM client, `BackgroundService` workers, and an orchestrator that dispatches stages (like BuildGenix). You are **adding IA stages**, not reinventing the host.

### C1. Copy these reference areas (from `poc/agenthub-api`)

| Copy from | Into your agenthub | Why |
|---|---|---|
| `src/AgentHub.Agents.Pipeline/` (especially `Ia/`, `Prompts/ia-*.txt`, `Prompts/persona-*.txt`, `Templates/`) | Your Pipeline / agent project | Handler stages + Git/Deploy seams + codegen templates |
| `src/AgentHub.Hosting/AgentWorkersServiceCollectionExtensions.cs` pieces for `IaGitOptions` / `IaDeployOptions` + `PipelineAgentHandler` registration | Your DI / worker registration | Same pattern as existing workers |
| `src/AgentHub.WebApi/appsettings.Development.json` → `Ia:Git` / `Ia:Deploy` | Your Development settings | Defaults stay gitea + compose |
| `docker/ia-builder/` | Optional under your repo | Same builder image later |
| `poc/scripts/seed-gitea.sh`, `verify-ia.sh` | Your scripts (adapt URLs/ports) | Local prove path |

Do **not** copy Azure-only App Settings yet. Local first.

### C2. Wire into your existing host (checklist)

1. **Register** Pipeline (or your renamed IA) worker the same way you register existing agents (`AddHostedService` / dispatcher).
2. **Bind** `Ia:Git` and `Ia:Deploy` options; keep Development = `gitea` + `compose`.
3. **Orchestrator:** after PRD + System Design approved, dispatch `IA_PLAN` → (gate) → `IA_CODEGEN` → `IA_REVIEW` → (gate) → `IA_PUSH_DEPLOY` → (gate). Map those stage ids to whatever your WorkItemEvents / UI strip uses.
4. **Artifacts:** IA expects PRD + system-design blobs already present (your existing SD path). Outputs: `implementation-plan.md`, codegen folder, `review.json`, `pull-request.json`, deploy URL.
5. **Gitea (or your Git stand-in):** run seed script or point `Ia:Git` at an existing local Git server with an empty-initialized `main`.
6. **Workspace root:** mono folder lands under the configured workspace (POC uses `/tmp/bgx-poc/...`). Align with your sandbox path policy.

### C3. What you can leave for later

| Later | Why |
|---|---|
| `IA_GIT_PROVIDER=azuredevops` + PAT | After local PR path is green |
| `IA_DEPLOY_PROVIDER=appservice` | After Compose deploy is green |
| `Auth=managedIdentity` | Only when AgentHub is hosted in Azure |
| TA stages (`TA_*`) | After IA Push & Deploy is trusted |
| Real LLM (Ollama/Gateway) | Fixture proves plumbing first |

### C4. Prove integration on the new machine

Same bar as Part A, but against **your** ports and scripts:

1. Seed Git stand-in + start **your** AgentHub with IA worker enabled.
2. Start **your** orchestrator pointed at that AgentHub.
3. Run a Calculator-equivalent work item through `IA_PUSH_DEPLOY`.
4. Confirm: mono folder on disk, PR on stand-in Git, Compose (or your local host) URL healthy.

---

## Part D — Quick troubleshooting (local)

| Symptom | Fix |
|---|---|
| `dotnet` / no 10.x SDK | Install .NET 10 SDK; new terminal |
| Gitea seed hangs | Docker running; ports `43185` / `10000` free |
| AgentHub starts but no IA | `Workers:Pipeline:Enabled=true` (or your flag); Development `Ia:Git:Provider=gitea` |
| verify-ia fails on PR | Re-run `seed-gitea.sh`; AgentHub still up |
| Deploy health fail | `docker ps`; Compose file in mono folder; wait for publish |
| Wrong tree | You ran root `agenthub-api` instead of `poc/agenthub-api` |

---

## One-page runbook (reference POC only)

```text
1. Unzip main-azure → cd into repo
2. cd poc && ./scripts/seed-gitea.sh
3. Terminal A: AgentHub :43180 (Development)
4. Terminal B: BuildGenix :43181 (PocPipeline__AgentHubBaseUrl=http://127.0.0.1:43180)
5. Terminal C: ./scripts/verify-ia.sh     ← IA green
6. Optional: ./scripts/verify-ta.sh      ← IA+TA green
7. Then copy Pipeline/Ia + prompts + worker registration into your agenthub (Part C)
```

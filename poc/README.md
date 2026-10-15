# Pre-phase-0 pipeline POC

One Healthy-shaped **Pipeline** agent walks lifecycle stages. BuildGenix drives the happy path. Root `agenthub-api` / `buildgenix-api` are untouched — everything lives under `poc/`.

## Implementation Agent (IA) + Testing Agent (TA) — local Docker slice

IA owns code generation through DEV deploy (**option B**). TA then hits the live URL, reports, and merges the PR.

| UI state | Stage id | What happens |
|---|---|---|
| Implementation Plan | `IA_PLAN` | Conversational plan from PRD + system design → `implementation-plan.md` (human gate) |
| Code Generation | `IA_CODEGEN` | Mono-repo folder `project-{projectId}-work-item-{workItemId}/{frontend,backend}`; TDD in builder/host |
| Code Review | `IA_REVIEW` | Conversational review → `review.json` / `code-review.md` (human gate) |
| Push & Deploy | `IA_PUSH_DEPLOY` | Push branch, open PR on **Gitea** (AzDo stand-in), Compose deploy, then DEV accept gate |
| Generate Tests | `TA_GENERATE` | Write `tests/e2e` live-URL automation under the mono folder |
| Verify Deployed | `TA_VERIFY` | Run smoke against the deployed URL → `ta-results.json` |
| Test Report | `TA_REPORT` | Conversational QA report (human gate) |
| Merge PR | `TA_MERGE` | Merge Gitea PR after green |

Design: [`docs/ia-local-docker-design.md`](docs/ia-local-docker-design.md), [`docs/ta-local-docker-design.md`](docs/ta-local-docker-design.md).

**Independent personas:** each stage uses its own `Prompts/persona-*.txt` and a filtered evidence pack (Reviewer does not see implementer narrative; QA does not inherit “review passed”). See [`docs/persona-independence.md`](docs/persona-independence.md).

**Verified 2026-10-15:** Docker + Gitea + Compose + studio UI e2e + six-persona sign-off → [`docs/ia-ta-six-persona-verification-2026-10-15.md`](docs/ia-ta-six-persona-verification-2026-10-15.md).

**Production SD/IA/TA work prompts:** [`docs/prompts/system-design-system.txt`](../docs/prompts/system-design-system.txt), [`implementation-agent-system.txt`](../docs/prompts/implementation-agent-system.txt), [`testing-agent-system.txt`](../docs/prompts/testing-agent-system.txt); alignment [`sd-ia-ta-prompt-alignment-2026-10-15.md`](../docs/prompts/sd-ia-ta-prompt-alignment-2026-10-15.md).

**Start here on a clean machine (zip only):** [`docs/OTHER-MACHINE-SETUP.md`](docs/OTHER-MACHINE-SETUP.md) — install → local Docker → Azure/AzDO.  
**Integrate IA into an existing agenthub (local first):** [`docs/INTEGRATE-IA-LOCAL-FROM-ZIP.md`](docs/INTEGRATE-IA-LOCAL-FROM-ZIP.md).

**Hand to DevOps (provision `abc-agent-sg` / ACR / builder MI):** [`docs/azure-devops-provision-brief.md`](docs/azure-devops-provision-brief.md).

**Azure QA Runbook (other machine + real subscription):** [`docs/qa/QA_Runbook-azure-dual-mode.md`](docs/qa/QA_Runbook-azure-dual-mode.md) — Gates A–E (`verify-ta`, contract tests, `verify-azure-preflight`, `verify-azure-e2e`, local dual-mode).

**Azure dual-mode (same code):** defaults stay Gitea + Compose; flip via `IA_GIT_PROVIDER` / `IA_DEPLOY_PROVIDER` (or `Ia:*` config). **Auth:** local laptop → Azure uses keys (`IA_GIT_AUTH=pat`, `IA_DEPLOY_AUTH=publishProfile`, storage ConnectionString); AgentHub hosted in Azure uses managed identity (`Auth=managedIdentity`, storage ServiceUri). ClickOps: [`docs/azure-clickops-integration.md`](docs/azure-clickops-integration.md). Design: [`docs/azure-dual-mode-design.md`](docs/azure-dual-mode-design.md). Example config: `agenthub-api/src/AgentHub.WebApi/appsettings.Azure.example.json`. Older stepwise notes: [`docs/azure-integration-next-steps.md`](docs/azure-integration-next-steps.md).

```bash
# From poc/ — Azurite + Gitea, then APIs, then IA+TA verify
./scripts/seed-gitea.sh
# (start AgentHub :43180 and BuildGenix :43181 as below)
./scripts/verify-ia.sh    # through Push & Deploy (still valid)
./scripts/verify-ta.sh    # full path including TA merge
./scripts/verify-interactive.sh  # human gates through TA_REPORT then merge
```

Optional IA/TA toolchain image (otherwise host SDK is used). Includes .NET 10, Node 22, Angular CLI, Python 3, Azure CLI, git:

```bash
./poc/scripts/build-ia-builder.sh
./poc/scripts/save-ia-builder.sh   # tar.gz for manual ACR / blob upload (gitignored)
# Point at ACR later: export IA_BUILDER_IMAGE=youracr.azurecr.io/agenthub-ia-builder:YYYYMMDD
```

See `poc/agenthub-api/docker/ia-builder/TOOLCHAIN.md`.

## Was this verified for real?

**Yes.** On 2026-10-08 the three examples were run end-to-end against:

| Resource | Actual runtime |
|---|---|
| Azure Blob + Table | Docker Azurite (`agenthub-azurite`, ports `10000–10002`) |
| LLM (pipeline + Hello World thought) | Ollama `llama3.2:1b` |
| Deployed apps | Docker Compose per work item |

| Example | Result | Smoke |
|---|---|---|
| Hello World (`fe-be-llm`) | `Completed` / `DONE` | `/health` ok; `/api/thought` → `source=ollama` |
| Calculator add (`fe-be`) | `Completed` / `DONE` | `/api/add?a=2&b=3` → `{"result":5}` |
| Calculator subtract (Existing project) | `Completed` / `DONE` | add still 5; `/api/subtract?a=5&b=2` → `{"result":3}` |

Checked-in proof (from that Azurite run):

- [`blob-storage-snapshot/`](blob-storage-snapshot/) — live dump of the `agenthub-artifacts` container + AuditLog.json + verify JSON
- [`docs/six-persona-happy-path-verification.md`](docs/six-persona-happy-path-verification.md) — SA / PE / QA / DBA / DevOps / PM
- [`docs/what-was-done.md`](docs/what-was-done.md)

---

## Layout

| Path | Role |
|---|---|
| `poc/agenthub-api` | Agent host + `AgentHub.Agents.Pipeline` (handler, templates, LocalCompose deploy) |
| `poc/buildgenix-api` | Orchestrator: sync `/run` + interactive `/start` with human gates |
| `poc/buildgenix-web` | Angular chat UI: stage strip, approvals, Documents + Logs tabs |
| `poc/scripts/verify-three.sh` | Runs all three happy paths and smokes the URLs (auto-approve) |
| `poc/scripts/verify-ia.sh` | IA path: plan → codegen → review → push/PR/deploy + mono-folder checks |
| `poc/scripts/seed-gitea.sh` | Start Azurite + Gitea; create `poc` user and `buildgenix/app-monorepo` |
| `poc/scripts/verify-interactive.sh` | Interactive: PRD / SD / IA_PLAN / IA_REVIEW / Push&Deploy gates |
| `poc/scripts/verify-steering.sh` | Chat steering: PAUSE / STOP / CONTINUE / ABORT |
| `poc/blob-storage-snapshot/` | Snapshot of Azurite blobs from the verified run |

Design notes: agent writes AH tables/blobs; buildgenix owns WorkItems / Approvals / WorkItemEvents / AuditLog. Sync `POST /api/poc/pipeline/run` auto-approves gates (scripts). Interactive `POST /api/poc/pipeline/start` pauses at PRD, System Design, Implementation Plan, Code Review, and post Push & Deploy.

**IA codegen** is **request-driven** at runtime (hello / hello+LLM / todo / calculator / generic) — not a fixed calculator template copy. See [`docs/ia-request-driven-codegen.md`](docs/ia-request-driven-codegen.md). Unit/HTTP tests run inside `IA_CODEGEN`. After review, **`graphify extract . --code-only`** writes `code-graph.json` + `code-state.json`. **Push & Deploy** opens a feature-branch PR (not `main`) then Compose-deploys.

**Intra-stage tasks (not sub-agents):** each stage runs ordered tasks (`TaskStarted` / `TaskCompleted`) into ActivityLog + AgentResult.Steps → BuildGenix `WorkItemEvents`.

### Chat UI (interactive)

```bash
# With AgentHub :43180 and BuildGenix :43181 already running:
cd poc/buildgenix-web
npm install
npm start
# open http://127.0.0.1:43182
```

Or script the same gates without the browser:

```bash
./scripts/verify-interactive.sh
./scripts/verify-steering.sh   # PAUSE / STOP / CONTINUE / ABORT
```

Chat steering (while a work item is active): type `PAUSE`, `STOP`, `ABORT`, or `CONTINUE` in the studio, or call `POST /api/poc/pipeline/{workItemId}/steer` with `{"command":"PAUSE"|"STOP"|"ABORT"|"CONTINUE"}`.

---

## Step-by-step: run on another machine (Azurite + Ollama + Docker)

Use this path for the **actual Azure emulator** verification (same mode that was signed off).

### 0. Prerequisites

Install and confirm:

1. **Git**, **Docker** (daemon running), **.NET 10 SDK**
2. **Ollama**
3. Ports free: `43180`, `43181`, `10000–10002`, `11434` (plus ephemeral ports for Compose apps)

```bash
dotnet --version          # 10.x
docker version
ollama --version
```

Clone the repo and enter the POC:

```bash
git clone <this-repo-url>
cd <repo-root>
git checkout main
cd poc
```

### 1. Start Azurite (Docker)

From `poc/agenthub-api` start **only** Azurite (Service Bus emulator is not required — POC uses InProcessOnly):

```bash
cd poc/agenthub-api
docker compose up -d azurite
docker ps --filter name=agenthub-azurite
```

You should see `agenthub-azurite` with `0.0.0.0:10000-10002->10000-10002`.

Optional smoke:

```bash
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:10000/devstoreaccount1
# expect 400 (Azurite answered; bare path has no API)
```

### 2. Start Ollama and pull a model

Bind on all interfaces so **Compose containers** can reach the host via `host.docker.internal`:

```bash
# Linux / cloud VM
export OLLAMA_HOST=0.0.0.0:11434
ollama serve
```

In another terminal:

```bash
ollama pull llama3.2:1b
curl -s http://127.0.0.1:11434/api/tags
```

### 3. Export shared env

Run this in every shell that starts an API (or put it in a small `env.sh` you `source`):

```bash
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

export AZURITE_CS='DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;'

export OLLAMA_BASE_URL=http://127.0.0.1:11434
export OLLAMA_MODEL=llama3.2:1b
# Do NOT set BGX_SKIP_TABLE_INIT for the Azurite path
unset BGX_SKIP_TABLE_INIT
```

### 4. Start AgentHub (terminal A)

```bash
cd poc/agenthub-api

ASPNETCORE_URLS=http://127.0.0.1:43180 \
ASPNETCORE_ENVIRONMENT=Development \
TableStorage__Provider=Azure \
TableStorage__ConnectionString="$AZURITE_CS" \
BlobStorage__Provider=Azure \
BlobStorage__ConnectionString="$AZURITE_CS" \
BlobStorage__ContainerName=agenthub-artifacts \
Llm__Provider=Ollama \
Llm__FallbackProvider=Gateway \
Llm__Ollama__BaseUrl=http://127.0.0.1:11434 \
Llm__Ollama__DefaultModel=llama3.2:1b \
Llm__Gateway__ChatEndpoint="${Llm__Gateway__ChatEndpoint:-https://cc-sb-ts-aigw-apim.azure-api.net/ai/chat/completions}" \
Llm__Gateway__DefaultModelAlias=openai \
Llm__Gateway__SubscriptionKey="${AIGW_SUBSCRIPTION_KEY:-}" \
OLLAMA_BASE_URL=http://127.0.0.1:11434 \
OLLAMA_MODEL=llama3.2:1b \
Workers__Pipeline__Enabled=true \
Workers__Pipeline__InProcessOnly=true \
dotnet run --project src/AgentHub.WebApi --no-launch-profile
```

> With `FallbackProvider=Gateway`, both clients are registered. Stage calls use Ollama unless you pass `gateway:…` ModelIds or Ollama fails. Set `AIGW_SUBSCRIPTION_KEY` for real APIM (or point `ChatEndpoint` at `poc/scripts/gateway-stub.py` for a local smoke).


Wait until logs show table storage ready, e.g. `AgentHub table storage ready`.

Check:

```bash
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:43180/
# expect 200
# /health may be 503 if Service Bus emulator is not running — that is OK for this POC
```

### 5. Start BuildGenix (terminal B)

```bash
cd poc/buildgenix-api

ASPNETCORE_URLS=http://127.0.0.1:43181 \
ASPNETCORE_ENVIRONMENT=Development \
TableStorage__ConnectionString="$AZURITE_CS" \
PocPipeline__UseInMemoryTables=false \
PocPipeline__AgentHubBaseUrl=http://127.0.0.1:43180 \
PocPipeline__AuditLogDirectory=/tmp/bgx-poc-audit-azurite \
PocPipeline__ModelId= \
dotnet run --project src/BuildGenix.WebApi --no-launch-profile
```

Wait for `BuildGenix table storage ready. Ensured 10/10 catalog tables.`

```bash
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:43181/
# expect 200
```

### 6. Run all three examples (terminal C)

```bash
cd poc
export BGX_URL=http://127.0.0.1:43181
export OUT_DIR=/tmp/bgx-poc-verify
./scripts/verify-three.sh
```

The script:

1. Runs **Hello World** (`fe-be-llm`) → asserts `Completed` + `/health` + `/api/thought`
2. Runs **Calculator add** (`fe-be`) → asserts add `2+3=5`
3. Runs **Calculator subtract** on the **same project** (`Existing`) → asserts subtract `5-2=3` and add still works

Each run takes on the order of tens of seconds (Compose build + Ollama). First Docker build is slower.

### 7. Confirm smoke yourself

Replace URLs with the ones printed by the script / JSON under `$OUT_DIR`:

```bash
# Hello World
curl -s "$HELLO_URL/health"
curl -s "$HELLO_URL/api/thought"    # expect "source":"ollama"

# Calculator
curl -s "$ADD_URL/api/add?a=2&b=3"           # {"result":5}
curl -s "$SUB_URL/api/subtract?a=5&b=2"      # {"result":3}
```

### 8. Confirm data landed in Azurite (not in-memory)

Artifacts (blob):

```bash
# After a run, blobs exist under container agenthub-artifacts
# Example listing with Python (pip install azure-storage-blob):
python3 - <<'PY'
from azure.storage.blob import BlobServiceClient
import os
cs = os.environ["AZURITE_CS"]
c = BlobServiceClient.from_connection_string(cs)
cc = c.get_container_client("agenthub-artifacts")
for b in cc.list_blobs():
    print(b.name, b.size)
PY
```

Tables:

```bash
# pip install azure-data-tables
python3 - <<'PY'
from azure.data.tables import TableServiceClient
import os
cs = os.environ["AZURITE_CS"]
c = TableServiceClient.from_connection_string(cs)
for t in c.list_tables():
    n = len(list(c.get_table_client(t.name).list_entities()))
    print(f"{t.name}: {n}")
PY
```

Audit logs (orchestrator file mirror):

```bash
ls -la /tmp/bgx-poc-audit-azurite
```

### 9. Stop / clean up

```bash
# Stop APIs: Ctrl+C in terminals A/B
# Stop generated apps
docker ps --format '{{.Names}}' | grep -E 'api-1$' | xargs -r docker rm -f
# Stop Azurite (keeps volume unless -v)
cd poc/agenthub-api && docker compose stop azurite
# Optional wipe Azurite data
docker compose down -v
```

---

## Faster local path (no Azurite — fixture LLM)

For a quick plumbing check only (not the Manual QA “actual resources” bar):

```bash
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
export BGX_SKIP_TABLE_INIT=1

# Terminal 1
cd poc/agenthub-api
ASPNETCORE_URLS=http://127.0.0.1:43180 dotnet run --project src/AgentHub.WebApi

# Terminal 2
cd poc/buildgenix-api
ASPNETCORE_URLS=http://127.0.0.1:43181 dotnet run --project src/BuildGenix.WebApi

# Terminal 3
cd poc && ./scripts/verify-three.sh
```

Default Development config uses Memory tables + File blobs + Fixture LLM. Prefer the Azurite + Ollama path above for real verification.

---

## API cheatsheet

| Call | URL |
|---|---|
| Full pipeline run | `POST http://127.0.0.1:43181/api/poc/pipeline/run` |
| Single stage dispatch | `POST http://127.0.0.1:43180/api/pipeline/dispatch` |

Hello World body:

```json
{
  "userRequest": "Build a Hello World app: Angular FE + .NET 10 BE + an LLM giving a thought of the day",
  "stackProfile": "fe-be-llm",
  "projectMode": "New",
  "projectName": "Hello World"
}
```

Header: `X-User-Id: dummy-user` and `Content-Type: application/json`.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| Compose app `/api/thought` has `source=local` | Ollama must listen on `0.0.0.0:11434`; compose uses `host.docker.internal` + `extra_hosts: host-gateway` |
| AgentHub build CS0136 / old binary | Rebuild: `dotnet build poc/agenthub-api/src/AgentHub.WebApi` |
| Port already in use | Kill old `dotnet` / Compose containers; LocalComposeHost rewrites host ports, but API ports `43180/43181` are fixed |
| Tables empty / orchestrator ignores Azurite | Ensure `PocPipeline__UseInMemoryTables=false` and **unset** `BGX_SKIP_TABLE_INIT` |
| `/health` returns 503 | Service Bus emulator not running — expected; POC is InProcessOnly |
| Docker permission denied | `sudo usermod -aG docker $USER` or fix socket permissions |

---

## AI Gateway alongside Ollama

AgentHub supports **both** providers in one process via `Llm:FallbackProvider`.

| Setting | Meaning |
|---|---|
| `Llm__Provider=Ollama` | Primary (used for stage LLM calls by default) |
| `Llm__FallbackProvider=Gateway` | Registers Gateway alongside; used on primary failure or explicit route |
| ModelId `ollama:llama3.2:1b` | Force Ollama |
| ModelId `gateway:openai` | Force AI Gateway (alias after the prefix) |

### Enable alongside mode (with real AI Gateway)

```bash
export AIGW_SUBSCRIPTION_KEY='<from Key Vault — never commit>'

# AgentHub extras on top of the Azurite env from step 3–4:
Llm__Provider=Ollama \
Llm__FallbackProvider=Gateway \
Llm__Gateway__ChatEndpoint=https://cc-sb-ts-aigw-apim.azure-api.net/ai/chat/completions \
Llm__Gateway__DefaultModelAlias=openai \
Llm__Gateway__SubscriptionKey="$AIGW_SUBSCRIPTION_KEY" \
# ...plus the usual Table/Blob/Ollama vars
```

### Smoke both providers

```bash
# Optional local OpenAI-shaped stub when you do not have the APIM key yet:
python3 poc/scripts/gateway-stub.py   # listens on :43190
# point Llm__Gateway__ChatEndpoint=http://127.0.0.1:43190/ai/chat/completions
# and Llm__Gateway__SubscriptionKey=poc-local-stub-key

./poc/scripts/smoke-llm.sh
# POST /api/pipeline/llm-smoke  provider=primary|ollama|gateway
```

Expected: `composite` reports `ollama+gateway`, and each provider returns `ok:true`.

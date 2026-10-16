# Post–Agent Integration QA Runbook

**Audience:** Copilot / engineer on the **other machine** after integrating each agent (or the full Pipeline) from this reference repo.  
**Purpose:** Re-run **exactly** the verification performed on the reference machine (Azurite + Fixture LLM + Gitea + Compose) so every agent hand-off is proven: logs, blobs, tables, decisions, agent↔orchestrator calls, RouteBack, deploy smoke.  
**Do not invent shortcuts.** Prefer the automated harness; then complete the agent-by-agent deep checklist.  
**Companion docs:** [`OTHER-MACHINE-SETUP.md`](../OTHER-MACHINE-SETUP.md) · [`QA_Runbook-azure-dual-mode.md`](QA_Runbook-azure-dual-mode.md) · [`PRODUCTION-GATE-SIGNOFF-2026-10-16.md`](PRODUCTION-GATE-SIGNOFF-2026-10-16.md) · [`MANUAL-QA-GREENFIELD-SIGNOFF.md`](MANUAL-QA-GREENFIELD-SIGNOFF.md)

| Field | Value |
|---|---|
| **Reference branch** | `main-azure` (contains greenfield + TA Playwright/RouteBack + production gate) |
| **Reference tip proven** | `d629d18` — *fix(poc): production gate — Rewrite FE Playwright + Azurite QA* |
| **Reference totals** | **8 cases · 545 checks · 0 failed** · Pipeline.Tests **58/58** |
| **Evidence JSON** | [`evidence/production-gate-2026-10-16/PRODUCTION-GATE-RESULTS.json`](evidence/production-gate-2026-10-16/PRODUCTION-GATE-RESULTS.json) |
| **Harness** | `poc/scripts/manual-qa-production-gate.sh` (`ONLY_CASES=` for per-agent slices) |
| **LLM for local proof** | `Llm__Provider=Fixture` — **do not change Azure Gateway** to make Fixture work |
| **Storage for local proof** | Azurite (`BlobStorage__Provider=Azure` + `TableStorage__Provider=Azure`) — **do not assume File/Memory == Azure** |

---

## 0. How Copilot should use this document

1. Checkout `main-azure` at or after `d629d18`.  
2. Bring up infra + AgentHub + BuildGenix (§1–2).  
3. After integrating **one** agent (e.g. only PRD, or only TA), run the **agent slice** in §4 for that agent, then at least one end-to-end case that exercises it.  
4. After integrating **the full Pipeline**, run §3 automated matrix (all cases) + §5 deep AgentHub surface.  
5. Fill the evidence tables; attach harness JSON; refuse sign-off if any required check fails.  
6. For **live Azure** (AzDO + App Service), additionally run [`QA_Runbook-azure-dual-mode.md`](QA_Runbook-azure-dual-mode.md) Gates C–E — not covered as green in the reference session.

**Prompt you can paste to Copilot:**

> Follow `poc/docs/qa/POST-AGENT-INTEGRATION-RUNBOOK.md` exactly. Start services per §1–2 with Azurite + Fixture LLM. Run `bash poc/scripts/manual-qa-production-gate.sh` and require overallPass=true with 0 failed checks. Then complete §4 agent-by-agent deep inspect and §5 surfaces for every case. Do not skip Rewrite, MultiWI Subtract, Hello±LLM, Todo, or RouteBack. Do not change Azure LLM Gateway. Record SHA, WI ids, deployed URLs, and blob prefixes in the evidence section.

---

## 1. Prerequisites (reference machine)

| Tool | Why |
|---|---|
| Docker | Azurite, Gitea, per-WI compose deploys |
| .NET 10 SDK | AgentHub, BuildGenix, generated backends |
| Python 3 + `azure-storage-blob` (+ `azure-data-tables` for table inspect) | Gate script Azurite deep inspect |
| Node/npm | Playwright install inside TA suite (may soft-skip if browsers blocked; HTTP smoke is hard gate) |
| curl | Health / smoke |

```bash
docker version
dotnet --list-sdks    # need 10.x
python3 -c "from azure.storage.blob import BlobServiceClient; print('ok')"
git rev-parse HEAD    # must be >= d629d18 on main-azure
```

### Free ports (reference)

| Port | Service |
|---|---|
| `43180` | AgentHub.WebApi |
| `43181` | BuildGenix.WebApi |
| `43185` | Gitea |
| `10000–10002` | Azurite (blob/queue/table) |
| ephemeral | Per-WI compose published host ports |

---

## 2. Start stack (Azurite + Fixture — as reference)

### 2.1 Containers

`seed-gitea.sh` **starts** (or reuses) Gitea and typically expects Azurite already running (or start Azurite compose/service first). Reference container names: `agenthub-gitea`, `agenthub-azurite`.

```bash
# Azurite must be up on :10000–10002 before APIs with Provider=Azure
docker ps --format '{{.Names}} {{.Ports}}' | grep -E 'azurite|gitea' || true
# Seed Gitea org/repo/token used by IA (starts Gitea if needed)
cd poc && ./scripts/seed-gitea.sh
```

### 2.2 Azurite env file (example used on reference)

Create `/tmp/bgx-azurite.env` (or equivalent) and **source it in both API processes**:

```bash
export ASPNETCORE_ENVIRONMENT=Development
export BlobStorage__Provider=Azure
export BlobStorage__ConnectionString='DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;'
export BlobStorage__ContainerName=agenthub-artifacts
export TableStorage__Provider=Azure
export TableStorage__ConnectionString="$BlobStorage__ConnectionString"
export IA_GIT_PROVIDER=gitea
export IA_DEPLOY_PROVIDER=compose
export Messaging__Provider=InProcessOnly
export Llm__Provider=Fixture
export Workers__Pipeline__InProcessOnly=true
export Workers__Pipeline__Enabled=true
```

Ensure blob container + tables exist (AgentHub/BuildGenix also ensure on startup):

```bash
set -a; source /tmp/bgx-azurite.env; set +a
python3 - <<'PY'
from azure.storage.blob import BlobServiceClient
from azure.data.tables import TableServiceClient
import os
cs=os.environ['BlobStorage__ConnectionString']
b=BlobServiceClient.from_connection_string(cs)
try: b.create_container('agenthub-artifacts')
except Exception as e: print('container', type(e).__name__)
print('tables', [t.name for t in TableServiceClient.from_connection_string(cs).list_tables()])
PY
```

**Expected tables (BuildGenix + AgentHub catalog on reference):**  
`ActivityLog`, `AgProcessedEvents`, `ApprovalGates`, `Approvals`, `Artifacts`, `EffectLedger`, `Projects`, `SgProcessedEvents`, `WmProcessedEvents`, `WorkItemEvents`, `WorkItemIndex`, `WorkItems`, `WorkItemsByCreator` (plus any StoragePrefix variants).

### 2.3 Start AgentHub (terminal A)

```bash
set -a; source /tmp/bgx-azurite.env; set +a
cd poc/agenthub-api
ASPNETCORE_URLS=http://127.0.0.1:43180 dotnet run --project src/AgentHub.WebApi --no-launch-profile
```

Expect log: `PIPELINE worker idle (Enabled=True, InProcessOnly=True)` and `Now listening on: http://127.0.0.1:43180`.  
`curl -s http://127.0.0.1:43180/` → `"status":"ok"`.

### 2.4 Start BuildGenix (terminal B)

```bash
set -a; source /tmp/bgx-azurite.env; set +a
cd poc/buildgenix-api
ASPNETCORE_URLS=http://127.0.0.1:43181 \
  PocPipeline__UseInMemoryTables=false \
  PocPipeline__AgentHubBaseUrl=http://127.0.0.1:43180 \
  PocPipeline__AuditLogDirectory=/tmp/bgx-poc-audit \
  PocPipeline__ArtifactFileRootPath=/tmp/bgx-poc-blobs \
  TableStorage__ConnectionString="$BlobStorage__ConnectionString" \
  dotnet run --project src/BuildGenix.WebApi --no-launch-profile
```

`curl -s http://127.0.0.1:43181/` → `"status":"ok"`.

### 2.5 Unit contracts (always before Manual QA)

```bash
cd poc/agenthub-api
dotnet test tests/AgentHub.Agents.Pipeline.Tests
```

**Reference:** Passed 58. Fail = stop; do not run E2E.

---

## 3. Automated full matrix (what the reference ran)

```bash
set -a; source /tmp/bgx-azurite.env; set +a
export AZURE_STORAGE_CONNECTION_STRING="$BlobStorage__ConnectionString"
export BGX_URL=http://127.0.0.1:43181
export OUT_DIR=/tmp/bgx-production-gate
export BLOB_ROOT=/tmp/bgx-poc-blobs
export AUDIT_ROOT=/tmp/bgx-poc-audit
export WS_ROOT=/tmp/bgx-poc
cd poc
bash scripts/manual-qa-production-gate.sh
# subset after integrating one agent, e.g.:
# ONLY_CASES=Calc_Add,RouteBack bash scripts/manual-qa-production-gate.sh
```

**Pass bar:** `overallPass: true`, `failed_checks: 0`, all cases PASS.  
Copy `$OUT_DIR/PRODUCTION-GATE-RESULTS.json` into your evidence folder.

### 3.1 Use cases (exact requests — do not skip)

| # | Case id | projectMode | stackProfile | userRequest (exact intent) | Expect deliveryMode | Live smoke |
|---|---|---|---|---|---|---|
| 1 | `Calc_Add` | New | fe-be | Create a calculator app that adds two numbers | Basic | `GET /api/add?a=2&b=3` → `"result":5` |
| 2 | `Hello_no_LLM` | New | fe-be | Create a hello world app without llm | Basic | `GET /api/hello` → status ok |
| 3 | `Hello_LLM` | New | fe-be-llm | Hello world with a thought of the day | Basic | `GET /api/thought` contains thought |
| 4a | `MultiWI_Add` | New | fe-be | Create a calculator app that adds two numbers | Basic | `/api/add` → 5 |
| 4b | `MultiWI_Subtract` | **Existing** (same `projectId`) | fe-be | Update the calculator app to subtract two numbers | **FeatureIncrement** | `/api/add`→5 **and** `/api/subtract?a=5&b=2`→3 |
| 5 | `Todo` | New | fe-be | Create a todo list app | Basic | `GET /api/todos` → JSON array |
| 6 | `Rewrite_net8` | **Rewrite** | fe-be | Rewrite the existing net8 calculator repo as a greenfield .NET 10 app keeping /api/add | **Rewrite** | `/api/add`→5; `/health` contains `net10`; FE `#a/#b/#go` (not legacy `#add`) |
| 7 | `RouteBack` | New | fe-be | Calculator add + `"simulateTaRouteback": true` | Basic | Trail has RouteBack; revisions `v1`+`v2`; `ta-concerns` present; final add→5 |

**Rewrite baseline path:** `poc/fixtures/legacy-net8-calculator` via `baselineArtifacts.baselineSourcePath`.

**MultiWI Subtract — paste-ready body** (replace `PROJECT_ID` with `MultiWI_Add` response `projectId`):

```json
{
  "userRequest": "Update the calculator app to subtract two numbers",
  "stackProfile": "fe-be",
  "projectMode": "Existing",
  "projectId": "PROJECT_ID",
  "projectName": "Gate-Multi",
  "autoApprove": true,
  "baselineArtifacts": {
    "prd": "baseline/prd.json",
    "techSpec": "baseline/techspec.json"
  }
}
```

**Rewrite — paste-ready body:**

```json
{
  "userRequest": "Rewrite the existing net8 calculator repo as a greenfield .NET 10 app keeping /api/add",
  "stackProfile": "fe-be",
  "projectMode": "Rewrite",
  "projectName": "Gate-Rewrite",
  "autoApprove": true,
  "baselineArtifacts": {
    "baselineSourcePath": "/ABS/PATH/TO/poc/fixtures/legacy-net8-calculator"
  }
}
```

**RouteBack — paste-ready body:**

```json
{
  "userRequest": "Create a calculator app that adds two numbers",
  "stackProfile": "fe-be",
  "projectMode": "New",
  "projectName": "Gate-Routeback",
  "autoApprove": true,
  "simulateTaRouteback": true
}
```

### 3.2 Per-case checks the harness asserts (~76–78 each)

For every non-RouteBack case the gate checks **all** of the following (reference Calc_Add = 78 checks):

**Pipeline / events**

- `status == Completed`, `stage == DONE`, `deployedUrl` present  
- Trail contains: `PRD`, `SYSTEM_DESIGN`, `IA_CODEGEN`, `IA_PUSH_DEPLOY`, `TA_VERIFY`, `TA_MERGE`, `DEPLOY_SANDBOX`  
- Event types include: `ArtifactReady`, `GateApproved`, `Finalized`, `StageStarted`, `StageCompleted`, `TaskCompleted`

**Live deploy**

- `GET {deployedUrl}/health` → 200 + `ok`  
- Kind-specific smoke paths (§3.1)  
- `GET {deployedUrl}/` → HTML (`<!doctype` / `<html`)

**Blobs (Azurite-primary — list under `{projectId}/{workItemId}/`)**

Revision folder `v{n}/` **and/or** named copies. Required logical names:

| Blob / named | Notes |
|---|---|
| `prd.json` / `PRD_V{n}.json` | deliveryMode must match case |
| `prd.md` / `PRD_MD_V{n}.json` | |
| `techspec.json` / `SYSTEM_DESIGN_V{n}.json` | deliveryMode match |
| `system-design.md` | |
| `implementation-plan.md` | IA_PLAN |
| `test-results.json` / `IA_TEST_RESULTS_V{n}.json` | TDD |
| `review.json` / `IA_REVIEW_V{n}.json` | |
| `code-review.md` | |
| `code-state.json` / `CODE_STATE_V{n}.json` | must mention graphify |
| `code-graph.json` / `CODE_GRAPH_V{n}.json` | |
| `pull-request.json` / `PULL_REQUEST_V{n}.json` | provider=gitea; branch `feature/…` |
| `ta-test-plan.md` | |
| `ta-results.json` / `TA_RESULTS_V{n}.json` | status=green; named cases |
| `ta-report.md` / `ta-status.json` | |
| `ta-verification-report.json` / `TA_VERIFICATION_REPORT_V{n}.json` | status=green; readyForDeployment=true |
| `merge.json` / `MERGE_V{n}.json` | deployedUrl |
| `sandbox-url.json` / `SANDBOX_URL_V{n}.json` | verification fields |
| `decision-log.json` / `DECISION_LOG.json` + `DECISION_LOG_V{n}.json` | agents PRD, SystemDesign, IA, TA, Deploy |
| `completion-summary.json` / `COMPLETION_V{n}.json` | |
| `LATEST.json` | pointer to latest named/revision |
| `AuditLog` (BuildGenix file mirror) | transitions ≥ ~12; deployedUrl |

**Workspace**

- `/tmp/bgx-poc/project-{pid}-work-item-{wid}/backend/Program.cs` contains expected routes  
- `tests/e2e/cases.json` + `tests/e2e/playwright/fe.spec.js` exist  

**FE selectors (manual / unit — not all asserted by the gate script)**

- **Rewrite / Calculator:** FE must have `id="a"`, `id="b"`, `id="go"`, `id="out"` — **not** legacy fixture `id="add"` only. Enforced by `DeliveryModeTests` + Playwright Calculator branch; if Playwright soft-skips, **manually** open FE or `rg 'id=\"go\"' backend/wwwroot/index.html`.  
- **Todo:** Playwright uses `#title` / `#add` / `#list` — `#add` here is **correct** (do not reject Todo for having `#add`).  
- **Hello / HelloLlm:** `#go` + `#out`.

**TA / Playwright**

- `ta-results.json` status `green`  
- Playwright status `green` **or** `skipped` (browser install failure soft-skip)  
- HTTP cases still green when Playwright skipped  
- Soft-skip ≠ FE contract proven — for Rewrite always confirm `#go` in wwwroot (unit test + optional manual)

**RouteBack-only extras**

- Trail contains `TA_VERIFY:RouteBack` (or equivalent RouteBack token)  
- Event `RouteBack`  
- Revisions ≥ 2 (`v1`, `v2`)  
- `v1/ta-concerns.json` and/or `TA_CONCERNS_V1.json`  
- Final deploy still serves add→5  
- Status remains **Completed** (RouteBack is not Failed)

---

## 4. Agent-by-agent deep verification

Pipeline stage order (BuildGenix `PocPipelineOrchestrator.StageOrder` — **array ends at `DEPLOY_SANDBOX`**):

```text
PRD → SYSTEM_DESIGN → IA_PLAN → IA_CODEGEN → IA_REVIEW → IA_PUSH_DEPLOY
  → TA_GENERATE → TA_VERIFY → TA_REPORT → TA_MERGE → DEPLOY_SANDBOX
```

After sandbox accept, the work item is set to **`CurrentStage=DONE` / `Status=Completed`** (terminal state — not a StageOrder entry).

Orchestrator → AgentHub call: `POST http://127.0.0.1:43180/api/pipeline/dispatch` with stage task body.  
Each stage: AgentHub logs `PIPELINE event AgentStarted|TaskStarted|TaskCompleted|AgentCompleted` and BuildGenix appends `WorkItemEvent` rows + audit transitions.

### 4.1 Product agent — stage `PRD`

| Surface | What to verify |
|---|---|
| **Logs (AgentHub)** | `Stage=PRD` AgentStarted → TaskCompleted; ArtifactReady for prd |
| **Logs (BuildGenix)** | `WorkItemEvent StageStarted/Completed` PRD; `GateRaised` then `GateApproved` (autoApprove) |
| **Blobs** | `v1/prd.json`, `v1/prd.md`, named `PRD_V1.json`, `PRD_MD_V1.json`; `LATEST.json` updated |
| **Decision** | `DECISION_LOG` entry `agent=PRD` with summary |
| **DB** | `WorkItems` Status/CurrentStage progress; `WorkItemEvents` rows; `Artifacts` metadata if used; `Approvals` / `ApprovalGates` for PRD gate |
| **Content** | `deliveryMode` ∈ {Basic, Rewrite, FeatureIncrement}; stories present; stackProfile echoed |
| **Within-agent** | Fixture/LLM persona product call (Fixture = deterministic scaffolding) |

**After integrating only PRD:** run `ONLY_CASES=Calc_Add` and assert PRD blobs + decision_has_PRD before later stages (or stop pipeline after PRD in interactive mode).

### 4.2 Architect agent — stage `SYSTEM_DESIGN`

| Surface | Verify |
|---|---|
| **Logs** | Stage SYSTEM_DESIGN complete; gate approve |
| **Blobs** | `techspec.json`, `system-design.md`, `SYSTEM_DESIGN_V1.json` |
| **Decision** | `agent=SystemDesign` |
| **Content** | `deliveryMode` matches PRD; apis/routes consistent with later codegen |
| **DB** | WorkItemEvents + Approvals for SD gate |

### 4.3 Implementation Agent — `IA_PLAN` → `IA_CODEGEN` → `IA_REVIEW` → `IA_PUSH_DEPLOY`

| Stage | Logs | Files / workspace | DB / git / deploy |
|---|---|---|---|
| **IA_PLAN** | plan tasks | `implementation-plan.md` | gate Approvals |
| **IA_CODEGEN** | builder TDD; host fallback OK if builder shell warns | workspace tree; `Program.cs` routes; FE wwwroot; `test-results.json` green; `code-state.json` (graphify); `code-graph.json` | EffectLedger step intents |
| **IA_REVIEW** | review tasks | `review.json`, `code-review.md` | |
| **IA_PUSH_DEPLOY** | push branch; open PR; compose up | `pull-request.json` (gitea, `feature/…`); live `deployedUrl` | Gitea PR HTTP 200; compose container healthy |

**Decision:** `agent=IA` entries for plan/codegen/review/push.  
**Delivery modes:**

- Basic → `RequestDrivenCodegen`  
- Rewrite → `GreenfieldRewriteBuilder` (preserve `/api/add`, net10, **Playwright FE `#go`**)  
- FeatureIncrement → `FeatureIncrementBuilder` (keep add, add subtract)

**Known failure (fixed in d629d18):** Rewrite keeping legacy `#add` FE → Playwright red → RouteBack exhaustion. After integration, assert Rewrite FE selectors.

### 4.4 Testing Agent — `TA_GENERATE` → `TA_VERIFY` → `TA_REPORT` → `TA_MERGE`

| Stage | Verify |
|---|---|
| **TA_GENERATE** | `ta-test-plan.md`; `tests/e2e/cases.json`; `tests/e2e/playwright/fe.spec.js` |
| **TA_VERIFY** | HTTP + Playwright against `deployedUrl`; `ta-results.json` green; on red → **Completed + RouteBack** (not Failed) + `ta-concerns.json` |
| **TA_REPORT** | `ta-report.md`, `ta-status.json`, **`ta-verification-report.json`** (`status=green`, `readyForDeployment=true`) + named `TA_VERIFICATION_REPORT_V{n}.json` |
| **TA_MERGE** | merge PR; `merge.json` with deployedUrl |

**Decision:** `agent=TA`.  
**Agent↔agent:** RouteBack is **orchestrator-mediated** (TA sets `signal.kind=RouteBack` → BuildGenix bumps revision → resumes `IA_CODEGEN`). Max 2 RouteBacks then fail. Probe: `simulateTaRouteback: true`.

### 4.5 Deploy / sandbox — `DEPLOY_SANDBOX`

| Surface | Verify |
|---|---|
| **Logs** | Stage DEPLOY_SANDBOX; Finalized DONE |
| **Blobs** | `sandbox-url.json` / `SANDBOX_URL_V{n}.json` includes URL + verification linkage |
| **Decision** | `agent=Deploy` |
| **Note** | Stub publishes URL + waits (auto-approve). **Not** App Service create/promote |

---

## 5. Cross-cutting AgentHub surfaces (every successful WI)

Copy this checklist into your evidence for **each** work item id.

### 5.1 Logs

- [ ] AgentHub: `PIPELINE event` AgentStarted/TaskStarted/TaskCompleted/AgentCompleted per stage  
- [ ] AgentHub: `Wrote artifact blob {pid}/{wid}/…` for each publish  
- [ ] BuildGenix: `WorkItemEvent` StageStarted, StageCompleted, GateRaised, GateApproved, ArtifactReady, TaskCompleted, Finalized  
- [ ] On RouteBack: `TA_VERIFY:RouteBack` transition message with attempt + rev  

### 5.2 Files / blobs (Azurite container `agenthub-artifacts`)

```bash
# List
python3 - <<'PY'
from azure.storage.blob import BlobServiceClient
import os, sys
pid, wid = sys.argv[1], sys.argv[2]
c=BlobServiceClient.from_connection_string(os.environ['AZURE_STORAGE_CONNECTION_STRING']).get_container_client('agenthub-artifacts')
for b in sorted(c.list_blobs(name_starts_with=f'{pid}/{wid}/'), key=lambda x: x.name):
    print(b.name)
PY
```

- [ ] `v1/` (and `v2/` if RouteBack) populated  
- [ ] Named `PRD_V1.json`, `DECISION_LOG.json`, `TA_VERIFICATION_REPORT_V1.json`, `LATEST.json`  
- [ ] Local `/tmp/bgx-poc-blobs` may only partially mirror (AuditLog/LATEST) when Azure-primary — **trust Azurite**  

### 5.3 DB / table updates

- [ ] `WorkItems` row: Status Completed, CurrentStage DONE (or terminal), RepoFactsJson has deployedUrl  
- [ ] `WorkItemEvents` growth across stages  
- [ ] `Approvals` / `ApprovalGates` for auto-approved gates  
- [ ] `EffectLedger` / `ActivityLog` / `AgProcessedEvents` activity during Pipeline  
- [ ] `Artifacts` rows if catalogued  

### 5.4 Decisions

- [ ] `DECISION_LOG.json` agents include **PRD, SystemDesign, IA, TA, Deploy**  
- [ ] `decisionSummary` non-empty chain  

### 5.5 Agent calls

- [ ] BuildGenix → AgentHub `POST /api/pipeline/dispatch` 200 per stage  
- [ ] Within-agent: LLM/persona, TDD, graphify, Playwright process  
- [ ] RouteBack path exercised at least once after TA integration  

### 5.6 Deploy / FE / PR

- [ ] Compose URL health + smoke + FE index  
- [ ] Gitea PR page 200; feature branch (not direct main)  
- [ ] Playwright suite files present; status green or skipped  

---

## 6. When to re-run (after each agent integration)

| You just integrated… | Minimum re-run |
|---|---|
| PRD / Product only | Unit tests + `ONLY_CASES=Calc_Add` + §4.1 blob/decision/DB |
| System Design / Architect | Above + assert techspec deliveryMode |
| IA (plan/codegen/review/push) | Full matrix cases 1–6 (all deliveryModes) + Rewrite FE selectors |
| TA (generate/verify/report/merge) | Full matrix + **RouteBack** + verification-report named blobs |
| Deploy sandbox stub | Assert sandbox-url + Finalized after TA green |
| Storage provider flip (File↔Azure) | Full gate with Azurite; never sign File-only as Azure |
| Git/Deploy provider flip (gitea↔AzDO, compose↔App Service) | [`QA_Runbook-azure-dual-mode.md`](QA_Runbook-azure-dual-mode.md) Gates C–E |

---

## 7. Reference evidence (what was already run)

| Iteration | Artifact | Result |
|---|---|---|
| Greenfield Manual QA | [`MANUAL-QA-GREENFIELD-SIGNOFF.md`](MANUAL-QA-GREENFIELD-SIGNOFF.md) · `evidence/MANUAL-QA-RESULTS-greenfield.json` | 8 cases · 443 checks PASS |
| TA Playwright + RouteBack | [`TA-PLAYWRIGHT-ROUTEBACK-SIGNOFF.md`](TA-PLAYWRIGHT-ROUTEBACK-SIGNOFF.md) | PASS |
| Production gate Azurite+Fixture | [`PRODUCTION-GATE-SIGNOFF-2026-10-16.md`](PRODUCTION-GATE-SIGNOFF-2026-10-16.md) · `evidence/production-gate-2026-10-16/` | 8 cases · **545** checks PASS |
| Azure docs vs code | [`AZURE-DOCS-CODE-REVIEW-2026-10-16.md`](AZURE-DOCS-CODE-REVIEW-2026-10-16.md) · [`AZURE-DOCS-CODE-REVIEW-TA-2026-10-16.md`](AZURE-DOCS-CODE-REVIEW-TA-2026-10-16.md) | Dual-mode aligned; Gate C/D not live-run |

### 7.1 Fixes discovered during reference gate (must not regress)

1. **Rewrite FE ↔ Playwright mismatch** — always emit `#a/#b/#go`; unit test in `DeliveryModeTests`.  
2. **Gate false negatives on Azure-primary** — inspect Azurite for `DECISION_LOG` / `PRD_Vn` / content, not only local file mirror.  
3. **Compose network clash** — `prune-poc-compose.sh` / LocalComposeHost prune+retry.

---

## 8. Explicit non-claims (do not tell stakeholders these are green)

| Item | Status on reference |
|---|---|
| Live Azure App Service deploy | Not run |
| Live Azure DevOps PR/merge | Not run (Gitea stand-in used) |
| Azure LLM Gateway | Untouched; Fixture used locally |
| Service Bus real consumer (`InProcessOnly=false`) | Not exercised |
| Interactive studio full HITL UX | Out of scope for autoApprove gate |
| `DEPLOY_SANDBOX` as production promote | Stub only |

---

## 9. Evidence capture template (fill per machine)

```text
Machine:
Git SHA:
Date:
AgentHub URL:
BuildGenix URL:
Blob provider / account:
Table provider:
LLM provider:
Gate script overallPass:
Failed checks:
WI ids (per case):
  Calc_Add:
  Hello_no_LLM:
  Hello_LLM:
  MultiWI_Add / Subtract:
  Todo:
  Rewrite_net8:
  RouteBack:
Deployed URLs:
Attached: PRODUCTION-GATE-RESULTS.json
Unit tests: ___/___ passed
Signer:
```

---

## 10. Six-persona review of **this runbook**

Reviewed against harness + `StageOrder` + Rewrite FE unit tests ([review agent](bc-832a6c12-f493-54ab-9f5d-ebce403a7ea1)). Fixes applied: StageOrder (no `DONE` in array), paste-ready MultiWI/Rewrite/RouteBack bodies, FE selector guidance (Todo `#add` OK; Rewrite `#go` required; gate does not assert FE ids).

| Persona | Verdict | Notes |
|---|---|---|
| **Product / PM** | **APPROVE** | All user-facing use cases + paste-ready bodies; deliveryMode + non-claims honest |
| **Solution Architect** | **APPROVE** | StageOrder accurate; dual-mode Azurite; orchestrator-mediated RouteBack; DONE = terminal state |
| **Principal / Backend** | **APPROVE** | Agent-by-agent log/blob/decision/DB; dispatch; TDD/graphify/Playwright; Rewrite FE regression called out |
| **Frontend** | **APPROVE** | Per-kind selectors; Rewrite `#go` vs Todo `#add` distinguished; soft-skip hole documented |
| **DevOps / Azure** | **APPROVE (local)** | Ports, env, Azurite, prune; Gates C–E for real Azure; Fixture ≠ Gateway |
| **Senior QA** | **APPROVE** | Harness bar + per-agent re-run matrix + evidence template; fail-closed on overallPass |

**Overall runbook verdict: APPROVE for other-machine Copilot execution after each agent integration.**  
Cloud live Azure remains gated by `QA_Runbook-azure-dual-mode.md`, not this document alone.  
Detail: `/cursor/stores/.../internal/post-agent-runbook-six-persona-review.md` (agent store).

---

## 11. Quick command card

```bash
# 1) units
cd poc/agenthub-api && dotnet test tests/AgentHub.Agents.Pipeline.Tests

# 2) full post-integration gate (Azurite + Fixture)
set -a; source /tmp/bgx-azurite.env; set +a
export AZURE_STORAGE_CONNECTION_STRING="$BlobStorage__ConnectionString"
cd poc && bash scripts/manual-qa-production-gate.sh

# 3) after TA only
ONLY_CASES=Calc_Add,Rewrite_net8,RouteBack bash scripts/manual-qa-production-gate.sh

# 4) prune leftover compose apps between big runs
bash scripts/prune-poc-compose.sh
```

**Done when:** units green · gate `overallPass=true` · §5 checklist ticked for each WI · SHA recorded · no skipped cases from §3.1.

# Spoon-feed: integrate buildgenix-api catalog endpoints on the new machine

**Purpose:** Paste into VS Code Copilot Chat on the isolated / new machine that already has `inno-tech-aisdlc-buildgenix-api` boilerplate (Data project, heartbeat ping/pong, Service Bus).  
**Source repo for the piece to integrate:** `https://github.com/badrusiddique/agent-sdlc` branch `main` (commit at least `39673c4`).  
**Do not guess.** Follow every DO / DON'T. Verification section is mandatory before you say done.

---

## 0. What you are integrating

Bring the **I1 catalog endpoint piece** from agent-sdlc into the new-machine BuildGenix API boilerplate:

| Include | Exclude |
|---|---|
| Controllers for all 46 `CatalogRoutes` | AgentHub code |
| `ApiDtos.cs`, `CatalogRoutes.cs`, `ApiProblems.cs` | Angular UI |
| Dummy `X-User-Id` middleware | Production SSO |
| Work item create / summary marker / Implement (`~ACTIVE`, D-C4b/C5) | Results consumer / stage task publish (later) |
| Table ensure for all BG `TableCatalog.Built` tables | AgentHub tables |
| Emulator-aware `/health` + heartbeat D-C1 order | Docker deploy to Azure |

---

## 1. DO / DON'T

### DO

1. Clone / open the **new-machine** BuildGenix solution (boilerplate). Keep its folder layout (Data, Data.Migration, Messaging, WebApi) if present.
2. Open a second folder or git worktree of **agent-sdlc** at `main` ≥ `39673c4` for copy reference only.
3. Port by **file path mapping**, not by inventing new routes.
4. Keep **one route inventory** (`CatalogRoutes` or equivalent). Controllers must stay 1:1 with that list.
5. Preserve problem+json **`code`** extensions (`NotFound`, `ValidationFailed`, `SummaryNotFound`, `ActiveConflict`, `NotImplemented`, `ServiceUnavailable`).
6. Keep D-C1 for heartbeat: **publish pong → mark processed → complete**.
7. Keep D-C4b: create WI does **not** take `~ACTIVE`; Implement does.
8. Keep D-C5: same `summaryRevision` → success no-op (`alreadyStarted: true`).
9. After port, run the **Verification** section end to end. If any check fails, fix before claiming done.

### DON'T

1. Do **not** invent extra REST endpoints for the same UI need.
2. Do **not** take `~ACTIVE` on create.
3. Do **not** start Execute without a committed summary revision (must return `400 SummaryNotFound`).
4. Do **not** invent MessageIds for Service Bus dedup (never `Guid.NewGuid()` for MessageId).
5. Do **not** write AgentHub tables from BuildGenix.
6. Do **not** put secrets in source. Use env / Key Vault / user-secrets.
7. Do **not** skip verification because “it compiles”.
8. Do **not** change product decisions (decision-log). If unclear, stop and ask Badru.

---

## 2. Exact files to copy / merge from agent-sdlc

From `agent-sdlc/buildgenix-api/` into the new-machine BuildGenix solution (adjust namespaces only if the new repo already uses a different root namespace; prefer keeping `BuildGenix.*`).

### Must port (code)

| agent-sdlc path | Merge into |
|---|---|
| `src/BuildGenix.WebApi/Contracts/CatalogRoutes.cs` | WebApi Contracts |
| `src/BuildGenix.WebApi/Contracts/ApiDtos.cs` | WebApi Contracts |
| `src/BuildGenix.WebApi/Infrastructure/ApiProblems.cs` | WebApi Infrastructure |
| `src/BuildGenix.WebApi/Auth/DummyUserIdMiddleware.cs` | WebApi Auth |
| `src/BuildGenix.WebApi/Controllers/*.cs` (all controllers) | WebApi Controllers |
| `src/BuildGenix.WebApi/Health/ServiceBusEmulatorHealthCheck.cs` | WebApi Health |
| Heartbeat consumer/publisher/endpoints if new machine already has them | **Diff carefully**: keep new-machine topic names if already Azure-configured; keep D-C1 order + no random MessageId + no throw on missing CS |
| `src/BuildGenix.Data/Repositories/WorkItemRepository.cs` (+ interface) | Data Repositories |
| Related entities / table catalog / initializer ensure-all-Built | Data |
| `TableClientFactory` ConnectionString **or** ServiceUri + `DefaultAzureCredential` | Data |
| Tests under `tests/BuildGenix.WebApi.Tests/` that cover catalog + lifecycle | Tests |

### Must read (docs; do not treat as optional)

| Path |
|---|
| agent-sdlc `docs/junior-ui-endpoint-wiring.md` |
| agent-sdlc `docs/ui-mock-endpoint-map.md` |
| Context (if available) `docs/buildgenix-api-contracts.md`, `docs/decision-log.md` |

---

## 3. Paste-ready Copilot prompt

Copy everything below into VS Code Copilot Chat on the **new machine BuildGenix API workspace**:

```text
ROLE: Senior .NET engineer. Zero assumptions. Do not invent product rules.

GOAL: Integrate the buildgenix-api I1 catalog endpoint piece from github.com/badrusiddique/agent-sdlc (main ≥ 39673c4) into THIS boilerplate solution that already has BuildGenix.Data + heartbeat ping/pong + Service Bus wiring.

HARD RULES:
1) Catalog routes must match agent-sdlc CatalogRoutes.cs exactly (46 routes). No extras. No missing.
2) Controllers must use ApiProblems with stable `code` extensions.
3) D-C4b: POST work-items must NOT take ~ACTIVE. Only POST …/execute takes ~ACTIVE.
4) D-C5: same summaryRevision → alreadyStarted true (HTTP 200).
5) Execute without committed summary → HTTP 400 code=SummaryNotFound.
6) Cross-WI Implement while lock held → HTTP 409 code=ActiveConflict with activeWorkItemId.
7) Heartbeat D-C1: publish pong → mark processed → complete. Never Guid.NewGuid() for MessageId; dead-letter if MessageId missing.
8) Ensure ALL BuildGenix Built tables at startup (Projects, WorkItems, WorkItemIndex, WorkItemsByCreator, Artifacts, ApprovalGates, Approvals, WorkItemEvents, SgProcessedEvents, WmProcessedEvents).
9) /health with UseDevelopmentEmulator=true must use emulator HTTP health (not Azure topic check). Return JSON details.
10) TableStorage: ConnectionString for local; ServiceUri + DefaultAzureCredential for App Service.
11) Do not touch AgentHub. Do not invent Angular. Do not invent results consumer yet.
12) Keep existing new-machine Azure topic names if already configured for heartbeat; do not rename Azure resources without asking.

WORK STEPS:
A) Diff agent-sdlc buildgenix-api controllers/contracts/data repos against this repo.
B) Port missing files; merge carefully where heartbeat/Data already exist.
C) Wire Program.cs: AddBuildGenixData, controllers, dummy user middleware, emulator health, swagger.
D) Port unit tests for CatalogRoutes count=46, lifecycle, SummaryNotFound, ActiveConflict, AlreadyStarted.
E) Run VERIFICATION checklist below. Fix until all PASS. Report a table of PASS/FAIL with evidence.

VERIFICATION (must all PASS; do not claim done otherwise):
V1) dotnet test — 0 failed, 0 skipped.
V2) docker compose down -v && up -d; wait until curl http://127.0.0.1:5300/health shows "status":"healthy" (not unhealthy).
V3) run API; GET /health/live → 200; GET /health → JSON Healthy with servicebus-emulator Healthy.
V4) POST /api/connectivity/heartbeat → 202; logs show pong published BEFORE mark.
V5) POST /api/projects with X-User-Id → 201.
V6) POST …/work-items → 201; Azurite has NO ~ACTIVE row for that project.
V7) POST …/execute summaryRevision=1 without summary → 400 SummaryNotFound.
V8) POST …/conversations/{thread}/summaries → 200 markerOnly=true; GET summary → contentUrl null, markerOnly=true.
V9) POST …/execute summaryRevision=1 → 200 alreadyStarted=false; ~ACTIVE exists.
V10) POST …/execute same revision → 200 alreadyStarted=true.
V11) Create second WI + summary + execute → 409 ActiveConflict.
V12) Hit all 46 catalog routes once; no 500; stubs return 501 NotImplemented when expected.
V13) List Azurite tables = 10 Built BG tables (no AgentHub table names).

OUTPUT: list of files changed + verification table PASS/FAIL + any STOP questions for Badru.
```

---

## 4. Verification checklist (human / Copilot)

| ID | Check | Expected | Pass? |
|---|---|---|---|
| V1 | `dotnet test` | 0 failed, 0 skipped | |
| V2 | SB emulator health | `"status":"healthy"` | |
| V3 | `/health` + `/health/live` | Healthy JSON + 200 | |
| V4 | Heartbeat D-C1 logs | publish then mark | |
| V5 | Create project | 201 | |
| V6 | Create WI | 201; no `~ACTIVE` | |
| V7 | Execute no summary | 400 `SummaryNotFound` | |
| V8 | Summary marker | `markerOnly=true`, `contentUrl=null` on GET | |
| V9 | Execute start | 200 `alreadyStarted=false` | |
| V10 | Execute noop | 200 `alreadyStarted=true` | |
| V11 | Second WI execute | 409 `ActiveConflict` | |
| V12 | 46 routes | no 500 | |
| V13 | Tables | 10 BG Built only | |

Reference evidence from agent-sdlc cloud run: Context `internal/migration-prep-e2e/buildgenix/`.

---

## 5. Junior UI wiring after integration

Give juniors: agent-sdlc `docs/junior-ui-endpoint-wiring.md` (and Context copy under `docs/`).  
They map mock 01–23 → endpoints → DTO names → wire status. They must not invent routes.

# Spoon-feed: fold the interactive BuildGenix chat UI into the POC on the other machine

**Purpose:** Paste this whole file into the coding agent on the other machine. It tells you exactly what to pick from the new change, what not to touch, how to integrate it on top of the POC already on `main`, and how to prove it.

**Date:** 2026-10-09  
**Repo:** `https://github.com/badrusiddique/agent-sdlc` only. Never `ai-sdlc`.  
**Source of this change (until it lands on GitHub `main`):** branch `cursor/buildgenix-chat-ui-1fa7` tip **`6e123b2`** (steering + CONTINUE message fix + `verify-steering.sh` Aborted wait fix).  
**Baseline already on the other machine:** GitHub `main` with the pre-phase-0 POC under `poc/` (one Pipeline agent + auto-approve `/run` + `verify-three.sh`). That work stays. This change **extends** it; it does not replace the System Design agent work.

---

## 0. Read this first (scope lock)

| Do | Do not |
|---|---|
| Work only under `poc/` for this prompt | Edit root `agenthub-api/` or root `buildgenix-api/` for this fold-in |
| Keep the existing POC Pipeline agent | Rewrite the one-agent happy path into multi-agent Phase 0 |
| Add interactive gates + Angular chat UI + cooperative steering | Mix this into the System Design first-slice (`i3-system-design-agent-spoon-feed.md`) |
| Keep `POST /api/poc/pipeline/run` auto-approve for scripts | Break `poc/scripts/verify-three.sh` |
| Use Azurite + real LLM for Manual QA | Claim Manual QA on Fixture-only |

**Conflict with the SD spoon-feed:** that file says “do not edit `poc/`”. That rule is for the **System Design agent** pass. **This** pass is the opposite: you **only** edit `poc/`. If the other machine is mid System Design Part A, finish or park that work on its own branch before applying this. Do not interleave the two.

---

## 1. What this change is (one paragraph)

The POC already walks Intake → PRD → System Design → Implementation → Testing → Deploy → Finalize with **auto-approved** gates via `POST /api/poc/pipeline/run`. This change makes the same pipeline **user-driven**:

1. Angular app `poc/buildgenix-web` (chat + stage strip + Documents + Logs + steer bar).
2. BuildGenix `POST /api/poc/pipeline/start` starts a background run and **pauses** at PRD, SYSTEM_DESIGN, and post-DEPLOY for human `POST …/approve`.
3. Status / events / artifacts / logs (+ SSE) feed the UI.
4. Cooperative chat steering: `POST …/steer` with `PAUSE` | `STOP` | `ABORT` | `CONTINUE` (also typeable in the studio).
5. AgentHub gains `GET /api/pipeline/blob?path=` so Documents can read blob bodies.
6. `verify-three.sh` still uses `/run` with auto-approve. `verify-interactive.sh` exercises human gates. `verify-steering.sh` exercises PAUSE/STOP/CONTINUE/ABORT.

---

## 2. How to get the bits onto the other machine

### Option A — GitHub `main` already has this tip (preferred after Badru pushes)

```bash
cd <agent-sdlc>
git fetch origin main
git checkout main
git pull origin main
git rev-parse HEAD   # must include commits that add poc/buildgenix-web, interactive APIs, and steer
```

Confirm these paths exist:

- `poc/buildgenix-web/package.json`
- `poc/buildgenix-api/src/BuildGenix.WebApi/Poc/PocPipelineModels.cs`
- `poc/scripts/verify-interactive.sh`
- `poc/scripts/verify-steering.sh`

### Option B — Main is still the older POC only (current state until push)

Fetch the feature branch (after it is published to GitHub), then **cherry-pick or checkout only `poc/` paths**:

```bash
cd <agent-sdlc>
git fetch origin
# After the branch is on GitHub:
git fetch origin cursor/buildgenix-chat-ui-1fa7
git checkout -b fold/poc-chat-ui origin/main

# Take the whole tree from the feature tip for poc/ + the tiny root README line:
git checkout origin/cursor/buildgenix-chat-ui-1fa7 -- \
  poc/README.md \
  poc/agenthub-api/src/AgentHub.WebApi/Pipeline/PipelineEndpoints.cs \
  poc/buildgenix-api/src/BuildGenix.Data/Entities/WorkItemEntity.cs \
  poc/buildgenix-api/src/BuildGenix.WebApi/Poc/ \
  poc/buildgenix-api/src/BuildGenix.WebApi/Program.cs \
  poc/buildgenix-api/src/BuildGenix.WebApi/appsettings.Development.json \
  poc/buildgenix-web/ \
  poc/scripts/verify-interactive.sh \
  poc/scripts/verify-steering.sh \
  README.md

git status
# Expect only poc/** and README.md. If anything under root agenthub-api/ or root buildgenix-api/ appears, abort and reset those paths.
```

If the branch is not on GitHub yet, Badru must push `cursor/buildgenix-chat-ui-1fa7` to `badrusiddique/agent-sdlc` first (or paste a patch). Do not invent files.

---

## 3. Exact file inventory — pick these, nothing else

### 3.1 Modified (merge carefully if you edited the same files locally)

| Path | Why |
|---|---|
| `poc/agenthub-api/src/AgentHub.WebApi/Pipeline/PipelineEndpoints.cs` | Adds `GET /api/pipeline/blob?path=` |
| `poc/buildgenix-api/src/BuildGenix.Data/Entities/WorkItemEntity.cs` | `Paused` / `Aborted` statuses (+ terminal includes Aborted) |
| `poc/buildgenix-api/src/BuildGenix.WebApi/Poc/PocPipelineController.cs` | `/run`, `/start`, approve, **steer**, status, events, artifacts, logs, SSE |
| `poc/buildgenix-api/src/BuildGenix.WebApi/Poc/PocPipelineOrchestrator.cs` | Session + human gates + cooperative steering; `/run` still auto-approves |
| `poc/buildgenix-api/src/BuildGenix.WebApi/Poc/PocPipelineOptions.cs` | `ArtifactFileRootPath` |
| `poc/buildgenix-api/src/BuildGenix.WebApi/Program.cs` | CORS for the Angular origin |
| `poc/buildgenix-api/src/BuildGenix.WebApi/appsettings.Development.json` | Artifact root |
| `poc/README.md` | How to run UI + interactive + steering scripts |
| `README.md` | One line pointing at `poc/` UI |

### 3.2 New

| Path | Why |
|---|---|
| `poc/buildgenix-api/src/BuildGenix.WebApi/Poc/PocPipelineModels.cs` | DTOs for interactive + steer API |
| `poc/buildgenix-web/**` | Entire Angular studio (take the folder as a unit) |
| `poc/scripts/verify-interactive.sh` | Human-gate happy path |
| `poc/scripts/verify-steering.sh` | PAUSE / STOP / CONTINUE / ABORT smoke |

### 3.3 Do **not** pick / do not rewrite

- Root `agenthub-api/**` and root `buildgenix-api/**` — out of scope for this fold-in.
- `poc/agenthub-api/src/AgentHub.Agents.Pipeline/**` — Pipeline agent itself is unchanged by this UI work; keep the version already on your `main` unless a later tip explicitly changes it.
- `poc/blob-storage-snapshot/**` — historical proof from the first Azurite sign-off; optional to refresh, not required to integrate.
- Anything from `docs/prompts/i3-system-design-agent-*.md` — different workstream.

---

## 4. Behaviour contract the other machine must preserve

1. **`POST /api/poc/pipeline/run`**  
   - Body: `{ userRequest, stackProfile?, projectMode?, projectName?, projectId?, baselineArtifacts?, autoApprove? }`  
   - Default `autoApprove=true`. Blocks until Completed (or throws).  
   - Used by `poc/scripts/verify-three.sh`. Must still pass.

2. **`POST /api/poc/pipeline/start`**  
   - Forces interactive mode (`autoApprove=false`). Returns **202** with `{ projectId, workItemId, status, stage, message }` immediately.  
   - Pipeline continues in-process on the BuildGenix host.

3. **Human gates only at** `PRD`, `SYSTEM_DESIGN`, and after `DEPLOY_DEV` Deploy (before Finalize).  
   - Implementation and Testing do not wait for a person.

4. **`POST /api/poc/pipeline/{workItemId}/approve`**  
   - Releases the current gate. 204 on success. 400 if no gate. 404 if unknown WI.

5. **`POST /api/poc/pipeline/{workItemId}/steer`**  
   - Body: `{ "command": "PAUSE" | "STOP" | "ABORT" | "CONTINUE" }`  
   - Returns `{ workItemId, command, status, message }` — **`message` must be non-null** (especially CONTINUE).  
   - Semantics (cooperative, not hard kill of the current LLM call):
     - **PAUSE** — finish the current stage (or note while at a gate); then enter `Paused` and wait for CONTINUE / STOP / ABORT.
     - **STOP** — cancel the pending gate if any; revert resume point to **last approved** stage (or INTAKE); enter `Paused`. CONTINUE resumes from the stage after last approved.
     - **ABORT** — set status `Aborted`, cancel the run, clear the active chat to idle. Terminal.
     - **CONTINUE** — only valid when `Paused` (or a continue gate is open); resumes the pipeline. Response `message` must not be null.

6. **Read APIs** (all under `/api/poc/pipeline/{workItemId}/…`):  
   - `status` — stage strip states: `pending|active|running|awaiting|done|failed`; also `isPaused`, `canResume`, `lastApprovedStage`, `steerHint`  
   - `events` — WorkItemEvents for chat bubbles  
   - `artifacts` / `artifacts/{key}` — registry + body (file root and/or AgentHub blob GET)  
   - `logs` — readable timeline lines  
   - `stream` — SSE `event: status` snapshots  

7. **AgentHub** `GET /api/pipeline/blob?path=` returns JSON body or 404. Reject `..` in path.

8. **UI** `poc/buildgenix-web` on port **43182**, proxies `/api` → BuildGenix **43181**. Example chips: Hello World, Calculator add, Calculator subtract. Approve CTA only for the **current** `pendingGate`. Steer bar / chat commands: PAUSE, STOP, ABORT, CONTINUE.

---

## 5. Runbook on the other machine (Manual QA = actual resources)

Use **Gateway** on the isolated machine if that is the house rule; use **Ollama** if this box has it. Do not use Fixture for the Manual QA rows below.

### 5.1 Shared env

```bash
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

export AZURITE_CS='DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;'

# Isolated machine: set Gateway key; omit Ollama.
# Local Ollama machine:
export OLLAMA_HOST=0.0.0.0:11434
export OLLAMA_BASE_URL=http://127.0.0.1:11434
export OLLAMA_MODEL=llama3.2:1b
unset BGX_SKIP_TABLE_INIT
```

Start Azurite (Docker compose from `poc/agenthub-api` **or** `npx azurite` with `--skipApiVersionCheck` if the SDK complains about API version).

### 5.2 AgentHub (terminal A)

Same as `poc/README.md` Azurite path: `TableStorage__Provider=Azure`, `BlobStorage__Provider=Azure`, `Llm__Provider=Ollama` **or** `Gateway`, `Workers__Pipeline__InProcessOnly=true`, port **43180**.

Wait for: `AgentHub table storage ready`.

### 5.3 BuildGenix (terminal B)

```bash
cd poc/buildgenix-api
ASPNETCORE_URLS=http://127.0.0.1:43181 \
ASPNETCORE_ENVIRONMENT=Development \
TableStorage__ConnectionString="$AZURITE_CS" \
PocPipeline__UseInMemoryTables=false \
PocPipeline__AgentHubBaseUrl=http://127.0.0.1:43180 \
PocPipeline__AuditLogDirectory=/tmp/bgx-poc-audit-azurite \
PocPipeline__ArtifactFileRootPath=/tmp/bgx-poc-blobs \
dotnet run --project src/BuildGenix.WebApi --no-launch-profile
```

Wait for: `BuildGenix table storage ready. Ensured 10/10 catalog tables.`

### 5.4 Angular UI (terminal C)

```bash
cd poc/buildgenix-web
npm install
npm start
# http://127.0.0.1:43182
```

---

## 6. Verification table (fill every row)

| ID | Check | Pass criteria | Result |
|---|---|---|---|
| V1 | `verify-three.sh` | Hello + add + subtract all `Completed`; thought `source=ollama` **or** Gateway model text (not Fixture); add=5; subtract=3 and add still 5 | |
| V2 | Azurite blobs | Under `agenthub-artifacts`: `prd.json`, `techspec.json`, `code-state.json`, `test-results.json`, `completion-summary.json` for each WI. `test-results.json` includes `kind=http-roundtrip` and a `roundTrips` array (request path + response body) when TESTING ran WebApplicationFactory tests | |
| V3 | Azurite tables | `WorkItemEvents`, `Artifacts`, `Approvals`, `ActivityLog`, `EffectLedger` populated | |
| V4 | AuditLog | `/tmp/bgx-poc-audit-azurite/*-AuditLog.json` has original request + transitions through Finalize | |
| V5 | `verify-interactive.sh` | Pauses at PRD, SYSTEM_DESIGN, DEPLOY_DEV; each approve advances; ends Completed; add=5 | |
| V6 | `verify-steering.sh` | At PRD: PAUSE notes; STOP → `Paused` + revert to last approved; CONTINUE message non-null and PRD gate returns; ABORT → `Aborted`; fresh-run early ABORT → `Aborted` | |
| V7 | UI happy path | Chat → Calculator add → Approve PRD (Documents show PRD) → Approve SD → Approve Deploy → Completed banner with URL; Logs tab non-empty | |
| V8 | UI steering | Active run: type PAUSE / STOP / CONTINUE / ABORT (or use steer bar); bubbles show steer messages; ABORT returns to idle chat | |
| V9 | CORS / proxy | Browser calls go through `:43182/api/...` without browser CORS errors | |
| V10 | Non-interference | Root `agenthub-api` / `buildgenix-api` git tree unchanged by this fold-in | |

Stop on the first FAIL. Do not invent alternate APIs.

Commands:

```bash
cd poc
export BGX_URL=http://127.0.0.1:43181
export OUT_DIR=/tmp/bgx-poc-verify
./scripts/verify-three.sh
./scripts/verify-interactive.sh
./scripts/verify-steering.sh
```

---

## 7. Copilot / coding-agent prompt (paste as-is)

```text
You are on the other machine working in https://github.com/badrusiddique/agent-sdlc.

Task: fold in the interactive BuildGenix chat UI POC change (including cooperative steering). Scope is ONLY poc/.

Baseline: main already has the one-agent pipeline POC (Pipeline agent, POST /api/poc/pipeline/run auto-approve, verify-three.sh).

Source: branch cursor/buildgenix-chat-ui-1fa7 (latest tip once on GitHub). If main already contains poc/buildgenix-web and verify-steering.sh, just verify. If not, checkout those paths from the branch onto a fold branch from main:

  poc/README.md
  poc/agenthub-api/src/AgentHub.WebApi/Pipeline/PipelineEndpoints.cs
  poc/buildgenix-api/src/BuildGenix.Data/Entities/WorkItemEntity.cs
  poc/buildgenix-api/src/BuildGenix.WebApi/Poc/
  poc/buildgenix-api/src/BuildGenix.WebApi/Program.cs
  poc/buildgenix-api/src/BuildGenix.WebApi/appsettings.Development.json
  poc/buildgenix-web/
  poc/scripts/verify-interactive.sh
  poc/scripts/verify-steering.sh
  README.md

Do NOT touch root agenthub-api/ or root buildgenix-api/.
Do NOT mix this with the System Design agent spoon-feed.
Preserve POST /api/poc/pipeline/run auto-approve for verify-three.sh.
Interactive path: POST /start, approve at PRD + SYSTEM_DESIGN + post-DEPLOY only, status/events/artifacts/logs, Angular UI on :43182.
Steering: POST /api/poc/pipeline/{workItemId}/steer with PAUSE|STOP|ABORT|CONTINUE (cooperative; STOP reverts to lastApproved; ABORT abandons WI; CONTINUE message must be non-null).

Then run Manual QA with Azurite + real LLM (Gateway on isolated machine, Ollama if local):
1) ./poc/scripts/verify-three.sh
2) ./poc/scripts/verify-interactive.sh
3) ./poc/scripts/verify-steering.sh
4) npm start in poc/buildgenix-web and walk Calculator add with three Approves; optionally exercise PAUSE/STOP/CONTINUE/ABORT in chat
Fill the V1–V10 table in docs/prompts/poc-interactive-chat-ui-other-machine-2026-10-09.md.
Stop on FAIL. Report PASS/FAIL per row with URLs and evidence paths.
```

---

## 8. What was already verified on the cloud machine (do not re-claim without re-running)

On 2026-10-09 against Azurite + Ollama `llama3.2:1b` (branch `cursor/buildgenix-chat-ui-1fa7`, tip `6e123b2`):

| Suite | Result |
|---|---|
| `verify-three.sh` (Hello / add / subtract) | PASS — thought `source=ollama`; add 5; subtract 3 |
| Azurite blobs + tables + AuditLog | PASS |
| `verify-interactive.sh` | PASS — PRD / SYSTEM_DESIGN / DEPLOY_DEV gates; Completed; add=5 |
| `verify-steering.sh` | PASS — PAUSE at gate; STOP → Paused + revert INTAKE; CONTINUE message non-null + PRD returns; ABORT → Aborted; fresh early ABORT → Aborted; ends with `Steering verification OK` |
| Playwright UI (Calculator add + 3 Approves) | PASS (`UI_VERIFY_PASS`) earlier same day |

Evidence notes (cloud store): Project Context `internal/poc-reverify-azurite-2026-10-09.md`, `internal/buildgenix-chat-ui-verify-2026-10-09.md`, `internal/poc-steering-verify-2026-10-09.md`.

The **other machine** must still fill V1–V10 itself (Gateway instead of Ollama if that is the house rule).

---

## 9. What is next after this fold-in (for Badru / planning — not this prompt’s coding work)

1. Push `cursor/buildgenix-chat-ui-1fa7` to GitHub `main` when ready (cloud Cursor origin already has the branch).
2. Other machine: execute this prompt → V1–V10.
3. Keep System Design Part A (`i3-system-design-agent-spoon-feed.md`) on root APIs as a **separate** track; do not merge POC UI patterns into Phase 0 agents until Badru says fold POC → production hosts.
4. Optional later: SignalR instead of polling, real SSO instead of `X-User-Id: dummy-user`, reject/redraft at gates (POC today is approve-only).

---

## 10. Done when

- [ ] Paths from §3 are on the other machine’s branch  
- [ ] V1–V10 filled with PASS  
- [ ] `verify-three.sh` still green  
- [ ] Interactive or UI path shows human gates at PRD, System Design, Deploy  
- [ ] Steering path shows PAUSE / STOP / CONTINUE / ABORT as in §4.5  
- [ ] Root production API trees untouched by this fold-in  

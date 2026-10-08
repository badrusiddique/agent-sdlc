# Six-persona happy-path verification (POC)

**Date:** 2026-10-08  
**Scope:** Pre-phase-0 pipeline POC under `poc/` only. Happy path for three examples against **Azurite (Docker) + Ollama + Docker Compose**.  
**Personas:** SA, PE, QA, DBA, DevOps, PM (same six as the system-design pack gate).

## Evidence summary

| Example | Status | Smoke | Runtime |
|---|---|---|---|
| Hello World (`fe-be-llm`) | Completed → DONE | `/health` ok; `/api/thought` `source=ollama` | Docker Compose + Azurite blobs + Ollama `llama3.2:1b` |
| Calculator add (`fe-be`) | Completed → DONE | `/api/add?a=2&b=3` → `{"result":5}` | Docker Compose + Azurite |
| Calculator subtract (Existing) | Completed → DONE | add still 5; `/api/subtract?a=5&b=2` → `{"result":3}` | Docker Compose + Azurite; same `projectId` |

Raw responses: `poc/blob-storage-snapshot/verify-results/`.  
Blob dump: `poc/blob-storage-snapshot/agenthub-artifacts/`.  
Audit trails: `poc/blob-storage-snapshot/audit-logs/`.

Stage trail on every run:  
`INTAKE → PRD (+gate) → SYSTEM_DESIGN (+gate) → IMPLEMENTATION → TESTING → DEPLOY_DEV Deploy (+gate) → Finalize`.

---

## 1. Solution Architect — APPROVE

- Ownership matches phase-0: AgentHub Pipeline agent writes agent tables / blobs; buildgenix owns WorkItems, Approvals, WorkItemEvents, AuditLog, stage loop.
- Single Healthy-shaped agent walks all six stages (InProcessOnly dispatch); no premature multi-agent split.
- Artifact names match phase-0 contracts (`prd.json`, `techspec.json`, `code-state.json`, `test-results.json`, `completion-summary.json`, …).
- Deploy then Finalize; three auto-approved gates on the happy path.
- **Condition (documented, not blocking POC):** Service Bus remains InProcessOnly; real topics are out of POC scope.

## 2. Principal Engineer — APPROVE

- POC lives under `poc/` copies; root `agenthub-api` / `buildgenix-api` untouched.
- `LocalComposeHost` rewrites compose host ports, waits on `/health`, uses distinct compose project names.
- `ApplyImplementationHints` no longer dumps raw non-JSON LLM text into `ThoughtCopy.txt` (only explicit `thought` / `headline`).
- Ollama used for pipeline stage LLM calls (`POST http://127.0.0.1:11434/api/chat` in AgentHub logs) and for the deployed Hello World thought endpoint (`source=ollama`).
- AI Gateway registered alongside via `Llm:FallbackProvider=Gateway` (`CompositeLlmClient`); smoke + fallback verified; Gateway JSON body no longer empty.
- **Condition:** Happy path only — no fault injection / CrashAt coverage in this POC. Live APIM requires `AIGW_SUBSCRIPTION_KEY` (stub used when key absent).

## 3. Senior QA — APPROVE

- Three golden examples completed with live smoke assertions (see `verify-results/`).
- Hello World thought re-verified after Ollama `0.0.0.0` bind + ThoughtCopy fix.
- AuditLog.json present per work item with original request + transitions.
- Script: `poc/scripts/verify-three.sh` (fixture/file mode); Azurite+Ollama mode documented in `poc/README.md`.
- **Condition:** Automated unit tests of Pipeline agent remain secondary to this Manual E2E actual-resources pass.

## 4. DBA / Data Engineer — APPROVE

- Azurite tables ensured at host start (BG: 10 catalog tables; AH: ActivityLog, EffectLedger, AgProcessedEvents).
- Final row counts captured in `tables-summary/row-counts.json` (WorkItems, WorkItemEvents, Approvals, Artifacts, EffectLedger, ActivityLog all populated).
- Blob paths follow `{projectId}/{workItemId}/v1/…`.
- Single-writer POC assumption holds: agent → AH tables/blobs; orchestrator → BG tables + AuditLog file mirror.
- **Condition:** No retention / soft-delete policy exercised in POC.

## 5. DevOps — APPROVE

- Azurite runs in Docker (`agenthub-azurite` ports 10000–10002).
- Generated apps deploy via `docker compose up -d --build` with free host port mapping.
- Ollama hosted locally (`OLLAMA_HOST=0.0.0.0:11434`) so containers reach it via `host.docker.internal`.
- AgentHub `:43180`, BuildGenix `:43181`; SB emulator health 503 expected (InProcessOnly).
- Snapshot of blob storage checked into git for later reference.
- **Condition:** No App Service / ACA publish in this POC; LocalCompose only.

## 6. Product Manager — APPROVE

- Delivers the three requested demos: Hello World with LLM thought, calculator add, calculator subtract-on-existing.
- Requester-facing outcome is a live URL + Completed status without manual gate clicks (auto-approve happy path).
- POC folder isolates risk from production APIs; lift path is documented in `poc/README.md`.
- **Condition:** UI / chat intake not in this slice — API-driven `POST /api/poc/pipeline/run` only.

---

## Sign-off table

| Persona | Verdict |
|---|---|
| Solution Architect | APPROVE |
| Principal Engineer | APPROVE |
| Senior QA | APPROVE |
| DBA / Data Engineer | APPROVE |
| DevOps | APPROVE |
| Product Manager | APPROVE |

All six approve for the **POC happy path** against Azurite + Ollama + Docker Compose. This is not a blanket Phase-0 implementation GO for production hosts.

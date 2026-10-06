# Manual QA E2E — what Badru means (durable)

**Status:** Binding for this Project when Badru says "manual QA E2E" / "manual e2e" / "E2E manual verification" / "verify like a QA".  
**Date locked:** 2026-10-06 (Badru correction after Fixture-only run; reinforced into 5-persona + preferences).  
**Also enforced by:** [`preferences.md`](../../preferences.md), [`prompts/five-persona-review-gate.md`](../prompts/five-persona-review-gate.md) (Senior QA), [`prompts/qa-runbook-manual-e2e.md`](../prompts/qa-runbook-manual-e2e.md), decision-log **P-MANUAL-E2E-ACTUAL**.

## Definition (no guessing)

| Layer | Required for "manual QA E2E" on **this** (local) machine | Not sufficient |
|---|---|---|
| App process | Real `dotnet run` host(s), real BackgroundService workers | WebApplicationFactory-only |
| Storage tables | **Azurite** (or real Azure Tables) via Azure SDK; rows readable after the run | In-memory fake repos |
| Blobs | **Azurite** (or real Azure Blob); file downloadable after the run | Mock `IArtifactBlobStore` |
| Messaging | **Local Service Bus emulator** (or real Azure SB); real publish **and** subscribe | Fake publishers |
| LLM (local machine) | **Ollama** (`Llm:Provider=Ollama`) with a real model responding | **Fixture** / stub LLM |
| LLM (isolated / Azure machine) | **AI Gateway** only (`Llm:Provider=Gateway`) | Ollama or Fixture as the only proof |
| Logs | Serilog / host logs inspected for D-C1 order, correlation ids, worker start, faults | "tests passed" alone |
| Host survival | Inject or trigger a handler fault; host `/health/live` stays 200 | Assume try/catch works |

## Explicit bans when Badru says manual QA E2E

1. Do **not** treat Fixture LLM as the E2E proof on this machine.
2. Do **not** skip blob/table/SB read-back verification.
3. Do **not** claim cloud Azure was used if only Azurite/emulator ran — say "local Azure emulators + Ollama".
4. Do **not** mark done without AgentHub **and** BuildGenix when both are in scope (run sequentially if ports collide).

## Minimum AgentHub HEALTHY checklist (local)

1. `docker compose down -v && up -d` → SB health `"status":"healthy"`.
2. `Llm:Provider=Ollama`; Ollama up; model pull if needed.
3. API up; `/health` + `/health/live`.
4. Heartbeat ping→pong; D-C1 in logs; CorrelationId present.
5. `POST /api/connectivity/healthy` with known ids.
6. Worker log: run starting, DeliveryCount, LLM via **Ollama**, blob write, result publish, mark, complete.
7. Download blob; confirm content from Ollama (not Fixture string).
8. List Azurite tables = AH only; EffectLedger Committed; AgProcessedEvents has MessageId.
9. Republish same MessageId → dedup no-op; host still live.
10. Fault path: force a bad task / handler error path; host not killed.
11. CorrelationId / MessageId (`{TaskId}:{InputVersion}`) consistent across REST → logs → result.

## Minimum BuildGenix checklist (local)

1. Compose + SB healthy; API up; `/health` Healthy.
2. Heartbeat D-C1.
3. Create project/WI against Azurite; no `~ACTIVE` on create.
4. Summary marker; Execute; D-C5 noop; ActiveConflict; SummaryNotFound.
5. Catalog routes exercised; tables 10/10; logs inspected.

## Agent structure note (Phase 0 HEALTHY)

HEALTHY is a **single** harness agent (handler + dispatcher), not a fan-out of production subagents. Manual E2E still must prove: worker spin-up, handler invoke, LLM call, ledger, blob, result publish, mark, complete, host survival. Full subagent create/terminate matrix applies when a multi-subagent agent (e.g. System Design) is in scope — do not fake that for HEALTHY.

## AI Gateway

Review code + `docs/ai-gateway.md` on this machine; **live** Gateway E2E is on the isolated machine (no Ollama there).

## 5-persona enforcement

| Persona | Duty re: this doc |
|---|---|
| Senior QA | Must BLOCK (or refuse APPROVE) any Manual QA claim that used Fixture-only / mocked storage / fake SB |
| Principal Engineer | Confirm fault isolation + host survival paths exist in code under review |
| DevOps | Confirm compose/emulator/Ollama (or Gateway) runbooks are executable by a newcomer |
| Architect / DBA | Confirm table ownership + blob/path checks are part of the Manual QA evidence, not assumed |

Agents: do **not** ask Badru to restate this definition. Load this file and run it.

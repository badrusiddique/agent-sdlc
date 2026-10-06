# Spoon-feed: integrate agenthub-api (HEALTHY template + AI Gateway + guardrails) on the new machine

**Purpose:** Paste into VS Code Copilot Chat on the isolated / new machine that already has `inno-tech-aisdlc-agenthub-api` boilerplate (Data, heartbeat, Service Bus).  
**Source:** `https://github.com/badrusiddique/agent-sdlc` `main` ≥ `fbb71b1`.  
**LLM on new machine:** AI Gateway only (`Llm:Provider=Gateway`). Discard Ollama there.  
**Manual QA on new machine:** actual Azure/emulator + **Gateway** (never Fixture as Manual QA proof). See `docs/manual-e2e-means-actual.md`.  
**Do not guess.** Verification is mandatory.

---

## 0. What you are integrating

| Include | Exclude |
|---|---|
| AgentHub.Core / Hosting / Infrastructure / Agents.Healthy seams | Production PRD/SystemDesign agents (I3) |
| HEALTHY harness (REST → topic → worker → **guardrails** → LLM → blob → sdlc-results) | BuildGenix controllers |
| `IGuardrailPipeline` / `LocalGuardrailPipeline` (Phase 0 always-on) | GuardrailsAI Python runtime |
| AH tables only: ActivityLog, EffectLedger, AgProcessedEvents | BG tables |
| D-C1 / D-C2 / D-C3 / D-I2-* HEALTHY rules | Docker as deploy target |
| Gateway LLM client + config swap (`max_completion_tokens` for 5.1/5.4) | Hard-coded secrets |

Agent template items that HEALTHY proves (must remain true after merge):

1. Host-agnostic handler library (Core only refs)
2. BackgroundService worker that never kills the host on handler faults
3. Dedup before handler (`AgProcessedEvents`)
4. EffectLedger Add + 409 Committed-only (D-C2)
5. Blob path `{projectId}/{workItemId}/v{n}/{filename}` (D-C3)
6. Publish result **before** mark processed (D-C1)
7. MessageId = `{TaskId}:{InputVersion}` (never invent GUID)
8. `Llm:Provider` swap: Fixture / Ollama / Gateway
9. Per-service heartbeat (D-C8) separate from HEALTHY topics
10. **Guardrails Phase 0:** input/output/artifact checks; Block → Failed **without retry**; verdict + CorrelationId in logs (`docs/plans/agent-guardrails-design.md`)

---

## 1. DO / DON'T

### DO

1. Keep **AH table ownership**. Never create Projects/WorkItems/etc. from AgentHub.
2. Keep project graph: Core → Agents.* ; Hosting → workers; Infrastructure → SB/Blob/LLM; WebApi thin.
3. Wire `Llm:Provider=Gateway` for the new machine; read subscription key from env / Key Vault secret **name** patterns in `docs/ai-gateway.md` (never commit values).
4. Use `DefaultAzureCredential` + ServiceUri for Table/Blob on App Service; connection strings only for local/Azurite.
5. Port emulator-aware `/health` (HTTP emulator health when `UseDevelopmentEmulator=true`).
6. Dead-letter messages with missing MessageId (no random GUID).
7. Port `AgentHub.Core/Guardrails/**` and register `IGuardrailPipeline` → `LocalGuardrailPipeline` in `AddAgentWorkers`.
8. Run Verification: Fixture plumbing first, then **Gateway Manual QA** (actual stack).
9. Keep Gateway body branch: aliases `5.1` / `5.4` → `max_completion_tokens` (never “simplify” to always `max_tokens`).

### DON'T

1. Do **not** call other agents from HEALTHY (agents never call agents).
2. Do **not** use temperature; use `reasoning_effort` / alias rules per AI Gateway docs.
3. Do **not** deploy via Docker to Azure App Service (Docker is local only).
4. Do **not** share BuildGenix heartbeat topics (`bg-*`). AH uses `ah-*`.
5. Do **not** mark processed before publishing AgentResult.
6. Do **not** skip Gateway verification headers (`Ocp-Apim-Subscription-Key`, `X-AIGW-Model`).
7. Do **not** add GuardrailsAI / Python sidecars. Stack = .NET 10 + Azure. Azure Content Safety Prompt Shields is the later cloud plug-in behind `IGuardrailPipeline` (optional; not required for Phase 0 green).
8. Do **not** claim Manual QA PASS on Fixture-only runs.

---

## 2. Files to port from agent-sdlc `agenthub-api/`

| Source | Notes |
|---|---|
| `src/AgentHub.Core/**` | Models + ports + **`Guardrails/`** |
| `src/AgentHub.Agents.Healthy/**` | Handler (guardrail enforce), dispatcher (Block → Failed no retry), prompt, validation |
| `src/AgentHub.Hosting/**` | `HealthyAgentWorker`, `AddAgentWorkers` (+ `IGuardrailPipeline` DI) |
| `src/AgentHub.Infrastructure/**` | LLM (Fixture/Ollama/Gateway), Blob, SB publishers, adapters |
| `src/AgentHub.Data/**` | AH tables only + TableClientFactory credential branch |
| `src/AgentHub.WebApi/Healthy/**`, `Health/**`, `Infrastructure/ApiProblems.cs` | REST + problems with `code` |
| Heartbeat pieces | Merge with existing; keep D-C1 + no throw on missing CS |
| `docker/servicebus-config.json` topics | Ensure `sdlc-tasks-healthy`, `sdlc-results`, `sdlc-events`, `ah-heartbeat-*` exist locally; Azure topics must already/be created to match |
| Tests under `tests/**` | Core goldens, **Guardrails**, Healthy dispatcher (incl. guardrail block), MessageId, heartbeat |

Read: Context `docs/agent-structure-template.md`, `docs/plans/healthy-agent-design.md`, `docs/plans/agent-guardrails-design.md`, `docs/ai-gateway.md`, `docs/manual-e2e-means-actual.md`.

---

## 3. AI Gateway config (new machine)

Set (values via Key Vault / App Settings, not source):

```json
{
  "Llm": {
    "Provider": "Gateway",
    "Gateway": {
      "ChatEndpoint": "https://cc-sb-ts-aigw-apim.azure-api.net/ai/chat/completions",
      "DefaultModelAlias": "<alias from platform list>",
      "SubscriptionKey": ""
    }
  }
}
```

Environment / App Setting for key (example names only; never commit values):

```text
Llm__Gateway__SubscriptionKey = <from Key Vault secret apim-{consumer-id}-subscription-key>
# OR
AIGW_SUBSCRIPTION_KEY = <same value>
```

**Body rule (5.x):** aliases `5.1` / `5.4` must send `max_completion_tokens` (not `max_tokens`). AgentHub `GatewayLlmClient` already branches on this — do not “simplify” the body to always `max_tokens`.

Headers the Gateway client must send (see `docs/ai-gateway.md`):

- `Ocp-Apim-Subscription-Key`
- `X-AIGW-Model`

If Consumer ID header is later required by platform (OPEN-I2-6), add it only when issued. Do not invent.

**Optional later:** Azure AI Content Safety endpoint for Prompt Shields behind `IGuardrailPipeline` (DefaultAzureCredential). Phase 0 ships with local C# validators only; do not block merge on Content Safety if ACS resource is not ready — document as deferred.

---

## 4. Paste-ready Copilot prompt

```text
ROLE: Senior .NET + Azure engineer. Zero assumptions.

GOAL: Integrate agent-sdlc agenthub-api HEALTHY agent template seams + heartbeat hardening + AI Gateway provider + Phase 0 guardrails into THIS inno-tech agenthub-api boilerplate (already has Data + heartbeat + SB).

SOURCE: github.com/badrusiddique/agent-sdlc main ≥ fbb71b1 folders agenthub-api/src/AgentHub.{Core,Hosting,Infrastructure,Agents.Healthy,Data,WebApi} and tests. Also read docs/plans/agent-guardrails-design.md, docs/ai-gateway.md, docs/manual-e2e-means-actual.md.

HARD RULES:
1) Project graph: Core (no SB/ASP.NET) ← Agents.Healthy; Hosting workers; Infrastructure adapters; WebApi thin host.
2) AH tables ONLY: ActivityLog, EffectLedger, AgProcessedEvents.
3) D-C1: publish AgentResult then mark AgProcessedEvents then complete. Same for heartbeat pong.
4) D-C2: EffectLedger Add; on 409 return existing Committed only. TryGetCommitted filters Status==Committed.
5) D-C3 blob: {projectId}/{workItemId}/v{n}/healthy-result.json
6) MessageId = {TaskId}:{InputVersion}. If SB MessageId empty, use body LogicalMessageId; else dead-letter. NEVER Guid.NewGuid() for dedup.
7) Worker faults must not kill the host. Missing ServiceBus CS → log + return (do not throw).
8) /health emulator path when UseDevelopmentEmulator=true; JSON ResponseWriter.
9) ApiProblems with code=ValidationFailed / ServiceUnavailable on HEALTHY + heartbeat REST.
10) Llm:Provider Gateway on this machine (no Ollama). Fixture for unit tests OK — Fixture is NOT Manual QA.
11) DefaultAzureCredential + ServiceUri for Table/Blob when ConnectionString absent.
12) Port IGuardrailPipeline + LocalGuardrailPipeline. HEALTHY: EvaluateInput before LLM; EvaluateOutput before ledger; artifact secret scan before blob. GuardrailBlockedException → Failed + mark + dead-letter WITHOUT abandon/retry. Log every verdict with CorrelationId.
13) Gateway aliases 5.1/5.4 must use max_completion_tokens (not max_tokens). No temperature.
14) Do not implement I3 System Design agent. Do not write BG tables. Do not add GuardrailsAI Python.

WORK STEPS:
A) Port/merge projects into this solution; fix package versions to compile on .NET 10.
B) Ensure docker/servicebus-config (local) and Azure topics include sdlc-tasks-healthy, healthy-tasks sub, sdlc-results, sdlc-events, ah-heartbeat-ping/pong.
C) Wire Gateway options from configuration/env.
D) Port Core/Guardrails + DI in AddAgentWorkers; port guardrail unit tests.
E) Port tests; dotnet test must be green with 0 skips.
F) Run VERIFICATION (Fixture plumbing then Gateway Manual QA).

VERIFICATION:
V1) dotnet test — 0 failed, 0 skipped (includes LocalGuardrailPipelineTests + guardrail dispatcher block test).
V2) Local Docker: compose down -v && up -d; wait for http://127.0.0.1:5300/health "status":"healthy".
V3) Run API; GET /health → Healthy JSON; GET /health/live → 200.
V4) POST /api/connectivity/heartbeat → 202; logs pong before mark.
V5) Fixture plumbing: POST /api/connectivity/healthy → 202; D-C1; blob; ledger (not Manual QA proof).
V6) Guardrail logs on happy path: stage=input/output decision=Allow + CorrelationId.
V7) Dedup republish same MessageId → no-op; host live.
V8) Manual QA Gateway: Llm:Provider=Gateway + AIGW_SUBSCRIPTION_KEY; new taskId; Completed with Gateway model thought OR stop with exact Gateway HTTP body (do not hide). Try alias openai; also smoke 5.1 or 5.4 if platform allows (max_completion_tokens path).
V9) Fault: bad modelId or forced handler fault → host /health/live still 200.
V10) problem+json on invalid healthy body includes code=ValidationFailed.
V11) AH tables only; EffectLedger Committed; AgProcessedEvents MessageId; blob D-C3 path downloadable.

OUTPUT: files changed + verification PASS/FAIL table + Gateway evidence + guardrail log excerpts + STOP questions.
```

---

## 5. Verification table

| ID | Check | Expected | Pass? |
|---|---|---|---|
| V1 | `dotnet test` | 0 failed / 0 skipped | |
| V2 | Emulator healthy | `"status":"healthy"` | |
| V3 | `/health` + `/health/live` | Healthy / 200 | |
| V4 | Heartbeat D-C1 | publish then mark | |
| V5 | HEALTHY Fixture plumbing | 202 + blob/ledger (not Manual QA) | |
| V6 | Guardrail Allow logs | input/output Allow + CorrelationId | |
| V7 | Dedup republish | no-op; live 200 | |
| V8 | **Gateway Manual QA** | Completed or documented blocker with HTTP body | |
| V9 | Fault → host lives | `/health/live` 200 | |
| V10 | Validation problem `code` | `ValidationFailed` | |
| V11 | Storage ownership | AH tables only; D-C3 blob | |

Reference (this cloud machine Ollama Manual QA + guardrails): Context `internal/manual-e2e-ollama/agenthub/`.

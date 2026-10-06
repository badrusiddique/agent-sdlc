# Spoon-feed: integrate agenthub-api (HEALTHY template + AI Gateway) on the new machine

**Purpose:** Paste into VS Code Copilot Chat on the isolated / new machine that already has `inno-tech-aisdlc-agenthub-api` boilerplate (Data, heartbeat, Service Bus).  
**Source:** `https://github.com/badrusiddique/agent-sdlc` `main` ≥ `39673c4`.  
**LLM on new machine:** AI Gateway only (`Llm:Provider=Gateway`). Discard Ollama there.  
**Do not guess.** Verification is mandatory.

---

## 0. What you are integrating

| Include | Exclude |
|---|---|
| AgentHub.Core / Hosting / Infrastructure / Agents.Healthy seams | Production PRD/SystemDesign agents (I3) |
| HEALTHY harness (REST → topic → worker → LLM → blob → sdlc-results) | BuildGenix controllers |
| AH tables only: ActivityLog, EffectLedger, AgProcessedEvents | BG tables |
| D-C1 / D-C2 / D-C3 / D-I2-* HEALTHY rules | Docker as deploy target |
| Gateway LLM client + config swap | Hard-coded secrets |

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

---

## 1. DO / DON'T

### DO

1. Keep **AH table ownership**. Never create Projects/WorkItems/etc. from AgentHub.
2. Keep project graph: Core → Agents.* ; Hosting → workers; Infrastructure → SB/Blob/LLM; WebApi thin.
3. Wire `Llm:Provider=Gateway` for the new machine; read subscription key from env / Key Vault secret **name** patterns in `docs/ai-gateway.md` (never commit values).
4. Use `DefaultAzureCredential` + ServiceUri for Table/Blob on App Service; connection strings only for local/Azurite.
5. Port emulator-aware `/health` (HTTP emulator health when `UseDevelopmentEmulator=true`).
6. Dead-letter messages with missing MessageId (no random GUID).
7. Run Verification with Fixture first, then Gateway.

### DON'T

1. Do **not** call other agents from HEALTHY (agents never call agents).
2. Do **not** use temperature; use `reasoning_effort` / alias rules per AI Gateway docs.
3. Do **not** deploy via Docker to Azure App Service (Docker is local only).
4. Do **not** share BuildGenix heartbeat topics (`bg-*`). AH uses `ah-*`.
5. Do **not** mark processed before publishing AgentResult.
6. Do **not** skip Gateway verification headers (`Ocp-Apim-Subscription-Key`, `X-AIGW-Model`).

---

## 2. Files to port from agent-sdlc `agenthub-api/`

| Source | Notes |
|---|---|
| `src/AgentHub.Core/**` | Models + ports |
| `src/AgentHub.Agents.Healthy/**` | Handler, dispatcher, prompt, validation |
| `src/AgentHub.Hosting/**` | `HealthyAgentWorker`, `AddAgentWorkers` |
| `src/AgentHub.Infrastructure/**` | LLM (Fixture/Ollama/Gateway), Blob, SB publishers, adapters |
| `src/AgentHub.Data/**` | AH tables only + TableClientFactory credential branch |
| `src/AgentHub.WebApi/Healthy/**`, `Health/**`, `Infrastructure/ApiProblems.cs` | REST + problems with `code` |
| Heartbeat pieces | Merge with existing; keep D-C1 + no throw on missing CS |
| `docker/servicebus-config.json` topics | Ensure `sdlc-tasks-healthy`, `sdlc-results`, `sdlc-events`, `ah-heartbeat-*` exist locally; Azure topics must already/be created to match |
| Tests under `tests/**` | Core goldens, Healthy dispatcher, MessageId resolution, heartbeat |

Read: Context `docs/agent-structure-template.md`, `docs/plans/healthy-agent-design.md`, `docs/ai-gateway.md`.

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

---

## 4. Paste-ready Copilot prompt

```text
ROLE: Senior .NET + Azure engineer. Zero assumptions.

GOAL: Integrate agent-sdlc agenthub-api HEALTHY agent template seams + heartbeat hardening + AI Gateway provider into THIS inno-tech agenthub-api boilerplate (already has Data + heartbeat + SB).

SOURCE: github.com/badrusiddique/agent-sdlc main ≥ 39673c4 folders agenthub-api/src/AgentHub.{Core,Hosting,Infrastructure,Agents.Healthy,Data,WebApi} and tests.

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
10) Llm:Provider Gateway on this machine (no Ollama). Fixture for local unit tests OK.
11) DefaultAzureCredential + ServiceUri for Table/Blob when ConnectionString absent.
12) Do not implement I3 System Design agent. Do not write BG tables.

WORK STEPS:
A) Port/merge projects into this solution; fix package versions to compile on .NET 10.
B) Ensure docker/servicebus-config (local) and Azure topics include sdlc-tasks-healthy, healthy-tasks sub, sdlc-results, sdlc-events, ah-heartbeat-ping/pong.
C) Wire Gateway options from configuration/env.
D) Port tests; dotnet test must be green with 0 skips.
E) Run VERIFICATION.

VERIFICATION:
V1) dotnet test — 0 failed, 0 skipped.
V2) Local Docker: compose down -v && up -d; wait for http://127.0.0.1:5300/health "status":"healthy".
V3) Run API; GET /health → Healthy JSON; GET /health/live → 200.
V4) POST /api/connectivity/heartbeat → 202; logs pong before mark.
V5) With Llm:Provider=Fixture: POST /api/connectivity/healthy with fixed projectId/workItemId/taskId/inputVersion → 202.
V6) Worker log DeliveryCount=1; D-C1 order complete; blob exists at D-C3 path; EffectLedger two Committed rows (llm.healthy, artifact.healthy); AgProcessedEvents has MessageId; AH tables only.
V7) Republish same taskId:inputVersion → already processed complete no-op; host still live.
V8) Switch Llm:Provider=Gateway with Key Vault/env subscription key; POST healthy again with new taskId; expect Completed result OR stop with exact Gateway HTTP error body (do not hide). Target: Gateway path works; if Consumer ID missing, report OPEN blocker with response evidence — do not invent headers.
V9) Confirm problem+json on invalid healthy body includes code=ValidationFailed.

OUTPUT: files changed + verification PASS/FAIL table + Gateway evidence + STOP questions.
```

---

## 5. Verification table

| ID | Check | Expected | Pass? |
|---|---|---|---|
| V1 | `dotnet test` | 0 failed / 0 skipped | |
| V2 | Emulator healthy | `"status":"healthy"` | |
| V3 | `/health` | Healthy JSON | |
| V4 | Heartbeat D-C1 | publish then mark | |
| V5 | HEALTHY Fixture publish | 202 | |
| V6 | Blob + ledger + AH tables only | as above | |
| V7 | Dedup republish | no-op; live 200 | |
| V8 | Gateway run | Completed or documented blocker with HTTP body | |
| V9 | Validation problem `code` | `ValidationFailed` | |

Reference: Context `internal/migration-prep-e2e/agenthub/` (Fixture path PASS on `39673c4`).

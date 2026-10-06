# Other machine — detailed proceed instructions

**Audience:** Badru (or Copilot on the isolated machine)  
**Repo tip to use:** `https://github.com/badrusiddique/agent-sdlc` `main` ≥ **`fbb71b1`**  
**Date:** 2026-10-06  

This machine (cloud/dev) already proved AgentHub Manual QA with **Azurite + SB emulator + Ollama + Phase 0 guardrails**. The isolated machine proves the same seams with **AI Gateway** instead of Ollama.

---

## 0. Mental model (read once)

| Concern | This (cloud) machine | Other (isolated) machine |
|---|---|---|
| LLM | Ollama | **AI Gateway only** |
| Manual QA proof | Azurite + SB + Ollama + logs + blob | Emulator or real Azure + **Gateway** + logs + blob |
| Fixture | Unit/plumbing only | Same — never Manual QA |
| Guardrails | `LocalGuardrailPipeline` (C#) | Same C# seam; optional later: Azure Content Safety |
| Deploy | Docker local only | App Service zip (see baby-steps) |

Binding definition: [`manual-e2e-means-actual.md`](./manual-e2e-means-actual.md).  
Guardrails design: [`plans/agent-guardrails-design.md`](./plans/agent-guardrails-design.md).  
Gateway rules: Context/`docs/ai-gateway.md` (also referenced from agenthub README).

---

## 1. Prep on the other machine (before coding)

1. Clone or pull:
   ```bash
   git clone https://github.com/badrusiddique/agent-sdlc.git
   cd agent-sdlc
   git fetch origin main && git checkout main && git pull origin main
   git rev-parse HEAD   # expect ≥ fbb71b1
   ```
2. Confirm you have the inno-tech **agenthub-api** boilerplate (Data + heartbeat + SB) in a separate working tree/repo you will merge into.
3. Get from platform (values stay in Key Vault / env — never commit):
   - APIM subscription key for AI Gateway (`apim-{consumer-id}-subscription-key`)
   - Chat alias list (`openai`, `opus`, `sonnet1`, `5.1`, `5.4`, …)
   - Consumer ID if required (OPEN-I2-6 — do not invent headers)
4. Optional later: Azure AI Content Safety resource + RBAC for Prompt Shields (not a merge blocker for Phase 0).

---

## 2. BuildGenix first (if not already merged)

1. Open the inno-tech **buildgenix-api** boilerplate in VS Code.
2. Open [`buildgenix-api-new-machine-integration.md`](./buildgenix-api-new-machine-integration.md).
3. Paste the **Copilot prompt** from that file into Copilot Chat.
4. Fill the verification table **V1–V13**. Stop on any FAIL; do not invent routes.

---

## 3. AgentHub merge (main work)

1. Open the inno-tech **agenthub-api** boilerplate.
2. Open [`agenthub-api-new-machine-integration.md`](./agenthub-api-new-machine-integration.md) (updated for guardrails + tip `fbb71b1`).
3. Paste the **entire Copilot prompt** from §4 of that file into Copilot Chat.
4. Insist Copilot ports:
   - `AgentHub.Core/Guardrails/**`
   - Healthy handler/dispatcher guardrail paths
   - `AddAgentWorkers` DI for `IGuardrailPipeline`
   - Gateway `max_completion_tokens` branch for `5.1` / `5.4`
5. Run **V1–V11** from the verification table in that doc.
6. For **V8 Manual QA (Gateway)**:
   ```bash
   export AIGW_SUBSCRIPTION_KEY='<from Key Vault — do not commit>'
   # or set Llm__Gateway__SubscriptionKey in App Settings
   # appsettings / env:
   # Llm__Provider=Gateway
   # Llm__Gateway__DefaultModelAlias=openai
   ```
   Publish HEALTHY with a **new** `taskId`. Confirm:
   - Logs: `HEALTHY guardrail stage=input decision=Allow` and `output decision=Allow` with your CorrelationId
   - Blob thought is from Gateway (not Fixture string)
   - D-C1 publish-then-mark
   - Host still live after a forced fault (bad `modelId`)
7. Smoke a `5.1` or `5.4` alias once platform allows — must not 400 on `max_tokens` body mistake.

---

## 4. What “done” looks like on the other machine

| Gate | Done means |
|---|---|
| BG V1–V13 | Table filled PASS |
| AH V1–V11 | Table filled PASS; Gateway Manual QA evidence (logs + blob) attached |
| Secrets | No keys in git |
| I3 | Still **not** started |

If Gateway returns 401/400, paste the **exact HTTP body** into the verification notes and stop. Do not invent Consumer ID headers.

---

## 5. Azure App Service (after local/Gateway green)

Hand [`azure-appservice-deploy-baby-steps.md`](./azure-appservice-deploy-baby-steps.md) to DevOps:

- Zip deploy WebApi (not Docker)
- Always On, single instance (ADR-033)
- App Settings: SB connection / MI, Table/Blob ServiceUri + identity, `Llm__Provider=Gateway`, `AIGW_SUBSCRIPTION_KEY` or Key Vault reference
- Create topics/subscriptions to match `docker/servicebus-config.json` names

---

## 6. UI juniors (parallel track)

Use [`junior-ui-endpoint-wiring.md`](./junior-ui-endpoint-wiring.md). No new endpoints invented.

---

## 7. Explicitly out of scope on this track

- I3 / System Design agent coding
- GuardrailsAI Python install
- Full guardrails catalog items 9–18 / 20 / 22–23 (codegen sandbox, ADO allowlists, cost budgets, red-team suite) — design only until a later GO
- Claiming Manual QA on Fixture

---

## 8. Quick command cheat-sheet (AgentHub local on other machine)

```bash
cd agenthub-api   # or your merged boilerplate path
docker compose down -v && docker compose up -d
# wait until:
curl -s http://127.0.0.1:5300/health   # "status":"healthy"

export ASPNETCORE_ENVIRONMENT=Development
export Llm__Provider=Gateway
export AIGW_SUBSCRIPTION_KEY='…'
dotnet run --project src/AgentHub.WebApi --urls http://127.0.0.1:18766 --no-launch-profile

curl -s http://127.0.0.1:18766/health
curl -s http://127.0.0.1:18766/health/live
curl -s -X POST http://127.0.0.1:18766/api/connectivity/heartbeat
curl -s -X POST http://127.0.0.1:18766/api/connectivity/healthy \
  -H 'Content-Type: application/json' \
  -d '{"projectId":"nm-proj","workItemId":"nm-wi-1","taskId":"nm-task-1","correlationId":"nm-corr-1","inputVersion":1}'
# Watch logs for: guardrail Allow, Gateway completion, D-C1, blob path nm-proj/nm-wi-1/v1/healthy-result.json
```

---

## 9. Evidence already on this (cloud) machine

Do not re-ask for the Ollama Manual QA definition. Evidence paths under Project Context:

- `internal/manual-e2e-ollama/agenthub/REPORT.md`
- `internal/manual-e2e-ollama/agenthub/guardrails-live.log`
- Gate: `docs/design-review-gate-delta-manual-qa-guardrails-2026-10-06.md`

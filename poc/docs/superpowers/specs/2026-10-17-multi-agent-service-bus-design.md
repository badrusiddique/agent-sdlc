# Design: Multi-agent Service Bus (fix 9 Qs)

**Date:** 2026-10-17  
**Branch:** `cursor/multi-agent-sb-topics-4837` → `main-azure`  
**Constraint:** Do **not** change LLM Gateway/Fixture connectivity wiring (`Llm__Provider` stays as-is).

## Goals (map to 9 Qs)

| Q | Goal |
|---|---|
| 1 / 6 | FE+BE happy path + Manual QA matrix still green |
| 2 / 4 | Agents own LLM calls; IA materializes FE/BE from **System Design spoon-feed** (not classifier-only); keep templates as reliable scaffold under Fixture |
| 3 | PRD / SystemDesign / IA / TA each = dedicated `BackgroundService` + **one SB topic** |
| 5 | Richer SD techspec + md consumed by IA/TA |
| 7 | Keep AzDO / App Service dual-mode factories (no remove); local proof still Gitea+Compose+Azurite |
| 8 | State via blobs + tables; **stage advance via SB publish** (no BuildGenix stage loop) |
| 9 | DecisionLog + ActivityLog/EffectLedger continue per stage |

## Topology

```text
BuildGenix (intake REST)
    │  publish TaskMessage
    ▼
 topic agenthub.agent.prd  ──► PrdAgentWorker
    │  on success publish next
    ▼
 topic agenthub.agent.system-design ──► SystemDesignAgentWorker
    │
    ▼
 topic agenthub.agent.ia ──► IaAgentWorker  (handles IA_PLAN|CODEGEN|REVIEW|PUSH_DEPLOY via message.Stage)
    │
    ▼
 topic agenthub.agent.ta ──► TaAgentWorker  (TA_GENERATE|VERIFY|REPORT|MERGE)
    │
    ▼
 topic agenthub.agent.deploy ──► DeployAgentWorker  (DEPLOY_SANDBOX)
    │
    └── each agent: REST POST BuildGenix /api/poc/pipeline/agent-callback (events + result)
```

- **BuildGenix → agents:** Service Bus (or in-memory bus in Development).  
- **AgentHub → BuildGenix:** REST callback only.  
- **Between agents:** Service Bus topics (agent publishes next task).  
- **No orchestrator stage loop** in `PocPipelineOrchestrator` — only intake + wait for `DONE`.

## Local Development bus

When `ServiceBus:ConnectionString` is empty **or** `Workers:UseInMemoryBus=true`:  
`InMemoryAgentBus` (topic → concurrent queue) so Azurite+Fixture E2E works without Azure SB emulator.  
When connection string set and `UseInMemoryBus=false`: real `Azure.Messaging.ServiceBus` processors (same worker classes).

## LLM / FE+BE (Q2/Q4)

- Unchanged DI for `ILlmClient` (Fixture | Gateway).  
- IA_CODEGEN: prefer **techspec APIs / deliveryMode** from SD artifacts; call LLM for plan/review as today; generate FE+BE via builders driven by SD (not free-form invent).  
- Templates remain the reliable scaffold under Fixture; with Gateway, LLM text shapes PRD/SD/plan.

## Out of scope this slice

- Separate OS processes / multiple deployable host projects (BackgroundServices in AgentHub host = agent processes for POC).  
- Live AzDO epic/story create, az-cli deploy (dual-mode code retained; Gate C/D still ClickOps).  
- Changing LLM provider configuration.

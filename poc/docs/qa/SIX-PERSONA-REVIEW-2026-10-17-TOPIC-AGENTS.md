# 6-Persona Review — Multi-agent Service Bus (2026-10-17)

**Verdict (local POC):** **APPROVE** for merge to `main-azure` after 545/545 Manual QA.  
**Cloud adversarial:** still **CONDITIONAL** (no live Gate C/D / real Azure SB / AzDO epic create) — unchanged from prior sign-off.

---

## PM

**Approve.** Happy-path FE+BE matrix still green (Calc, Hello±LLM, Todo, MultiWI, Rewrite, RouteBack). Product gates (PRD / SD / plan / review / sandbox) still surface via callbacks; AutoApprove scripts stay sync. Scope did not invent open-ended “any app” breadth — honest Q1/Q6 gap retained as known limit.

## Architect

**Approve.** Topology matches mandate: one `BackgroundService` + topic per agent; BuildGenix → agents via enqueue/bus; AgentHub → BuildGenix via REST callback; peer chain via topics — **no orchestrator stage loop** when `UseTopicAgents=true`. Legacy `/dispatch` loop kept behind flag. SD spoon-feed enriched (`SystemDesign_v1` APIs/phases/acceptanceChecks); IA prefers techspec `appKind` for Basic scaffolds. LLM DI untouched.

## Backend

**Approve.** `InMemoryAgentTaskBus` + `/enqueue` + `StageProgression` are clear seams; Azure SB path remains option when `UseInMemory=false`. Callback DTO is versioned JSON, not shared assemblies. Dual-mode `IGitPullRequestClient` / `IDeployHost` factories untouched (Q7). DecisionLog + ActivityLog/EffectLedger still written per stage (Q9).

## Frontend

**Approve (POC).** Chat/status/artifacts still driven by BuildGenix session events from callbacks; no FE contract break observed in gate (`bgx_status_api`, Docs blobs). Interactive HITL still raises gates when `AutoApprove=false` and enqueues next on approve.

## DevOps

**Approve (local).** Azurite + Gitea + Compose proof path unchanged. Ports 43180/43181, env overlays for git/deploy retained. Real Service Bus emulator not required for local gate (`Workers:AgentBus:UseInMemory=true`). Live Azure SB / App Service / AzDO still ClickOps Gate C/D — out of this slice.

## QA

**Approve.** Re-ran full production gate on topic path: **8/8 · 545/545 · 0 fail**. RouteBack revises to v2. Unit tests **59/59**. Topic chain visible in AgentHub logs (`chained PRD → SYSTEM_DESIGN → … → DEPLOY_SANDBOX`).

---

## 9 Qs coverage map

| Q | Status |
|---|--------|
| 1 / 6 FE+BE + Manual QA | Green on topic path |
| 2 / 4 Agent-owned LLM + SD-driven IA | LLM unchanged; IA reads techspec spoon-feed |
| 3 Per-agent BackgroundService + topic | PRD/SD/IA/TA/Deploy workers |
| 5 Richer SD spoon-feed | TechSpecBuilder v1 pack + markdown sections |
| 7 AzDO / App Service dual-mode | Factories retained |
| 8 Event-driven handoff | Agents publish next topic; BG waits TopicDone |
| 9 Decision + activity logs | Present on all gate cases |

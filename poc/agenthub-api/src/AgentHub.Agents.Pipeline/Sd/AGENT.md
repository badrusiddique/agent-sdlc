# System Design Agent (Architect) — full behavior

**Code home:** this folder (`TechSpecBuilder.cs`) + stage `RunSystemDesignAsync` in `../PipelineAgentHandler.cs` + host worker `AgentHub.Hosting/Agents/SystemDesignAgentWorker.cs`.  
**Topic:** `agenthub.agent.system-design` · **Subscription:** `system-design-worker` · **AgentName:** `SystemDesign`  
**Stages owned:** `SYSTEM_DESIGN` only  
**Next topic after success:** `agenthub.agent.ia` (`IA_PLAN`)

---

## 0. End-to-end placement

```text
PRD worker (AutoApprove) OR BuildGenix Approve → enqueue SYSTEM_DESIGN
  → IAgentTaskBus.Publish(agenthub.agent.system-design)
  → SystemDesignAgentWorker.HandleAsync
  → PipelineAgentHandler.RunAsync(stage=SYSTEM_DESIGN)
  → REST callback BuildGenix agent-callback (agentName=SystemDesign)
  → Publish IA_PLAN → IaAgentWorker
  → Studio UI: System Design doc + gate + LiveArtifacts techSpec
```

Message artifacts already include PRD paths from prior stage (`artifacts["prd"]`, etc.) via `StageProgression.MergeArtifacts`.

---

## 1. Host worker

Same pattern as PRD:

1. `SystemDesignAgentWorker` → Topic `agenthub.agent.system-design`, Subscription `system-design-worker`, AgentName `SystemDesign`.
2. Log on start: `SystemDesign listening on topic=agenthub.agent.system-design subscription=system-design-worker`.
3. `HandleAsync`: receive → dispatcher → callback → if AutoApprove chain to `IA_PLAN` on `AgentTopics.Ia`.
4. Log: `SystemDesign chained SYSTEM_DESIGN → IA_PLAN topic=agenthub.agent.ia`.

---

## 2. Handler (`RunSystemDesignAsync`)

### Step `sd.draft` — “Draft system design with model”

1. `CallStageLlmAsync(message, intentSeq=300)` — architect persona / pipeline system prompts via `ILlmClient`.
2. `EnsureJson(llm.Text, () => TechSpecBuilder.BuildJson(message))`.

### `TechSpecBuilder.BuildJson` (authoritative spoon-feed for IA/TA)

1. Classify `deliveryMode` (`DeliveryModeClassifier`) and `appKind` (`RequestDrivenCodegen.Classify` from userRequest / stack / projectName).
2. `BuildApis(delivery, kind, userRequest)` — authoritative routes:

| Mode / kind | APIs |
|-------------|------|
| Rewrite | `GET /api/add` |
| FeatureIncrement Subtract | `/api/add`, `/api/subtract` |
| FeatureIncrement Multiply | `/api/add`, `/api/multiply` |
| FeatureIncrement other | `/api/add`, `/api/feature` |
| Basic Calculator | `/api/add`, `/health` |
| Basic Todo | `/api/todos` GET+POST, `/health` |
| Basic HelloLlm | `/api/thought`, `/api/hello`, `/health` |
| Basic Hello / default | `/api/hello`, `/health` |

3. Phases by deliveryMode (ingest/retarget/preserve vs clone/add feature vs scaffold/wire/compose).
4. Serialize techspec `SystemDesign_v1`:
   - `components`: frontend, backend
   - `monoRepo`: folder `project-{pid}-work-item-{wid}`, backend/, frontend/, docker-compose.yml
   - `apis`, `primaryApi` (first api), `phases`
   - `acceptanceChecks` (method/path/expectStatus=200/retired=false)
   - `filePlan`, `deploy.mode=local-compose`, `deploy.smokePath`
   - `stackProfile`, `deliveryMode`, `targetFramework`, `appKind`, `projectMode`
   - `workBranch` = `feature/project-{pid}-work-item-{wid}`

### Step `sd.persistJson`

1. Write `{BlobPrefix}techspec.json` (effect ~400).
2. Named `SYSTEM_DESIGN_V{n}.json`, LATEST pointer.
3. `artifacts["techSpec"] = path`.

### Step `sd.persistMd`

1. `TechSpecBuilder.BuildMarkdown` sections: Delivery, APIs, Phases, Acceptance checks, embedded techspec JSON.
2. Write `{BlobPrefix}system-design.md` as `{ "content": "<md>" }`.
3. `artifacts["techSpecMd"] = path`.

### Decision log

- agent=`SystemDesign`, stage=`SYSTEM_DESIGN`, phase=`persist`
- summary: Published techspec + system-design for IA/TA spoon-feed.
- decisions: Primary API and deliveryMode locked; IA must not invent routes; TA derives live checks from design.

### Spoon-feed API used by IA later

`TechSpecBuilder.TryReadSpoonFeed(techJson, out appKind, out primaryPath)`:

- Reads `appKind` enum and `primaryApi.path` (or first `apis[].path`).
- IA Basic codegen prefers this over classifier-only when `artifacts["techSpec"]` is present.

---

## 3. BuildGenix / UI after callback

1. Merge techSpec / techSpecMd into LiveArtifacts; register Artifacts rows.
2. StageCompleted + audit `agent=SystemDesign`.
3. HITL gate `SYSTEM_DESIGN`: auto-approve record or Studio “approve System Design”.
4. AutoApprove: SD worker already published `IA_PLAN`; interactive: Approve → `EnqueueStageAsync(IA_PLAN)`.

---

## 4. Artifacts produced

| Blob | Notes |
|------|-------|
| `v{n}/techspec.json` | Must include deliveryMode matching PRD |
| `v{n}/system-design.md` | Human-readable |
| `SYSTEM_DESIGN_V{n}.json` | Named copy |
| Decision log entry | agent=SystemDesign |

---

## 5. Integration checklist (other machine)

1. Keep `TechSpecBuilder` spoon-feed fields (`apis`, `primaryApi`, `acceptanceChecks`, `appKind`) — IA/TA depend on them.
2. Topic constant must stay `agenthub.agent.system-design`.
3. Chain must go to `IA_PLAN` via `StageProgression`, not BuildGenix stage loop.
4. Verify: `ONLY_CASES=Calc_Add` → techspec deliveryMode=Basic, appKind=Calculator, primary `/api/add`.

**Related:** `poc/docs/qa/POST-AGENT-INTEGRATION-RUNBOOK.md` §4.2.

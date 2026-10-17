# Product Agent (PRD) — full behavior

**Code home:** this folder (`PrdDocumentBuilder.cs`) + stage `RunPrdAsync` in `../PipelineAgentHandler.cs` + host worker `AgentHub.Hosting/Agents/PrdAgentWorker.cs` (extends `AgentTopicWorkerBase`).  
**Topic:** `agenthub.agent.prd` · **Subscription:** `prd-worker` · **AgentName:** `PRD`  
**Stages owned:** `PRD` only  
**Next topic after success:** `agenthub.agent.system-design` (`SYSTEM_DESIGN`)

This document lists every behavioral step the Product agent performs in the topic-agent path (`Workers__Pipeline__UseTopicAgents=true`). Do not invent shortcuts when integrating on another machine — match this sequence.

---

## 0. End-to-end placement

```text
BuildGenix Studio / POST start
  → PocPipelineOrchestrator.ExecuteViaTopicAgentsAsync
  → POST AgentHub /api/pipeline/enqueue  (stage=PRD only — first enqueue)
  → IAgentTaskBus.Publish(agenthub.agent.prd)
  → PrdAgentWorker.HandleAsync
  → PipelineAgentDispatcher → PipelineAgentHandler.RunAsync(stage=PRD)
  → REST POST BuildGenix /api/poc/pipeline/agent-callback
  → (AutoApprove) Publish SYSTEM_DESIGN → SystemDesign worker
  → BuildGenix ApplyAgentCallbackAsync updates WorkItems / events / LiveArtifacts / Studio UI
```

BuildGenix does **not** loop stages. After PRD enqueue it waits on `session.TopicDone` until Deploy finalizes (or fail/abort).

---

## 1. Host worker startup (`PrdAgentWorker`)

1. Registered as `IHostedService` in `AgentWorkersServiceCollectionExtensions` when topic agents are on (default).
2. `ExecuteAsync`: if `Workers:Pipeline:Enabled=false` → log `{Agent} worker disabled` and exit.
3. Else `IAgentTaskBus.Subscribe(Topic=agenthub.agent.prd, Subscription=prd-worker, HandleAsync)`.
4. Log: `PRD listening on topic=agenthub.agent.prd subscription=prd-worker`.
5. Block until host shutdown; on stop, dispose subscription.

Bus binding (local POC): `Workers__AgentBus__UseInMemory=true` (InMemoryAgentTaskBus). Real Service Bus uses the same topic/subscription names.

---

## 2. Message receive (`AgentTopicWorkerBase.HandleAsync`)

On every PRD message:

1. Log: `PRD received stage={Stage} wi={WorkItemId} msgId={MessageId}`.
2. (TA_GENERATE / IA_PUSH_DEPLOY probes do not apply to PRD.)
3. Serialize `TaskMessage` → `PipelineAgentDispatcher.ProcessAsync`.
4. If dispatcher returns **no result** (abandon / dead-letter):
   - Log warning with kind + reason.
   - Callback BuildGenix with **Failed** terminal (`FailTerminal`) so Studio does not hang.
   - Return (no chain).
5. Else callback BuildGenix with the `AgentResult`.
6. If `result.Status != Completed` → log chain stop; return.
7. If `Context.AutoApprove == false` → log waiting for HITL; return (BuildGenix Approve enqueues next).
8. Else `StageProgression.NextStageAfterSuccess("PRD")` → `SYSTEM_DESIGN`.
9. `BuildNextMessage` (new taskId, inputVersion+1, merge artifacts, clone context).
10. `PublishAsync(AgentTopics.SystemDesign, next)`.
11. Log: `PRD chained PRD → SYSTEM_DESIGN topic=agenthub.agent.system-design`.

### BuildGenix callback body (every success/fail)

`POST {BUILDGENIX_CALLBACK_BASE_URL}/api/poc/pipeline/agent-callback` with:

| Field | Source |
|-------|--------|
| workItemId, projectId, stage, taskId | TaskMessage |
| status, errorMessage, artifacts, outputs, steps | AgentResult |
| outputRevision | message.OutputRevision |
| routeBack | `StageProgression.IsRouteBack(result)` (false for PRD) |
| agentName | `"PRD"` |

Non-2xx / network errors are **logged non-fatal** for the agent chain (BuildGenix may already have UI state).

---

## 3. Handler entry (`PipelineAgentHandler.RunAsync` for `PRD`)

1. `message.EnsureDefaults()` + `PipelineTaskValidation.Validate` (throw if invalid).
2. Ensure `BlobPrefix` = `{projectId}/{workItemId}/v{outputRevision}/`.
3. Create `StageRunContext` with `RunId = taskId:inputVersion:deliveryCount`.
4. Activity / events:
   - Log `PIPELINE PRD started`
   - Publish progress `AgentStarted` / `Stage PRD started` / status `Running`
5. Dispatch `RunPrdAsync`.
6. On success: log `PIPELINE PRD completed`, publish `AgentCompleted`, return `AgentResult` Status=`Completed` with artifacts, steps, tokenUsage, durationMs.

---

## 4. Stage body (`RunPrdAsync`) — line-by-line behavior

### Step `prd.draft` — “Draft PRD with model”

1. `BeginStepAsync` → TaskStarted event / ActivityLog.
2. `CallStageLlmAsync(message, intentSeq=100)`:
   - Loads pipeline system + product persona prompts.
   - Calls `ILlmClient` (`Llm__Provider=Fixture` locally; Azure Gateway unchanged when configured).
   - Returns model text + token counts.
3. `EnsureJson(llm.Text, () => PrdDocumentBuilder.BuildJson(message))`:
   - If LLM text is valid JSON → use it.
   - Else fallback builder (below).
4. `CompleteStepAsync` — “PRD draft ready”.

### `PrdDocumentBuilder.BuildJson` (fallback / Fixture path)

1. `DeliveryModeClassifier.Classify(userRequest, projectMode, baselineArtifacts)` → `Basic` | `Rewrite` | `FeatureIncrement`.
2. Stories array by mode:
   - **Rewrite:** S1 greenfield rewrite to net10; acceptance includes baseline path, net10.0, preserved APIs, graphify code-state.
   - **FeatureIncrement:** S1 feature on existing project; prior workspace reused; new route + prior behaviour.
   - **Basic:** S1 primary behaviour; happy path + request-driven scaffold.
3. Serialize JSON object:
   - `title` = ProjectName or `"App"`
   - `summary` = UserRequest
   - `stories`, `stackProfile`, `projectMode`, `deliveryMode`
   - `targetFramework` = `GreenfieldRewriteBuilder.TargetFramework` (net10.0)
   - `baselineSourcePath` from baseline artifacts when present

### Step `prd.persistJson` — “Persist prd.json”

1. Path = `{BlobPrefix}prd.json`.
2. `WriteArtifactAsync` (effect intent ~200):
   - Writes revision blob via `IArtifactBlobStore` (Azurite when `BlobStorage__Provider=Azure`).
   - `VersionedArtifactWriter`: also named copy `PRD_V{n}.json`, updates `LATEST.json` + state-context pointer.
   - EffectLedger commit for artifact write.
3. `artifacts["prd"] = path`.
4. Complete step “Wrote prd.json”.

### Step `prd.persistMd` — “Persist PRD markdown”

1. `PrdDocumentBuilder.BuildMarkdown` → `# {ProjectName}\n\n{UserRequest}\n\n```json\n{prdJson}\n````.
2. Path = `{BlobPrefix}prd.md` stored as JSON `{ "content": "<md>" }`.
3. Named stem when applicable (`PRD_MD_V{n}.json`).
4. `artifacts["prdMd"] = path`.

### Decision log

`AppendDecisionAsync`:

- agent=`PRD`, stage=`PRD`, phase=`persist`
- summary: `Drafted PRD for '{ProjectName}' from user request.`
- decisions:
  - Product owns acceptance criteria; Implementation must not invent product behaviour.
  - deliveryMode recorded for downstream IA/TA smoke selection.
- Appends/merges `decision-log.json` + named `DECISION_LOG` / `DECISION_LOG_V{n}` under the WI prefix.

### Return

`TokenUsage` from the LLM call (Fixture often zeros).

---

## 5. What BuildGenix does on PRD callback (`ApplyAgentCallbackAsync`)

1. Load in-memory `PipelineSession` by workItemId (unknown → 500).
2. Update CurrentStage/TaskId; bump `ArtifactRevision` if callback revision higher; refresh `BlobPrefix`.
3. Merge `body.Artifacts` into `session.LiveArtifacts`; `RegisterArtifactAsync` each key (Artifacts table + Studio Docs).
4. Append WorkItemEvents for each step (`TaskStarted` / `TaskCompleted`).
5. If status ≠ Completed → Fail WI, audit, `TopicDone` exception → Studio shows Failed.
6. Else StageCompleted event + audit transition `agent=PRD`.
7. AutoApprove HITL gate stage `PRD`:
   - RecordApproval (Approvals / ApprovalGates tables).
   - Agents already chained SB → SystemDesign; BuildGenix does **not** enqueue next.
8. Interactive (`AutoApprove=false`): RaiseTopicGate → Status=`AwaitingApproval` → Studio shows approve chip; on Approve, BuildGenix `EnqueueStageAsync(SYSTEM_DESIGN)`.

Studio UI polls session / events → Docs chips for PRD, approval UI, activity trail.

---

## 6. Artifacts & tables produced by PRD

| Surface | Content |
|---------|---------|
| Blob | `v{n}/prd.json`, `v{n}/prd.md`, `PRD_V{n}.json`, `PRD_MD_V{n}.json`, `DECISION_LOG*.json`, `LATEST.json` |
| ActivityLog | Stage start/complete, step messages |
| EffectLedger | artifact write intents |
| WorkItems | CurrentStage=PRD then progresses; Status Running / AwaitingApproval |
| WorkItemEvents | StageStarted (on enqueue), Task*, StageCompleted, GateRaised/Approved |
| Approvals | Auto or human for PRD gate |
| Studio | PRD document available; LiveArtifacts map |

---

## 7. Files to touch when integrating PRD on another machine

| Path | Role |
|------|------|
| `Prd/PrdDocumentBuilder.cs` | Fallback JSON/MD |
| `PipelineAgentHandler.RunPrdAsync` | Stage orchestration |
| `Hosting/Agents/PrdAgentWorker.cs` | Topic binding |
| `Hosting/Agents/AgentTopicWorkerBase.cs` | Subscribe → dispatch → callback → chain |
| `Core/Messaging/AgentTopics.cs` | Topic constants |
| `StageProgression.cs` | PRD → SYSTEM_DESIGN |
| `WebApi/Pipeline/PipelineEndpoints.cs` | `/enqueue` publishes to topic |
| BuildGenix `PocPipelineOrchestrator` | First enqueue + callback + HITL |

**Do not** change LLM connectivity wiring to make Fixture work; set `Llm__Provider=Fixture` via env only.

---

## 8. Minimal verification after PRD-only integration

```bash
ONLY_CASES=Calc_Add bash poc/scripts/manual-qa-production-gate.sh
```

Assert: AgentHub log `PRD listening` + `PRD received` + `chained PRD → SYSTEM_DESIGN`; Azurite `prd.json` + decision agent=PRD; BuildGenix StageCompleted PRD; Studio Docs PRD (if UI up). Full matrix required after full Pipeline integration — see `poc/docs/qa/POST-AGENT-INTEGRATION-RUNBOOK.md`.

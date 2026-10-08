# Spoon-feed: implement the System Design agent in agenthub-api

**Purpose:** Paste this file into the coding agent on the other machine. Execute **Part A** only. Sections 0 to 14 are the later full System Design agent. Do not implement them in this pass.

**Repo:** `https://github.com/badrusiddique/agent-sdlc` only. Never `ai-sdlc`. Work in the root `agenthub-api/` and `buildgenix-api/`. Do not edit `poc/`. That folder is a separate happy-path proof and is not this agent.

**Date:** 2026-10-08

**This pass:** Badru asked for the first piece of the System Design agent, described in Part A. That piece is the work to do. The rest of this file stays for a later pass.

**Authority for this pass:** Part A below. It already contains the Phase 0 rules this piece needs. Do not search for another architecture document. Sections 0 to 14 are a later pass.

**Manual QA:** Azurite, the Service Bus emulator, and a real model. A Fixture run is not a Manual QA pass.

---

## Part A. First piece: one design file, one revise loop

Do this. Stop when the done list at the end of Part A is true. Do not build DesignValidator, section-by-section writing, templates, `techspec.json`, or the three full examples. Those are sections 0 to 14.

### A1. What "done" is

A person sends one System Design task. A new worker in agenthub-api calls the model once with a fixed prompt, writes `system-design.md` into blob storage, and publishes one result. buildgenix-api sets the work item to `AwaitingApproval`. If the person asks for changes, buildgenix-api sends a new task and the agent writes a new version file. The previous file stays. If the person approves, buildgenix-api marks the design gate approved and does not call the agent again. This pass does not start Implementation, Testing, or Deployment.

### A1b. Phase 0 rules this piece must follow

These are locked. Do not invent replacements.

1. buildgenix-api is the only orchestrator. It publishes the task. The agent never calls another agent, never publishes `agenthub.implementation.agent` or any other task topic, and never reads or writes a buildgenix table (D-AS-2, D-AS-4).
2. The agent reports by publishing one `AgentResult` and progress `AgentEvent` messages. buildgenix-api is the only writer of work-item state.
3. The result goes on a Service Bus topic, not as the agent's HTTP call. If buildgenix-api is restarting when the model call finishes, a REST call is lost. The topic keeps the result until buildgenix-api is back. `POST /api/internal/sdlc-results` is the same apply function, for a manual curl only. The agent does not call it.
4. `AgentResult.Status` is only `Completed`, `HumanActionRequired`, or `Failed`. There is no fourth status. Cancel is `Failed` with `errorCode` `Cancelled`.
5. Failed `errorCode` is only one of: `MissingInput`, `InvalidInput`, `GuardrailBlocked`, `BuildEnvironment`, `ExternalService`, `DeadlineExceeded`, `Cancelled`, `Unhandled`.
6. Task Service Bus `MessageId` is `{taskId}:{inputVersion}`. Result `MessageId` is `result:{taskId}:{inputVersion}`. Event `MessageId` is `{taskId}:{inputVersion}:{deliveryCount}:{seq}`. `runId` is `{taskId}:{inputVersion}:{deliveryCount}`. `stateContextId` is `{projectId}:{workItemId}`.
7. `taskId` changes when the stage is redrafted. `inputVersion` rises on every human input (approve, reject, answer). A redelivery keeps both. `outputRevision` is the blob folder number. Do not copy it from `inputVersion`.
8. Blob container is `artifacts`. Path is `{projectId}/{workItemId}/v{outputRevision}/system-design.md`. The agent writes only under `blobPrefix` from the message. It does not compute that prefix. The one path it may compute is the cancel marker `{projectId}/{workItemId}/control/cancelled.json` in the same container.
9. Files are write-once. A redraft writes a new folder. It does not overwrite `v1`.
10. EffectLedger is write-once `Add`. HTTP 409 means the row is already committed. No replace. No failed ledger rows. Row key is `{taskId}_{intentKind}_{ordinal:D6}`. Partition key is `workItemId`. This slice uses intent `LLM` ordinal `110` for the model call and intent `WriteArtifact` ordinal `201` for `system-design.md`.
11. Crash order: if `AgProcessedEvents` already has the task `MessageId`, complete and do nothing. Otherwise run the handler, publish the result, publish the terminal event, mark `AgProcessedEvents`, then complete the Service Bus message. Never mark before the result is published.
12. Worker settings for this agent: lock renewal `01:00:00`, handler deadline `00:48:00` (60 minutes minus 12, using the rule min(cap minus 10 minutes, 80 percent of the cap)). `MaxDeliveryCount` 5. `MaxConcurrentCalls` 1. `PrefetchCount` 0. `AutoCompleteMessages` false. Subscription `LockDuration` `PT5M`. `DeadLetteringOnMessageExpiration` true. On the deadline publish `Failed` / `DeadlineExceeded` and complete. Do not abandon. On the 5th delivery of an unknown exception publish `Failed` / `Unhandled` and complete. Earlier deliveries abandon so Service Bus retries.
13. No temperature. Model id and reasoning effort come from `config` on the message. Do not hard-code `opus`. Local proof uses `Llm:Provider=Ollama`. Azure uses `Llm:Provider=Gateway` with header `Ocp-Apim-Subscription-Key` and `X-AIGW-Model`, the same client HEALTHY already has. If the Gateway key is missing, the Ollama run can still pass and the Gateway run is recorded BLOCKED. Do not invent a consumer id.
14. Design stage token cap in the full design is 500000. This first piece stops the one call if `config.budget.stageMaxTokens` is present and the call would exceed it, and returns `HumanActionRequired` with `signal.kind` `BudgetExceeded`. If the field is absent, still make the one call.
15. A human may reject this stage 3 times. The 4th reject is not sent to the agent. buildgenix-api sets `HumanActionRequired`. Count rejects per stage. A test route-back is not this counter. This piece has no test route-back.
16. Today's `WorkItemStatuses` in `buildgenix-api` are `Queued`, `AwaitingApproval`, `HumanActionRequired`, `Failed`, `Completed`. There is no `Running` constant yet. Add `Running` next to those constants. `AgentStarted` may set `Running` only when the current status is `Queued`. A result may be applied only when the status is `Queued` or `Running`. Apply with the row ETag. Set `CurrentStage` to `SYSTEM_DESIGN` while this task is current. Store `CurrentTaskId` and `CurrentInputVersion` from the task so a late result for an older task is ignored.

### A1c. Messages, copied here so you do not need another document

Task published by buildgenix-api (or by the Development harness) to `agenthub.system-design.agent`. Service Bus `MessageId` equals `taskId` + `:` + `inputVersion`.

```json
{
  "contractVersion": "2.0",
  "workItemId": "wi-sd-1",
  "projectId": "proj-sd-1",
  "stage": "SYSTEM_DESIGN",
  "taskId": "task-sd-1",
  "correlationId": "corr-sd-1",
  "causationId": "harness",
  "inputVersion": 1,
  "pipelineVersion": 1,
  "loopIndex": 0,
  "outputRevision": 1,
  "blobPrefix": "proj-sd-1/wi-sd-1/v1/",
  "stateContextId": "proj-sd-1:wi-sd-1",
  "artifacts": {},
  "humanAnswer": null,
  "config": {
    "modelId": null,
    "reasoningEffort": "low",
    "budget": { "stageMaxTokens": 500000, "workItemRemainingTokens": 3000000 },
    "limits": { "maxRevisions": 3, "maxFixAttempts": 3 }
  },
  "requestText": "Design a calculator page with one add endpoint."
}
```

`requestText` is the only extra field for this first piece. Put it on `TaskMessage` as an optional string so old HEALTHY JSON still deserializes. The user prompt is `requestText`, plus `humanAnswer` when a question was answered, plus the text of `artifacts.feedback` when this is a redraft. Do not require `prd` or `intakeSummary` in this piece. Those files arrive in the later pass.

Result the agent publishes to `buildgenix.agent-results`. Service Bus `MessageId` is `result:{taskId}:{inputVersion}`.

```json
{
  "contractVersion": "2.0",
  "workItemId": "wi-sd-1",
  "projectId": "proj-sd-1",
  "taskId": "task-sd-1",
  "stage": "SYSTEM_DESIGN",
  "status": "Completed",
  "correlationId": "corr-sd-1",
  "inputVersion": 1,
  "outputRevision": 1,
  "runId": "task-sd-1:1:1",
  "stateContextId": "proj-sd-1:wi-sd-1",
  "artifacts": {
    "techSpecMd": "proj-sd-1/wi-sd-1/v1/system-design.md"
  },
  "outputs": {},
  "signal": null,
  "humanMessage": null,
  "errorCode": null,
  "errorMessage": null,
  "tokenUsage": { "input": 0, "output": 0, "modelId": "llama3.2" },
  "durationMs": 1
}
```

`tokenUsage` comes from the model client. Ollama fields are `prompt_eval_count` and `eval_count`. If the client still returns 0, write 0. Do not invent a count.

Progress event on `buildgenix.agent-events`. This piece publishes `AgentStarted`, then `SubStepCompleted` for `write`, then one terminal kind (`AgentCompleted`, `AgentFailed`, or `AgentHumanActionRequired`).

```json
{
  "contractVersion": "2.0",
  "eventId": "task-sd-1:1:1:1",
  "workItemId": "wi-sd-1",
  "taskId": "task-sd-1",
  "stage": "SYSTEM_DESIGN",
  "runId": "task-sd-1:1:1",
  "kind": "AgentStarted",
  "subStep": null,
  "progress": null,
  "tokens": null,
  "message": "System Design started",
  "level": "info",
  "occurredAt": "2026-10-08T00:00:00Z"
}
```

What buildgenix-api does with a System Design result in this piece:

| Result | What to write |
|---|---|
| `Completed` | Save `artifacts.techSpecMd`. Set `Status` to `AwaitingApproval`. Set `CurrentStage` to `SYSTEM_DESIGN`. Do not approve. Do not publish another task. |
| `Failed` | Set `Status` to `Failed`. Save `errorCode` and `errorMessage` on `LastError`. |
| `HumanActionRequired` | Set `Status` to `HumanActionRequired`. Save `humanMessage`. |

`AgentStarted` sets `Status` to `Running` only from `Queued`.

A result whose `taskId` and `inputVersion` do not equal `CurrentTaskId` and `CurrentInputVersion` is stored in the processed-events table and does not move the work item.

### A2. Shape

Copy the HEALTHY agent. Same host process. New worker. Not a second operating-system process.

| Item | Value |
|---|---|
| Library | `agenthub-api/src/AgentHub.Agents.SystemDesign/` (references Core only, same as `AgentHub.Agents.Healthy`) |
| Worker | `SystemDesignAgentWorker` registered in `AddAgentWorkers`. `Workers:SystemDesign:Enabled` defaults to false |
| Topic | `agenthub.system-design.agent` |
| Subscription | `agenthub-system-design` |
| Stage | `SYSTEM_DESIGN` |
| Prompt | `Prompts/system-design-system.txt`. Fixed text is enough. Example: "Write a short system design in markdown for the request in the user message. Use headings and bullets." |
| Model | `Llm:Provider`. Local Manual QA uses `Ollama`. Azure uses `Gateway` when `AIGW_SUBSCRIPTION_KEY` (or the existing Gateway setting) is present. Do not hard-code a model. If Gateway is not configured, record Gateway as BLOCKED and still pass the Ollama run. Open item 9 stays open |
| Artifact | `{BlobPrefix}system-design.md`. The filename is `system-design.md`, not `System_Design.md` and not `techspec.md`. `BlobPrefix` is `{projectId}/{workItemId}/v{outputRevision}/` and comes from the message. Do not derive `v{n}` from `InputVersion` |
| Result artifact key | `techSpecMd` |
| Result topic | `buildgenix.agent-results`. Result Service Bus `MessageId` is `result:{taskId}:{inputVersion}` |
| Events topic | `buildgenix.agent-events`. Publish `AgentStarted` when the run starts, one `SubStepCompleted` when the file is written, and `AgentCompleted` or `AgentFailed` or `AgentHumanActionRequired` at the end |
| Tables the agent may write | `EffectLedger`, `ActivityLog`, `AgProcessedEvents` only |

Copy these HEALTHY files and rename them. Do not change HEALTHY behaviour:

- `HealthyAgentHandler.cs`, `HealthyAgentDispatcher.cs`, `HealthyAgentConstants.cs`, `HealthyTaskValidation.cs`, `PromptLoader.cs`
- `HealthyAgentWorker.cs` and the DI block in `AgentWorkersServiceCollectionExtensions.cs`
- `HealthyEndpoints.cs` becomes `POST /api/connectivity/system-design`, mapped only when the environment is `Development`
- Add the topic and subscription to `agenthub-api/docker/servicebus-config.json`. Also add `buildgenix.agent-results` / `buildgenix-api` and `buildgenix.agent-events` / `buildgenix-api` if they are not there. Recreate the emulator container after the edit. Do not use `docker compose down -v`

Handler steps, in order. This is the whole agent. One model call. No sub-agent loop.

1. Deserialize. Malformed JSON: dead-letter, no result.
2. `Stage` must be `SYSTEM_DESIGN`. `BlobPrefix` must equal `{projectId}/{workItemId}/v{outputRevision}/`. `requestText` must be non-empty on the first version. A redraft (`outputRevision` greater than 1) must include `artifacts.feedback` or a non-empty `requestText`. Anything else: publish `Failed` / `InvalidInput`, terminal event, mark, complete.
3. Dedup on the task `MessageId`. If `AgProcessedEvents` already has it, complete and stop.
4. If `{projectId}/{workItemId}/control/cancelled.json` exists in container `artifacts`, publish `Failed` / `Cancelled`, mark, complete. Do not call the model.
5. Publish `AgentStarted`.
6. Call the model once. System prompt is the fixed file. User prompt is `requestText`, then `humanAnswer` if set, then the feedback file text if `artifacts.feedback` is set. Read that blob from container `artifacts`. Ledger intent `LLM` ordinal `110` before you would call again. A replay returns the stored text and does not call the model.
7. Guardrail the user prompt and the markdown before the blob write. Use a System Design `LocalGuardrailPipeline` constructed with max input 400000, max output 200000, and headings allowed. Do not change the HEALTHY singleton (8000 / 4000, headings blocked). A block throws, the dispatcher publishes `Failed` / `GuardrailBlocked`, marks, and completes. No second model call.
8. Write `{blobPrefix}system-design.md` with create-if-absent (`If-None-Match: *`). Content type `text/markdown`. Ledger intent `WriteArtifact` ordinal `201`. If the blob already exists and the ledger row exists, treat it as success. Do not overwrite.
9. Publish the result JSON from section A1c with `status` `Completed` and `artifacts.techSpecMd` set to that path. Then publish `AgentCompleted`. Then mark `AgProcessedEvents`. Then complete the Service Bus message.

Leave HEALTHY's result publisher alone. It uses a different `MessageId`. Add a System Design publisher whose `MessageId` is `result:{taskId}:{inputVersion}`. Create the Service Bus client on first publish, not in the constructor, so the host still starts when `Enabled` is false.

Harness `POST /api/connectivity/system-design`, mapped only when `ASPNETCORE_ENVIRONMENT` is `Development`. Body: `projectId`, `workItemId`, `taskId`, `inputVersion`, `outputRevision`, `correlationId`, `requestText`, optional `feedbackPath` and `humanAnswer`. The endpoint sets `blobPrefix` to `{projectId}/{workItemId}/v{outputRevision}/` itself. It publishes to `agenthub.system-design.agent` with `MessageId` `{taskId}:{inputVersion}`. Return 202. Return 400 on validation failure. Return 503 with no exception text if send fails. A Production host returns 404 for this route.

### A3. How buildgenix-api learns the state

The agent does not write buildgenix tables and does not call buildgenix-api as its only report. It publishes the result on `buildgenix.agent-results`. That topic holds the result if buildgenix-api is restarting.

`POST /api/internal/sdlc-results` in `InternalNotifyController` is still a stub that returns not-implemented. Replace that stub with one apply method, and call the same method from a Service Bus listener on `buildgenix.agent-results` / `buildgenix-api`. The HTTP route stays as a manual test door onto that method. It is not the path the agent uses.

The apply method, for `stage` = `SYSTEM_DESIGN` only. Use the statuses in section A1b item 16. Dedup key is the result `MessageId` (`result:{taskId}:{inputVersion}`), stored the same way buildgenix already stores processed messages (`SgProcessedEvents` or the table that repository already has). A repeat returns 200 and does not write the work item again.

- `Completed`: save `artifacts.techSpecMd` on the work item (add a nullable `TechSpecMdPath` column if none exists). Set `Status` to `AwaitingApproval`. Set `CurrentStage` to `SYSTEM_DESIGN`. Do not set `Completed`. Do not publish another agent.
- `Failed`: set `Status` to `Failed` and `LastError` to `errorCode` plus `errorMessage`.
- `HumanActionRequired`: set `Status` to `HumanActionRequired` and `HumanMessage` from the result.
- Any other stage: log and return 200. Do not move the work item.

Also listen to `buildgenix.agent-events` / `buildgenix-api`. `AgentStarted` sets `Running` only from `Queued`. The terminal event does not move the status by itself if the result was already applied. Forward only: do not move `AwaitingApproval` back to `Running` because an old event arrives late. Do not add a browser API.

### A4. Changes and approval

Both decisions are buildgenix-api actions. The agent does not decide them.

**Changes.** Allowed only when `Status` is `AwaitingApproval` and `CurrentStage` is `SYSTEM_DESIGN`. Body is `{ "feedback": "make the add endpoint return decimals" }`. buildgenix-api writes container `artifacts` blob `{projectId}/{workItemId}/v{next}/feedback-system-design.json` where `next` is the previous `outputRevision` plus 1. It creates a new `taskId`, sets `inputVersion` to the current value plus 1, sets `outputRevision` to `next`, sets `pipelineVersion` to the current value plus 1, sets `blobPrefix` to `{projectId}/{workItemId}/v{next}/`, sets `artifacts.feedback` to that blob path, copies `requestText` from the previous task, sets `Status` back to `Queued`, and publishes to `agenthub.system-design.agent` with `MessageId` `{newTaskId}:{newInputVersion}`. The agent writes `v{next}/system-design.md`. It must not change the previous file. After 3 rejects, do not publish. Set `Status` to `HumanActionRequired` and `HumanMessage` to the limit text.

**Approve.** Allowed only from `AwaitingApproval`. Set the design gate approved if `GatesController` already has a decision route. Use that route with stage `SYSTEM_DESIGN`. If it does not know `SYSTEM_DESIGN` yet, add that one stage to the existing decision action. Set the work item so the screen can show the design as approved. Do not set the whole pipeline `Completed` unless that is what the existing approve action already does for a final stage. Do not publish `agenthub.implementation.agent`. Do not call the model.

Route names, if you add them: `POST /api/work-items/{workItemId}/system-design/changes` and `POST /api/work-items/{workItemId}/system-design/approve`. Both check the work item ETag. A wrong status returns 409.

### A5. Prove it

1. `dotnet test` on `agenthub-api/AgentHub.slnx` and `buildgenix-api` still passes, including HEALTHY.
2. Start Azurite and the Service Bus emulator the way `agenthub-api/README.md` describes.
3. Enable the worker with `Workers__SystemDesign__Enabled=true` and `Llm__Provider=Ollama`. Do not commit `Enabled=true`.
4. POST the harness with a one-line request. Expect 202.
5. Download `system-design.md`. The path matches `blobPrefix`. The result is on `buildgenix.agent-results`.
6. The work item `Status` is `AwaitingApproval` and `CurrentStage` is `SYSTEM_DESIGN`. It is not `Completed`.
7. POST the changes route with one sentence of feedback. A new blob `v2/system-design.md` appears. `v1/system-design.md` is byte-for-byte unchanged. The new task has a new `taskId` and `outputRevision` 2.
8. POST the approve route. The design is approved. The model is not called. No message is published to `agenthub.implementation.agent`.
9. Publish the same `taskId` and `inputVersion` again. The run is a no-op.
10. Write the evidence in `docs/qa/manual-qa-system-design-first-slice.md` with the commands and the blob paths. If Gateway was configured, record that run too. If not, write BLOCKED for Gateway and PASS for Ollama.

### A6. Stop

Do not continue into section 0 or anything after it. Report the evidence path and stop.

---

## 0. Later work: the full System Design agent

Do not implement this section, or anything after it, in the first pass. Part A is the pass. This section is the target for the next pass.

| Include | Exclude |
|---|---|
| Library `agenthub-api/src/AgentHub.Agents.SystemDesign` (references Core only) | Any other pipeline agent |
| The Core and Infrastructure seams in section 4 | A rewrite of HEALTHY behaviour or its tests |
| A core-host worker, default **disabled** | The builder container, Graphify, ADO, Azure deploy |
| Harness routes under `/api/connectivity/system-design`, mapped only when `ASPNETCORE_ENVIRONMENT` is `Development` | Production task publish (that is buildgenix-api) |
| Unit tests plus Ollama Manual QA for the three guide examples | Claiming Phase 0 is done |

The agent consumes one `TaskMessage` with `stage = SYSTEM_DESIGN`, reads blobs by the paths in the message, runs the sub-steps in section 5, writes `techspec.json` and `system-design.md` under `BlobPrefix`, and publishes one `AgentResult`. It never publishes the next stage.

---

## 1. DO / DON'T

### DO

1. Copy the HEALTHY shape: handler library, dispatcher, constants, `BackgroundService` worker, DI in `AddAgentWorkers`.
2. Keep this order: structural checks, dedup, cancel check, `AgentStarted`, blob validation, handler, publish `AgentResult`, publish the terminal event, mark `AgProcessedEvents`, then **complete** the Service Bus message. After a result is published, never abandon and never dead-letter. Dead-letter only when no result was published (malformed JSON, or a body that fails `TaskMessage.Validate` or has the wrong stage).
3. Keep D-C2: EffectLedger `Add` only. HTTP 409 means the row is already Committed. No Replace. No Failed ledger rows.
4. Keep D-AS-2, D-AS-3, D-AS-4: no agent calls, three statuses only, AH tables only (`EffectLedger`, `ActivityLog`, `AgProcessedEvents`) plus blobs.
5. Task `MessageId` = `{taskId}:{inputVersion}` (D-I2-4). Result `MessageId` = `result:{taskId}:{inputVersion}` (guide section 3.4). Event `MessageId` = `{taskId}:{inputVersion}:{deliveryCount}:{seq}` (guide section 3.5). Leave HEALTHY's publisher, which uses `{runId}:result`, unchanged.
6. `Llm:Provider` is `Fixture` in committed settings, `Ollama` for local Manual QA, `Gateway` only when the consumer id exists. No temperature. Model and reasoning effort come from `Config`.
7. Run guardrails on every LLM input and both artifact bodies before any ledger commit or blob write. A block is `Failed` / `GuardrailBlocked`, then mark and complete. No repair, no abandon.
8. Leave `Workers:SystemDesign:Enabled` false in every committed settings file.

### DON'T

1. Do not derive a blob path. `BlobPrefix` must equal `{ProjectId}/{WorkItemId}/v{OutputRevision}/`.
2. Do not set `v{n}` from `InputVersion`.
3. Do not write `techspec.md`. The markdown file is `system-design.md`.
4. Do not write the `templates` container or the `agenthub-ledger` container except through the spill path in section 4.5.
5. Do not add a fourth status. Cancel is `status=Failed` and `errorCode=Cancelled`.
6. Do not publish any task topic, including `agenthub.implementation.agent`.
7. Do not mark processed before the result is published.
8. Do not claim Manual QA on Fixture or on a scripted test double.
9. Do not commit a GitHub token, and do not leave a token in `.git/config`.
10. Do not change `IArtifactBlobStore`. HEALTHY test fakes implement it (`FakeBlobStore`, `NoopBlob`).
11. Do not replace the `IGuardrailPipeline` singleton. HEALTHY keeps the 8,000 / 4,000 limits and the heading block.

---

## 2. Read these files before editing

```text
docs/architecture/phase-0-implementation-guide.md          # sections 3, 5.1, 6.3, 8.2, 8.7, 8.8
docs/manual-e2e-means-actual.md
docs/plans/agent-guardrails-design.md
agenthub-api/src/AgentHub.Agents.Healthy/
agenthub-api/src/AgentHub.Core/Models/TaskMessage.cs
agenthub-api/src/AgentHub.Core/Models/AgentResult.cs
agenthub-api/src/AgentHub.Core/Messaging/MessageIds.cs
agenthub-api/src/AgentHub.Core/Abstractions/
agenthub-api/src/AgentHub.Core/Guardrails/LocalGuardrailPipeline.cs
agenthub-api/src/AgentHub.Hosting/HealthyAgentWorker.cs
agenthub-api/src/AgentHub.Hosting/AgentWorkersServiceCollectionExtensions.cs
agenthub-api/src/AgentHub.Infrastructure/Blob/AzureArtifactBlobStore.cs
agenthub-api/src/AgentHub.Infrastructure/Messaging/ServiceBusPublishers.cs
agenthub-api/src/AgentHub.Infrastructure/Llm/LlmClients.cs
agenthub-api/src/AgentHub.Infrastructure/Persistence/TablePortsAdapters.cs
agenthub-api/src/AgentHub.Data/Entities/EffectLedgerEntity.cs
agenthub-api/src/AgentHub.Data/Repositories/EffectLedgerRepository.cs
agenthub-api/src/AgentHub.WebApi/Program.cs
agenthub-api/src/AgentHub.WebApi/Healthy/HealthyEndpoints.cs
agenthub-api/docker/servicebus-config.json
agenthub-api/docker-compose.yml
agenthub-api/AgentHub.slnx
agenthub-api/tests/AgentHub.Agents.Healthy.Tests/
agenthub-api/tests/AgentHub.Core.Tests/MessagingContractGoldenTests.cs
```

Today `TaskMessage` has no `outputRevision`, `blobPrefix` or `context`. `IArtifactBlobStore` has only `WriteJsonAsync`. `LocalGuardrailPipeline` blocks input over 8,000 characters, output over 4,000 characters, and any markdown heading. `OllamaLlmClient` returns 0 tokens and `GatewayLlmClient` sends `max_tokens` 256. Section 4 is the required delta. Do not work around a missing field by hiding it inside `Artifacts`.

---

## 3. Build order

After every step, `dotnet test agenthub-api/AgentHub.slnx` passes, including every existing HEALTHY test. A change that breaks HEALTHY is wrong. `TreatWarningsAsErrors` is on.

1. Core models, blob-access port, guardrail limits, LLM request fields (section 4). Golden JSON still deserializes.
2. System Design library with no host (sections 5 and 6). Scripted-LLM tests.
3. Dispatcher tests.
4. Worker, DI, harness (section 8). `Enabled=false` and a missing Service Bus connection string must still let the host start.
5. Emulator config and Development settings (section 9).
6. Ollama Manual QA (section 11). Write the evidence file only after the run.

---

## 4. Seams (edit these files; do not fork the types)

### 4.1 `TaskMessage.cs`

Remove the comment that freezes the type at contract 1.3.0. Add members. Every new member is optional or has a default, so today's HEALTHY JSON and `tests/AgentHub.Core.Tests/Contracts/task-message.v1.json` still deserialize and validate.

```csharp
public string ContractVersion { get; set; } = "2.0";
public int PipelineVersion { get; set; } = 1;
public int LoopIndex { get; set; }
public int OutputRevision { get; set; } = 1;
public string BlobPrefix { get; set; } = string.Empty;
public string? HumanAnswer { get; set; }
public TaskMessageContext Context { get; set; } = new();
```

Extend `TaskMessageConfig` with `TaskBudget Budget` (default `StageMaxTokens` 500000, `WorkItemRemainingTokens` 3000000) and `TaskLimits Limits` (default `MaxRevisions` 3, `MaxFixAttempts` 3).

`TaskMessageContext`: `ProjectName`, `ProjectSlug`, `ProjectMode` (`New` or `Existing`), `BaselineGeneration`, `TargetGeneration`, `StackProfile`, `TemplateId`, `Repo` (`Provider`, `Url`, `DefaultBranch`, `WorkBranch`, `HeadCommit`, `PullRequestId`), `Ado` (`Organization`, `Project`, `EpicId`), `Environment` (default `DEV`), `DeploymentAction`, `ImplementationMode`.

`TaskMessage.Validate()` stays as it is. It must not require `BlobPrefix`. System Design checks live in `SystemDesignTaskValidation`.

Add to `MessageIds`:

```csharp
public static string ResultMessageId(string taskId, int inputVersion) =>
    $"result:{taskId}:{inputVersion}";

public static string EventId(string taskId, int inputVersion, int deliveryCount, int seq) =>
    $"{taskId}:{inputVersion}:{deliveryCount}:{seq}";
```

### 4.2 `AgentResult.cs`

Add, all optional for old JSON: `ContractVersion` default `"2.0"`, `OutputRevision`, `Outputs` as `Dictionary<string, JsonElement>` (empty object, not `Dictionary<string, object?>`), `Signal` of type `AgentSignal?`, `ErrorCode`.

```csharp
public sealed class AgentSignal
{
    public string Kind { get; set; } = string.Empty;
    public string? RouteBackTo { get; set; }
    public string? Reason { get; set; }
}

public static class AgentErrorCodes
{
    public const string MissingInput = "MissingInput";
    public const string InvalidInput = "InvalidInput";
    public const string GuardrailBlocked = "GuardrailBlocked";
    public const string BuildEnvironment = "BuildEnvironment";
    public const string ExternalService = "ExternalService";
    public const string DeadlineExceeded = "DeadlineExceeded";
    public const string Cancelled = "Cancelled";
    public const string Unhandled = "Unhandled";
}

public static class AgentSignalKind
{
    public const string Question = "Question";
    public const string LoopLimit = "LoopLimit";
    public const string BudgetExceeded = "BudgetExceeded";
    public const string RouteBack = "RouteBack";
}
```

System Design sets `Kind` and, for a question, `Reason` null. `RouteBackTo` stays null. `Outputs` is `{}`. `HumanMessage` is the question or the limit text. `agent-result.v1.json` must still deserialize.

### 4.3 Blob access (do not change `IArtifactBlobStore`)

Add `AgentHub.Core/Abstractions/IArtifactBlobAccess.cs`:

```csharp
public interface IArtifactBlobAccess : IArtifactBlobStore
{
    Task<bool> ExistsAsync(string blobPath, string? containerName, CancellationToken cancellationToken = default);
    Task<string?> TryReadTextAsync(string blobPath, string? containerName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListAsync(string prefix, string? containerName, CancellationToken cancellationToken = default);
    Task<bool> TryCreateTextAsync(string blobPath, string body, string contentType, string? containerName, CancellationToken cancellationToken = default);
}
```

`containerName` null means `BlobStorage:ContainerName`. Pass `BlobStorage:TemplatesContainerName` (default `templates`) for the manifest and the standards pack.

`AzureArtifactBlobStore` implements `IArtifactBlobAccess` and keeps the `BlobServiceClient` it already builds. `TryReadTextAsync` returns null on 404. `ListAsync` returns blob names under the prefix, ordinal order. `TryCreateTextAsync` uploads with `Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }` and returns false on 409. None of these four methods call `CreateIfNotExistsAsync`. Containers are created by the operator (section 11) or by the infrastructure script. Leave the existing `WriteJsonAsync` body as it is, so HEALTHY does not change.

Register in `AddAgentHubInfrastructure`:

```csharp
services.AddSingleton<AzureArtifactBlobStore>();
services.AddSingleton<IArtifactBlobStore>(sp => sp.GetRequiredService<AzureArtifactBlobStore>());
services.AddSingleton<IArtifactBlobAccess>(sp => sp.GetRequiredService<AzureArtifactBlobStore>());
```

Remove the old `AddSingleton<IArtifactBlobStore, AzureArtifactBlobStore>()` line. System Design injects `IArtifactBlobAccess` only.

`BlobStorageOptions`: add `TemplatesContainerName` default `templates` and `LedgerContainerName` default `agenthub-ledger`. Set `ContainerName` default and both `appsettings.json` and `appsettings.Development.json` to `artifacts`, which is the guide name. Local Manual QA creates that container. Do not point Azure at `agenthub-artifacts` while buildgenix-api writes `artifacts`.

### 4.4 Guardrails

Add to `GuardrailModels.cs`:

```csharp
public sealed record GuardrailLimits(int MaxInputChars, int MaxOutputChars, bool BlockMarkdownHeadings);
```

Add a constructor on `LocalGuardrailPipeline` that takes `GuardrailLimits`. The parameterless constructor keeps `DefaultMaxInputChars` 8000, `DefaultMaxOutputChars` 4000, and headings blocked, so `LocalGuardrailPipelineTests` and HEALTHY do not change. The new constructor uses the passed limits in `EvaluateInput` and `EvaluateOutput`, and skips `HasMarkdownHeading` when `BlockMarkdownHeadings` is false.

Do not replace the `IGuardrailPipeline` singleton. In the System Design handler factory:

```csharp
new LocalGuardrailPipeline(new GuardrailLimits(
    MaxInputChars: 400_000,
    MaxOutputChars: 200_000,
    BlockMarkdownHeadings: false))
```

Read those three numbers from `Workers:SystemDesign:Guardrails` with those defaults. A block throws `GuardrailBlockedException` and is never repaired or committed to the ledger.

### 4.5 Ledger spill

In `EffectLedgerEntity` add `InputBlobPath`, `InputSha256`, `OutputBlobPath`, `OutputSha256` (lowercase hex). In `EffectLedgerAdapter.CommitAsync`, before `Add`:

- Always, when `IntentKind` is `LLM`: write `InputJson` to the ledger container and store the path and SHA-256. The inline `InputJson` on the row becomes null. The stored JSON is `{ "sha256": "...", "blobPath": "..." }` is not required; the columns hold that. `TryGetCommittedAsync` returns the original input text by reading the blob.
- When `InputJson` or `OutputJson` is longer than 30,000 characters: same treatment for that side.
- Upload with `IfNoneMatch = ETag.All` at `{workItemId}/{taskId}/{intentKind}_{ordinal:D6}.input.json` or `.output.json` in container `BlobStorage:LedgerContainerName`. On 409, read the existing blob and use it.
- `TryGetCommittedAsync` and the repository 409 path both read the blob back, check the SHA-256, and throw `InvalidOperationException` if it does not match. Do not change the Add / 409 rule.

`CommittedEffect` stays `(InputJson, OutputJson)` with the text rehydrated, so callers do not learn about blobs.

### 4.6 LLM request

Add optional parameters at the end of `LlmCompletionRequest`, with defaults, so every existing call site still compiles:

```csharp
int? MaxOutputTokens = null,
bool JsonOutput = false,
int? ContextTokens = null
```

`OllamaLlmClient`: when `JsonOutput` is true, send `format: "json"`. When `ContextTokens` is set, send `options.num_ctx`. Read `prompt_eval_count` and `eval_count` from the response into `InputTokens` and `OutputTokens` (0 only when the field is absent). `GatewayLlmClient`: use `MaxOutputTokens` instead of 256 when it is set, on both the `max_tokens` and `max_completion_tokens` bodies. Read `usage.prompt_tokens` and `usage.completion_tokens` when present. `FixtureLlmClient` ignores the new fields and stays deterministic prose. Do not point System Design tests at `FixtureLlmClient`.

HttpClient timeouts: add `Llm:TimeoutSeconds` (default 120). The System Design run sets 600 through the environment, not by editing the committed default. Both the `ollama` and `aigw` clients read it.

### 4.7 Events and publishers

`AgentHub.Core/Models/AgentEvent.cs` matches guide section 3.5 exactly: `ContractVersion`, `EventId`, `WorkItemId`, `TaskId`, `Stage`, `RunId`, `Kind`, `SubStep` (`Id`, `Ordinal`, `Label`, `Iteration`, `Of`), `Progress` as `JsonElement?`, `Tokens` (`Input`, `Output`, `ModelId`), `Message`, `Level`, `OccurredAt`.

`IAgentEventPublisher` and `ISystemDesignResultPublisher` live in `AgentHub.Core/Abstractions`. `ISystemDesignTaskPublisher` lives next to `IHealthyTaskPublisher`.

`SystemDesignWorkerOptions` lives in `AgentHub.Infrastructure/Messaging/ServiceBusPublishers.cs` beside `HealthyWorkerOptions`. Properties are `TimeSpan`, not strings:

```csharp
public const string SectionName = "Workers:SystemDesign";
public bool Enabled { get; set; }
public string Topic { get; set; } = "agenthub.system-design.agent";
public string Subscription { get; set; } = "agenthub-system-design";
public string ResultsTopic { get; set; } = "buildgenix.agent-results";
public string EventsTopic { get; set; } = "buildgenix.agent-events";
public int MaxDeliveryCount { get; set; } = 5;
public TimeSpan MaxAutoLockRenewalDuration { get; set; } = TimeSpan.FromHours(1);
public TimeSpan HandlerDeadline { get; set; } = TimeSpan.FromMinutes(48);
public int MaxConcurrentCalls { get; set; } = 1;
```

`HandlerDeadline` is 48 minutes because the guide rule is `min(cap - 10 minutes, 0.8 x cap)` and the cap here is 60 minutes. If `HandlerDeadline` is not strictly below `MaxAutoLockRenewalDuration`, the worker logs an error and does not start the processor.

Publishers create `ServiceBusClient` and senders on first publish, not in the constructor, so a missing connection string cannot stop the host. One shared `ServiceBusClient` singleton for the System Design worker, the result publisher, the event publisher and the harness task publisher. The emulator allows 10 connections per namespace.

Result `MessageId` uses `MessageIds.ResultMessageId`. Do not edit `ServiceBusAgentResultPublisher`.

---

## 5. Library

Create `agenthub-api/src/AgentHub.Agents.SystemDesign/` from `AgentHub.Agents.Healthy.csproj`. `ProjectReference` is Core only. Add it to `AgentHub.slnx` and to `AgentHub.Hosting.csproj`.

| File | Role |
|---|---|
| `SystemDesignAgentConstants.cs` | Names and ordinals |
| `SystemDesignTaskValidation.cs` | Required fields and path shape |
| `SystemDesignAgentHandler.cs` | Sub-step machine |
| `SystemDesignAgentDispatcher.cs` | Order in section 7 |
| `Design/TechSpecDocument.cs` | Section 6 shape |
| `Design/DesignValidator.cs` | Deterministic checks |
| `Design/DesignRenderer.cs` | JSON to markdown |
| `Design/SectionCatalog.cs` | The nine section ids |
| `Prompts/*.txt` | Planner, section, critic |

Constants:

```csharp
public const string AgentId = "agenthub-system-design-agent";
public const string StageCode = "SYSTEM_DESIGN";
public const string IntentLlm = "LLM";
public const string IntentWriteArtifact = "WriteArtifact";
public const string IntentAsk = "Ask";
public const string IntentAnswer = "Answer";
public const string IntentAskedAt = "AskedAt";
public const int OrdinalPlanBase = 110;
public const int OrdinalCriticBase = 120;
public const int OrdinalSectionBase = 130;
public const int OrdinalTechSpec = 200;
public const int OrdinalMarkdown = 201;
public const int OrdinalAskBase = 900;
public const int OrdinalAnswerBase = 950;
public const int OrdinalAskedAtBase = 1000;
```

`r` runs from 0 through `min(Limits.MaxRevisions, 3) - 1`, plus one extra revision per answered question (guide section 6.3). Plan ordinal is `110 + r`. Critic is `120 + r`. Section `s` (the table in section 6.2, 1-based) is `130 + r * 10 + s`.

### 5.1 Validation (no blob I/O in the structural half)

Structural, before dedup, dead-letter with no result: `TaskMessage.Validate()` fails, or `Stage` is not `SYSTEM_DESIGN`.

After dedup and the cancel check, blob checks. Fail `MissingInput` when a required blob is absent, `InvalidInput` when a present value has the wrong shape.

Shape:

- `ProjectId` and `WorkItemId` match `^[A-Za-z0-9-]{1,64}$`.
- `BlobPrefix` equals `{ProjectId}/{WorkItemId}/v{OutputRevision}/` and `OutputRevision >= 1`.
- `Context.ProjectSlug` matches `^[a-z0-9](?:[a-z0-9-]{0,28}[a-z0-9])?$` (30 characters). Empty slug is `InvalidInput`.
- `Context.StackProfile` is `fe-be` or `fe-be-llm`.
- `Context.TemplateId` is non-empty. The blob is `{TemplateId}/manifest.json` in the templates container. A missing manifest is `MissingInput`. A manifest without `layout.roots` (a non-empty string array) or `commands` (`build`, `test`, `run`) is `InvalidInput`.
- `Artifacts["prd"]` and `Artifacts["intakeSummary"]` are non-empty, start with `{ProjectId}/`, contain no `..`, `\`, `//` or leading `/`, and both blobs exist.
- `templates` prefix `standards/v1/` lists at least one blob. An empty prefix is `MissingInput`.
- Existing mode (`Context.ProjectMode == "Existing"`): `baselineTechSpec` and `baselineCodeState` exist and pass the same path shape.
- `OutputRevision > 1`: `Artifacts["feedback"]` exists. If `Artifacts["techSpec"]` is present, it is the previous techspec and is loaded into the plan prompt. Both pass the path shape.

### 5.2 Handler

`RunAsync(TaskMessage message, int deliveryCount, RunEventSequence events, CancellationToken deadlineToken, Func<CancellationToken, Task<bool>> cancelled)`.

`RunEventSequence.Next()` uses `Interlocked.Increment` and starts at 1. The dispatcher creates one per delivery and uses it for `AgentStarted`, heartbeats, sub-steps and the terminal event, so `eventId` values do not collide.

At the start and before every sub-step, `if (await cancelled(deadlineToken))` publish nothing from the handler: return `Failed` / `Cancelled` and do not call the LLM or write blobs.

Sub-steps:

1. `context`. Read the manifest and every blob under `standards/v1/` in name order. Join the standards text with a blank line. If `Artifacts["techSpec"]` is present, read it. Log `SubStepStarted` / `SubStepCompleted` with id `context`, ordinal 1, `iteration` = `r + 1`, `of` = the revision cap.
2. `repo-context`, only when mode is `Existing`. Read `baselineCodeState`. Keep `path`, `name` and `signature`. Drop bodies. Log id `repo-context`, ordinal 2.
3. `plan`. One LLM call, intent `LLM`, ordinal `110 + r`. The model returns one JSON object: `components`, `phases`, `filePlan`, `apiContracts`, and optional `question`. Parse failures are repaired at most 2 more times inside the same ordinal (guide 8.7). Commit only the parsed attempt, with the token sum of all attempts. If the third attempt is not JSON, treat it as a validator violation for this revision. A non-null `question` commits `Ask` at `900 + q` (q = how many `Ask` rows this task already has) and returns `HumanActionRequired` / `Question`. Do not also commit the plan row for that attempt, so the resume runs plan again. A fourth question in one run is `LoopLimit`.
4. `write`. For each section in section 6.2 that applies, one LLM call, intent `LLM`, ordinal `130 + r * 10 + s`. The call returns only that section's properties. Merge them in C#. A `question` is handled like step 3, and the resume continues at `write` for that section.
5. `validate`. `DesignValidator.Validate`. No LLM. Violations while `r` is below the last allowed revision: set `r = r + 1` and go to step 3. Do not call critique. A violation on the last revision is `LoopLimit`.
6. `critique`. LLM, intent `LLM`, ordinal `120 + r`. Returns `{ "issues": [ "..." ], "question": null }`. Empty issues means clean. An issue that starts with `plan:` goes back to step 3 at `r + 1` when a revision remains. Any other issue goes back to step 4 at `r + 1`. No revision left: `LoopLimit`. A `question` is handled like step 3, and the resume continues at `critique` is wrong: the resume continues at `write` unless the question was asked from `plan`.
7. `render`. Deterministic markdown. No LLM row.
8. `publish`. Guardrail-scan both bodies. `TryCreateTextAsync` for `{BlobPrefix}techspec.json` (`application/json`, intent `WriteArtifact` ordinal 200) and `{BlobPrefix}system-design.md` (`text/markdown`, ordinal 201). A false return (blob already exists) is success when the ledger row for that ordinal is already committed; otherwise `Failed` / `Unhandled` with `ErrorMessage` naming the path. Never overwrite.

Token stop, before each LLM call: sum tokens stored on this task's `LLM` rows. If that sum is at least `min(Budget.StageMaxTokens, Budget.WorkItemRemainingTokens)`, return `HumanActionRequired` / `BudgetExceeded`. Each row stores `inputVersion` and the token sum of its attempts. `AgentResult.TokenUsage` counts only calls whose row `inputVersion` equals `message.InputVersion`. A replayed row adds 0 to `TokenUsage` and still counts toward the budget sum.

`humanAnswer`: when it is non-empty and an `Ask` row has no matching `Answer`, commit `Answer` at `950 + q` and `AskedAt` at `1000 + q` before step 3. Set `r` to one past the revision that asked, which is the one extra revision. Put every committed answer in the next prompt. A replayed model output whose question already has an `Answer` is never asked again.

Completed result:

```text
Artifacts["techSpec"]   = {BlobPrefix}techspec.json
Artifacts["techSpecMd"] = {BlobPrefix}system-design.md
OutputRevision          = message.OutputRevision
Outputs                 = {}
Signal                  = null
```

### 5.3 Prompts

Three files under `Prompts/`, embedded and copied to output the same way as `healthy-system.txt`.

Each says: return JSON only, no markdown fences; stack is `stackProfile`; do not invent packages; do not invent resource names; do not invent commands; for an existing project carry every baseline acceptance check forward; set `question` instead of assuming a product fact; the critic prefixes an issue with `plan:` when components or the file plan must change. Prompt files hold instructions only. A test scans `Prompts/` with the secret-shape checks and fails on any hit.

---

## 6. `techspec.json`

Camel case. `TechSpecDocument` is this object.

```json
{
  "schemaVersion": "1",
  "stackProfile": "fe-be",
  "templateId": "fe-be@1.0.0",
  "components": [{ "name": "CalculatorApi", "responsibility": "Adds two decimals" }],
  "phases": [{ "id": "1", "title": "Add", "milestone": "POST /api/calculator/add returns the sum" }],
  "filePlan": [{
    "path": "src/Calculator.Api/CalculatorController.cs",
    "action": "create",
    "storyKeys": ["add"],
    "symbols": [{ "name": "Add", "kind": "method", "signature": "Task<IActionResult> Add(AddRequest body)" }]
  }],
  "apiContracts": [{
    "method": "POST",
    "path": "/api/calculator/add",
    "request": { "a": "number", "b": "number" },
    "response": { "result": "number" },
    "errors": [{ "status": 400, "shape": { "error": "string" } }]
  }],
  "dataModel": [{ "name": "none", "storage": "TableStorage", "partitionKey": "n/a", "rowKey": "n/a", "fields": [] }],
  "llmIntegration": null,
  "tddPlan": [{ "phaseId": "1", "tests": ["Add_returns_expected_sum"] }],
  "acceptanceChecks": [{
    "id": "ac-add-decimals",
    "criterionId": "prd-ac-1",
    "method": "POST",
    "path": "/api/calculator/add",
    "body": { "a": 0.1, "b": 0.2 },
    "expectedStatus": 200,
    "expectedShape": { "result": "number" },
    "expectedBody": { "result": 0.3 },
    "retired": false
  }],
  "backlog": {
    "epicTitle": "Calculator",
    "stories": [{ "key": "add", "title": "Add two numbers", "status": "added", "acceptanceCriterionIds": ["prd-ac-1"] }]
  },
  "deploymentPlan": {
    "resourceGroup": "rg-bgx-sandbox-dev",
    "plan": "asp-bgx-sandbox-dev",
    "healthPath": "/health",
    "apps": [
      { "name": "bgx-calculator-be-dev", "kind": "be" },
      { "name": "bgx-calculator-fe-dev", "kind": "fe" }
    ]
  },
  "commands": { "build": "dotnet build", "test": "dotnet test", "run": "dotnet run --project src/Calculator.Api" },
  "packages": []
}
```

`action` is `create` or `modify`. Story `status` is `added`, `changed`, `unchanged` or `retired`. `llmIntegration` is null for `fe-be`. For `fe-be-llm` it is `{ "endpoint": "<the configured gateway route>", "requestShape": {}, "responseShape": {} }` and it never contains a key.

### 6.1 Provisional input contracts (record as OPEN)

These are not in the guide as JSON schemas. The PRD agent and the template author must adopt them or this slice changes.

- `prd.json` has `acceptanceCriteria: [{ "id", "text", "status" }]` with `status` `active` or `retired`.
- `manifest.json` has `layout.roots` (string array of first path segments, such as `src` and `tests`), `allowlist: [{ "id", "version" }]`, and `commands` with `build`, `test`, `run`.
- `standards/v1/*.txt` is plain text.

Fixture manifest for `fe-be@1.0.0`:

```json
{
  "templateId": "fe-be@1.0.0",
  "layout": { "roots": ["src", "tests"] },
  "allowlist": [],
  "commands": {
    "build": "dotnet build",
    "test": "dotnet test",
    "run": "dotnet run --project src/Calculator.Api"
  }
}
```

### 6.2 Sections

| s | Id | Included when | Properties the call may set |
|---|---|---|---|
| 1 | architecture | always | `components`, `phases` |
| 2 | data | always | `dataModel` |
| 3 | api | always | `apiContracts` |
| 4 | llmIntegration | `fe-be-llm` only | `llmIntegration` |
| 5 | tdd | always | `tddPlan` |
| 6 | acceptance | always | `acceptanceChecks` |
| 7 | backlog | always | `backlog` |
| 8 | deployment | always | `deploymentPlan`, `packages` |
| 9 | commands | always | none. C# copies `commands` from the manifest after this step |

`fe-be` skips s = 4 and still uses the other numbers. `filePlan` is only produced by `plan`.

### 6.3 DesignValidator

Return a list of strings. Empty means clean.

1. `schemaVersion` is `1`. `stackProfile` equals `Context.StackProfile` and is `fe-be` or `fe-be-llm`.
2. Every PRD criterion whose `status` is `active` appears as `criterionId` on a check that is not retired.
3. Every story whose `status` is `added` or `changed` appears in at least one `filePlan[].storyKeys`. `unchanged` and `retired` stories need no file.
4. Every `filePlan.path` is a relative path: no leading `/` or `~`, no `\`, `:`, NUL or control character, no empty, `.` or `..` segment, at most 200 characters. Its first segment equals one `layout.roots` entry exactly (segment match, not `StartsWith`). No segment is `.git`. The path is not `package.json`, `package-lock.json`, `*.csproj`, `Directory.Packages.props`, `NuGet.config`, `.npmrc`, `azure-pipelines*.yml`, anything under `.azure-pipelines/`, `pipelines/` or `.husky/`.
5. Existing mode: every `modify` path exists in baseline code-state. Every `create` path does not.
6. Existing mode: every baseline acceptance check id is present. `retired: true` is allowed only when that criterion is `retired` or absent in the current `prd.json`.
7. Every `packages[].id` is in the manifest `allowlist`. Empty `packages` is valid.
8. `deploymentPlan.resourceGroup` is `rg-bgx-sandbox-dev`, `plan` is `asp-bgx-sandbox-dev`, `healthPath` is `/health`, and `apps` is exactly two entries: `bgx-{slug}-be-dev` / `be` and `bgx-{slug}-fe-dev` / `fe`.
9. `fe-be-llm` requires `llmIntegration.endpoint` equal to `Workers:SystemDesign:GeneratedAppLlmEndpoint`. Any other value is a violation. The document contains no key-shaped string. `fe-be` requires null `llmIntegration`.
10. `commands` equals the manifest `commands` object.
11. Every `dataModel[].storage` is `TableStorage`.
12. Every API and acceptance `path` starts with a single `/` and contains no `//`, `\`, `..`, `@` or a scheme. `method` is GET, POST, PUT, PATCH or DELETE. Every acceptance check has `method`, `path`, `expectedStatus` and `expectedShape`.

### 6.4 Renderer

`DesignRenderer.ToMarkdown` emits one heading per section that ran. Model strings are escaped (`<`, `>`, `&`, and markdown link syntax). No raw HTML. File plan paths, the endpoint and the commands appear in code spans. A unit test feeds a component name `<script>` and asserts the output contains the escaped form and does not contain `techspec.md`.

---

## 7. Dispatcher

Copy the outcome kinds from `HealthyAgentDispatcher`: `Handled`, `AlreadyProcessed`, `Abandon`, `DeadLetter`.

Order:

1. Deserialize. Malformed JSON: `DeadLetter`, no result.
2. `TaskMessage.Validate()` fails, or `Stage` is not `SYSTEM_DESIGN`: `DeadLetter`, no result.
3. MessageId mismatch: log and continue. Dedup key is the Service Bus `MessageId`.
4. `IsProcessedAsync`: `AlreadyProcessed` (complete, no work).
5. Cancel marker `{ProjectId}/{WorkItemId}/control/cancelled.json` exists in `BlobStorage:ContainerName`: publish `Failed` / `Cancelled`, terminal event, mark, `Handled`.
6. Publish `AgentStarted`.
7. `SystemDesignTaskValidation` blob checks. On failure publish `Failed` / `MissingInput` or `InvalidInput`, terminal event, mark, `Handled`.
8. Call the handler with a linked token that cancels after `HandlerDeadline`. Pass `RunEventSequence`. The cancel delegate reads the same marker path.
9. If the deadline token fired and the host token did not: publish `Failed` / `DeadlineExceeded`, `AgentFailed`, mark, `Handled`. Do not abandon.
10. Host shutdown (`args.CancellationToken`): abandon, do not publish.
11. `GuardrailBlockedException`: publish `Failed` / `GuardrailBlocked`, mark, `Handled`. Do not abandon.
12. Any other exception: if `deliveryCount < MaxDeliveryCount`, `Abandon` and do not publish. On the last delivery, publish `Failed` / `Unhandled`, mark, `Handled`.
13. A normal result: publish with `MessageIds.ResultMessageId`, publish the terminal event (`AgentCompleted`, `AgentHumanActionRequired` or `AgentFailed`), mark, `Handled`.

Heartbeat: while the handler runs, a timer every 60 seconds publishes kind `Heartbeat` on the same sequence. A failed heartbeat is logged and does not fail the run. Stop the timer in `finally`.

---

## 8. Host

### 8.1 Worker

Copy `HealthyAgentWorker` to `SystemDesignAgentWorker`. Processor: `AutoCompleteMessages = false`, `PrefetchCount = 0`, `MaxConcurrentCalls = 1`. Map outcomes the same way: `DeadLetter` and `Abandon` as HEALTHY does, `Handled` and `AlreadyProcessed` complete.

If `Enabled` is false, log once and return. The host must still start. Test `Host_starts_when_SystemDesign_disabled_and_connection_string_missing`.

### 8.2 DI

In `AddAgentWorkers`, after HEALTHY:

- `Configure<SystemDesignWorkerOptions>`
- Handler singleton. Inject `ILlmClient`, `IArtifactBlobAccess`, `IEffectLedger`, `IActivityLog`, the dedicated `LocalGuardrailPipeline` from section 4.4, `IAgentEventPublisher`, `IOptions<SystemDesignWorkerOptions>`, logger.
- Dispatcher singleton.
- `AddHostedService<SystemDesignAgentWorker>`

### 8.3 Harness (Development only)

In `Program.cs`, call `MapSystemDesignEndpoints()` only inside `if (app.Environment.IsDevelopment())`. A test boots with `ASPNETCORE_ENVIRONMENT=Production` and asserts `POST /api/connectivity/system-design` is 404.

`POST /api/connectivity/system-design` publishes a task. Summary text says it is not the production path.

Body fields: `projectId`, `workItemId`, `taskId`, `inputVersion`, `outputRevision`, `correlationId`, `templateId`, `stackProfile`, `projectMode`, `projectSlug`, `prdPath`, `intakeSummaryPath`, and optional `feedbackPath`, `previousTechSpecPath`, `baselineTechSpecPath`, `baselineCodeStatePath`, `humanAnswer`, `modelId`, `maxRevisions`, `pipelineVersion`.

The endpoint sets `BlobPrefix` to `{projectId}/{workItemId}/v{outputRevision}/` itself. It does not trust a caller-supplied prefix. It applies section 5.1 shape checks and returns 400 with the validation strings. A publish failure returns 503 with no exception text and no connection string.

`GET /api/connectivity/system-design/peek?topic=results|events&max=20` peeks (`PeekMessagesAsync`, do not settle) subscription `buildgenix-api` and returns bodies plus `MessageId`. Service Bus Explorer cannot see emulator messages. This route is how Manual QA reads them.

---

## 9. Emulator, settings, README

### 9.1 `docker/servicebus-config.json`

Add three topics. Topic properties: `DefaultMessageTimeToLive` `PT1H`, `RequiresDuplicateDetection` false. Subscription properties: `LockDuration` `PT5M`, `MaxDeliveryCount` 5, `DeadLetteringOnMessageExpiration` true, `DefaultMessageTimeToLive` `PT1H`, `RequiresSession` false.

| Topic | Subscription |
|---|---|
| `agenthub.system-design.agent` | `agenthub-system-design` |
| `buildgenix.agent-results` | `buildgenix-api` |
| `buildgenix.agent-events` | `buildgenix-api` |

Leave HEALTHY topics in place. The emulator reads config only when its container starts and has no volume of its own. After the edit run `docker compose up -d --force-recreate servicebus-emulator` from `agenthub-api/`. Do not use `docker compose down -v` for this: `-v` deletes `azurite-data`.

### 9.2 Settings

`appsettings.json` and `appsettings.Development.json` keep `Workers:SystemDesign:Enabled` false. Add the topic, subscription, results topic, events topic, `MaxDeliveryCount` 5, `MaxAutoLockRenewalDuration` `01:00:00`, `HandlerDeadline` `00:48:00`, `MaxConcurrentCalls` 1, and:

```json
"Guardrails": { "MaxInputChars": 400000, "MaxOutputChars": 200000, "BlockMarkdownHeadings": false },
"GeneratedAppLlmEndpoint": ""
```

`GeneratedAppLlmEndpoint` empty means `fe-be-llm` validation fails `InvalidInput` until the operator sets it. That is OPEN item 4. Do not invent a host.

For the Manual QA process only:

```bash
Workers__SystemDesign__Enabled=true Llm__Provider=Ollama Llm__TimeoutSeconds=600 \
  dotnet run --project src/AgentHub.WebApi
```

`dotnet user-secrets` needs a `UserSecretsId`. Run `dotnet user-secrets init --project src/AgentHub.WebApi` and commit the csproj property, never a secret. Connection strings stay in `appsettings.Development.json` as they are today (the emulator placeholder and the Azurite development account). Do not add a real secret there.

### 9.3 README

Add a section: how to enable the worker, the harness URL and the sentence that it is not production, the Manual QA steps in section 11, and OPEN items 1 and 9. Remove "I3 System Design agent" from "Out of scope". Add the three topics to the config list.

---

## 10. Tests

Create `agenthub-api/tests/AgentHub.Agents.SystemDesign.Tests/` from the HEALTHY test csproj. Reference the new library and Core. Add it to the solution.

Handler tests use a test-only `ScriptedLlmClient : ILlmClient` that returns a reply keyed by the user-prompt marker you put in the prompt (`plan`, `section:{id}`, `critic`) and records calls. Do not change `FixtureLlmClient`.

Fixtures under `testdata/`:

- `new-fe-be/`: `prd.json` (criteria `prd-ac-1`, `prd-ac-2`, `prd-ac-3`), `intake-summary.json`, `manifest.json` exactly as section 6.1, `standards.txt`.
- `new-fe-be-llm/`: same plus a non-null llm section and `fe-be-llm@1.0.0` manifest.
- `existing-modify/`: cumulative `prd.json`, `baseline-techspec.json` containing `ac-add-decimals`, `ac-add-integers`, `ac-add-non-numeric`, `code-state.json` with the add files.

| Test | Assert |
|---|---|
| `Validator_rejects_unknown_stack` | non-empty |
| `Validator_rejects_missing_criterion` | names the id |
| `Validator_rejects_story_without_files` | an `added` story with no `storyKeys` |
| `Validator_accepts_unchanged_story_without_files` | clean |
| `Validator_rejects_parent_segment` | `src/../../x` |
| `Validator_rejects_root_lookalike` | `srcx/Evil.cs` |
| `Validator_rejects_pipeline_file` | `src/azure-pipelines.yml` |
| `Validator_rejects_create_that_collides` | existing mode |
| `Validator_rejects_modify_target_missing` | existing mode |
| `Validator_rejects_dropped_baseline_check` | id absent and not retired |
| `Validator_accepts_retired_when_prd_retires_it` | clean |
| `Validator_rejects_retired_while_prd_keeps_it` | violation |
| `Validator_rejects_bad_app_name` | not `bgx-{slug}-be-dev` |
| `Validator_rejects_commands_that_differ_from_manifest` | violation |
| `Validator_rejects_llm_endpoint_not_configured` | `fe-be-llm` |
| `Validator_accepts_clean_new_fe_be` | empty list |
| `Renderer_escapes_script_and_omits_techspec_md` | escaped `<script>`, no `techspec.md` |
| `Handler_completes_and_writes_both_files` | `Completed`; both paths; `WriteArtifact` 200 and 201; both bodies pass the System Design guardrail instance |
| `Handler_loops_to_plan_on_validator_errors_then_limits` | scripted stack always wrong; `LoopLimit`; plan ordinals 110, 111, 112 exist; 113 does not; critic 120 to 122 absent |
| `Handler_does_not_critique_a_dirty_document` | critic ordinals absent when validation never passes |
| `Handler_critique_issue_goes_to_write` | section ordinals at `r = 1` exist; plan ordinal 111 does not |
| `Handler_plan_prefixed_issue_goes_to_plan` | plan ordinal 111 exists |
| `Handler_question_then_answer_continues` | first result `Question` and `Ask` at 900; second delivery with `humanAnswer` commits `Answer` at 950 and does not call the LLM for the answered plan row |
| `Handler_fourth_question_is_loop_limit` | `LoopLimit` |
| `Handler_existing_mode_runs_repo_context` | sub-step id `repo-context` |
| `Handler_redelivery_replays_ledger` | second `RunAsync` makes zero LLM calls; `TokenUsage` is 0 |
| `Dispatcher_publishes_then_marks` | call order |
| `Dispatcher_guardrail_block_completes` | `Handled`, `GuardrailBlocked`, LLM not retried |
| `Dispatcher_cancel_marker_before_start` | `Cancelled`, LLM not called |
| `Dispatcher_deadline_publishes_deadline_exceeded` | `Handled`, `DeadlineExceeded` |
| `Dispatcher_last_delivery_publishes_unhandled` | delivery 5, `Unhandled`, `Handled` |
| `Dispatcher_malformed_json_has_no_result` | `DeadLetter`, publisher count 0 |
| `Dispatcher_missing_prd_publishes_missing_input` | `Handled`, `MissingInput` |
| `Validation_rejects_prefix_not_matching_output_revision` | prefix `v4/` with `OutputRevision` 1 is `InvalidInput` |
| `Message_round_trip_keeps_output_revision` | guide section 3.3 sample: `OutputRevision` is 1 and is not copied from `InputVersion` |
| `Ledger_spills_llm_input_and_long_output` | row columns set, inline input null, SHA matches |
| `Event_json_matches_guide_3_5` | nested `subStep`, `tokens` |
| `Harness_not_mapped_outside_development` | Production returns 404 |
| `Prompts_have_no_secret_shape` | scan passes |

A skipped test fails the suite. Do not use `[Fact(Skip=...)]`.

---

## 11. Manual QA (Ollama)

Follow `docs/manual-e2e-means-actual.md`.

1. From `agenthub-api/`: `docker compose up -d`. If you already edited the emulator config, `docker compose up -d --force-recreate servicebus-emulator`. Do not use `down -v` unless you mean to wipe Azurite. Wait until `curl -sS http://127.0.0.1:5300/health` is healthy.
2. `dotnet run --project src/AgentHub.WebApi` with `Workers__SystemDesign__Enabled=true` and `Llm__Provider=Ollama`. Within 30 seconds the log shows listening on `agenthub.system-design.agent/agenthub-system-design` and no `MessagingEntityNotFound`. Port is `http://127.0.0.1:18766`. `GET /health/live` returns 200.
3. Create containers `artifacts`, `templates` and `agenthub-ledger` with `az storage container create --connection-string "UseDevelopmentStorage=true"`. Upload the fixture manifest to `templates/fe-be@1.0.0/manifest.json` and one file to `templates/standards/v1/standards.txt`. Upload `prd.json` and `intake-summary.json` under `proj-sd-febe/{workItemId}/v1/`.
4. Set `Workers:SystemDesign:GeneratedAppLlmEndpoint` only for the `fe-be-llm` run, to the gateway route operations already use. Do not put a key in the techspec or the evidence file.
5. POST the harness for example 2 (`fe-be`, new). Expect 202.
6. `GET /api/connectivity/system-design/peek?topic=results&max=5` until the result appears. `MessageId` is `result:{taskId}:{inputVersion}`. Download both blobs. Paths equal `BlobPrefix`. Markdown name is `system-design.md`.
7. Peek events. Each of `context`, `plan`, `write`, `validate`, `critique`, `render`, `publish` appears. Logs show the D-C1 order and the correlation id. Ollama `ollama ps` shows the model you configured (`Llm:Ollama:DefaultModel`, at least a 7B model with a 32k context). `llama3.2` at 3B is not expected to pass; record the exact tag. A run that only returns Fixture prose is a fail.
8. Tables: new rows only in `EffectLedger`, `ActivityLog`, `AgProcessedEvents`.
9. Republish the same `taskId` and `inputVersion`. The second delivery completes as already processed. Ollama shows no new chat. EffectLedger row count is unchanged.
10. Example 1 (`fe-be-llm`) and example 3 (existing, modify-only, the three `ac-add-*` checks still present, subtract check added) the same way. Example 3 uses `workItemId` `wi-sd-existing` and uploads the baseline blobs.
11. Cancel: work item `wi-sd-cancel`. Upload `proj-sd-febe/wi-sd-cancel/control/cancelled.json` with `reason`, `at`, and `taskId`. Publish. Expect `Failed` / `Cancelled` and no blob under that prefix. Then work item `wi-sd-cancel2`: publish, upload the marker while `plan` is running, expect `Failed` / `Cancelled` and no techspec blob.
12. Redraft: work item from step 5, `outputRevision` 2, new `taskId`. Upload `feedback-system-design.json` and set `previousTechSpecPath` to the v1 techspec. Record the ETag of both v1 blobs before the publish. Expect v2 blobs and unchanged v1 ETags.
13. `GET /health/live` still 200 after a forced handler fault (stop Ollama mid-run and let the last delivery publish `Unhandled`).

Write `docs/qa/manual-qa-system-design-ollama-2026-10-08.md` with the commands, the blob paths, the peeked result JSON and the event list. Gateway on the isolated machine is BLOCKED until OPEN item 9 (the consumer id) exists. Say BLOCKED, not PASS. Ollama is the local pass. Slice 7 stays open until the Azure run passes.

---

## 12. Done for this slice

- [ ] `dotnet test agenthub-api/AgentHub.slnx` passes, HEALTHY included
- [ ] Section 10 tests pass
- [ ] Emulator has the three topics and subscription `buildgenix-api`
- [ ] Examples 1, 2 and 3 each produce `techspec.json` and `system-design.md` that pass `DesignValidator` on Ollama, with evidence
- [ ] Example 3 is modify-only and still contains the add checks
- [ ] README describes the harness as non-production
- [ ] Worker default `Enabled` is false in committed config
- [ ] No `techspec.md` in the new library
- [ ] Implementation notes name MIG-7 (shared dispatcher extraction) as follow-up if you copied the dispatcher
- [ ] Azure Gateway run recorded as PASS or BLOCKED on OPEN item 9

---

## 13. Open items (do not close them)

From guide section 16.1. The ones that touch this slice:

1. Inbound result and event topic names. Use `buildgenix.agent-results`, `buildgenix.agent-events` and subscription `buildgenix-api` until Badru confirms.
4. Which key and which route a generated app calls. `GeneratedAppLlmEndpoint` stays empty until that is decided. `fe-be` Manual QA does not need it.
7. Model id per agent. Read `Config.ModelId`. Do not hard-code a model.
9. AI Gateway consumer id. Gateway Manual QA stays BLOCKED.

---

## 14. Paste block

```text
Open docs/prompts/i3-system-design-agent-spoon-feed.md from https://github.com/badrusiddique/agent-sdlc branch main. Execute only the section "Part A. First piece" through "A6. Stop". That section already contains the Phase 0 topics, message JSON, statuses, blob path, and error codes. Do not open another architecture document. Do not implement sections 0 to 14. Do not edit the poc folder. Edit root agenthub-api and buildgenix-api. Never use the ai-sdlc repo. Keep the HEALTHY tests green. Stop when docs/qa/manual-qa-system-design-first-slice.md records the checks in A5.
```

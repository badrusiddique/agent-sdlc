# Spoon-feed: implement the System Design agent in agenthub-api

**Purpose:** Paste this file into the coding agent on the other machine. It is the slice 7 start instructions. Implement only `AgentHub.Agents.SystemDesign` and the Core seams it needs. Do not implement PRD, Implementation, Testing, Deployment, or buildgenix-api.

**Repo:** `https://github.com/badrusiddique/agent-sdlc` only. Never `ai-sdlc`.

**Date:** 2026-10-07

**Authority for this slice:** `docs/architecture/phase-0-implementation-guide.md` sections 3, 5.1, 8.2, 8.7 and 8.8. Where this prompt and an older note disagree, the guide wins. The older prompt `docs/prompts/i3-system-design-agent-implementation.md` is background. This file is the one to follow.

**Not in scope:** closing any open item in guide section 16.1. If a step below depends on an open item, use the configured default named here and record the item as OPEN in the implementation notes. Do not invent a product decision.

**Manual QA:** actual Azurite, the Service Bus emulator, and Ollama. A Fixture run is a unit test, never a Manual QA pass. See `docs/manual-e2e-means-actual.md`.

---

## 0. What you are building

| Include | Exclude |
|---|---|
| Library `agenthub-api/src/AgentHub.Agents.SystemDesign` (references Core only) | Any other pipeline agent |
| Core fields the guide already defines and HEALTHY does not have yet | A rewrite of HEALTHY behaviour |
| Blob read, so the agent can load `prd.json` and the cancel marker | Writing buildgenix tables |
| A core-host worker, default **disabled** | The builder container, Graphify, ADO, Azure deploy |
| Harness `POST /api/connectivity/system-design` for local Manual QA only | Production task publish (that is buildgenix-api) |
| Unit tests plus one Ollama Manual QA | The three full pipeline examples end to end |

The agent consumes one `TaskMessage` with `stage = SYSTEM_DESIGN`, reads blobs by the paths in the message, runs the sub-steps in section 8.2, writes `techspec.json` and `system-design.md`, and publishes one `AgentResult`. It never publishes the next stage.

---

## 1. DO / DON'T

### DO

1. Copy the HEALTHY shape: handler library, dispatcher, constants, `BackgroundService` worker, DI in `AddAgentWorkers`.
2. Keep D-C1 order for System Design: dedup check, cancel check, handler, publish `AgentResult`, publish the terminal event, mark `AgProcessedEvents`, then complete the Service Bus message.
3. Keep D-C2: EffectLedger `Add` only. A 409 means the row is already Committed. No Replace. No Failed ledger rows.
4. Keep D-AS-2, D-AS-3, D-AS-4: no agent calls, three statuses only, AH tables only (`EffectLedger`, `ActivityLog`, `AgProcessedEvents`) plus blobs.
5. Set Service Bus `MessageId` on the **task** to `{taskId}:{inputVersion}` (D-I2-4).
6. Set Service Bus `MessageId` on the **result** to `result:{taskId}:{inputVersion}` (guide section 3.4). This is not what HEALTHY does today. Leave HEALTHY's publisher alone.
7. Use `Llm:Provider` = `Fixture` for unit tests, `Ollama` for local Manual QA, `Gateway` only when the consumer id exists. No temperature. Use `Config.ModelId` and `Config.ReasoningEffort`.
8. Run `IGuardrailPipeline` on every LLM input and output, and on artifact JSON before the blob write. Block becomes `Failed` with `errorCode` `GuardrailBlocked`, then mark and dead-letter, no retry.
9. Leave `Workers:SystemDesign:Enabled` false in committed `appsettings.json`. Turn it on only in Development or user secrets for the run.

### DON'T

1. Do not derive a blob path. Write only under `TaskMessage.BlobPrefix`. If `BlobPrefix` is empty, return `Failed` / `InvalidInput`.
2. Do not set `v{n}` from `InputVersion`. `n` is `OutputRevision`, and the prefix already contains it.
3. Do not write `techspec.md`. The markdown file is `system-design.md`.
4. Do not write into the `templates` container.
5. Do not add a fourth status. Cancel is `status=Failed` and `errorCode=Cancelled`.
6. Do not publish `agenthub.implementation.agent` or any other task topic.
7. Do not mark processed before the result is published.
8. Do not claim Manual QA on Fixture.
9. Do not commit a GitHub token, and do not leave a token in `.git/config`.

---

## 2. Read these files before editing

Repo (this checkout):

```text
docs/architecture/phase-0-implementation-guide.md          # sections 3, 5.1, 8.2, 8.7, 8.8
docs/manual-e2e-means-actual.md
docs/plans/agent-guardrails-design.md
agenthub-api/src/AgentHub.Agents.Healthy/                  # behavioural oracle, do not restyle it
agenthub-api/src/AgentHub.Core/Models/TaskMessage.cs
agenthub-api/src/AgentHub.Core/Models/AgentResult.cs
agenthub-api/src/AgentHub.Core/Abstractions/IArtifactBlobStore.cs
agenthub-api/src/AgentHub.Core/Abstractions/IEffectLedger.cs
agenthub-api/src/AgentHub.Hosting/AgentWorkersServiceCollectionExtensions.cs
agenthub-api/src/AgentHub.Hosting/HealthyAgentWorker.cs
agenthub-api/src/AgentHub.Infrastructure/Blob/AzureArtifactBlobStore.cs
agenthub-api/src/AgentHub.Infrastructure/Messaging/ServiceBusPublishers.cs
agenthub-api/src/AgentHub.WebApi/Healthy/HealthyEndpoints.cs
agenthub-api/src/AgentHub.WebApi/Program.cs
agenthub-api/docker/servicebus-config.json
agenthub-api/AgentHub.slnx
```

HEALTHY today cannot read blobs, and `TaskMessage` has no `outputRevision`, `blobPrefix`, or `context`. Section 4 of this prompt is the required Core delta. Do not work around the gap by hiding values inside `Artifacts` only. The guide JSON is the contract.

---

## 3. Build order on this machine

Do the steps in this order. After each step, `dotnet test agenthub-api/AgentHub.slnx` must pass, including the existing HEALTHY tests. A change that breaks HEALTHY is wrong.

1. Extend Core models and the blob port (section 4). Add Core unit tests for JSON round-trip of the guide sample.
2. Add the System Design library with no host (section 5 and 6). Fixture tests for DesignValidator and the revision cap.
3. Add the dispatcher (section 7). Tests for D-C1 order, guardrail dead-letter, last-delivery Failed, cancel.
4. Register the worker and the harness endpoint (section 8). Default Enabled=false, so a normal `dotnet run` still behaves as HEALTHY-only until you flip the flag.
5. Update the emulator config and Development settings (section 9).
6. Ollama Manual QA (section 11). Record evidence paths in `docs/qa/` only after the run. Do not invent a pass.

---

## 4. Core delta (edit these files, do not fork the types)

### 4.1 `TaskMessage.cs`

Delete the comment that freezes the type at contract 1.3.0. Add these members. Existing HEALTHY JSON must still deserialize: every new member is optional or has a default.

```csharp
public string ContractVersion { get; set; } = "2.0";
public int PipelineVersion { get; set; } = 1;
public int LoopIndex { get; set; }
public int OutputRevision { get; set; } = 1;
public string BlobPrefix { get; set; } = string.Empty;
public string? HumanAnswer { get; set; }
public TaskMessageContext Context { get; set; } = new();
```

Extend `TaskMessageConfig`:

```csharp
public TaskBudget Budget { get; set; } = new();
public TaskLimits Limits { get; set; } = new();
```

```csharp
public sealed class TaskBudget
{
    public int StageMaxTokens { get; set; } = 500_000;
    public int WorkItemRemainingTokens { get; set; } = 3_000_000;
}

public sealed class TaskLimits
{
    public int MaxRevisions { get; set; } = 3;
    public int MaxFixAttempts { get; set; } = 3;
}

public sealed class TaskMessageContext
{
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectSlug { get; set; } = string.Empty;
    public string ProjectMode { get; set; } = "New"; // New | Existing
    public int BaselineGeneration { get; set; }
    public int TargetGeneration { get; set; } = 1;
    public string StackProfile { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public TaskRepoContext Repo { get; set; } = new();
    public TaskAdoContext Ado { get; set; } = new();
    public string Environment { get; set; } = "DEV";
    public string? DeploymentAction { get; set; }
    public string? ImplementationMode { get; set; }
}

public sealed class TaskRepoContext
{
    public string Provider { get; set; } = "AzureDevOps";
    public string? Url { get; set; }
    public string DefaultBranch { get; set; } = "main";
    public string? WorkBranch { get; set; }
    public string? HeadCommit { get; set; }
    public int? PullRequestId { get; set; }
}

public sealed class TaskAdoContext
{
    public string? Organization { get; set; }
    public string? Project { get; set; }
    public int? EpicId { get; set; }
}
```

`EnsureDefaults` stays as it is, and also rejects nothing new. Validation of System Design inputs stays in `SystemDesignTaskValidation`, not in `TaskMessage.Validate`, so HEALTHY messages without `BlobPrefix` still validate.

### 4.2 `AgentResult.cs`

Add, all optional for old JSON:

```csharp
public string ContractVersion { get; set; } = "2.0";
public int OutputRevision { get; set; }
public Dictionary<string, object?> Outputs { get; set; } = new(StringComparer.Ordinal);
public AgentSignal? Signal { get; set; }
public string? ErrorCode { get; set; }
```

```csharp
public sealed class AgentSignal
{
    public string Kind { get; set; } = string.Empty; // Question | LoopLimit | BudgetExceeded | RouteBack
    public string? RouteBackTo { get; set; }
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
```

System Design sets `Signal.Kind` only. `RouteBackTo` stays null. `Outputs` is `{}` on every status. `HumanMessage` is the question or the limit text.

### 4.3 `IArtifactBlobStore` and `AzureArtifactBlobStore`

Add:

```csharp
Task<bool> ExistsAsync(string blobPath, string? containerName, CancellationToken cancellationToken = default);
Task<string?> TryReadTextAsync(string blobPath, string? containerName, CancellationToken cancellationToken = default);
```

`containerName` null means `BlobStorage:ContainerName` (today `agenthub-artifacts`). Pass `"templates"` for the manifest and the standards pack. Add `BlobStorage:TemplatesContainerName` defaulting to `templates`.

`TryReadTextAsync` returns null when the blob is missing. It does not throw for 404.

`WriteJsonAsync` stays overwrite-capable for HEALTHY. System Design must not call it twice for the same path in one task: the ledger hit returns before the write. Do not change HEALTHY's write path.

### 4.4 Events

Add `AgentHub.Core/Models/AgentEvent.cs` and `IAgentEventPublisher` with `PublishAsync(AgentEvent, CancellationToken)`.

Fields: `ContractVersion`, `EventId`, `WorkItemId`, `TaskId`, `Stage`, `RunId`, `Kind`, `SubStepId`, `SubStepOrdinal`, `Message`, `Level`, `OccurredAt`.

`EventId` = `{taskId}:{inputVersion}:{deliveryCount}:{seq}`. That string is also the Service Bus `MessageId`.

Kinds used here: `AgentStarted`, `SubStepStarted`, `SubStepCompleted`, `TokensUsed`, `AgentCompleted`, `AgentFailed`, `AgentHumanActionRequired`.

Implement `ServiceBusAgentEventPublisher` the same way as `ServiceBusAgentResultPublisher`, sending to `Workers:SystemDesign:EventsTopic`. Do not point HEALTHY at it.

### 4.5 Ledger spill (do this in Infrastructure, behind `IEffectLedger`)

If `InputJson` or `OutputJson` is longer than 30,000 characters, write the text to container `agenthub-ledger` at `{workItemId}/{taskId}/{intentKind}_{ordinal:D6}.input.json` or `.output.json`, store the path and the SHA-256 on the row, and clear the inline JSON. `TryGetCommittedAsync` reads the blob back and returns the original text. Design prompts will cross 30,000 characters. Add the two path columns and the two hash columns on `EffectLedgerEntity` if they are not there. Do not change the Add / 409 rule.

### 4.6 Result message id

Add a System Design publisher (or a parameter on a new class). Do not edit `ServiceBusAgentResultPublisher` if that would change HEALTHY's `MessageId` of `{runId}:result`.

System Design result `MessageId` = `result:{taskId}:{inputVersion}`.

---

## 5. Library layout

Create `agenthub-api/src/AgentHub.Agents.SystemDesign/`. Copy `AgentHub.Agents.Healthy.csproj` and change the names. `ProjectReference` is Core only. No Data, no Hosting, no Infrastructure.

Add the project to `agenthub-api/AgentHub.slnx` under `/src/`.

Add `ProjectReference` from `AgentHub.Hosting.csproj` to this library.

| File | Role |
|---|---|
| `SystemDesignAgentConstants.cs` | Names, ordinals, topics |
| `SystemDesignTaskValidation.cs` | Required fields for this stage |
| `SystemDesignAgentHandler.cs` | Sub-step machine |
| `SystemDesignAgentDispatcher.cs` | D-C1 plus cancel and deadline |
| `Design/TechSpecDocument.cs` | The JSON shape in section 6 |
| `Design/DesignValidator.cs` | Deterministic checks |
| `Design/DesignRenderer.cs` | JSON to markdown |
| `Design/SectionWriter.cs` | One LLM call per section, returns a JSON patch |
| `Prompts/system-design-planner.txt` | System prompt |
| `Prompts/system-design-section.txt` | System prompt |
| `Prompts/system-design-critic.txt` | System prompt |

Constants:

```csharp
public const string AgentId = "agenthub-system-design-agent";
public const string StageCode = "SYSTEM_DESIGN";
public const string TaskTopic = "agenthub.system-design.agent";
public const string Subscription = "agenthub-system-design";
public const string TechSpecFile = "techspec.json";
public const string MarkdownFile = "system-design.md";
public const string ArtifactKeyJson = "techSpec";
public const string ArtifactKeyMd = "techSpecMd";
public const int OrdinalPlanBase = 110;
public const int OrdinalCriticBase = 120;
public const int OrdinalSectionBase = 130;
public const int OrdinalTechSpec = 200;
public const int OrdinalMarkdown = 201;
```

Ordinal for plan revision `r` is `110 + r`. Critic is `120 + r`. Section `s` (1-based) at revision `r` is `130 + r * 10 + s`. `r` is 0, 1, or 2.

### 5.1 Validation (`SystemDesignTaskValidation`)

Fail the handler with `Failed` / `MissingInput` when any of these is absent. The dispatcher turns a thrown validation into that result on the **first** delivery as well, because a missing input will not succeed on retry. Do not dead-letter it as malformed JSON. Malformed JSON is still a dead-letter with no result, matching HEALTHY.

Required:

- `Stage` equals `SYSTEM_DESIGN`
- `BlobPrefix` non-empty
- `Artifacts["prd"]` and `Artifacts["intakeSummary"]` non-empty, and both blobs exist
- `Context.TemplateId` non-empty, and `templates` blob `{templateId}/manifest.json` exists. `templateId` may contain `@`. Use the value as the blob name prefix, for example `fe-be-llm@1.0.0/manifest.json`. Do not invent file contents if it is missing.
- At least one blob under `templates` prefix `standards/v1/`. If the prefix is empty, `MissingInput`.
- When `Context.ProjectMode` is `Existing`: `Artifacts["baselineTechSpec"]` and `Artifacts["baselineCodeState"]` exist.

`feedback` is required only when `OutputRevision` is greater than 1. The key is `Artifacts["feedback"]`.

### 5.2 Handler flow

`RunAsync(TaskMessage message, int deliveryCount, CancellationToken deadlineToken, Func<CancellationToken, Task<bool>> cancelled)`.

At the start and before every sub-step, if `cancelled()` is true, return `Failed` / `Cancelled` and do not call the LLM or write blobs.

Sub-steps, in order:

1. `context`. Deterministic. Read the template manifest and the standards prefix (concatenate the text, cap the prompt at the stage budget). Set mode from `Context.ProjectMode`. Log `SubStepStarted` / `SubStepCompleted` with id `context`.
2. `repo-context`, only when mode is `Existing`. Read `baselineCodeState`. This slice does not run Graphify. Pass the JSON through a trimmer that keeps `path`, `name`, and `signature` fields and drops bodies. If the file is already small, pass it whole. Log id `repo-context`.
3. `plan`. LLM. Ledger intent `llm.plan` ordinal `110 + r`. The model returns **one JSON object** matching section 6, not prose. Validate with `DesignValidator` only after write, but reject non-JSON here and repair up to 2 times (new ordinals are not used; the repair is inside the same ledger ordinal, stored as the committed output of the successful parse). If the model says it must assume a product fact, return `HumanActionRequired` with `Signal.Kind = Question` and stop the run.
4. `write`. For each section id in section 6.2, one LLM call, ledger `llm.section` ordinal `130 + r * 10 + s`. The call returns a JSON object with only that section's properties. Merge onto the plan document in C#. Do not let the model return the whole document.
5. `validate`. `DesignValidator.Validate(doc, inputs)`. No LLM. If there are violations and `r + 1 < limits.MaxRevisions`, set `r = r + 1` and go back to step 3 with the violation list in the prompt. If there are violations and no revisions remain, return `HumanActionRequired` / `LoopLimit`. Do not call critique on a dirty document.
6. `critique`. LLM. Ledger `llm.critic` ordinal `120 + r`. Returns `{ "issues": [ "..." ] }`. Empty issues means clean. Non-empty and revisions remain: `r = r + 1`, go to step 4 (`write`), not to a full replan, unless an issue text starts with `plan:`. An issue starting with `plan:` goes back to step 3. No revisions remain: `LoopLimit`.
7. `render`. Deterministic markdown from the document. No ledger LLM row.
8. `publish`. Guardrail-scan both bodies. Write `{BlobPrefix}techspec.json` (ordinal 200) and `{BlobPrefix}system-design.md` (ordinal 201). Markdown is written with `WriteJsonAsync` only if you first add `WriteTextAsync(path, body, contentType)`. Add that method. Content types `application/json` and `text/markdown`.

Token stop: before each LLM call, if input+output so far is at least `min(Budget.StageMaxTokens, Budget.WorkItemRemainingTokens)`, return `HumanActionRequired` / `BudgetExceeded`. Count tokens from the ledger outputs of this task only. A replayed call adds 0.

`humanAnswer`: if it is non-empty, commit ledger intent `answer` ordinal `950` with that text before step 3, and include it in the next plan or section prompt. Do not re-run a step whose ledger row is already committed.

Return `Completed` with:

```text
Artifacts["techSpec"]    = {BlobPrefix}techspec.json
Artifacts["techSpecMd"]  = {BlobPrefix}system-design.md
OutputRevision           = message.OutputRevision
TokenUsage               = sum of calls made this run (replays are 0)
Outputs                  = empty object
```

### 5.3 Prompts

Three text files under `Prompts/`, copied to output the same way as `healthy-system.txt`.

Each prompt says:

- Return JSON only. No markdown fences.
- Stack is the `stackProfile` on the message. Allowed: `fe-be` and `fe-be-llm`. Anything else is a validator failure, not a prompt invention.
- Do not invent packages. Packages must be names the user message lists as allowed (you pass the allowlist from the template manifest).
- Do not invent resource names. Use `bgx-{slug}-be-dev`, `bgx-{slug}-fe-dev`, resource group `rg-bgx-sandbox-dev`, plan `asp-bgx-sandbox-dev`.
- For an existing project, carry every baseline acceptance check forward. Mark a removed check `"retired": true` and include the old id. Never drop one silently.

---

## 6. `techspec.json` shape

`TechSpecDocument` is this object. Property names are camelCase in JSON.

```json
{
  "schemaVersion": "1",
  "stackProfile": "fe-be",
  "templateId": "fe-be@1.0.0",
  "components": [{ "name": "CalculatorApi", "responsibility": "Adds two decimals" }],
  "phases": [{ "id": "1", "title": "Add", "milestone": "POST /api/calculator/add returns a decimal" }],
  "filePlan": [{
    "path": "src/Calculator.Api/CalculatorController.cs",
    "action": "create",
    "symbols": [{ "name": "Add", "kind": "method", "signature": "Task<IActionResult> Add(AddRequest body)" }]
  }],
  "apiContracts": [{
    "method": "POST",
    "path": "/api/calculator/add",
    "status": 200
  }],
  "llmIntegration": null,
  "tddPlan": [{ "phaseId": "1", "tests": ["Add_returns_expected_sum"] }],
  "acceptanceChecks": [{
    "id": "ac-add-decimals",
    "criterionId": "prd-ac-1",
    "method": "POST",
    "path": "/api/calculator/add",
    "expectedStatus": 200,
    "retired": false
  }],
  "backlog": {
    "epicTitle": "Calculator",
    "stories": [{ "key": "add", "title": "Add two numbers", "acceptanceCriterionIds": ["prd-ac-1"] }]
  },
  "deploymentPlan": {
    "resourceGroup": "rg-bgx-sandbox-dev",
    "plan": "asp-bgx-sandbox-dev",
    "apps": [
      { "name": "bgx-calculator-be-dev", "kind": "be" },
      { "name": "bgx-calculator-fe-dev", "kind": "fe" }
    ]
  },
  "commands": {
    "build": "dotnet build",
    "test": "dotnet test"
  },
  "packages": []
}
```

`action` is `create` or `modify`. `retired` defaults to false. `llmIntegration` is null for `fe-be` and a non-null object for `fe-be-llm` (fields `endpoint`, `requestShape`, `responseShape` are enough).

### 6.1 DesignValidator rules

Return a list of strings. Empty means clean.

1. `schemaVersion` is `1`.
2. `stackProfile` is `fe-be` or `fe-be-llm`, and it equals `Context.StackProfile`.
3. Every id in the PRD acceptance criteria (read `acceptanceCriteria[].id` from `prd.json`; if the PRD uses a different array name, also accept `acceptanceChecks[].id`) appears as `criterionId` on a check.
4. Every story `key` has at least one `filePlan` path.
5. Every `filePlan.path` starts with a folder named in the template manifest `layout.roots` array. If the manifest has no `layout.roots`, fail `InvalidInput` on the manifest rather than guessing roots.
6. Existing mode: every `modify` path exists in baseline code-state (`files[].path` or `path`). Every `create` path does not.
7. Existing mode: every baseline acceptance check id is present. A missing id fails unless the document has that id with `retired: true`.
8. Every `packages[].id` is in the manifest `allowlist` array. Empty `packages` is valid.
9. `deploymentPlan.resourceGroup` is `rg-bgx-sandbox-dev`, `plan` is `asp-bgx-sandbox-dev`, and app names are `bgx-{projectSlug}-be-dev` and `bgx-{projectSlug}-fe-dev`. Slug comes from `Context.ProjectSlug`.
10. `fe-be-llm` requires non-null `llmIntegration`. `fe-be` requires null `llmIntegration`.

### 6.2 Section ids for `write`

| s | Id | Included when |
|---|---|---|
| 1 | architecture | always |
| 2 | data | always |
| 3 | api | always |
| 4 | llmIntegration | `stackProfile` is `fe-be-llm` only |
| 5 | tdd | always |
| 6 | acceptance | always |
| 7 | backlog | always |
| 8 | deployment | always |
| 9 | commands | always |

`fe-be` therefore has 8 calls. `fe-be-llm` has 9. Number `s` as in the table, and skip 4 for `fe-be` so ordinals stay stable.

### 6.3 Renderer

`DesignRenderer.ToMarkdown(TechSpecDocument)` returns markdown with one heading per section id. No extra claims. Unit-test that the string contains each non-null section heading and does not contain the file name `techspec.md`.

---

## 7. Dispatcher

Copy `HealthyAgentDispatcher.cs` to `SystemDesignAgentDispatcher.cs`. Keep the same outcomes: `Handled`, `AlreadyProcessed`, `Abandon`, `DeadLetter`.

Differences from HEALTHY, in this order:

1. Deserialize. Malformed JSON: `DeadLetter`, no result.
2. `SystemDesignTaskValidation` failures: build `Failed` / `MissingInput` or `InvalidInput`, publish, mark, `DeadLetter`. Do not abandon.
3. MessageId mismatch: log and continue, same as HEALTHY. Dedup key is the Service Bus `MessageId`.
4. `IsProcessedAsync`: `AlreadyProcessed`.
5. If `{projectId}/{workItemId}/control/cancelled.json` exists in the artifacts container: publish `Failed` / `Cancelled`, mark, `DeadLetter`.
6. Call the handler with a linked token that cancels after `Workers:SystemDesign:HandlerDeadline` (default `00:50:00`). Host shutdown is a different token: on host shutdown, abandon and do not publish.
7. Handler returns a result: publish result with `MessageId` `result:{taskId}:{inputVersion}`, publish terminal event, mark, `Handled`.
8. `GuardrailBlockedException`: publish `Failed` / `GuardrailBlocked`, mark, `DeadLetter`. No abandon.
9. Any other exception: if `deliveryCount < MaxDeliveryCount` (5), `Abandon`. On the last delivery, publish `Failed` / `Unhandled`, mark, `DeadLetter`.

Terminal event kind: `AgentCompleted`, `AgentHumanActionRequired`, or `AgentFailed`, matching `Status`.

Heartbeat: while the handler is running, publish an event kind `Heartbeat` every 60 seconds on the events topic. Failures to publish a heartbeat are logged and do not fail the run.

---

## 8. Host wiring

### 8.1 Worker

Copy `HealthyAgentWorker.cs` to `SystemDesignAgentWorker.cs`.

`SystemDesignWorkerOptions.SectionName` = `Workers:SystemDesign`.

Defaults:

```text
Enabled = false
Topic = agenthub.system-design.agent
Subscription = agenthub-system-design
ResultsTopic = buildgenix.agent-results
EventsTopic = buildgenix.agent-events
MaxDeliveryCount = 5
MaxAutoLockRenewalDuration = 01:00:00
HandlerDeadline = 00:50:00
MaxConcurrentCalls = 1
```

Processor options: `AutoCompleteMessages = false`, `PrefetchCount = 0`, `MaxConcurrentCalls = 1`.

If `Enabled` is false, log once and return. The host must still start. If the connection string is missing and Enabled is true, log an error and return. Do not throw out of `ExecuteAsync`.

In `ProcessMessageAsync`, map dispatcher outcomes the same way `HealthyAgentWorker` does (complete, abandon, dead-letter). Read that method and keep the mapping. Do not autocomplete.

### 8.2 DI

In `AddAgentWorkers`, after the HEALTHY registration:

- `Configure<SystemDesignWorkerOptions>`
- `AddSingleton<SystemDesignAgentHandler>` (inject `ILlmClient`, `IArtifactBlobStore`, `IEffectLedger`, `IActivityLog`, `IGuardrailPipeline`, `IAgentEventPublisher`, logger)
- `AddSingleton<SystemDesignAgentDispatcher>`
- `AddHostedService<SystemDesignAgentWorker>`
- Register the System Design result publisher and event publisher as their own singletons. Do not replace `IAgentResultPublisher` if HEALTHY still uses it. Give the dispatcher a dedicated interface `ISystemDesignResultPublisher` so the two MessageId formulas cannot collide.

### 8.3 Harness endpoint

Copy `HealthyEndpoints.cs` to `SystemDesignEndpoints.cs`.

`POST /api/connectivity/system-design`

This is a local harness. The summary text must say it is not the production path.

Request body:

```json
{
  "projectId": "proj-sd-local",
  "workItemId": "wi-sd-local",
  "taskId": "01JSDTASK",
  "inputVersion": 1,
  "outputRevision": 1,
  "blobPrefix": "proj-sd-local/wi-sd-local/v1/",
  "correlationId": "manual-qa-1",
  "templateId": "fe-be@1.0.0",
  "stackProfile": "fe-be",
  "projectMode": "New",
  "projectSlug": "calculator",
  "prdPath": "proj-sd-local/wi-sd-local/v1/prd.json",
  "intakeSummaryPath": "proj-sd-local/wi-sd-local/v1/intake-summary.json"
}
```

Build a `TaskMessage` from those fields. `Stage` is always `SYSTEM_DESIGN`. Publish with `MessageId` = `{taskId}:{inputVersion}` to `Workers:SystemDesign:Topic`. Return 202 with the message id. Return 400 if validation fails. Return 503 if the sender throws.

Call `MapSystemDesignEndpoints()` from `Program.cs` next to `MapHealthyEndpoints()`.

---

## 9. Emulator and settings

### 9.1 `docker/servicebus-config.json`

Copy the `sdlc-tasks-healthy` topic block three times, with these names and `MaxDeliveryCount` 5:

| Topic | Subscription |
|---|---|
| `agenthub.system-design.agent` | `agenthub-system-design` |
| `buildgenix.agent-results` | `buildgenix-results` |
| `buildgenix.agent-events` | `buildgenix-events` |

Leave the existing HEALTHY topics in place. Recreate the emulator volume after the edit, or the new topics will not appear. The compose file comment already says how the emulator is started. Follow `agenthub-api/README.md`. Do not invent a second compose file.

### 9.2 Development settings

In `appsettings.Development.json` add:

```json
"Workers": {
  "Healthy": { "Enabled": true, "DeliberatePauseMs": 100 },
  "SystemDesign": {
    "Enabled": false,
    "Topic": "agenthub.system-design.agent",
    "Subscription": "agenthub-system-design",
    "ResultsTopic": "buildgenix.agent-results",
    "EventsTopic": "buildgenix.agent-events",
    "MaxDeliveryCount": 5,
    "MaxAutoLockRenewalDuration": "01:00:00",
    "HandlerDeadline": "00:50:00",
    "MaxConcurrentCalls": 1
  }
}
```

The results topic name is OPEN item 1 in the guide. The default above is the pack name. Do not rename HEALTHY's `sdlc-results`. For the Manual QA run, set `Workers:SystemDesign:Enabled` to true with `dotnet user-secrets` or an environment variable, not by committing `Enabled: true`.

`Llm:Provider` stays `Fixture` in the committed file. Set `Ollama` only for the Manual QA process environment.

### 9.3 README

Add a short section to `agenthub-api/README.md`:

- How to enable the worker
- The harness URL and the sentence that it is not production
- The Ollama steps in section 11 of this prompt
- OPEN item 1 still open

---

## 10. Tests

Create `agenthub-api/tests/AgentHub.Agents.SystemDesign.Tests/` from the HEALTHY test csproj. Reference the new library and Core. Add it to `AgentHub.slnx`.

Put fixture JSON under `testdata/`:

- `new-fe-be/prd.json`, `intake-summary.json`, `manifest.json`, `standards.txt`
- `existing-modify/prd.json`, `baseline-techspec.json`, `code-state.json`

`prd.json` for the new fixture has two acceptance criteria ids: `prd-ac-1` (decimals) and `prd-ac-2` (non-numeric returns 400). The existing fixture's baseline techspec contains check id `ac-add-decimals`.

| Test | Assert |
|---|---|
| `Validator_rejects_unknown_stack` | violation list non-empty |
| `Validator_rejects_missing_criterion` | mentions the missing id |
| `Validator_rejects_create_that_collides` | existing mode, path already in code-state |
| `Validator_rejects_dropped_baseline_check` | baseline id absent and not retired |
| `Validator_accepts_retired_baseline_check` | same id with `retired: true` is clean |
| `Validator_rejects_bad_app_name` | name is not `bgx-{slug}-be-dev` |
| `Renderer_does_not_emit_techspec_md` | markdown has no `techspec.md` |
| `Handler_loops_to_plan_on_validator_errors_then_limits` | Fixture LLM always returns a document that fails rule 2; result is `HumanActionRequired`, `Signal.Kind` is `LoopLimit`, LLM plan ordinals 110, 111, 112 exist, ordinal 113 does not |
| `Handler_does_not_critique_a_dirty_document` | critic ordinal 120 is absent when validation never passes |
| `Dispatcher_publishes_then_marks` | fake publisher and dedup record the call order |
| `Dispatcher_guardrail_block_does_not_abandon` | outcome is DeadLetter, status Failed, errorCode GuardrailBlocked |
| `Dispatcher_cancel_marker_publishes_cancelled` | ExistsAsync true, errorCode Cancelled, LLM not called |
| `Dispatcher_last_delivery_publishes_unhandled` | throwing handler, delivery 5, errorCode Unhandled |
| `Dispatcher_malformed_json_has_no_result` | DeadLetter, publisher call count 0 |
| `Blob_prefix_comes_from_the_message` | written path starts with the given prefix and ends with `techspec.json` |
| `Message_round_trip_keeps_output_revision` | JSON from guide section 3.3 deserializes `OutputRevision` 1 and does not copy it from `InputVersion` |

A skipped test fails CI. Do not `[Fact(Skip=...)]`.

HEALTHY tests must still pass. If a new required constructor argument breaks HEALTHY, you added the dependency in the wrong place.

---

## 11. Manual QA (Ollama, actual)

Follow `docs/manual-e2e-means-actual.md`. Fixture is not this run.

1. Start Azurite and the Service Bus emulator the way `agenthub-api/README.md` starts them. Wait until health is green.
2. Confirm the emulator has topic `agenthub.system-design.agent` and subscription `agenthub-system-design`. If not, the config edit was not loaded. Fix that before continuing.
3. Create containers `agenthub-artifacts`, `templates`, and `agenthub-ledger` in Azurite.
4. Upload `testdata/new-fe-be/*` to the paths you will send in the harness body. Upload `templates/fe-be@1.0.0/manifest.json` and one file under `templates/standards/v1/`.
5. Start Ollama with the model you already use for HEALTHY Manual QA. Set `Llm:Provider=Ollama` and `Workers:SystemDesign:Enabled=true` in the process environment.
6. `dotnet run` the WebApi project.
7. `curl -sS -X POST http://127.0.0.1:<port>/api/connectivity/system-design` with the JSON in section 8.3, paths adjusted to the blobs you uploaded. Expect 202.
8. Wait until a result is on `buildgenix.agent-results` (or the topic you configured). Download both blobs. The prefix must equal `blobPrefix`. The markdown name is `system-design.md`.
9. In the logs, each of `context`, `plan`, `write`, `validate`, `critique`, `render`, `publish` appears as a sub-step. `repo-context` appears only on the existing-project run.
10. Table check: new rows only in `EffectLedger`, `ActivityLog`, `AgProcessedEvents`. No buildgenix table writes.
11. Republish the same `taskId` and `inputVersion`. The second delivery completes as already processed and does not call Ollama again.
12. Upload `control/cancelled.json` for a new task id and publish again. The result is `Failed` with `errorCode` `Cancelled`, and no new techspec blob appears for that task.
13. One redraft: upload `feedback` , publish `outputRevision` 2 and a new `taskId`, with `blobPrefix` ending in `v2/`. The v1 blobs are unchanged.

Write the evidence under `docs/qa/manual-qa-system-design-ollama-2026-10-07.md` with the commands you ran and the blob paths. If the AI Gateway consumer id is still missing, the Gateway run is **BLOCKED**, not PASS. Ollama local is the pass for this slice.

---

## 12. Done for this slice

- [ ] `dotnet test agenthub-api/AgentHub.slnx` passes, HEALTHY included
- [ ] DesignValidator tests in section 10 pass
- [ ] Emulator has the three new topics
- [ ] Ollama Manual QA section 11 has an evidence file
- [ ] README mentions the harness and OPEN item 1
- [ ] No `techspec.md` anywhere in the new library
- [ ] Worker default Enabled is false in committed config
- [ ] Implementation notes in the PR or commit body say MIG-7 (shared dispatcher extraction) is follow-up, not done, if you copied the dispatcher instead of extracting it

---

## 13. Open items (do not close them)

From guide section 16.1. The ones that touch this slice:

1. Inbound result and event topic names. Use the defaults in section 9.2 until Badru confirms.
7. Model id per agent. Read `Config.ModelId` from the message. Do not hard-code `opus`.
9. AI Gateway consumer id. Gateway Manual QA stays BLOCKED until it exists.

Everything else in section 16.1 is another slice.

---

## 14. Paste block

```text
Implement the System Design agent in https://github.com/badrusiddique/agent-sdlc only. Never ai-sdlc.
Follow docs/prompts/i3-system-design-agent-spoon-feed.md from the top. Do not skip the Core delta in section 4.
Scope: AgentHub.Agents.SystemDesign plus the Core and blob-read seams that file names. HEALTHY stays green.
Authority: docs/architecture/phase-0-implementation-guide.md sections 3, 5.1, 8.2, 8.7, 8.8.
Out of scope: PRD, Implementation, Testing, Deployment, buildgenix-api, closing guide section 16.1.
Done: dotnet test on AgentHub.slnx, then Ollama Manual QA per section 11 of the spoon-feed. Fixture is not Manual QA.
Stop and ask if a required blob path or a topic name is not in that file.
```

# Spoon-feed: implement the System Design agent in agenthub-api

**Purpose:** Paste this file into the coding agent on the other machine. It is the slice 7 start instructions. Implement only `AgentHub.Agents.SystemDesign` and the Core seams it needs. Do not implement PRD, Implementation, Testing, Deployment, or buildgenix-api.

**Not coding GO.** Start only after Badru's explicit GO for slice 7 (guide section 14). A pack gate or this paste block does not authorize implementation.

**Repo:** `https://github.com/badrusiddique/agent-sdlc` only. Never `ai-sdlc`.

**Date:** 2026-10-08 (gate fixes on tip after 1fdd2a5)

**Authority for this slice:** `docs/architecture/phase-0-implementation-guide.md` sections 3, 5.1, 6.3, 8.2, 8.7 and 8.8. Where this prompt and an older note disagree, the guide wins. The older prompt `docs/prompts/i3-system-design-agent-implementation.md` is background. This file is the one to follow.

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
8. Run `IGuardrailPipeline` on every LLM input and output and on both artifact bodies before the blob write, using a System Design instance built with `new LocalGuardrailPipeline(new GuardrailLimits(MaxInputChars: 400_000, MaxOutputChars: 200_000, AllowMarkdownHeadings: true))`. Add `public sealed record GuardrailLimits(int MaxInputChars, int MaxOutputChars, bool AllowMarkdownHeadings)` to `AgentHub.Core/Guardrails/GuardrailModels.cs` and a second constructor on `LocalGuardrailPipeline`. The parameterless constructor keeps 8000, 4000 and headings blocked, so HEALTHY and `LocalGuardrailPipelineTests` do not change. Read the limits from `Workers:SystemDesign:Guardrails`. Construct the instance in the `SystemDesignAgentHandler` factory in `AddAgentWorkers`; do not replace the `IGuardrailPipeline` singleton. Block becomes `Failed` with `errorCode` `GuardrailBlocked`, then mark and dead-letter, no retry.
9. Leave `Workers:SystemDesign:Enabled` false in committed `appsettings.json`. For Manual QA set `Workers__SystemDesign__Enabled=true` (bash-safe) after `dotnet user-secrets init` if needed, or use process env; never commit `Enabled: true`.

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

### 4.3 Blob access (do not extend `IArtifactBlobStore`)

Do not change `IArtifactBlobStore`; HEALTHY test fakes implement only `WriteJsonAsync`. Add `AgentHub.Core/Abstractions/IArtifactBlobAccess.cs`:

```csharp
public interface IArtifactBlobAccess : IArtifactBlobStore
{
    Task<bool> ExistsAsync(string blobPath, string? containerName, CancellationToken cancellationToken = default);
    Task<string?> TryReadTextAsync(string blobPath, string? containerName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListAsync(string prefix, string? containerName, CancellationToken cancellationToken = default);
    Task<bool> TryCreateTextAsync(string blobPath, string body, string contentType, string? containerName, CancellationToken cancellationToken = default);
}
```

`containerName` null means `BlobStorage:ContainerName`, which must name the same container buildgenix-api writes: **`artifacts`** (guide sections 2.2, 5.1, 13). The code default today may still say `agenthub-artifacts`; set `BlobStorage:ContainerName` to `artifacts` in `appsettings.json`, `appsettings.Development.json` and the `BlobStorageOptions` default, then keep HEALTHY tests green. Pass `"templates"` (or `BlobStorage:TemplatesContainerName`, default `templates`) for the manifest and the standards pack.

`TryReadTextAsync` returns null when the blob is missing. It does not throw for 404. `TryCreateTextAsync` uploads with `If-None-Match: *` and returns false on 409. System Design writes `techspec.json` and `system-design.md` only through it. New blob methods never call `CreateIfNotExistsAsync`.

`AzureArtifactBlobStore` keeps its `BlobServiceClient` and implements `IArtifactBlobAccess`. Register `services.AddSingleton<AzureArtifactBlobStore>()`, then `IArtifactBlobStore` and `IArtifactBlobAccess` both resolving to it. System Design injects `IArtifactBlobAccess` only. `WriteJsonAsync` stays overwrite-capable for HEALTHY.

### 4.4 Events

Add `AgentHub.Core/Models/AgentEvent.cs` and `IAgentEventPublisher` with `PublishAsync(AgentEvent, CancellationToken)`.

Fields exactly as the guide section 3.5 JSON: `contractVersion`, `eventId`, `workItemId`, `taskId`, `stage`, `runId`, `kind`, `subStep` `{ id, ordinal, label, iteration, of }`, `progress`, `tokens`, `message`, `level`, `occurredAt`.

`EventId` = `{taskId}:{inputVersion}:{deliveryCount}:{seq}`. That string is also the Service Bus `MessageId`. The dispatcher creates one `RunEventSequence` per delivery (`Next()` uses `Interlocked.Increment`, first value 1) and passes it to the handler so heartbeat and sub-step events cannot collide.

Kinds used here: `AgentStarted`, `Heartbeat`, `SubStepStarted`, `SubStepCompleted`, `TokensUsed`, `AgentCompleted`, `AgentFailed`, `AgentHumanActionRequired`.

Implement `ServiceBusAgentEventPublisher` the same way as `ServiceBusAgentResultPublisher`, sending to `Workers:SystemDesign:EventsTopic`. Lazy-create the Service Bus client on first publish so `Enabled=false` still lets the host start. Do not point HEALTHY at it.

### 4.5 Ledger spill (do this in Infrastructure, behind `IEffectLedger`)

If `InputJson` or `OutputJson` is longer than 30,000 characters, and always for the input of an LLM intent, write the text before the row `Add` to container `BlobStorage:LedgerContainerName` (default `agenthub-ledger`) at `{workItemId}/{taskId}/{intentKind}_{ordinal:D6}.input.json` or `.output.json`, with `If-None-Match: *`. On 409, read the existing blob and use it. Store columns `InputBlobPath`, `InputSha256`, `OutputBlobPath`, `OutputSha256` (lowercase hex) on the row and set the inline JSON to null. `TryGetCommittedAsync` and the 409 path of `CommitAsync` both read the blob back, check the SHA-256, and throw on a mismatch. Design prompts will cross 30,000 characters. Do not change the Add / 409 rule.

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
public const string IntentLlm = "LLM";
public const string IntentWriteArtifact = "WriteArtifact";
public const string IntentAsk = "Ask";
public const string IntentAnswer = "Answer";
public const int OrdinalPlanBase = 110;
public const int OrdinalCriticBase = 120;
public const int OrdinalSectionBase = 130;
public const int OrdinalTechSpec = 200;
public const int OrdinalMarkdown = 201;
public const int OrdinalAskBase = 900;
public const int OrdinalAnswerBase = 950;
```

Ordinal for plan revision `r` is `110 + r`. Critic is `120 + r`. Section `s` (1-based) at revision `r` is `130 + r * 10 + s`. `r` is 0, 1, or 2 (at most 3 plans per run: 110, 111, 112). Cap `r` with `min(Limits.MaxRevisions, 3) - 1`.

### 5.1 Validation (`SystemDesignTaskValidation`)

Fail the handler with `Failed` / `MissingInput` when any of these is absent. The dispatcher turns a thrown validation into that result on the **first** delivery as well, because a missing input will not succeed on retry. Do not dead-letter it as malformed JSON. Malformed JSON is still a dead-letter with no result, matching HEALTHY.

Required:

- `Stage` equals `SYSTEM_DESIGN`
- `BlobPrefix` non-empty and ends with `/`; also `BlobPrefix` equals `{ProjectId}/{WorkItemId}/v{OutputRevision}/` exactly and `OutputRevision` is at least 1, otherwise `Failed` / `InvalidInput`. Never derive the prefix from `InputVersion`.
- `Artifacts["prd"]` and `Artifacts["intakeSummary"]` non-empty, and both blobs exist in the `artifacts` container
- `Context.TemplateId` non-empty, and `templates` blob `{templateId}/manifest.json` exists. `templateId` may contain `@`. Use the value as the blob name prefix, for example `fe-be-llm@1.0.0/manifest.json`. Do not invent file contents if it is missing.
- At least one blob under `templates` prefix `standards/v1/` (use `ListAsync`). If the prefix is empty, `MissingInput`.
- When `Context.ProjectMode` is `Existing`: `Artifacts["baselineTechSpec"]` and `Artifacts["baselineCodeState"]` exist.

`feedback` is required only when `OutputRevision` is greater than 1. The key is `Artifacts["feedback"]`. If `Artifacts["techSpec"]` is present (redraft), load it with the feedback into the plan prompt.

### 5.2 Handler flow

`RunAsync(TaskMessage message, int deliveryCount, RunEventSequence events, CancellationToken deadlineToken, Func<CancellationToken, Task<bool>> cancelled)`.

At the start and before every sub-step, if `await cancelled(deadlineToken)` is true, return `Failed` / `Cancelled` and do not call the LLM or write blobs.

Sub-steps, in order:

1. `context`. Deterministic. Read `{templateId}/manifest.json` with `TryReadTextAsync(path, "templates")`. Call `ListAsync("standards/v1/", "templates")`, read each blob in ordinal name order, and join with a blank line (cap the prompt at the stage budget). Set mode from `Context.ProjectMode`. Log `SubStepStarted` / `SubStepCompleted` with id `context`.
2. `repo-context`, only when mode is `Existing`. Read `baselineCodeState`. This slice does not run Graphify. Pass the JSON through a trimmer that keeps `path`, `name`, and `signature` fields and drops bodies. If the file is already small, pass it whole. Log id `repo-context`.
3. `plan`. LLM. Ledger intent `LLM` ordinal `110 + r`. The model returns **one JSON object** matching section 6, not prose. Every LLM call in this agent returns JSON. Parse before committing. On a parse failure re-ask with the parse error, at most 2 more times inside the same ordinal. Commit only the parsed attempt, with the token sum of all attempts. If the third attempt is not JSON, record the violation `model output was not JSON` for this revision and continue as in `validate`. The planner and the critic return an optional top-level `"question": string or null`. When it is non-null and no `Answer` row exists for that question, commit `Ask` at `900 + q` (q = number of `Ask` rows already in this task, 0-based) with `{ question, askedAtInputVersion }`, then return `HumanActionRequired`, `Signal.Kind = Question`, `HumanMessage` = the question. A fourth question returns `LoopLimit`. A replayed output whose question already has an `Answer` row is never asked again.
4. `write`. For each section id in section 6.2, one LLM call, ledger `LLM` ordinal `130 + r * 10 + s`. The call returns a JSON object with only that section's properties (see section map after 6.2). Merge onto the plan document in C#. Do not let the model return the whole document. Plan returns only `components`, `phases`, `filePlan`, `apiContracts`.
5. `validate`. `DesignValidator.Validate(doc, inputs)`. No LLM. Violations go back to `plan` while another revision is allowed (`r` is 0 or 1; at most 3 plans: 110–112) and do not call critique. A violation on revision 2 is `HumanActionRequired` `LoopLimit`. An answered question grants one extra revision (guide 6.3).
6. `critique`. LLM. Ledger `LLM` ordinal `120 + r`. Returns `{ "issues": [ "..." ], "question": null }`. Empty issues means clean. Non-empty and revisions remain: `r = r + 1`, go to step 4 (`write`), unless an issue text starts with `plan:`, which goes back to step 3. Critic may also ask via `question` (same Ask rules as plan). No revisions remain: `LoopLimit`.
7. `render`. Deterministic markdown from the document. No ledger LLM row.
8. `publish`. Guardrail-scan both bodies with the System Design pipeline instance. Write `{BlobPrefix}techspec.json` (`WriteArtifact` ordinal 200) and `{BlobPrefix}system-design.md` (`WriteArtifact` ordinal 201) via `TryCreateTextAsync`. Content types `application/json` and `text/markdown`.

Token stop: each LLM row's `OutputJson` stores `inputVersion` and the summed tokens of every attempt in that row. Before each call: if the sum over all LLM rows of this `taskId` is at least `Budget.StageMaxTokens`, or the sum over rows whose `inputVersion` equals `message.InputVersion` is at least `Budget.WorkItemRemainingTokens`, return `HumanActionRequired` / `BudgetExceeded`. `AgentResult.TokenUsage` counts only calls made in this delivery.

`humanAnswer`: if non-empty and `InputVersion` is greater than `askedAtInputVersion`, commit ledger intent `Answer` at ordinal `950 + q` before step 3, set `r` to the revision after the one that asked, raise the revision cap by one (guide 6.3), and continue at `plan` (asked in plan) or `write` (asked in critique) with every committed answer in the prompt. Do not re-run a step whose ledger row is already committed.

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
    "storyKeys": ["add"],
    "symbols": [{ "name": "Add", "kind": "method", "signature": "Task<IActionResult> Add(AddRequest body)" }]
  }],
  "apiContracts": [{
    "method": "POST",
    "path": "/api/calculator/add",
    "status": 200,
    "requestShape": { "a": "number", "b": "number" },
    "responseShape": { "result": "number" },
    "errors": [{ "status": 400, "shape": { "error": "string" } }]
  }],
  "dataModel": [],
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
    "smoke": { "method": "GET", "path": "/health", "body": null, "expectedStatus": 200 },
    "apps": [
      { "name": "bgx-calculator-be-dev", "kind": "be" },
      { "name": "bgx-calculator-fe-dev", "kind": "fe" }
    ]
  },
  "commands": {
    "build": "dotnet build",
    "test": "dotnet test",
    "run": "dotnet run --project src/Calculator.Api"
  },
  "packages": []
}
```

`action` is `create` or `modify`. Story `status` is `added`, `changed`, `unchanged`, or `retired`. `retired` on a check defaults to false. `llmIntegration` is null for `fe-be` and a non-null object for `fe-be-llm` (fields `endpoint`, `requestShape`, `responseShape` are enough; `endpoint` must equal the configured AI Gateway route; the techspec never carries a key).

**Provisional input contracts owned by this slice** (record as OPEN in implementation notes; PRD agent and template authoring must adopt or change them): `prd.json` has `acceptanceCriteria: [{ "id", "text" }]`; `manifest.json` has `layout.roots: string[]` and `allowlist: [{ "id", "version" }]`; techspec `packages: [{ "id", "version" }]`. Fixture `new-fe-be/manifest.json` is exactly `{ "templateId": "fe-be@1.0.0", "layout": { "roots": ["src/", "tests/"] }, "allowlist": [] }`. Commands in the techspec must equal the template manifest commands; the model never invents build/test/run strings.

### 6.1 DesignValidator rules

Return a list of strings. Empty means clean.

1. `schemaVersion` is `1`.
2. `stackProfile` is `fe-be` or `fe-be-llm`, and it equals `Context.StackProfile`.
3. Every id in the PRD acceptance criteria (read `acceptanceCriteria[].id` from `prd.json`) appears as `criterionId` on a check.
4. Every story whose `status` is `added` or `changed` has at least one `filePlan` path whose `storyKeys` contains it; carried-forward (`unchanged`) stories need none.
5. Every `filePlan.path` starts with a folder named in the template manifest `layout.roots` array. The `context` step fails `InvalidInput` when the manifest has no `layout.roots`; the validator assumes it is present.
6. Existing mode: every `modify` path exists in baseline code-state (`files[].path` or `path`). Every `create` path does not.
7. Existing mode: every baseline acceptance check id is present. A missing id fails unless the document has that id with `retired: true`.
8. Every `packages[].id` is in the manifest `allowlist` array. Empty `packages` is valid. Every `commands` value equals the matching manifest command.
9. `deploymentPlan.resourceGroup` is `rg-bgx-sandbox-dev`, `plan` is `asp-bgx-sandbox-dev`, and app names are `bgx-{projectSlug}-be-dev` and `bgx-{projectSlug}-fe-dev`. Slug comes from `Context.ProjectSlug`.
10. `fe-be-llm` requires non-null `llmIntegration` with `endpoint` equal to the configured Gateway route. `fe-be` requires null `llmIntegration`.
11. Every acceptance check has `method`, `path`, `expectedStatus`, and `expectedShape`. Every `dataModel[].storage` is `TableStorage`.

### 6.2 Section ids for `write`

| s | Id | Included when | Properties replaced |
|---|---|---|---|
| 1 | architecture | always | `components`, `phases` |
| 2 | data | always | `dataModel` |
| 3 | api | always | `apiContracts` |
| 4 | llmIntegration | `stackProfile` is `fe-be-llm` only | `llmIntegration` |
| 5 | tdd | always | `tddPlan` |
| 6 | acceptance | always | `acceptanceChecks` |
| 7 | backlog | always | `backlog` |
| 8 | deployment | always | `deploymentPlan`, `packages` |
| 9 | commands | always | `commands` |

`fe-be` therefore has 8 calls. `fe-be-llm` has 9. Number `s` as in the table, and skip 4 for `fe-be` so ordinals stay stable. No section returns `filePlan` (that comes from `plan`).

### 6.3 Renderer

`DesignRenderer.ToMarkdown(TechSpecDocument)` returns markdown with one heading per section id. No extra claims. Unit-test that the string contains each non-null section heading and does not contain the file name `techspec.md`.

---

## 7. Dispatcher

Copy `HealthyAgentDispatcher.cs` to `SystemDesignAgentDispatcher.cs`. Keep the same outcomes: `Handled`, `AlreadyProcessed`, `Abandon`, `DeadLetter`.

Differences from HEALTHY, in this order (D-C1: structural checks and dedup before blob I/O):

1. Deserialize. Malformed JSON: `DeadLetter`, no result.
2. Structural checks with no I/O (`message.Validate()`, `Stage == SYSTEM_DESIGN`): `DeadLetter`, no result, as HEALTHY does for wrong stage.
3. MessageId mismatch: log and continue, same as HEALTHY. Dedup key is the Service Bus `MessageId`.
4. `IsProcessedAsync`: `AlreadyProcessed`.
5. If `{projectId}/{workItemId}/control/cancelled.json` exists in the container named by `BlobStorage:ContainerName` (`artifacts`; never `templates` or `agenthub-ledger`): publish `Failed` / `Cancelled`, terminal event, mark, `Handled` (complete). The handler's `cancelled` delegate reads the same path in the same container.
6. Publish `AgentStarted`, then run `SystemDesignTaskValidation` (blob checks). On failure publish `Failed` / `MissingInput` or `InvalidInput`, terminal event, mark, `Handled`, on any delivery.
7. Call the handler with a linked token that cancels after `Workers:SystemDesign:HandlerDeadline` (default `00:48:00` = min(cap − 10 minutes, 0.8 × cap) for a 60-minute lock). Parse `HandlerDeadline` as `TimeSpan` options binding; do not fall back to 2 minutes on bad config (fail worker start instead). Host shutdown is a different token: on host shutdown, abandon and do not publish.
8. If the deadline token fired and the host token did not: publish `Failed` / `DeadlineExceeded`, publish `AgentFailed`, mark, return `Handled` (complete the message; never abandon). Pass `handlerDeadline` into `ProcessAsync` the same way HEALTHY passes `maxDeliveryCount`.
9. Handler returns a result: publish result with `MessageId` `result:{taskId}:{inputVersion}`, publish terminal event, mark, `Handled`. When a delivery has already published a result, dead-letter only with reason `ResultPublished` if the host requires a dead-letter settle; the watcher only logs those. Cancelled from either check: publish, terminal event, mark, `Handled` (complete).
10. `GuardrailBlockedException`: publish `Failed` / `GuardrailBlocked`, mark, `DeadLetter`. No abandon.
11. Any other exception: if `deliveryCount < MaxDeliveryCount` (5), `Abandon`. On the last delivery, publish `Failed` / `Unhandled`, mark, `DeadLetter`.

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
HandlerDeadline = 00:48:00
MaxConcurrentCalls = 1
```

Processor options: `AutoCompleteMessages = false`, `PrefetchCount = 0`, `MaxConcurrentCalls = 1`.

If `Enabled` is false, log once and return. The host must still start (lazy Service Bus clients on first publish). If the connection string is missing and Enabled is true, log an error and return. Do not throw out of `ExecuteAsync`.

In `ProcessMessageAsync`, map dispatcher outcomes the same way `HealthyAgentWorker` does (complete, abandon, dead-letter). Read that method and keep the mapping. Do not autocomplete.

### 8.2 DI

In `AddAgentWorkers`, after the HEALTHY registration:

- `Configure<SystemDesignWorkerOptions>`
- Register `SystemDesignAgentHandler` via factory that constructs the SD-scoped `LocalGuardrailPipeline(GuardrailLimits)` (inject `ILlmClient`, `IArtifactBlobAccess`, `IEffectLedger`, `IActivityLog`, that pipeline instance, `IAgentEventPublisher`, logger). Do not replace the HEALTHY `IGuardrailPipeline` singleton.
- `AddSingleton<SystemDesignAgentDispatcher>`
- `AddHostedService<SystemDesignAgentWorker>`
- Put `IAgentEventPublisher` and `ISystemDesignResultPublisher` in `AgentHub.Core/Abstractions/`. Put `ISystemDesignTaskPublisher` and its Service Bus impl in Infrastructure. Register the System Design result publisher and event publisher as their own singletons. Do not replace `IAgentResultPublisher` if HEALTHY still uses it.

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
  "intakeSummaryPath": "proj-sd-local/wi-sd-local/v1/intake-summary.json",
  "projectModeExisting": {
    "baselineTechSpecPath": null,
    "baselineCodeStatePath": null
  },
  "feedbackPath": null,
  "humanAnswer": null
}
```

Map optional paths to `Artifacts["baselineTechSpec"]`, `Artifacts["baselineCodeState"]`, and `Artifacts["feedback"]`. Build a `TaskMessage` from those fields. `Stage` is always `SYSTEM_DESIGN`. Publish with `MessageId` = `{taskId}:{inputVersion}` to `Workers:SystemDesign:Topic`. Return 202 with the message id. Return 400 if validation fails. Return 503 if the sender throws.

Call `MapSystemDesignEndpoints()` from `Program.cs` next to `MapHealthyEndpoints()`. Map harness endpoints only in `Development`, never on the builder deployment.

---

## 9. Emulator and settings

### 9.1 `docker/servicebus-config.json`

Copy the `sdlc-tasks-healthy` topic block three times, with these names and `MaxDeliveryCount` 5:

| Topic | Subscription |
|---|---|
| `agenthub.system-design.agent` | `agenthub-system-design` |
| `buildgenix.agent-results` | `buildgenix-api` |
| `buildgenix.agent-events` | `buildgenix-api` |

Subscription properties for the new topics: `LockDuration` `PT5M`, `DeadLetteringOnMessageExpiration` true, `MaxDeliveryCount` 5 (guide section 3.2). Leave the existing HEALTHY topics in place. After the edit, `docker compose up -d --force-recreate servicebus-emulator` so the emulator reloads config. Do **not** `down -v` for this (that wipes Azurite data). Follow `agenthub-api/README.md`. Do not invent a second compose file.

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
    "HandlerDeadline": "00:48:00",
    "MaxConcurrentCalls": 1
  }
}
```

The results topic name is OPEN item 1 in the guide. The default above is the pack name. Do not rename HEALTHY's `sdlc-results`. For the Manual QA run: `dotnet user-secrets init` if the WebApi project has no secrets id yet, then set `Workers__SystemDesign__Enabled=true` and `Llm__Provider=Ollama` in the process environment (or user-secrets). Never commit `Enabled: true`.

`Llm:Provider` stays `Fixture` in the committed file. Set `Ollama` only for the Manual QA process environment. Use an Ollama model of at least 7B with a 32k context and record the exact tag; `llama3.2:1b` is not expected to produce a valid techspec.

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
| `Handler_loops_to_plan_on_validator_errors_then_limits` | A test `ILlmClient` fake that always returns a document failing rule 2; result is `HumanActionRequired`, `Signal.Kind` is `LoopLimit`; LLM plan ordinals 110, 111, 112 exist, 113 does not; critic ordinals 120–122 absent; section ordinals present for the attempts that ran |
| `Handler_does_not_critique_a_dirty_document` | critic ordinals 120 to 122 are all absent when validation never passes |
| `Handler_completes_and_writes_both_files` | fake LLM returns a valid document; result `Completed`; `techspec.json` and `system-design.md` under the prefix; `WriteArtifact` rows 200 and 201; both bodies pass the System Design `LocalGuardrailPipeline` instance |
| `Validation_rejects_prefix_not_matching_output_revision` | prefix ending `v4/` with `OutputRevision` 1 is `InvalidInput` |
| `Dispatcher_publishes_then_marks` | fake publisher and dedup record the call order |
| `Dispatcher_guardrail_block_does_not_abandon` | outcome is DeadLetter, status Failed, errorCode GuardrailBlocked |
| `Dispatcher_cancel_marker_publishes_cancelled` | ExistsAsync true, errorCode Cancelled, LLM not called, settle Handled |
| `Dispatcher_deadline_publishes_deadline_exceeded` | deadline token fires; Failed / DeadlineExceeded; Handled (complete) |
| `Dispatcher_last_delivery_publishes_unhandled` | throwing handler, delivery 5, errorCode Unhandled |
| `Dispatcher_malformed_json_has_no_result` | DeadLetter, publisher call count 0 |
| `Blob_prefix_comes_from_the_message` | written path starts with the given prefix and ends with `techspec.json` |
| `Message_round_trip_keeps_output_revision` | JSON from guide section 3.3 deserializes `OutputRevision` 1 and does not copy it from `InputVersion` |

A skipped test fails CI. Do not `[Fact(Skip=...)]`.

HEALTHY tests must still pass. If a new required constructor argument breaks HEALTHY, you added the dependency in the wrong place.

---

## 11. Manual QA (Ollama, actual)

Follow `docs/manual-e2e-means-actual.md`. Fixture is not this run.

1. Start Azurite and the Service Bus emulator the way `agenthub-api/README.md` starts them. Wait until health is green. WebApi listens on port `18766` in Development (`launchSettings.json`).
2. Confirm the emulator has topic `agenthub.system-design.agent` and subscription `agenthub-system-design`, and inbound subscriptions `buildgenix-api` on both result and event topics. If not, force-recreate the emulator container (section 9.1).
3. Create containers `artifacts`, `templates`, and `agenthub-ledger` in Azurite.
4. Upload `testdata/new-fe-be/*` to the paths you will send in the harness body. Upload `templates/fe-be@1.0.0/manifest.json` and one file under `templates/standards/v1/`.
5. Start Ollama with a model of at least 7B / 32k context. Set `Llm__Provider=Ollama` and `Workers__SystemDesign__Enabled=true` in the process environment.
6. `dotnet run` the WebApi project.
7. `curl -sS -X POST http://127.0.0.1:18766/api/connectivity/system-design` with the JSON in section 8.3, paths adjusted to the blobs you uploaded. Expect 202.
8. Wait until a result is on `buildgenix.agent-results` (peek with a short Service Bus receive/peek script or the existing connectivity tools; if none exist, add a throwaway peek in the harness README notes). Download both blobs. The prefix must equal `blobPrefix`. The markdown name is `system-design.md`.
9. In the logs, each of `context`, `plan`, `write`, `validate`, `critique`, `render`, `publish` appears as a sub-step. `repo-context` appears only on the existing-project run.
10. Table check: new rows only in `EffectLedger`, `ActivityLog`, `AgProcessedEvents`. No buildgenix table writes. Host stays up after a forced handler fault (correlation id in logs).
11. Republish the same `taskId` and `inputVersion`. The second delivery completes as already processed and does not call Ollama again.
12. Use a second work item for cancel (`wi-sd-cancel`, prefix `proj-sd-local/wi-sd-cancel/v1/`). Upload `proj-sd-local/wi-sd-cancel/control/cancelled.json` to `artifacts`, publish a task for that work item, and expect `Failed` with `errorCode` `Cancelled` and no blob under that prefix.
13. One redraft on `wi-sd-local`: upload `feedback`, publish `outputRevision` 2 and a new `taskId`, with `blobPrefix` ending in `v2/`. The v1 blobs are unchanged. Run this after step 12 so the cancel marker on the other work item cannot cancel it.
14. Examples 1 to 3 of the guide (including `fe-be-llm` and example 3 existing mode) must pass DesignValidator end to end before claiming slice 7 Done when (guide section 14). Local Ollama proves the happy path; Fixture proves plumbing only.

Write the evidence under `docs/qa/manual-qa-system-design-ollama-2026-10-08.md` with the commands you ran and the blob paths. If the AI Gateway consumer id is still missing, the Gateway run is **BLOCKED**, not PASS. Ollama local is the pass for this slice.

---

## 12. Done for this slice

- [ ] `dotnet test agenthub-api/AgentHub.slnx` passes, HEALTHY included
- [ ] DesignValidator tests in section 10 pass, including the happy-path handler write test
- [ ] Emulator has the System Design topic and `buildgenix-api` subscriptions on results/events
- [ ] Ollama Manual QA section 11 has an evidence file (actual stack; Fixture is never Manual QA)
- [ ] README mentions the harness and OPEN item 1
- [ ] No `techspec.md` anywhere in the new library
- [ ] Worker default Enabled is false in committed config; `BlobStorage:ContainerName` is `artifacts`
- [ ] Implementation notes in the PR or commit body say MIG-7 (shared dispatcher extraction) is follow-up, not done, if you copied the dispatcher instead of extracting it
- [ ] Badru's explicit slice 7 GO was given before coding started

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
Not coding GO until Badru explicitly says GO for slice 7. A pack gate does not authorize implementation.
Implement the System Design agent in https://github.com/badrusiddique/agent-sdlc only. Never ai-sdlc.
Follow docs/prompts/i3-system-design-agent-spoon-feed.md from the top. Do not skip the Core delta in section 4.
Scope: AgentHub.Agents.SystemDesign plus IArtifactBlobAccess, GuardrailLimits, and the other Core seams that file names. Do not extend IArtifactBlobStore. HEALTHY stays green.
Authority: docs/architecture/phase-0-implementation-guide.md sections 3, 5.1, 6.3, 8.2, 8.7, 8.8.
Out of scope: PRD, Implementation, Testing, Deployment, buildgenix-api, closing guide section 16.1.
Done: dotnet test on AgentHub.slnx, then Ollama Manual QA per section 11 of the spoon-feed. Fixture is not Manual QA.
Stop and ask if a required blob path or a topic name is not in that file.
```

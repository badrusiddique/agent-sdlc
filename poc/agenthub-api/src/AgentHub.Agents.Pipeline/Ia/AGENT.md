# Implementation Agent (IA) — full behavior

**Code home:** this folder (codegen, git, deploy, graphify, delivery mode) + stages in `../PipelineAgentHandler.cs` + host worker `AgentHub.Hosting/Agents/IaAgentWorker.cs`.  
**Topic:** `agenthub.agent.ia` · **Subscription:** `ia-worker` · **AgentName:** `IA`  
**Stages owned (same worker, sequential messages):**

```text
IA_PLAN → IA_CODEGEN → IA_REVIEW → IA_PUSH_DEPLOY
```

Legacy aliases mapped in handler: `IMPLEMENTATION`→codegen, `TESTING`→legacy unit path, `DEPLOY_DEV`→push-deploy.  
**Next after `IA_PUSH_DEPLOY`:** `agenthub.agent.ta` (`TA_GENERATE`).  
**RouteBack target:** TA republishes `IA_CODEGEN` on this topic with bumped revision.

---

## 0. End-to-end placement

```text
SystemDesign (or HITL enqueue) → Publish IA_PLAN on agenthub.agent.ia
  → IaAgentWorker for each IA_* stage in turn (AutoApprove chain)
  → Each stage: PipelineAgentHandler → blobs/workspace/git/compose → callback BuildGenix
  → After IA_PUSH_DEPLOY: Publish TA_GENERATE
  → On TA red RouteBack: TA worker Publish IA_CODEGEN (rev+1) → IA regenerates → …
  → Studio: plan/codegen/review/PR/deployedUrl chips + LiveArtifacts
```

Env for local proof:

- `IA_GIT_PROVIDER=gitea` · `IA_DEPLOY_PROVIDER=compose`
- Dual-mode factories also support Azure DevOps + App Service (not required for local gate).

---

## 1. Host worker (`IaAgentWorker`)

1. Subscribe `agenthub.agent.ia` / `ia-worker`.
2. Log: `IA listening on topic=agenthub.agent.ia subscription=ia-worker`.
3. On each message `HandleAsync`:
   - If stage=`IA_PUSH_DEPLOY` and `DeploymentAction` empty → set `"Deploy"`.
   - Dispatcher → callback → AutoApprove chain via `StageProgression.NextStageAfterSuccess`:
     - IA_PLAN → IA_CODEGEN (same topic)
     - IA_CODEGEN → IA_REVIEW
     - IA_REVIEW → IA_PUSH_DEPLOY
     - IA_PUSH_DEPLOY → TA_GENERATE on **TA topic**
4. Interactive: stop after each stage if AutoApprove=false; BuildGenix gates IA_PLAN / IA_REVIEW then enqueues next.

---

## 2. Stage `IA_PLAN` (`RunIaPlanAsync`)

### Setup outputs

- `workspaceRoot` = `ResolveWorkspace` → typically `/tmp/bgx-poc/project-{pid}-work-item-{wid}` (or configured root).
- `workBranch` = `MonoRepoLayout.WorkBranch` → `feature/project-{pid}-work-item-{wid}`.
- `monoFolder` = `project-{pid}-work-item-{wid}`.

### Step `ia.plan.converse`

1. `BuildPersonaContextAsync(Planner)` — PRD/techspec context packed for LLM.
2. `CallPersonaAsync` intent `ia.pipeline`, prompt `Prompts/ia-plan.txt`.
3. On timeout/cancel/HttpRequestException → fallback `DefaultIaPlanMarkdown(message)`.

### Step `ia.plan.persist`

1. Write `{BlobPrefix}implementation-plan.md` (raw markdown text).
2. `artifacts["implementationPlan"]`.
3. Decision: agent=IA, phase=plan — plan follows PRD+SD; codegen will honor deliveryMode.

### Callback / UI

BuildGenix registers plan artifact; HITL gate `IA_PLAN` when interactive.

---

## 3. Stage `IA_CODEGEN` (`RunIaCodegenAsync`)

Also entered on RouteBack from TA (new OutputRevision, `taConcerns` may be in artifacts).

### Step `ia.codegen.hints`

1. Persona **Developer** + prompt `ia-codegen.txt`.
2. Network/timeout → `{}` completion (codegen still runs deterministically).

### EffectLedger workspace intent `workspace.pipeline` seq 600

If **not** already committed for this task:

#### Classify delivery

`DeliveryModeClassifier.Classify(userRequest, projectMode, baselineArtifacts)`:

| Mode | Trigger |
|------|---------|
| Rewrite | projectMode Rewrite/GreenfieldRewrite, or baseline path + rewrite/migrate/net8 cues |
| FeatureIncrement | projectMode Existing, “update the…”, subtract calculator, etc. |
| Basic | default New |

#### Branch A — Rewrite

1. Baseline = `ResolveBaselinePath` or `GreenfieldRewriteBuilder.ResolveDefaultFixtureRoot()` (`poc/fixtures/legacy-net8-calculator`).
2. `GreenfieldRewriteBuilder.Rewrite(workspace, baseline, pid, wid, userRequest)`:
   - Retarget **net10.0**, preserve `/api/add`, FE selectors `#a/#b/#go/#out` (not legacy `#add` only).
3. outputs: `appKind=Rewrite`, `deliveryMode=Rewrite`.

#### Branch B — FeatureIncrement

1. `FeatureIncrementBuilder.Apply(workspace, pid, wid, userRequest, priorPath)`.
2. Classify feature (Subtract / Multiply / …); add routes; keep prior add.
3. outputs: `appKind={Feature}`, `deliveryMode=FeatureIncrement`.

#### Branch C — Basic (request-driven)

1. `RequestDrivenCodegen.Classify` → Calculator | Todo | Hello | HelloLlm | …
2. If `artifacts["techSpec"]` readable → `TechSpecBuilder.TryReadSpoonFeed` overrides kind + sets `outputs["primaryApi"]`.
3. `RequestDrivenCodegen.Generate(workspace, kind, pid, wid, userRequest, hintJson from LLM)`.
4. outputs: `appKind`, `deliveryMode=Basic`.

#### Common after generate

1. `MonoRepoLayout.SyncFrontendToWwwroot(workspace)`.
2. `CommitLedgerAsync` workspace intent with meta.
3. Complete step with mono folder name.

If ledger **already** committed → step `ia.codegen.reuse` (idempotent redelivery).

### Step `ia.codegen.tdd`

1. `RunBuilderTestsAsync` — builder shell / dockerized `dotnet test` (BuilderShell; host fallback if builder warns).
2. Then `RunTestingAsync` publishes `test-results.json` / named `IA_TEST_RESULTS_V{n}.json` for Docs/UI.

### Step `ia.codegen.commit`

1. `EnsureLocalGitCommitAsync` → `outputs["headCommit"]`.
2. Also set workBranch, workspaceRoot, monoFolder.

### Decision

agent=IA, phase=codegen — workspace source of truth; notes RouteBack concerns if `taConcerns` present.

**Note:** `code-state.json` / `code-graph.json` are **not** written here — they are written after review via graphify.

---

## 4. Stage `IA_REVIEW` (`RunIaReviewAsync`)

### Step `ia.review.converse`

1. Enumerate workspace files (exclude `.git/`); sample first 40 for reviewer pack (guardrail length).
2. Persona **Reviewer** + `ia-review.txt`.
3. Timeout → default passed JSON.

### Step `ia.review.persist`

1. Extract trailing JSON or wrap LLM text → `{BlobPrefix}review.json`.
2. `{BlobPrefix}code-review.md` = raw LLM text.
3. artifacts `review`, `codeReview`; named `IA_REVIEW_V{n}.json`.

### Step `ia.codestate.graphify` (locked)

1. Command: `GraphifyCodeStateBuilder.LockedCommand` = `graphify extract . --code-only`.
2. `BuildAsync(workspace)` → CodeGraphJson + CodeStateJson.
3. Write `code-graph.json`, `code-state.json` (+ named `CODE_GRAPH_V{n}`, `CODE_STATE_V{n}`).
4. Decision: reviewer did not re-implement; graphify command locked.

HITL gate `IA_REVIEW` when interactive.

---

## 5. Stage `IA_PUSH_DEPLOY` (`RunIaPushDeployAsync`)

If `DeploymentAction=Finalize` → only `RunDeployAsync` (rare). Else:

### Step `ia.push.pr`

1. Branch = `MonoRepoLayout.WorkBranch`.
2. `IGitPullRequestClient` from `GitPullRequestClientFactory` (`IA_GIT_PROVIDER`):
   - **gitea:** `GiteaPullRequestClient.PushAndOpenPrAsync`
   - **azuredevops:** `AzureDevOpsPullRequestClient`
3. Uses `MonoRepoGitPush` to push feature branch (not main).
4. Write `{BlobPrefix}pull-request.json` with provider, prUrl, prNumber, branch, headCommit, cloneUrl.
5. outputs: prUrl, prNumber, workBranch, headCommit.

### Deploy (`RunDeployAsync` inside push-deploy)

1. `IDeployHost` from `DeployHostFactory` (`IA_DEPLOY_PROVIDER`):
   - **compose:** `LocalComposeDeployHost` / `LocalComposeHost` — docker compose up, publish host port, health wait; prune/retry on network clash.
   - **appservice:** `AzureAppServiceDeployHost` (cloud dual-mode).
2. Write deploy summary blob; set `outputs["deployedUrls"]` (and related).
3. Decision: feature-branch PR; DeployedUrl is live base for TA.

### Chain

AutoApprove → Publish `TA_GENERATE` on `agenthub.agent.ta` with DeployedUrl + PrUrl in context.

---

## 6. Supporting modules in this folder (what each file does)

| File | Responsibility |
|------|----------------|
| `DeliveryMode.cs` | Classify Basic/Rewrite/FeatureIncrement + baseline path keys |
| `RequestDrivenCodegen.cs` | Scaffold Calculator/Todo/Hello/HelloLlm mono-repos |
| `GreenfieldRewriteBuilder.cs` | net8 fixture → net10 rewrite + FE `#go` |
| `FeatureIncrementBuilder.cs` | Prior workspace + subtract/multiply/feature routes |
| `GraphifyCodeStateBuilder.cs` | Locked graphify extract → code-state/graph |
| `MonoRepoLayout.cs` | Folder/branch names; SyncFrontendToWwwroot |
| `MonoRepoGitPush.cs` | Git push mechanics for PR clients |
| `GiteaPullRequestClient.cs` / `AzureDevOpsPullRequestClient.cs` | Open/merge PR |
| `LocalComposeDeployHost.cs` / `AzureAppServiceDeployHost.cs` | Deploy hosts |
| `BuilderShell.cs` | Containerized/host TDD |
| `IaGitOptions.cs` / `IaDeployOptions.cs` | Options + env overlays |
| Factories | Provider selection without changing call sites |

Templates under `../Templates/fe-be` and `fe-be-llm` feed scaffolds.

---

## 7. BuildGenix callback effects (every IA stage)

1. Merge artifacts (plan, review, code-state, pull-request, …).
2. Capture outputs → session.WorkspaceRoot, HeadCommit, DeployedUrl, PrUrl/PrNumber → `RepoFactsJson`.
3. WorkItemEvents Task*/StageCompleted; Approvals on gate stages.
4. Studio LiveArtifacts + Docs chips update; deployed URL appears after push-deploy.
5. RouteBack inbound: session bumps ArtifactRevision, removes `taRoutebackProbe`, event `RouteBack`; worker already republished IA_CODEGEN when AutoApprove (interactive: BuildGenix enqueues IA_CODEGEN).

---

## 8. Artifacts produced across IA stages

| Stage | Blobs / workspace |
|-------|-------------------|
| IA_PLAN | `implementation-plan.md` |
| IA_CODEGEN | workspace tree, `test-results.json`, git head |
| IA_REVIEW | `review.json`, `code-review.md`, `code-state.json`, `code-graph.json` |
| IA_PUSH_DEPLOY | `pull-request.json`, deploy summary, live Compose URL |
| Always | Decision log entries agent=IA per phase |

---

## 9. Integration / verification (other machine)

After integrating IA:

1. Full gate cases 1–6 (all deliveryModes) — runbook §3.
2. **Rewrite FE:** assert `#a/#b/#go` in `backend/wwwroot/index.html` (unit `DeliveryModeTests`).
3. **MultiWI Subtract:** Existing mode keeps add + adds subtract.
4. Gitea PR HTTP 200; branch `feature/…`.
5. Compose health + smoke on DeployedUrl.
6. Do **not** change LLM Gateway; Fixture env only for local.

See `poc/docs/qa/POST-AGENT-INTEGRATION-RUNBOOK.md` §4.3.

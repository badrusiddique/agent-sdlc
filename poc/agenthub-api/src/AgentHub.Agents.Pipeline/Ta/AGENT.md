# Testing Agent (TA) — full behavior

**Code home:** this folder (smoke, Playwright, verify runner, concerns, verification report) + stages in `../PipelineAgentHandler.cs` + host worker `AgentHub.Hosting/Agents/TaAgentWorker.cs`.  
**Topic:** `agenthub.agent.ta` · **Subscription:** `ta-worker` · **AgentName:** `TA`  
**Stages owned:**

```text
TA_GENERATE → TA_VERIFY → TA_REPORT → TA_MERGE
```

**Next after `TA_MERGE`:** `agenthub.agent.deploy` (`DEPLOY_SANDBOX`).  
**Special:** red `TA_VERIFY` → RouteBack to **IA** (`IA_CODEGEN`, revision+1), not Failed.

---

## 0. End-to-end placement

```text
IA_PUSH_DEPLOY success → Publish TA_GENERATE (Context.DeployedUrl required)
  → TaAgentWorker stages in turn
  → TA_VERIFY green → TA_REPORT → TA_MERGE → Deploy topic
  → TA_VERIFY red → signal.kind=RouteBack → callback BuildGenix
       → worker Publish IA_CODEGEN on agenthub.agent.ia (rev+1, drop taRoutebackProbe)
       → IA fix → push/deploy → TA again
  → Studio: ta-results, RouteBack trail, verification report, merge
```

Probe for Manual QA RouteBack without real product bugs:

- Request flag `simulateTaRouteback: true` → BuildGenix seeds `LiveArtifacts["taRoutebackProbe"]=1`
- Worker on first `TA_GENERATE` with `SimulateTaRouteback` + RouteBackCount=0 also seeds artifact if missing
- `PoisonHttpExpectOnce` rewrites non-HealthOk expects to `__ROUTEBACK_PROBE_FAIL__`
- After red, `ClearHttpPoison`; RouteBack message **removes** `taRoutebackProbe` so second pass is green

Max RouteBacks: **2** (worker + BuildGenix `MaxTaRoutebacks`); then Failed.

---

## 1. Host worker (`TaAgentWorker`)

1. Subscribe `agenthub.agent.ta` / `ta-worker`.
2. Log: `TA listening on topic=agenthub.agent.ta subscription=ta-worker`.
3. `HandleAsync` extras before dispatch:
   - If stage=`TA_GENERATE` and `SimulateTaRouteback` and `RouteBackCount==0` and no `taRoutebackProbe` → set probe `"1"`.
4. After Completed + AutoApprove:
   - If stage=`TA_VERIFY` and `StageProgression.IsRouteBack(result)`:
     - Increment count; if `>2` → Failed callback + stop.
     - Else `BuildRouteBackMessage` → Publish `AgentTopics.Ia` (`IA_CODEGEN`, rev+1, strip probe).
     - Log: `TA RouteBack → IA_CODEGEN rev=v{Rev} attempt={Attempt}`.
     - **Do not** chain to TA_REPORT.
   - Else normal `NextStageAfterSuccess` (GENERATE→VERIFY→REPORT→MERGE→ Deploy topic).

---

## 2. Stage `TA_GENERATE` (`RunTaGenerateAsync`)

**Requires** `Context.DeployedUrl` (from IA) — else throw.

### Step `ta.gen.converse`

1. Persona **QaAutomation** + prompt `ta-generate.txt`.
2. Timeout → `{}`.

### Step `ta.gen.write`

1. `DeployedSmokeGenerator.ParseCasesOrDefault(llm.Text, stack, userRequest, projectName, projectMode, baseline)`  
   → HTTP cases from deliveryMode / request classifier (not from review cheerleading).
2. `DeployedSmokeGenerator.WriteSuite(workspace, cases)` → `tests/e2e/cases.json` + `deployed-smoke.sh`.
3. `PlaywrightSuiteGenerator.WriteSuite` → `tests/e2e/playwright/fe.spec.js` + `run-playwright.sh`  
   - Calculator: `#a/#b/#go/#out`  
   - Todo: `#title/#add/#list`  
   - Hello/HelloLlm: `#go/#out`
4. If `taRoutebackProbe` artifact or env `BGX_TA_ROUTEBACK_PROBE=1` → `PoisonHttpExpectOnce`.
5. Write `{BlobPrefix}ta-test-plan.md` (LLM text or default plan string).
6. Decision: cases from deliveryMode; Playwright soft-skips if browsers missing; HTTP hard gate.

---

## 3. Stage `TA_VERIFY` (`RunTaVerifyAsync`)

1. Ensure e2e suite exists (else write defaults).
2. Step `ta.verify.run`: `TaVerifyRunner.RunAsync(workspace, deployedUrl)`:
   - Run `bash deployed-smoke.sh` with `DEPLOYED_URL`, `TA_RESULTS_PATH` (HTTP hard gate).
   - Run `bash run-playwright.sh` if present; status `skipped` → treat exit as soft OK.
   - Combined JSON: `{ status, deployedUrl, http, playwright, results }`.
   - Green iff HTTP green **and** Playwright exit green/skipped.
3. Persist `{BlobPrefix}ta-results.json` (+ named `TA_RESULTS_V{n}.json`).

### Red path

1. `TaConcernPackage.Build(deployedUrl, resultsJson, failingNames, attempt)` → `ta-concerns.json`.
2. outputs:
   - `taStatus=red`
   - `signal.kind=RouteBack`
   - `signal.routeBackTo=IA_CODEGEN`
   - `signal.detail` truncated
3. `ClearHttpPoison`.
4. Complete verify step as failed label but **AgentResult.Status remains Completed** (orchestrator/worker route on signal, not Failed status).
5. Decision: RouteBack with concerns; IA must fix and re-deploy.

### Green path

1. `taStatus=green`, clear poison, decision verify-green.
2. Chain to `TA_REPORT`.

---

## 4. Stage `TA_REPORT` (`RunTaReportAsync`)

### Step `ta.report.converse`

1. Persona **QaReporter** + `ta-report.txt`.
2. Timeout → green readyToMerge JSON.

### Step `ta.report.persist`

1. `ta-report.md` = LLM text.
2. `ta-status.json` = extracted JSON or default green.
3. Load plan + results + decision summary from blobs.
4. `TaVerificationReportBuilder.BuildJson` → `ta-verification-report.json`:
   - `kind=TaVerificationReport`
   - `readyForDeployment` true when status green
   - embeds verification + taStatus + decisionSummary + handoff text
5. Also `ta-verification-report.md` (+ named `TA_VERIFICATION_REPORT_V{n}.json`).
6. Decision: report consolidates plan/results; Deploy must not re-test.
7. HITL gate `TA_REPORT` when interactive.

---

## 5. Stage `TA_MERGE` (`RunTaMergeAsync`)

1. Resolve `prNumber` from context or `pull-request.json`.
2. `IGitPullRequestClient.MergePullRequestAsync` (Gitea/AzDO) or logical merge if no number.
3. Write `{BlobPrefix}merge.json` (provider, urls, deployedUrl, …).
4. May refresh deploy summary via `RunDeployAsync`.
5. Decision agent=TA merge phase.
6. Chain → `DEPLOY_SANDBOX` on Deploy topic.

---

## 6. Modules in this folder

| File | Role |
|------|------|
| `DeployedSmokeGenerator.cs` | Default/parse HTTP cases; write smoke suite |
| `PlaywrightSuiteGenerator.cs` | FE Playwright suite by app kind |
| `TaVerifyRunner.cs` | HTTP + Playwright process runner; combined green/red |
| `TaConcernPackage.cs` | RouteBack payload JSON + failing case names |
| `TaVerificationReportBuilder.cs` | Structured report before sandbox |

Deploy sandbox itself lives under `../Deploy/` + `RunDeploySandboxAsync` (Deploy worker topic) — attaches this verification report into `sandbox-url.json`.

---

## 7. BuildGenix on TA callbacks

1. Merge ta* artifacts; update DeployedUrl from outputs if present.
2. On RouteBack flag / `signal.kind`:
   - Increment `TaRoutebackCount`; remove probe from LiveArtifacts.
   - Exhausted → Failed + TopicDone exception.
   - Else bump ArtifactRevision (`v2`…); audit `TA_VERIFY:RouteBack`; event RouteBack.
   - AutoApprove: worker already published IA; interactive: `EnqueueStageAsync(IA_CODEGEN)`.
3. Studio trail shows RouteBack; Docs show ta-concerns v1 + later green v2.

---

## 8. Artifacts produced

| Stage | Blobs / files |
|-------|----------------|
| TA_GENERATE | `ta-test-plan.md`, `tests/e2e/cases.json`, Playwright suite |
| TA_VERIFY green | `ta-results.json` status green |
| TA_VERIFY red | `ta-results.json` red + `ta-concerns.json` |
| TA_REPORT | `ta-report.md`, `ta-status.json`, `ta-verification-report.json/.md` |
| TA_MERGE | `merge.json` |

---

## 9. Integration / verification (other machine)

After TA integration:

```bash
ONLY_CASES=Calc_Add,Rewrite_net8,RouteBack bash poc/scripts/manual-qa-production-gate.sh
```

Assert:

- HTTP green hard; Playwright green **or** skipped.
- RouteBack case: trail RouteBack, revisions v1+v2, `ta-concerns`, final Completed + add→5.
- Named `TA_VERIFICATION_REPORT_V{n}.json` with `readyForDeployment=true`.
- Decision log agent=TA entries.

See `poc/docs/qa/POST-AGENT-INTEGRATION-RUNBOOK.md` §4.4 · `TA-PLAYWRIGHT-ROUTEBACK-SIGNOFF.md`.

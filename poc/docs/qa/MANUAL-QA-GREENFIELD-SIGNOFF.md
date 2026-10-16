# Manual E2E QA Sign-off — `main-greenfield`

**Date:** 2026-10-16  
**Branch / tip:** `main-greenfield` @ `2cd7b48` + LocalComposeHost network prune/retry  
**Environment:** Local Docker — Gitea `:43185`, Azurite, AgentHub `:43180`, BuildGenix `:43181`, real `docker compose` deploys per work item  
**Harness:** `poc/scripts/manual-qa-greenfield-deep.sh`  
**Evidence JSON:** [`evidence/MANUAL-QA-RESULTS-greenfield.json`](evidence/MANUAL-QA-RESULTS-greenfield.json)  
**Verdict:** **PASS — signed off for local Docker production-lean POC path.** Ready for Testing Agent integration phase.  
**Out of scope this pass (documented, not assumed green):** live Azure Gate C/D (needs subscription), Interactive full UI gates, Service Bus emulator (POC uses InProcessOnly).

---

## 1. Scope (what a Manual QA would sign before users)

| Layer | Verified? | How |
|---|---|---|
| Happy path (Calculator → DONE) | Yes | Full pipeline + deep inspect |
| Every delivery case (Basic / Rewrite / FeatureIncrement / kinds) | Yes | 8 cases, 443 checks |
| Docker resource (compose deploy, health, smoke APIs) | Yes | Live containers + HTTP |
| Artifacts (blob set v1) | Yes | 16 required blobs per WI |
| Agent output (PRD, techspec, plan, review, code-state, graphify) | Yes | Content + deliveryMode |
| Message / pub-sub events | Yes | Trail + eventTypes (InProcessOnly) |
| Logs / audit | Yes | `*-AuditLog.json` transitions + deployedUrl |
| Code generated | Yes | Workspace `Program.cs` routes / net10 / FEATURE.md |
| TDD | Yes | `test-results.json` green / compile-or-unit |
| Code review artifact | Yes | `review.json` + `code-review.md` |
| PR / merge (Gitea) | Yes | feature/* branch, PR page 200, merge.json |
| Deployment | Yes | compose URL `/health` + kind smoke |
| TA verify | Yes | `ta-results.json` status=green + named cases |
| Unit / Azure contract tests | Yes | 25 filtered Pipeline tests PASS |
| Azure integration **docs** vs code | Yes | See §5 (docs review) |
| Live Azure Gate C/D | **Not run** | No subscription in this VM — contracts + docs only |

---

## 2. Happy path — Calculator

| Check group | Result | Evidence |
|---|---|---|
| Pipeline `Completed` / stage `DONE` | PASS | WI `01a1423e33af720c89ba09fab4c72733` |
| Trail INTAKE→…→TA_MERGE | PASS | 17 transitions |
| Events ArtifactReady, GateApproved, Finalized | PASS | eventTypes on run |
| Deploy `http://127.0.0.1:38939/health` | PASS | `{"status":"ok"}` |
| Smoke `/api/add?a=2&b=3` → result 5 | PASS | |
| Blobs (16) | PASS | under `/tmp/bgx-poc-blobs/.../v1/` |
| deliveryMode Basic | PASS | intake/prd/techspec |
| graphify `extract . --code-only` + request-driven | PASS | code-state.json; graph nodes 51 |
| Review passed | PASS | review.json |
| TDD green | PASS | test-results.json |
| Gitea PR feature branch (not main) | PASS | pulls/23 |
| TA green (HealthOk, Add) | PASS | ta-results.json |
| Audit log | PASS | deployedUrl + transitions |

**Checks:** 56/56 PASS.

---

## 3. Full matrix — every case

| # | Case | deliveryMode | Deploy smoke | Checks | Result |
|---|---|---|---|---|---|
| 1 | HP_Calculator | Basic | `/api/add` → 5 | 56 | PASS |
| 2 | Todo | Basic | `/api/todos` → `[]` | 54 | PASS |
| 3 | Hello_no_LLM | Basic | `/api/hello` | 54 | PASS |
| 4 | Hello_LLM | Basic | `/api/thought` | 54 | PASS |
| 5 | Generic (Inventory → hello scaffold) | Basic | `/api/hello` | 54 | PASS |
| 6 | Rewrite_net8_to_net10 | Rewrite | `/api/add` + health `net10` | 58 | PASS |
| 7 | MultiWI_Add (project QA-Multi) | Basic | `/api/add` → 5 | 56 | PASS |
| 8 | MultiWI_Subtract (Existing same projectId) | FeatureIncrement | `/api/add` + `/api/subtract` | 57 | PASS |

**Total:** 8/8 cases, **443/443** checks, **0** failures.

### Per-case deep checklist (applied to each WI)

For every case the harness asserted:

1. **Pipeline:** status Completed; trail has IA_CODEGEN, IA_REVIEW, IA_PUSH_DEPLOY, TA_VERIFY, TA_MERGE  
2. **Events:** ArtifactReady, GateApproved, Finalized  
3. **Deploy:** URL present; `/health` 200 + ok; kind-specific smoke  
4. **Blobs:** intake-summary, prd.json/md, techspec, system-design, implementation-plan, test-results, review, code-review, code-state, code-graph, pull-request, completion-summary, ta-test-plan, ta-results, merge  
5. **deliveryMode** matches expect (Basic / Rewrite / FeatureIncrement) on intake + prd + techspec  
6. **Graphify code-state:** command `graphify extract . --code-only`; mode request-driven; code-graph nodes > 0  
7. **Review + TDD:** review blob nonempty; test-results present/green shape  
8. **Git:** provider gitea; PR URL; branch `feature/…`; not `main`; merge.json; Gitea PR HTTP 200  
9. **TA:** status green; named cases ok (Add / Todos / Hello / Thought / Subtract / …)  
10. **Audit:** AuditLog present; transitions; deployedUrl  
11. **Workspace code:** backend `.cs` exists; expected routes in `Program.cs`; Rewrite → REWRITE.md + net10.0 csproj; FeatureIncrement → FEATURE.md present (Subtract)

### Multi-WI specifics

- Same `projectId` `01a14240642f7e4aa094ed083ca8db6a`  
- WI1 New/Basic scaffolds calculator; WI2 Existing/FeatureIncrement adds `/api/subtract` while keeping `/api/add`  
- Live compose still healthy at sign-off: `:42851` (Add), `:45491` (Subtract)

---

## 4. Defects found and fixed

| Defect | Impact | Fix | Verified |
|---|---|---|---|
| Docker `fully subnetted` / address pools after many compose projects | MultiWI deploy failed mid Manual QA | `LocalComposeHost`: on subnet/pool error, `docker network prune -f` then retry once; `poc/scripts/prune-poc-compose.sh` for operators | MultiWI_Add + MultiWI_Subtract PASS after fix; AgentHub restarted with patched binary |

**Open (non-blocking for local sign-off):**

| Item | Notes |
|---|---|
| Builder quote WRN host-fallback | Observed in prior runs; not a pipeline fail |
| Service Bus emulator unhealthy on `:5300` | AgentHub `/health` 503 for that probe; POC messaging is InProcessOnly — pipelines still Complete |
| Live Azure Gate C/D | Requires real AzDO + App Service; contract tests cover HTTP shapes |
| Generic ≠ free-form LLM codegen | Inventory request correctly lands closed Generic/hello scaffold |

---

## 5. Azure integration — documentation vs code review

Reviewed against: `AzureDevOpsPullRequestClient`, `AzureAppServiceDeployHost`, `AzureArtifactBlobStore`, `appsettings.Azure.example.json`, `AzureContractVerificationTests`, factories/options.

| Doc claim | Code reality | Verdict |
|---|---|---|
| Dual-mode via `IA_GIT_PROVIDER` / `IA_DEPLOY_PROVIDER` | `IGitPullRequestClient` / `IDeployHost` factories select gitea\|azuredevops and compose\|appservice | **Aligned** |
| Laptop auth = PAT + publish profile; cloud = managedIdentity + ServiceUri | `UseManagedIdentity` → Bearer AzDO resource `499b84ac-…` / ARM scope; else Basic PAT / publish profile | **Aligned** |
| Storage ConnectionString (local) vs ServiceUri (MI cloud) | Blob/Table options + example appsettings | **Aligned** |
| Hybrid flip one concern at a time | Independent options; Gate E runbook | **Aligned** |
| No secrets in git | example uses placeholders; Token/Password via env | **Aligned** |
| ClickOps steps (RG, storage, AzDO PAT, App Service publish profile, ACR builder) | Operational (human); scripts `verify-azure-preflight.sh` / `verify-azure-e2e.sh` exist | **Aligned** (not executed here) |
| Feature PRs never push generated apps to `main` | Local Gitea uses `feature/…`; AzDO same branch pattern | **Aligned** (local proven) |
| Docs tip still said `main-azure` in places | Updated tip → `main-greenfield` in ClickOps, QA runbook, dual-mode design | **Fixed this pass** |

**Doc gaps / follow-ups (do not block local sign-off):**

1. `OTHER-MACHINE-SETUP.md` / `INTEGRATE-IA-LOCAL-FROM-ZIP.md` still prefer `main-azure` wording in places — operators on greenfield should checkout `main-greenfield`.  
2. Gate C/D still require a human-subscribed machine; this VM cannot close Azure e2e.  
3. Service Bus dual-mode is not part of the Azure ClickOps doc (InProcessOnly local) — call out explicitly when adding hosted AgentHub.

---

## 6. Unit / contract verification (this machine)

```text
dotnet test ... filter DeliveryMode|Azure|Smoke|Feature|Greenfield|Rewrite|Graphify
Passed!  Failed: 0, Passed: 25, Total: 25
```

---

## 7. How to re-run

```bash
cd poc
./scripts/seed-gitea.sh
# start AgentHub :43180 + BuildGenix :43181 (Development defaults)
./scripts/prune-poc-compose.sh   # if prior compose networks linger
./scripts/manual-qa-greenfield-deep.sh
./scripts/verify-greenfield.sh
```

---

## 8. Sign-off

| Role | Decision |
|---|---|
| Manual QA (local Docker, greenfield slice) | **APPROVED** |
| Azure live Gate C/D | Deferred to subscription machine per runbook |
| Next phase | **Testing Agent integration** |

Signed: Manual E2E QA pass on `main-greenfield` with LocalComposeHost prune fix, 2026-10-16.

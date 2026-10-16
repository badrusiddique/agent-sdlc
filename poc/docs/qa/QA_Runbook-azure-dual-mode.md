# QA Runbook — Azure dual-mode (IA + TA)

**Purpose:** After ClickOps wires a real Azure subscription on another machine, run this runbook to prove the **same code** works locally (Docker stand-ins) **and** against Azure / Azure DevOps.  
**Code branch:** `main-greenfield` (or `main-azure` for dual-mode-only tip). Tip must include dual-mode providers + builder image + request-driven/greenfield IA.  
**Companion ClickOps:** [`../azure-clickops-integration.md`](../azure-clickops-integration.md)  
**Local Manual QA (Docker Gitea+Compose):** `./scripts/manual-qa-greenfield-deep.sh` then `./scripts/verify-greenfield.sh`.  
**After each agent integration (detailed Copilot runbook):** [`POST-AGENT-INTEGRATION-RUNBOOK.md`](POST-AGENT-INTEGRATION-RUNBOOK.md) — Azurite+Fixture matrix, agent-by-agent log/blob/DB/decision checks, RouteBack.

---

## 0. Pass / fail bar

| Gate | Must be true |
|---|---|
| A. Local baseline | `./scripts/verify-ta.sh` exit 0 with defaults (Gitea + Compose) |
| B. Contract tests | `dotnet test` Pipeline tests all green (includes AzDO/App Service HTTP contracts) |
| C. Azure preflight | `./scripts/verify-azure-preflight.sh` exit 0 (creds + reachability) |
| D. Azure e2e | `./scripts/verify-azure-e2e.sh` exit 0 (Calculator Completes on AzDO + App Service) |
| E. Dual-mode proof | Flip back to local providers; Gate A still passes |

Senior QA does **not** mark Azure Manual QA done on Fixture-only or mock-only runs when Gate D is in scope.

---

## 1. Machine prerequisites

- Docker, .NET 10 SDK, Node (studio optional)
- `az` CLI logged in (`az account show`) **for Gate C/D**
- Clone:

```bash
git clone https://github.com/badrusiddique/agent-sdlc.git
cd agent-sdlc
git checkout main-greenfield   # or main-azure for dual-mode-only tip
git rev-parse HEAD   # record SHA in the evidence section below
```

Optional builder image:

```bash
./poc/scripts/build-ia-builder.sh
# or docker load from the exported tar / ACR
export IA_BUILDER_IMAGE=agenthub-ia-builder:local
```

---

## 2. Gate A — Local baseline (always first)

```bash
cd poc
./scripts/seed-gitea.sh
# Start AgentHub :43180 and BuildGenix :43181 per README (Development defaults)
./scripts/verify-ta.sh
```

**Expect:** status Completed; trail includes `IA_PUSH_DEPLOY` … `TA_MERGE`; Gitea PR `merged=True`; `/api/add` → 5.

| Check | Pass? | Evidence |
|---|---|---|
| verify-ta.sh exit 0 | | |
| merge.json provider=gitea | | |
| Deployed URL /health ok | | |

---

## 3. Gate B — Contract / unit verification (no Azure needed)

```bash
cd poc/agenthub-api
dotnet test tests/AgentHub.Agents.Pipeline.Tests
```

**Expect:** all passed (includes `AzureContractVerificationTests`, `AzureDevOpsPullRequestClientTests`, persona tests).

| Check | Pass? |
|---|---|
| AzDO create PR URL + body (refs/heads, api-version=7.1, Basic :PAT) | |
| AzDO create PR uses Bearer when `Auth=managedIdentity` | |
| AzDO complete PR (lastMergeSourceCommit + noFastForward) | |
| App Service POST …/api/zipdeploy + Basic publish profile | |
| App Service zipdeploy uses Bearer when `Auth=managedIdentity` | |
| Factories default to gitea/compose | |

---

## 4. Gate C — Azure preflight (real subscription)

Complete ClickOps first ([azure-clickops-integration.md](../azure-clickops-integration.md)): Storage (optional this gate), AzDO repo+PAT, App Service+publish profile.

```bash
cd poc
# Laptop → Azure uses keys / PAT / publish profile (not MI)
export IA_GIT_PROVIDER=azuredevops
export IA_GIT_AUTH=pat
export IA_GIT_ORGANIZATION=<org>
export IA_GIT_PROJECT=<project>
export IA_GIT_REPO=<repo>
export IA_GIT_TOKEN=<pat>

export IA_DEPLOY_PROVIDER=appservice
export IA_DEPLOY_AUTH=publishProfile
export IA_DEPLOY_APP_NAME=<webapp>
export IA_DEPLOY_PUBLIC_URL=https://<webapp>.azurewebsites.net
export IA_DEPLOY_USER='<from publish profile>'
export IA_DEPLOY_PASSWORD='<from publish profile>'

# Optional storage (key path)
# export BlobStorage__Provider=Azure
# export BlobStorage__ConnectionString='...'
# export TableStorage__Provider=Azure
# export TableStorage__ConnectionString="$BlobStorage__ConnectionString"

./scripts/verify-azure-preflight.sh
```

**Expect:** PAT can list the repo; SCM zipdeploy endpoint accepts auth (or returns a non-401); public `/health` reachable (200 or 404 until first deploy — script documents which).

| Check | Pass? | Notes |
|---|---|---|
| az account show | | |
| AzDO repo GET 200 with PAT | | |
| SCM basic auth not 401 on probe | | Enable SCM Basic Auth in portal if 401 |
| Public URL DNS resolves | | |
| Auth modes set to pat / publishProfile | | MI is for hosted AgentHub later |

---

## 5. Gate D — Azure e2e (real subscription)

Restart AgentHub **with the same env exports** as Gate C (BuildGenix can stay local).

```bash
cd poc
./scripts/verify-azure-e2e.sh
```

**Expect:**

1. Pipeline Completes (Calculator).
2. AzDO shows PR opened then **Completed** (merged).
3. `pull-request.json` / `merge.json` → `"provider":"azuredevops"`.
4. `deployedUrl` is `https://….azurewebsites.net`.
5. `curl $deployedUrl/health` and `/api/add?a=2&b=3` → 5.
6. `ta-results.json` status=green.

| Check | Pass? | Evidence (URL / PR id) |
|---|---|---|
| Completed trail through TA_MERGE | | |
| AzDO PR completed | | |
| App Service smoke | | |
| merge provider=azuredevops | | |

---

## 6. Gate E — Dual-mode still works locally

```bash
unset IA_GIT_PROVIDER IA_GIT_AUTH IA_GIT_TOKEN IA_DEPLOY_PROVIDER IA_DEPLOY_AUTH IA_DEPLOY_USER IA_DEPLOY_PASSWORD
# or explicitly:
export IA_GIT_PROVIDER=gitea
export IA_DEPLOY_PROVIDER=compose
# restart AgentHub with Development defaults
./scripts/verify-ta.sh
```

**Expect:** Gate A still green (no Azure required).

---

## 7. Evidence pack to attach

Record in your PR / chat:

```text
SHA:
Gate A: pass/fail + timestamp
Gate B: pass/fail + test count
Gate C: pass/fail
Gate D: AzDO PR #____  App Service URL: ____
Gate E: pass/fail
```

---

## 8. Failure triage

| Symptom | Action |
|---|---|
| Gate A fails after Azure work | Defaults changed — restore `Ia:Git:Provider=gitea`, `Ia:Deploy:Provider=compose` |
| AzDO 401 | PAT scopes Code + PR; org/project/repo names |
| AzDO PR create 400 | Empty repo? Initialize `main` with README |
| zipdeploy 401 | Enable **SCM Basic Auth Publishing Credentials**; re-download publish profile |
| Health timeout | App crash — Portal Log stream; confirm .NET runtime matches publish |
| TA red | Hit `/health` and `/api/add` manually on deployed URL first |

---

## 9. Out of scope for this runbook

- Creating Azure resources (ClickOps doc)
- Pushing builder image to ACR (EXPORT-NOTE.md)
- Ollama/Gateway LLM Manual QA (preferences Manual E2E checklist) — use Fixture only for plumbing; for full Manual QA swap LLM provider per prefs

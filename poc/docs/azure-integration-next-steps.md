# Azure integration — what to wire, how to verify, what next

Do this **after** local Docker e2e is green (Azurite + Gitea + Compose + studio). Replace one stand-in at a time; re-run the matching verify after each step.

## Local → Azure map

| Concern | Local (working now) | Azure target | Config / code seam |
|---|---|---|---|
| Blob + Table | Azurite / File blobs | Storage account (Blob + Table) | `BlobStorage` / `TableStorage` in AgentHub + BuildGenix `appsettings` |
| Git + PR + merge | Gitea `43185` | Azure DevOps Repos (or GitHub) | `Ia:Git` (`Provider`, `BaseUrl`, `Owner`/`Repo`, PAT/token) |
| App host (DEV) | `LocalComposeHost` (Compose) | App Service (or ACA) | New `IDeployHost` / `AzureAppServiceHost` beside `LocalComposeHost` |
| Builder compute | `docker run` `agenthub-ia-builder:local` | Azure Container Apps Job / ACI with same image in ACR | Builder shell already docker-shaped |
| LLM | Fixture / Ollama | Azure OpenAI or existing AI Gateway | `Llm:Provider` (+ Gateway subscription if used) |
| Secrets | Dev JSON / env | Key Vault + managed identity | Do not commit PATs; map `Ia:Git:Token`, storage connection |

---

## Step 0 — Preconditions (verify before changing code)

1. Local still green: `./scripts/seed-gitea.sh` then `./scripts/verify-ta.sh` → exit 0.
2. Azure CLI logged in: `az account show` (subscription id + tenant visible).
3. You have rights to create: Resource Group, Storage, App Service plan + web app (or ACA), ACR (if using builder jobs), DevOps PAT with Code Read/Write + PR permissions (or GitHub App/PAT).

**Exit criteria:** Account + permissions confirmed; local verify still pass.

---

## Step 1 — Storage account (replace Azurite / File blobs)

### Do

1. Create RG + storage (example names — pick yours):
   ```bash
   az group create -n rg-buildgenix-poc -l eastus
   az storage account create -n stbgxpocUNIQUE -g rg-buildgenix-poc -l eastus --sku Standard_LRS
   ```
2. Create container `agenthub-artifacts` (and table names AgentHub/BuildGenix already ensure at startup when using real Table provider).
3. Point AgentHub + BuildGenix Development/Production config at the account:
   - `BlobStorage:Provider=Azure` (or whatever the POC providers already support — prefer the same provider enum used in non-POC trees)
   - `BlobStorage:ConnectionString=<from portal or az storage account show-connection-string>`
   - `TableStorage:Provider=Azure` + connection string (if still Memory locally, switch only when ready)

### Verify

1. Restart AgentHub + BuildGenix with Azure storage config.
2. `./scripts/verify-ta.sh` (Gitea + Compose still local is OK).
3. In Portal → Storage → Containers → `agenthub-artifacts` → see `{projectId}/{workItemId}/v1/prd.json`, `ta-results.json`, `merge.json`.

**Exit criteria:** Blobs appear in Azure; pipeline Completed; no FileRootPath dependence for that run.

**Next:** Step 2 (Git).

---

## Step 2 — Azure DevOps Git (replace Gitea)

### Do

1. Create (or reuse) AzDO project + repo, e.g. `buildgenix/app-monorepo`, with a non-empty `main` (README commit).
2. Create PAT: Code (Read & Write) + Pull Request (Read & Write). Store in Key Vault / env `IA_GIT_TOKEN` — not in git.
3. Extend `Ia:Git` (keep Gitea for local):
   ```json
   "Ia": {
     "Git": {
       "Provider": "azuredevops",
       "BaseUrl": "https://dev.azure.com/{org}/{project}/_git/{repo}",
       "Owner": "{project}",
       "Repo": "app-monorepo",
       "Username": "pat",
       "Token": "<from env / Key Vault>"
     }
   }
   ```
4. Implement / finish an `AzureDevOpsPullRequestClient` parallel to `GiteaPullRequestClient`:
   - Push branch `feature/project-{pid}-wi-{wid}`
   - Create PR into `main` (REST: `POST …/_apis/git/pullrequests`)
   - Merge after TA green (same option-B ownership as today)
5. Keep `Provider=gitea` as default for local `seed-gitea.sh`.

### Verify

1. Run Calculator `/run` against AzDO.
2. AzDO shows open PR with mono folder `project-…/frontend` + `backend`.
3. After TA: PR completed/merged; `merge.json` `provider=azuredevops` with PR id.
4. Local Gitea path still: `Provider=gitea` + `verify-ta.sh` green.

**Exit criteria:** One green AzDO PR merge; local Gitea path unbroken.

**Next:** Step 3 (App Service deploy).

---

## Step 3 — App Service DEV deploy (replace Compose)

### Do

1. Create plan + web app (Linux .NET 10 when available in your region, or container web app matching current Dockerfile):
   ```bash
   az appservice plan create -n plan-bgx-poc -g rg-buildgenix-poc --sku B1 --is-linux
   az webapp create -n app-bgx-poc-UNIQUE -g rg-buildgenix-poc -p plan-bgx-poc --runtime "DOTNETCORE:8.0"
   ```
   Prefer aligning runtime with the generated backend TFM (`net10.0` in POC templates) — use container deploy if platform runtime lags.
2. Add `AzureAppServiceHost` implementing the same outcomes as `LocalComposeHost.DeployAsync`: return public HTTPS URL + wait on `/health`.
3. Auth: contributor on the web app for the agent identity (user-assigned MI preferred over long-lived publish profile in production).
4. Wire IA_PUSH_DEPLOY to choose host by config, e.g. `Ia:Deploy:Provider=compose|appservice`.

### Verify

1. Pipeline Completes with `deployedUrl` = `https://app-bgx-poc-UNIQUE.azurewebsites.net`.
2. `curl https://…/health` and `/api/add?a=2&b=3` → 5.
3. TA_VERIFY greens against that HTTPS URL; studio banner shows Azure URL.
4. Local Compose path still works with `Provider=compose`.

**Exit criteria:** TA green on App Service URL; local Compose still selectable.

**Next:** Step 4 (builder in cloud).

---

## Step 4 — ACR + Container Apps Job (replace local builder)

### Do

1. Build/push `agenthub-ia-builder` to ACR.
2. Run codegen commands as a Job with volume or clone-into-workspace pattern equivalent to today’s `docker run -v monoRoot:/work`.
3. Keep host-SDK fallback for laptop dev.

### Verify

1. IA_CODEGEN Completes with Job logs showing build/test.
2. Mono folder / PR still contains FE+BE.

**Exit criteria:** Codegen succeeds without requiring Docker on the AgentHub VM.

**Next:** Step 5 (LLM + secrets hardening).

---

## Step 5 — LLM + Key Vault

### Do

1. Set `Llm:Provider` to Gateway / Azure OpenAI with real credentials via Key Vault references.
2. Move `Ia:Git:Token`, storage keys, Gateway key out of `appsettings.*.json`.
3. Confirm persona system prompts still load (embedded resources) — independence does not change with provider.

### Verify

1. Interactive studio run: Reviewer language is skeptical (not copy of Developer); QA cases hit live Azure URL.
2. `PersonaIndependenceTests` still pass in CI.
3. No secrets in git diff.

**Exit criteria:** One full interactive Calculator Completed on Azure storage + AzDO + App Service + real LLM.

---

## Suggested order (do not skip verify)

```text
Local green
  → Storage (Step 1) → verify-ta
  → AzDO Git (Step 2) → PR+merge proof
  → App Service (Step 3) → HTTPS smoke + TA
  → ACR/Job builder (Step 4)
  → Key Vault + real LLM (Step 5)
  → Only then: CI in AzDO, multi-env (staging), MI everywhere
```

## Explicitly out of this POC until Steps 1–3 are green

- Production multi-tenant isolation
- Replacing BuildGenix/AgentHub hosts themselves onto ACA (can stay local or App Service later)
- Full Playwright suite against generated FE (HTTP smoke remains the TA contract until you expand `DeployedSmokeGenerator`)

## Rollback

Any step fails → set provider back to local stand-in (`gitea` / `compose` / File|Azurite) and re-run `./scripts/verify-ta.sh` to confirm the regression is Azure-side only.

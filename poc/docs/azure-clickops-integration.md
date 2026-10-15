# Azure integration — ClickOps (portal) step-by-step + verification

**Goal:** Same `poc/` code runs **locally** (Docker stand-ins) and can point at **real Azure** via config/env.  
**Code tip:** branch `main-azure` / `cursor/main-azure-4837`.  
**Do not commit secrets.** Use portal download + env vars.

---

## 0. What you already have locally (keep working)

| Concern | Local default | How to verify |
|---|---|---|
| Blob | File under `/tmp/bgx-poc-blobs` **or** Azurite `Provider=Azure` + `UseDevelopmentStorage=true` | Files appear after a run |
| Table | Memory **or** Azurite | Tables populated when Provider=Azure |
| Git / PR / merge | Gitea `:43185` | `./scripts/verify-ta.sh` |
| Deploy | Docker Compose | Deployed URL `/health` + `/api/add` |
| LLM | Fixture (or Ollama) | Pipeline Completes |

```bash
cd poc
./scripts/seed-gitea.sh
# start AgentHub :43180 + BuildGenix :43181 (see README)
./scripts/verify-ta.sh   # must stay exit 0 after Azure code lands
```

Hybrid is supported: flip **one** concern to Azure at a time (recommended).

---

## 1. Resource Group + Storage (replace Azurite / File)

### ClickOps

1. Portal → **Resource groups** → **Create**  
   - Name: e.g. `rg-buildgenix-poc`  
   - Region: your sandbox region  
   - Review + create.
2. Inside the RG → **Create** → search **Storage account** → Create  
   - Name: globally unique, e.g. `stbgxpoc1234`  
   - Performance: Standard  
   - Redundancy: LRS is fine for POC  
   - Review + create.
3. Open the storage account → **Containers** → **+ Container**  
   - Name: `agenthub-artifacts`  
   - Public access: Private.
4. Storage account → **Access keys** → **key1** → **Connection string** → Show → Copy.  
   Keep it only in a password manager / env — not git.
5. (Optional tables) Table provider uses the same connection string; AgentHub creates tables on start when Provider=Azure.

### Wire local process to Azure Storage

```bash
export BlobStorage__Provider=Azure
export BlobStorage__ConnectionString='DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net'
export BlobStorage__ContainerName=agenthub-artifacts
export TableStorage__Provider=Azure
export TableStorage__ConnectionString="$BlobStorage__ConnectionString"
```

Or edit a **local-only** `appsettings.Development.local.json` (gitignored) using `appsettings.Azure.example.json` as a template.

### Verify

1. Restart AgentHub + BuildGenix with those env vars (Git/Deploy still local).
2. `./scripts/verify-ta.sh`
3. Portal → Storage → Containers → `agenthub-artifacts` → see `{projectId}/{workItemId}/v1/prd.json`, `ta-results.json`, `merge.json`.

**Exit:** Blobs in Azure; local Gitea+Compose still work.

---

## 2. Azure DevOps repo + PAT (replace Gitea)

### ClickOps — project & repo

1. Open `https://dev.azure.com/{yourOrg}` (Phase 0 example org: `wcbbc`, project `AI-Operations`).
2. **Repos** → **Files** → ensure repo exists (e.g. `inno-tech-aisdlc-apps` or a POC repo `app-monorepo`).
3. If empty: **Initialize** with a README on `main` (required so clone has history).

### ClickOps — PAT

1. User icon → **Personal access tokens** → **+ New Token**  
   - Organization: All accessible or your org  
   - Scopes: **Code** → Read & write; **Pull Request** → Read & write  
   - Create → **copy the token once**.
2. Store as env (never commit):

```bash
export IA_GIT_PROVIDER=azuredevops
export IA_GIT_ORGANIZATION=wcbbc
export IA_GIT_PROJECT=AI-Operations
export IA_GIT_REPO=inno-tech-aisdlc-apps   # or your POC repo
export IA_GIT_TOKEN='…pat…'
```

### Verify

1. Keep `IA_DEPLOY_PROVIDER=compose` (local deploy).
2. Restart AgentHub; run `./scripts/verify-ta.sh` **or** interactive Calculator.
3. AzDO → Repos → **Pull requests**: new PR from `feature/project-…`, then **Completed** after TA merge.
4. Artifact `merge.json` shows `"provider":"azuredevops"`.

**Exit:** One green AzDO PR complete; switching `IA_GIT_PROVIDER=gitea` still passes local verify.

---

## 3. App Service DEV host (replace Compose)

### ClickOps — plan + web app

1. RG → **Create** → **App Service plan**  
   - OS: Linux  
   - SKU: B1 (or free F1 if available for smoke)  
   - Name: e.g. `plan-bgx-poc`
2. **Create** → **Web App**  
   - Name: globally unique, e.g. `app-bgx-poc-calc`  
   - Publish: Code  
   - Runtime: .NET 8 **or** container if your region lacks .NET 10 — match template TFM when possible  
   - Plan: the one above  
   - Review + create.
3. Web App → **Configuration** → **General settings**  
   - Ensure stack matches publish output.
4. Web App → **Deployment Center** / **Download publish profile** (or **Get publish profile** from Overview).  
   - Open the `.PublishSettings` XML.  
   - Note `userName` and `userPWD` for the MSDeploy/profile profile (often `app-name\$app-name`).
5. Web App → **Configuration** → **General settings** → enable **SCM Basic Auth Publishing Credentials** if zipdeploy with basic auth fails (portal toggle; required for publish-profile basic auth on newer apps).

### Wire deploy

```bash
export IA_DEPLOY_PROVIDER=appservice
export IA_DEPLOY_APP_NAME=app-bgx-poc-calc
export IA_DEPLOY_PUBLIC_URL=https://app-bgx-poc-calc.azurewebsites.net
export IA_DEPLOY_USER='app-bgx-poc-calc\$app-bgx-poc-calc'   # from profile
export IA_DEPLOY_PASSWORD='…from profile…'
```

### Verify

1. Restart AgentHub with AzDO (or Gitea) + App Service env.
2. Run Calculator pipeline.
3. Studio / API shows `deployedUrl` = `https://….azurewebsites.net`.
4. Browser or curl:
   ```bash
   curl -fsS "$IA_DEPLOY_PUBLIC_URL/health"
   curl -fsS "$IA_DEPLOY_PUBLIC_URL/api/add?a=2&b=3"
   ```
5. TA_VERIFY greens; TA_MERGE completes the PR.

**Exit:** Live Azure HTTPS smoke green; `IA_DEPLOY_PROVIDER=compose` still works for laptop-only days.

---

## 4. LLM + secrets hardening

### ClickOps

1. Prefer existing AI Gateway subscription key in Key Vault; grant your user/app read.
2. Or create Azure OpenAI / use Gateway endpoint already in `appsettings`.
3. Set:

```bash
export Llm__Provider=Gateway
export Llm__Gateway__SubscriptionKey='…'
# or AIGW_SUBSCRIPTION_KEY if your host maps it
```

### Verify

Interactive studio run with real model: PRD/SD/IA_REVIEW text is conversational; Fixture is only for unit/plumbing.

---

## 5. Recommended bring-up order

```text
Local green (Gitea+Compose+File)
  → Storage Azure (Step 1)
  → AzDO Git (Step 2)
  → App Service (Step 3)
  → Real LLM + Key Vault (Step 4)
```

After each step: re-run local mode (`IA_GIT_PROVIDER=gitea`, `IA_DEPLOY_PROVIDER=compose`) once to prove dual-mode.

---

## 6. Env cheat sheet

| Env | Purpose |
|---|---|
| `IA_GIT_PROVIDER` | `gitea` \| `azuredevops` |
| `IA_GIT_ORGANIZATION` | AzDO org |
| `IA_GIT_PROJECT` | AzDO project |
| `IA_GIT_REPO` | Repo name |
| `IA_GIT_TOKEN` / `AZDO_PAT` | PAT |
| `IA_DEPLOY_PROVIDER` | `compose` \| `appservice` |
| `IA_DEPLOY_APP_NAME` | Web app name |
| `IA_DEPLOY_PUBLIC_URL` | https URL |
| `IA_DEPLOY_USER` / `IA_DEPLOY_PASSWORD` | Publish profile |
| `BlobStorage__Provider` | `File` \| `Azure` |
| `BlobStorage__ConnectionString` | Azurite or Azure |
| `TableStorage__Provider` | `Memory` \| `Azure` |

Example file (no secrets): `poc/agenthub-api/src/AgentHub.WebApi/appsettings.Azure.example.json`.

---

## 7. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `Organization is required` | Provider=azuredevops without org | Set `IA_GIT_ORGANIZATION` |
| `Token is required` | Missing PAT | Create PAT; set `IA_GIT_TOKEN` |
| AzDO PR create 401/403 | PAT scopes | Code + PR read/write |
| zipdeploy 401 | SCM basic auth off / wrong user | Enable SCM basic auth; copy publish profile user/pwd |
| Health timeout after zipdeploy | Wrong runtime / app crash | Log stream in portal; confirm `/health` exists in published app |
| Local verify fails after Azure work | Defaults changed | Development JSON must stay `gitea` + `compose` |

---

## 8. What this POC still does not automate

- Creating RG / storage / web app from the agent (you ClickOps once).
- Managed Identity role assignments (person grants).
- Full AzDO Boards epic/story sync (design prompt has it; POC IA focuses on mono folder + PR).
- Frontend App Service as a second site (backend URL is enough for Calculator smoke).

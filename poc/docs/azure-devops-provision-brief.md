# DevOps provision brief — `cc-sb-sb-aisdlc-rg` + `cc-sb-sb-aisdlc-apps-rg`

**Audience:** DevOps / cloud engineer  
**From:** BuildGenix / AgentHub team (branch `main-azure`)  
**Goal:** Pre-create Azure resources so IA/TA can build, push, and deploy generated apps with **managed identity** (no PATs or publish-profile secrets in cloud App Settings).

Related ClickOps detail (portal clicks + env vars for a laptop): [`azure-clickops-integration.md`](azure-clickops-integration.md).

---

## 0. Resource-group split (locked for this ask)

| Resource group | Owns | Examples |
|---|---|---|
| **`cc-sb-sb-aisdlc-rg`** | Platform apps (already there) | `agenthub-api`, `buildgenix-api`, `buildgenix-web`, platform storage / Service Bus if already colocated |
| **`cc-sb-sb-aisdlc-apps-rg`** | IA/TA builder host + everything generated apps need | Builder App Service (container), ACR for builder image, DEV App Service plan + generated web apps, sandbox storage/KV if separate |

**Already assumed:** a managed identity used by platform / AgentHub in `cc-sb-sb-aisdlc-rg` has **Contributor** on `cc-sb-sb-aisdlc-apps-rg` so it can create resources there during Deploy.

---

## 1. Where should the Docker image (ACR) live?

**Put the ACR in `cc-sb-sb-aisdlc-apps-rg` (recommended).**

| Option | Verdict | Why |
|---|---|---|
| **ACR in `cc-sb-sb-aisdlc-apps-rg`** | **Preferred** | Builder image exists only for IA/TA. Lifecycle, cost, and RBAC stay with the agent/sandbox RG. Core apps in `cc-sb-sb-aisdlc-rg` are zip-deployed and do **not** need a registry. Matches Phase 0: ACR exception is scoped to the builder deployment. |
| ACR in `cc-sb-sb-aisdlc-rg` | Acceptable only if you already have a shared platform registry | Then grant the **builder** identity `AcrPull` on that registry (cross-RG). Do not force generated-app plans into `cc-sb-sb-aisdlc-rg`. |

**Runtime placement (not the registry):**

| What runs | Where |
|---|---|
| Builder container (agenthub-api image with SDK/Node/git/az) | **`cc-sb-sb-aisdlc-apps-rg`** App Service for Containers |
| Generated Calculator / customer FE+BE App Services | **`cc-sb-sb-aisdlc-apps-rg`** |
| Platform AgentHub / BuildGenix (HTTP APIs + UI) | **`cc-sb-sb-aisdlc-rg`** (already) |

So: **image registry + builder host + deploy targets = `cc-sb-sb-aisdlc-apps-rg`.** Platform control plane stays in `cc-sb-sb-aisdlc-rg`.

---

## 2. Please provision (checklist)

Names below are **conventions** — rename to your org standard; return the final names to the engineering team.

### 2.1 In `cc-sb-sb-aisdlc-apps-rg`

| # | Resource | Suggested name / notes | Done |
|---|---|---|---|
| 1 | **Azure Container Registry** | e.g. `ccsbaisdlcappsacr` (globally unique; your naming OK). Basic/Standard. Admin user **off**. | ☐ |
| 2 | **User-assigned managed identity — builder** | e.g. `id-aisdlc-builder`. Used as builder App Service identity **and** runtime (`AZURE_CLIENT_ID`). | ☐ |
| 3 | **App Service plan — builder** | Linux. Size from peak memory (start P2v3 or similar until measured). Always On. | ☐ |
| 4 | **Web App — builder (container)** | e.g. `agenthub-builder-dev`. Deploy **container** from ACR image `agenthub-ia-builder:<tag>`. Always On, 1 instance. Attach identity from #2. `acrUseManagedIdentityCreds=true`, `acrUserManagedIdentityID` = #2. | ☐ |
| 5 | **App Service plan — generated apps** | e.g. `asp-aisdlc-apps-dev`. Linux B2 (or agreed SKU). Shared by generated FE/BE. | ☐ |
| 6 | **(Optional first app)** empty placeholder web app | Only if you want a smoke target before first IA run; otherwise IA/Deploy creates `bgx-{slug}-*-dev` under Contributor. | ☐ |
| 7 | **Storage (sandbox artifacts)** — if not reusing platform storage | Account + container `agenthub-artifacts` (and tables as needed). Prefer MI data-plane roles, not account keys in cloud. | ☐ |
| 8 | **(Optional) Key Vault — sandbox** | Secrets for generated apps’ LLM keys later. | ☐ |
| 9 | **User-assigned identity — generated apps** | e.g. `id-aisdlc-sandbox-apps`. Attach to LLM-enabled generated apps; Key Vault Secrets User on #8. | ☐ |

### 2.2 In `cc-sb-sb-aisdlc-rg` (confirm / wire only)

| # | Item | Notes | Done |
|---|---|---|---|
| 10 | Platform apps already deployed | `agenthub-api`, `buildgenix-api`, `buildgenix-web` | ☐ |
| 11 | Platform managed identity (or App Service system/user MI) | The one that already has **Contributor** on `cc-sb-sb-aisdlc-apps-rg` | ☐ |
| 12 | App settings for dual-mode Azure | See §4 — `Auth=managedIdentity`, ServiceUri for storage, no PAT/publish profile in cloud | ☐ |

### 2.3 Azure DevOps / GitHub — what is needed now

| System | Needed now? | Detail |
|---|---|---|
| **Azure DevOps** | **Yes** (once builder MI exists) | Org **`wcbbc`**, project **`AI-Operations`**, repo **`inno-tech-aisdlc-apps`**. Agents push + PR there — not GitHub. |
| **GitHub** | **No** | Platform source only for engineers. DevOps does not need GitHub for this provision; agents do not use GitHub at runtime. |

| # | Item | Notes | Done |
|---|---|---|---|
| 13 | Repo `inno-tech-aisdlc-apps` | Initialized `main` (README). URL: `https://dev.azure.com/wcbbc/AI-Operations/_git/inno-tech-aisdlc-apps`. | ☐ |
| 14 | Add **builder identity** service principal to the AzDO org | Organization Settings → Users → add the Entra app/SP for `id-aisdlc-builder`. Grant **Basic** + contribute on the project/repo (**Code** read/write + **Pull Request** read/write). | ☐ |
| 15 | Do **not** rely on human PATs in cloud | Laptop may still use PAT for local→Azure; cloud AgentHub/builder uses MI Bearer. | ☐ |
| 16 | GitHub | Out of scope for DevOps — no PAT, no webhook, no deploy key. | ☐ |

### 2.4 RBAC matrix (minimum)

| Identity | Scope | Role |
|---|---|---|
| Platform / core AgentHub MI (`cc-sb-sb-aisdlc-rg`) | `cc-sb-sb-aisdlc-apps-rg` | **Contributor** (already planned — create plans/apps/storage) |
| Builder MI `id-aisdlc-builder` | ACR in `cc-sb-sb-aisdlc-apps-rg` | **AcrPull** |
| Builder MI | `cc-sb-sb-aisdlc-apps-rg` | **Contributor** (or narrower: Website Contributor + storage data roles if you harden later) |
| Builder MI | Storage account (artifacts) | **Storage Blob Data Contributor** (+ Table Data Contributor if tables live there) |
| Builder MI | AzDO org/project | Member with Code + PR contribute (see #14) |
| `id-aisdlc-sandbox-apps` | Sandbox Key Vault | **Key Vault Secrets User** |
| CI / human pushing builder image | ACR | **AcrPush** (DevOps SP or your pipeline identity) |

---

## 3. Builder image handoff (once ACR exists)

Engineering can build from repo `main-azure`:

```bash
cd poc
./scripts/build-ia-builder.sh
./scripts/save-ia-builder.sh   # optional tar for offline transfer
```

DevOps push:

```bash
az acr login --name <acr-name>
docker tag agenthub-ia-builder:local <acr-name>.azurecr.io/agenthub-ia-builder:<yyyyMMdd>
docker push <acr-name>.azurecr.io/agenthub-ia-builder:<yyyyMMdd>
```

Point the builder Web App container setting at that tag. App setting for runs that still use Docker locally against ACR:

```bash
IA_BUILDER_IMAGE=<acr-name>.azurecr.io/agenthub-ia-builder:<yyyyMMdd>
```

Details: `poc/agenthub-api/docker/ia-builder/EXPORT-NOTE.md`.

---

## 4. App settings / env the apps expect (cloud)

When AgentHub (core or builder) runs **in Azure**, use MI — not keys:

```text
Ia__Git__Provider=azuredevops
Ia__Git__Auth=managedIdentity
Ia__Git__Organization=<org>
Ia__Git__Project=<project>
Ia__Git__Repo=<repo>

Ia__Deploy__Provider=appservice
Ia__Deploy__Auth=managedIdentity
Ia__Deploy__AppName=<target-or-convention>
Ia__Deploy__PublicBaseUrl=https://….azurewebsites.net

BlobStorage__Provider=Azure
BlobStorage__ServiceUri=https://<account>.blob.core.windows.net
BlobStorage__ContainerName=agenthub-artifacts
TableStorage__Provider=Azure
TableStorage__ServiceUri=https://<account>.table.core.windows.net

AZURE_CLIENT_ID=<client-id of id-aisdlc-builder or platform UAMI>
```

**Do not** set `IA_GIT_TOKEN`, `IA_DEPLOY_USER`, `IA_DEPLOY_PASSWORD`, or storage `ConnectionString` on the cloud app when MI is configured.

Laptop bring-up still uses PAT / publish profile — see [`OTHER-MACHINE-SETUP.md`](OTHER-MACHINE-SETUP.md).

---

## 5. What DevOps returns to engineering

Please send back a short filled table:

```text
Region:
ACR login server:
Builder web app name + default hostname:
Builder identity client id / principal id:
Sandbox plan name:
Storage blob ServiceUri (if new):
AzDO: wcbbc / AI-Operations / inno-tech-aisdlc-apps ready (Y/N)
Confirmation: builder SP added to AzDO with Code+PR (Y/N)
Confirmation: AcrPull on builder identity (Y/N)
Confirmation: cc-sb-sb-aisdlc-rg MI still Contributor on cc-sb-sb-aisdlc-apps-rg (Y/N)
GitHub access for DevOps: not required
```


---

## 6. Out of scope for this ticket (engineering / later)

- Agent creating RGs from scratch (you pre-create `cc-sb-sb-aisdlc-apps-rg` contents above).
- Full Service Bus topic map hardening (if not already present on platform).
- Merging `main-azure` → `main`.
- Production network lockdown / private endpoints (nice-to-have after first green deploy).

---

## 7. One-line answer for the ACR question

**Yes — run/store the IA/TA Docker image registry in `cc-sb-sb-aisdlc-apps-rg` next to the builder and generated App Services; keep platform APIs in `cc-sb-sb-aisdlc-rg`.** Cross-RG Contributor from `cc-sb-sb-aisdlc-rg` → `cc-sb-sb-aisdlc-apps-rg` is what lets the platform identity create deploy targets; `AcrPull` on the builder identity is what lets the builder start.

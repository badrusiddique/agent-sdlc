# DevOps provision brief — `abc-sg` + `abc-agent-sg`

**Audience:** DevOps / cloud engineer  
**From:** BuildGenix / AgentHub team (branch `main-azure`)  
**Goal:** Pre-create Azure resources so IA/TA can build, push, and deploy generated apps with **managed identity** (no PATs or publish-profile secrets in cloud App Settings).

Related ClickOps detail (portal clicks + env vars for a laptop): [`azure-clickops-integration.md`](azure-clickops-integration.md).

---

## 0. Resource-group split (locked for this ask)

| Resource group | Owns | Examples |
|---|---|---|
| **`abc-sg`** | Platform apps (already there) | `agenthub-api`, `buildgenix-api`, `buildgenix-web`, platform storage / Service Bus if already colocated |
| **`abc-agent-sg`** | IA/TA builder host + everything generated apps need | Builder App Service (container), ACR for builder image, DEV App Service plan + generated web apps, sandbox storage/KV if separate |

**Already assumed:** a managed identity used by platform / AgentHub in `abc-sg` has **Contributor** on `abc-agent-sg` so it can create resources there during Deploy.

---

## 1. Where should the Docker image (ACR) live?

**Put the ACR in `abc-agent-sg` (recommended).**

| Option | Verdict | Why |
|---|---|---|
| **ACR in `abc-agent-sg`** | **Preferred** | Builder image exists only for IA/TA. Lifecycle, cost, and RBAC stay with the agent/sandbox RG. Core apps in `abc-sg` are zip-deployed and do **not** need a registry. Matches Phase 0: ACR exception is scoped to the builder deployment. |
| ACR in `abc-sg` | Acceptable only if you already have a shared platform registry | Then grant the **builder** identity `AcrPull` on that registry (cross-RG). Do not force generated-app plans into `abc-sg`. |

**Runtime placement (not the registry):**

| What runs | Where |
|---|---|
| Builder container (agenthub-api image with SDK/Node/git/az) | **`abc-agent-sg`** App Service for Containers |
| Generated Calculator / customer FE+BE App Services | **`abc-agent-sg`** |
| Platform AgentHub / BuildGenix (HTTP APIs + UI) | **`abc-sg`** (already) |

So: **image registry + builder host + deploy targets = `abc-agent-sg`.** Platform control plane stays in `abc-sg`.

---

## 2. Please provision (checklist)

Names below are **conventions** — rename to your org standard; return the final names to the engineering team.

### 2.1 In `abc-agent-sg`

| # | Resource | Suggested name / notes | Done |
|---|---|---|---|
| 1 | **Azure Container Registry** | e.g. `abcagentsacr` (globally unique). Basic/Standard. Admin user **off**. | ☐ |
| 2 | **User-assigned managed identity — builder** | e.g. `id-abc-builder`. Used as builder App Service identity **and** runtime (`AZURE_CLIENT_ID`). | ☐ |
| 3 | **App Service plan — builder** | Linux. Size from peak memory (start P2v3 or similar until measured). Always On. | ☐ |
| 4 | **Web App — builder (container)** | e.g. `agenthub-builder-dev`. Deploy **container** from ACR image `agenthub-ia-builder:<tag>`. Always On, 1 instance. Attach identity from #2. `acrUseManagedIdentityCreds=true`, `acrUserManagedIdentityID` = #2. | ☐ |
| 5 | **App Service plan — generated apps** | e.g. `asp-abc-agent-dev`. Linux B2 (or agreed SKU). Shared by generated FE/BE. | ☐ |
| 6 | **(Optional first app)** empty placeholder web app | Only if you want a smoke target before first IA run; otherwise IA/Deploy creates `bgx-{slug}-*-dev` under Contributor. | ☐ |
| 7 | **Storage (sandbox artifacts)** — if not reusing platform storage | Account + container `agenthub-artifacts` (and tables as needed). Prefer MI data-plane roles, not account keys in cloud. | ☐ |
| 8 | **(Optional) Key Vault — sandbox** | Secrets for generated apps’ LLM keys later. | ☐ |
| 9 | **User-assigned identity — generated apps** | e.g. `id-abc-sandbox-apps`. Attach to LLM-enabled generated apps; Key Vault Secrets User on #8. | ☐ |

### 2.2 In `abc-sg` (confirm / wire only)

| # | Item | Notes | Done |
|---|---|---|---|
| 10 | Platform apps already deployed | `agenthub-api`, `buildgenix-api`, `buildgenix-web` | ☐ |
| 11 | Platform managed identity (or App Service system/user MI) | The one that already has **Contributor** on `abc-agent-sg` | ☐ |
| 12 | App settings for dual-mode Azure | See §4 — `Auth=managedIdentity`, ServiceUri for storage, no PAT/publish profile in cloud | ☐ |

### 2.3 Azure DevOps (org / project)

| # | Item | Notes | Done |
|---|---|---|---|
| 13 | Repo for generated mono / apps | Initialized `main` (README). | ☐ |
| 14 | Add **builder identity** service principal to the AzDO org | Organization Settings → Users → add the Entra app/SP for `id-abc-builder`. Grant **Basic** + contribute on the target project/repos (Code + PR). | ☐ |
| 15 | Do **not** rely on human PATs in cloud | Laptop may still use PAT for local→Azure; cloud AgentHub/builder uses MI Bearer. | ☐ |

### 2.4 RBAC matrix (minimum)

| Identity | Scope | Role |
|---|---|---|
| Platform / core AgentHub MI (`abc-sg`) | `abc-agent-sg` | **Contributor** (already planned — create plans/apps/storage) |
| Builder MI `id-abc-builder` | ACR in `abc-agent-sg` | **AcrPull** |
| Builder MI | `abc-agent-sg` | **Contributor** (or narrower: Website Contributor + storage data roles if you harden later) |
| Builder MI | Storage account (artifacts) | **Storage Blob Data Contributor** (+ Table Data Contributor if tables live there) |
| Builder MI | AzDO org/project | Member with Code + PR contribute (see #14) |
| `id-abc-sandbox-apps` | Sandbox Key Vault | **Key Vault Secrets User** |
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
az acr login --name <abcagentsacr>
docker tag agenthub-ia-builder:local <abcagentsacr>.azurecr.io/agenthub-ia-builder:<yyyyMMdd>
docker push <abcagentsacr>.azurecr.io/agenthub-ia-builder:<yyyyMMdd>
```

Point the builder Web App container setting at that tag. App setting for runs that still use Docker locally against ACR:

```bash
IA_BUILDER_IMAGE=<abcagentsacr>.azurecr.io/agenthub-ia-builder:<yyyyMMdd>
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

AZURE_CLIENT_ID=<client-id of id-abc-builder or platform UAMI>
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
AzDO org / project / repo:
Confirmation: builder SP added to AzDO (Y/N)
Confirmation: AcrPull on builder identity (Y/N)
Confirmation: abc-sg MI still Contributor on abc-agent-sg (Y/N)
```

---

## 6. Out of scope for this ticket (engineering / later)

- Agent creating RGs from scratch (you pre-create `abc-agent-sg` contents above).
- Full Service Bus topic map hardening (if not already present on platform).
- Merging `main-azure` → `main`.
- Production network lockdown / private endpoints (nice-to-have after first green deploy).

---

## 7. One-line answer for the ACR question

**Yes — run/store the IA/TA Docker image registry in `abc-agent-sg` next to the builder and generated App Services; keep platform APIs in `abc-sg`.** Cross-RG Contributor from `abc-sg` → `abc-agent-sg` is what lets the platform identity create deploy targets; `AcrPull` on the builder identity is what lets the builder start.

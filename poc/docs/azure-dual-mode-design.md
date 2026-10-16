# Azure dual-mode design (local + cloud, same code)

**Branch:** `main-greenfield` (includes dual-mode seams from `main-azure` / `cursor/main-azure-4837`)  
**Invariant:** One codebase. Config selects stand-in vs Azure. Local Docker path stays the default and must stay green.

## Modes

| Concern | Local (default) | Azure | Seam |
|---|---|---|---|
| Blob | `File` or `Azure`+Azurite conn | `Azure` + storage account / MI | `BlobStorage:*` (already) |
| Table | `Memory` or `Azure`+Azurite | `Azure` + storage account / MI | `TableStorage:*` (already) |
| Git / PR / merge | `Ia:Git:Provider=gitea` | `Ia:Git:Provider=azuredevops` | `IGitPullRequestClient` |
| DEV deploy | `Ia:Deploy:Provider=compose` | `Ia:Deploy:Provider=appservice` | `IDeployHost` |
| LLM | Fixture / Ollama | Gateway / Azure OpenAI | `Llm:*` (already) |

## Auth (key local vs managed identity cloud)

When the **provider** points at Azure, **auth** still differs by where AgentHub runs:

| Concern | Laptop / local Docker → Azure | AgentHub hosted in Azure |
|---|---|---|
| Storage | `ConnectionString` (access key) | `ServiceUri` + `DefaultAzureCredential` |
| Git | `Ia:Git:Auth=pat` (`IA_GIT_TOKEN`) | `Ia:Git:Auth=managedIdentity` (Bearer AzDO AAD) |
| Deploy | `Ia:Deploy:Auth=publishProfile` | `Ia:Deploy:Auth=managedIdentity` (Bearer Kudu / ARM) |

Env overlays: `IA_GIT_AUTH`, `IA_DEPLOY_AUTH`. Defaults remain `pat` / `publishProfile` so a clean-machine zip + PAT path works without MI.

## Rules

1. Default Development config = Gitea + Compose + File/Memory (or Azurite). `verify-ta.sh` must pass unchanged.
2. Azure providers are selected only when config/env says so. Missing Azure credentials fail with an actionable message (point to ClickOps doc), never silently fall back in a way that claims Azure success.
3. Hybrid is allowed: e.g. local Gitea + real Azure Storage, or Azurite + AzDO, for incremental bring-up.
4. No secrets in git. Use env / user-secrets / Key Vault references.
5. Do not put PATs or publish-profile passwords in cloud App Settings when MI is available — set `Auth=managedIdentity` instead.

## Out of scope this slice

Creating Azure resources from the agent (ClickOps / `az` is human). Full BacklogSync epics. Managed Identity grant automation (person assigns roles in portal / AzDO).

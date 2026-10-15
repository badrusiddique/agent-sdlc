# Azure dual-mode design (local + cloud, same code)

**Branch:** `cursor/main-azure-4837` (also pushed as `main-azure`)  
**Invariant:** One codebase. Config selects stand-in vs Azure. Local Docker path stays the default and must stay green.

## Modes

| Concern | Local (default) | Azure | Seam |
|---|---|---|---|
| Blob | `File` or `Azure`+Azurite conn | `Azure` + storage account / MI | `BlobStorage:*` (already) |
| Table | `Memory` or `Azure`+Azurite | `Azure` + storage account / MI | `TableStorage:*` (already) |
| Git / PR / merge | `Ia:Git:Provider=gitea` | `Ia:Git:Provider=azuredevops` | `IGitPullRequestClient` |
| DEV deploy | `Ia:Deploy:Provider=compose` | `Ia:Deploy:Provider=appservice` | `IDeployHost` |
| LLM | Fixture / Ollama | Gateway / Azure OpenAI | `Llm:*` (already) |

## Rules

1. Default Development config = Gitea + Compose + File/Memory (or Azurite). `verify-ta.sh` must pass unchanged.
2. Azure providers are selected only when config/env says so. Missing Azure credentials fail with an actionable message (point to ClickOps doc), never silently fall back in a way that claims Azure success.
3. Hybrid is allowed: e.g. local Gitea + real Azure Storage, or Azurite + AzDO, for incremental bring-up.
4. No secrets in git. Use env / user-secrets / Key Vault references.

## Out of scope this slice

Creating Azure resources from the agent (ClickOps / `az` is human). Full BacklogSync epics. Managed Identity grant automation.

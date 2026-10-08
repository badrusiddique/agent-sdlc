# Azurite blob storage snapshot

Exported from the Docker Azurite container (`agenthub-azurite`) after the final happy-path verification with real Azure Storage emulator tables/blobs + Ollama + Docker Compose deploys.

## Layout

| Path | Contents |
|---|---|
| `agenthub-artifacts/` | Full dump of the `agenthub-artifacts` blob container (phase-0 artifact names per work item) |
| `manifest.json` | Blob name + size index for that container |
| `audit-logs/` | `AuditLog.json` files written by the buildgenix POC orchestrator during the Azurite run |
| `verify-results/` | Raw `POST /api/poc/pipeline/run` responses and smoke JSON from the three examples |
| `tables-summary/row-counts.json` | Azurite table row counts at export time |

## Final work items ( Azurite + Ollama + Docker )

| Example | ProjectId | WorkItemId | Deploy URL (at run time) |
|---|---|---|---|
| Hello World (fe-be-llm, rerun with live Ollama thought) | `01a11a5b648277e5baa20d1555157c7e` | `01a11a5b64877b7d9a7f98d6cd129587` | `http://127.0.0.1:44413` |
| Calculator add (fe-be) | `01a11a59e967760bafa174e6984ed15d` | `01a11a59e96c765c8816f61e918c084a` | `http://127.0.0.1:33011` |
| Calculator subtract (Existing) | `01a11a59e967760bafa174e6984ed15d` | `01a11a5a284f7f0086050ecff7c94322` | `http://127.0.0.1:44015` |

Artifact prefix pattern: `{projectId}/{workItemId}/v1/{artifact}`.

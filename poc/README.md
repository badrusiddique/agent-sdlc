# Pre-phase-0 pipeline POC

Copies of `agenthub-api` and `buildgenix-api` with a Healthy-shaped **Pipeline** agent and a happy-path orchestrator.

Root projects are untouched. Integrate later by lifting `AgentHub.Agents.Pipeline` and the buildgenix `Poc/` orchestrator patterns into the real hosts.

## Layout

| Path | Role |
|---|---|
| `poc/agenthub-api` | Agent host + `AgentHub.Agents.Pipeline` (handler, dispatcher, worker, templates) |
| `poc/buildgenix-api` | Orchestrator: `POST /api/poc/pipeline/run` drives stages, gates, `WorkItemEvents`, `AuditLog.json` |

## Design alignment

- Agent writes only `ActivityLog` / `EffectLedger` / `AgProcessedEvents` (or in-memory stand-ins in POC mode).
- buildgenix owns stage transitions, Approvals, Artifacts registry keys, WorkItemEvents.
- Artifact names match phase-0: `intake-summary.json`, `prd.json`, `prd.md`, `techspec.json`, `system-design.md`, `code-state.json`, `review.json`, `test-results.json`, `completion-summary.json`.
- Deploy then Finalize; three auto-approved gates (PRD, System Design, DEV acceptance).
- `AuditLog.json` holds the original request and every transition.

## Run locally (file + fixture LLM)

```bash
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$HOME/.dotnet:$PATH
export BGX_SKIP_TABLE_INIT=1

# Terminal 1 — AgentHub (port 43180)
cd poc/agenthub-api
ASPNETCORE_URLS=http://127.0.0.1:43180 dotnet run --project src/AgentHub.WebApi

# Terminal 2 — BuildGenix (port 43181)
cd poc/buildgenix-api
ASPNETCORE_URLS=http://127.0.0.1:43181 dotnet run --project src/BuildGenix.WebApi

# Terminal 3 — three happy paths
./scripts/verify-three.sh
```

## Run with Azurite + Ollama + Docker Compose

Requires Docker Azurite on `10000–10002`, Ollama with `llama3.2:1b`, and `OLLAMA_HOST=0.0.0.0:11434` so containers can reach the host.

```bash
AZURITE_CS='DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;'

# AgentHub
cd poc/agenthub-api
ASPNETCORE_URLS=http://127.0.0.1:43180 \
TableStorage__Provider=Azure TableStorage__ConnectionString="$AZURITE_CS" \
BlobStorage__Provider=Azure BlobStorage__ConnectionString="$AZURITE_CS" \
Llm__Provider=Ollama Llm__Ollama__BaseUrl=http://127.0.0.1:11434 \
Llm__Ollama__DefaultModel=llama3.2:1b \
OLLAMA_BASE_URL=http://127.0.0.1:11434 OLLAMA_MODEL=llama3.2:1b \
dotnet run --project src/AgentHub.WebApi --no-launch-profile

# BuildGenix (unset BGX_SKIP_TABLE_INIT)
cd poc/buildgenix-api
ASPNETCORE_URLS=http://127.0.0.1:43181 \
TableStorage__ConnectionString="$AZURITE_CS" \
PocPipeline__UseInMemoryTables=false \
PocPipeline__AgentHubBaseUrl=http://127.0.0.1:43180 \
PocPipeline__AuditLogDirectory=/tmp/bgx-poc-audit-azurite \
dotnet run --project src/BuildGenix.WebApi --no-launch-profile

./scripts/verify-three.sh
```

## Evidence and reviews

| Doc | Purpose |
|---|---|
| [`docs/what-was-done.md`](docs/what-was-done.md) | What this POC delivered |
| [`docs/six-persona-happy-path-verification.md`](docs/six-persona-happy-path-verification.md) | SA / PE / QA / DBA / DevOps / PM sign-off |
| [`blob-storage-snapshot/`](blob-storage-snapshot/) | Azurite blob dump + audit logs + verify JSON from the final run |

## Endpoints

- AgentHub dispatch (one stage): `POST http://127.0.0.1:43180/api/pipeline/dispatch`
- BuildGenix full run: `POST http://127.0.0.1:43181/api/poc/pipeline/run`

Example body:

```json
{
  "userRequest": "Build a Hello World app: Angular FE + .NET 10 BE + an LLM giving a thought of the day",
  "stackProfile": "fe-be-llm",
  "projectMode": "New",
  "projectName": "Hello World"
}
```

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

## Run locally

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

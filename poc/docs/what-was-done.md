# What was done — pre-phase-0 pipeline POC

## Goal

One Healthy-shaped Pipeline agent covering all six lifecycle stages, exercised on three happy-path examples, without editing the root APIs.

## Delivered under `poc/`

1. **`poc/agenthub-api`** — copy of AgentHub with `AgentHub.Agents.Pipeline` (handler, dispatcher, worker, templates `fe-be` / `fe-be-llm`, `LocalComposeHost`, in-process dispatch).
2. **`poc/buildgenix-api`** — copy of BuildGenix with `PocPipelineOrchestrator` (`POST /api/poc/pipeline/run`): stage loop, auto-gates, WorkItemEvents, Approvals, Artifacts registry, `AuditLog.json`.
3. **Templates** — Angular-static + .NET 10 backends; Hello World calls Ollama for thought-of-the-day; calculator add/subtract endpoints; Docker Compose deploy path.
4. **Verify script** — `poc/scripts/verify-three.sh`.
5. **Blob snapshot** — `poc/blob-storage-snapshot/` exported from Azurite after the final actual-resources run.
6. **Six-persona gate** — `poc/docs/six-persona-happy-path-verification.md`.

## Final verification (actual resources)

| Dependency | How it ran |
|---|---|
| Azure Blob + Table | Docker Azurite `agenthub-azurite` |
| LLM | Ollama `llama3.2:1b` (`OLLAMA_HOST=0.0.0.0:11434`) |
| Deployed apps | Docker Compose per work item |

All three examples reached **Completed / DONE** with live smoke checks. Hello World `/api/thought` returned `source=ollama`.

## Fixes applied during final pass

- CS0136 in `LocalComposeHost` (`healthy` renamed per branch).
- `ApplyImplementationHints` no longer overwrites `ThoughtCopy.txt` with raw LLM JSON.
- Ollama bound on all interfaces so Compose containers can call `host.docker.internal:11434`.
- **AI Gateway alongside Ollama:** `CompositeLlmClient` + `Llm:FallbackProvider`, explicit `ollama:` / `gateway:` ModelId routing, `/api/pipeline/llm-smoke`, Gateway request body serialization fix (`StringContent` JSON). Verified Ollama e2e still green; Gateway path verified via local stub + fallback when Ollama is down. Real APIM needs `AIGW_SUBSCRIPTION_KEY`.

## Out of scope (intentional)

- Root `agenthub-api` / `buildgenix-api` changes
- Real Service Bus topics (InProcessOnly)
- Fault paths / CrashAt
- Auth, UI, ADO repos, cloud deploy

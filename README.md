# agent-sdlc (BuildGenix)

Iteration 0 foundation for the BuildGenix AI-SDLC platform.

## Layout

| Folder | Role |
|---|---|
| `buildgenix-api/` | BuildGenix .NET 10 WebAPI + `BuildGenix.Data` (BG tables) + Docker + BG heartbeat ping/pong |
| `agenthub-api/` | AgentHub .NET 10 WebAPI + `AgentHub.Data` (AH tables only) + Docker + AH heartbeat ping/pong |

Project Context (decisions, iteration plan, full contracts) also lives in the Cursor Agent Store.

## Docs for UI wiring

| Doc | Audience |
|---|---|
| [`docs/junior-ui-endpoint-wiring.md`](docs/junior-ui-endpoint-wiring.md) | Juniors: mock → endpoint → DTO → wire status + problem codes |
| [`docs/ui-mock-endpoint-map.md`](docs/ui-mock-endpoint-map.md) | Juniors: page-by-page mock (01–23) → endpoints |
| [`docs/ui-endpoint-map.md`](docs/ui-endpoint-map.md) | Surface-level UI shell → endpoints |
| [`docs/buildgenix-api-new-machine-integration.md`](docs/buildgenix-api-new-machine-integration.md) | Spoon-feed Copilot prompt to merge BG catalog into new-machine boilerplate |
| [`docs/agenthub-api-new-machine-integration.md`](docs/agenthub-api-new-machine-integration.md) | Spoon-feed Copilot prompt for AH HEALTHY + Gateway + Phase 0 guardrails |
| [`docs/other-machine-proceed-instructions.md`](docs/other-machine-proceed-instructions.md) | Detailed other-machine proceed order (Badru) |
| [`docs/start-on-new-machine.md`](docs/start-on-new-machine.md) | Short start guide |
| [`docs/azure-appservice-deploy-baby-steps.md`](docs/azure-appservice-deploy-baby-steps.md) | Baby-step Azure App Service deploy + verify for DevOps |
| [`docs/manual-e2e-means-actual.md`](docs/manual-e2e-means-actual.md) | Binding Manual QA = actual resources (not Fixture) |
| [`docs/plans/agent-guardrails-design.md`](docs/plans/agent-guardrails-design.md) | Guardrails stack (C# + Azure Content Safety; not GuardrailsAI runtime) |

## Local run

See each folder's `README.md`. Ports: buildgenix `18765`, agenthub `18766`.

## Status

- I0a / I0b on `main` with D-C1 heartbeat order fixed (publish → mark → complete).
- I1 controllers + catalog routes on `main` under `buildgenix-api/`.
- PE Important hardenings on `main` (emulator `/health` on AH, MessageId safety, Implement lock compensate + terminal release, DefaultAzureCredential ServiceUri, problem `code`).
- Migration spoon-feed docs + junior wiring guide on `main`.
- Local Docker E2E: BG catalog + AH HEALTHY. **Manual QA** with Ollama + Phase 0 guardrails on `main` ≥ `fbb71b1` (see `docs/manual-e2e-agenthub-post-guardrails-2026-10-06.md`). Fixture is plumbing only.

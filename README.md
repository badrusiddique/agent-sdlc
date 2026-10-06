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
| [`docs/ui-mock-endpoint-map.md`](docs/ui-mock-endpoint-map.md) | Juniors: page-by-page mock (01–23) → endpoints |
| [`docs/ui-endpoint-map.md`](docs/ui-endpoint-map.md) | Surface-level UI shell → endpoints |

## Local run

See each folder's `README.md`. Ports: buildgenix `18765`, agenthub `18766`.

## Status

- I0a / I0b on `main` with D-C1 heartbeat order fixed (publish → mark → complete).
- I1 controllers + catalog routes on `main` under `buildgenix-api/`.
- Live Docker smoke needs a host that can run containers.

# agent-sdlc (BuildGenix)

Iteration 0 foundation for the BuildGenix AI-SDLC platform.

## Layout

| Folder | Role |
|---|---|
| `buildgenix-api/` | BuildGenix .NET 10 WebAPI + `BuildGenix.Data` (BG tables) + Docker + BG heartbeat ping/pong |
| `agenthub-api/` | AgentHub .NET 10 WebAPI + `AgentHub.Data` (AH tables only) + Docker + AH heartbeat ping/pong |

Project Context (decisions, iteration plan, contracts) lives in the Cursor Agent Store, not only in this git tree.

## Local run

See each folder's `README.md`. Ports: buildgenix `18765`, agenthub `18766`.

## Status

- I0a / I0b on `main` with D-C1 heartbeat order fixed (publish → mark → complete).
- Live Docker smoke needs a host that can run containers.
- Next: I1 API contracts (docs) via [iteration plan](docs not in this repo — use Project Context `docs/iteration-plan.md`).

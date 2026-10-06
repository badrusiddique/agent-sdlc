# AgentHub API

Independent .NET 10 solution for the AgentHub agent-host Web API.

## Solution layout

| Path | Role |
|------|------|
| `src/AgentHub.WebApi` | ASP.NET Core host: Serilog, OpenAPI, heartbeat, HEALTHY REST + BackgroundService |
| `src/AgentHub.Agents.Healthy` | **Functions-ready** host-agnostic HEALTHY handler, dispatcher, prompts |
| `src/AgentHub.Data` | AH tables only (`ActivityLog`, `EffectLedger`, `AgProcessedEvents`) |
| `tests/` | Unit tests (heartbeat + HEALTHY handler/dispatcher) |
| `docker-compose.yml` | Azurite + Service Bus emulator |
| `docker/servicebus-config.json` | Topics: heartbeat + `sdlc-tasks-healthy` / `sdlc-results` / `sdlc-events` |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker (for Azurite + Service Bus emulator). **This VM may not run Docker**; compose + unit tests still ship.

## Run locally (E2E)

### 1. Start infrastructure

```bash
cd agenthub-api
cp .env.example .env   # optional
docker compose up -d
```

- Azurite blob: `http://127.0.0.1:10000`
- Azurite table: `http://127.0.0.1:10002`
- Service Bus emulator AMQP: `localhost:5672`

If Docker fails on your machine: unit tests still prove handler + D-C1 publish→mark order without Docker.

### 2. Run the API

```bash
dotnet run --project src/AgentHub.WebApi
```

- App: [http://127.0.0.1:18766](http://127.0.0.1:18766)
- Swagger: [http://127.0.0.1:18766/swagger](http://127.0.0.1:18766/swagger)

Default `Llm:Provider` = **Fixture** (no Ollama required). For local Ollama: set `"Llm:Provider": "Ollama"`. Isolated machine: `"Gateway"` + `AIGW_SUBSCRIPTION_KEY` (see Context `docs/ai-gateway.md`).

### 3. Trigger HEALTHY agent

```bash
curl -s -X POST http://127.0.0.1:18766/api/connectivity/healthy \
  -H 'Content-Type: application/json' \
  -d '{}'
```

Optional body:

```bash
curl -s -X POST http://127.0.0.1:18766/api/connectivity/healthy \
  -H 'Content-Type: application/json' \
  -d '{"projectId":"demo","workItemId":"wi01","inputVersion":1}'
```

Flow:

1. REST publishes `TaskMessage` to `sdlc-tasks-healthy` with `MessageId={TaskId}:{InputVersion}`
2. `HealthyAgentWorker` consumes subscription `healthy-tasks`
3. Dedup check → deliberate pause → load prompt file → LLM → write blob `{projectId}/{workItemId}/v{n}/healthy-result.json` → publish `AgentResult` to `sdlc-results` → mark `AgProcessedEvents` → complete (D-C1)
4. Structured Serilog + ActivityLog progress lines; worker faults never kill the host

### 4. Heartbeat (unchanged)

```bash
curl -X POST http://127.0.0.1:18766/api/connectivity/heartbeat
```

## Tests

```bash
cd agenthub-api
dotnet test
```

## Configuration highlights

| Section | Purpose |
|---------|---------|
| `Llm:Provider` | `Fixture` \| `Ollama` \| `Gateway` |
| `Workers:Healthy:*` | Topic/sub, MaxDeliveryCount=5, lock renewal, pause |
| `BlobStorage:*` | Azurite / Azure Blob for D-C3 artifacts |
| `TableStorage:*` | AH tables only |

## Design notes (D-I2-*)

- Agent: `agenthub-health-agent`, Stage `HEALTHY`
- `StateContextId` = `{projectId}:{workItemId}`
- Harness filename `healthy-result.json` (outside production catalog)
- Not a production pipeline stage; do not add to buildgenix `Pipeline__StageTopics`

## Out of scope

- I3 System Design agent / production PRD agents
- Cloud App Service deploy (Docker is local only)

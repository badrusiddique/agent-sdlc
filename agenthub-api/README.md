# AgentHub API

Independent .NET 10 solution for the AgentHub agent-host Web API.

## Solution layout

| Path | Role |
|------|------|
| `src/AgentHub.WebApi` | Thin ASP.NET host: Serilog, OpenAPI, heartbeat REST, HEALTHY publish REST, DI composition |
| `src/AgentHub.Hosting` | `AddAgentWorkers` — BackgroundService workers (HEALTHY today; D-AS-1) |
| `src/AgentHub.Infrastructure` | SB / Blob / LLM clients + Data port adapters |
| `src/AgentHub.Core` | Host-agnostic contracts (`TaskMessage`, `AgentResult`) + ports (no SB/ASP.NET) |
| `src/AgentHub.Agents.Healthy` | HEALTHY harness handler/dispatcher/prompt; **Core only** (not Data) |
| `src/AgentHub.Data` | AH tables only (`ActivityLog`, `EffectLedger`, `AgProcessedEvents`) |
| `tests/` | WebApi, Healthy, Core (JSON contract goldens) |
| `docker-compose.yml` | Azurite + Service Bus emulator |
| `docker/servicebus-config.json` | Topics: heartbeat + `sdlc-tasks-healthy` / `sdlc-results` / `sdlc-events` |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker (for Azurite + Service Bus emulator). Local only (D-CL-2).

## Run locally (E2E)

### 1. Start infrastructure

```bash
cd agenthub-api
cp .env.example .env   # optional
# Prefer clean volumes if emulator state is stale:
docker compose down -v
docker compose up -d
```

- Azurite blob: `http://127.0.0.1:10000`
- Azurite table: `http://127.0.0.1:10002`
- Service Bus emulator AMQP: `localhost:5672` (HTTP health `http://127.0.0.1:5300/health`)

Compose note: SQL Edge and the Service Bus emulator share one network namespace (`network_mode: service:sqledge`) so the emulator reaches SQL on `127.0.0.1`. This avoids Docker bridge inter-container blocks seen on some VMs (iptables-legacy FORWARD DROP).

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

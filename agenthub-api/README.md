# AgentHub API

Independent .NET 10 solution for the AgentHub agent-host Web API (Iteration 0b).

## Solution layout

| Path | Role |
|------|------|
| `src/AgentHub.WebApi` | ASP.NET Core Web API: Serilog, OpenAPI/Swagger, health, heartbeat connectivity |
| `src/AgentHub.Data` | AgentHub Azure Table repositories only (`ActivityLog`, `EffectLedger`, `AgProcessedEvents`) |
| `tests/AgentHub.WebApi.Tests` | Unit tests including ping→pong path |
| `docker-compose.yml` | Azurite + Azure Service Bus emulator (+ SQL Edge dependency) |
| `docker/servicebus-config.json` | Emulator topics `ah-heartbeat-ping` / `ah-heartbeat-pong` |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker (for Azurite + Service Bus emulator)

## Run locally

### 1. Start infrastructure

```bash
cd agenthub-api
cp .env.example .env   # optional; defaults work for local
docker compose up -d
```

- Azurite table endpoint: `http://127.0.0.1:10002`
- Service Bus emulator AMQP: `localhost:5672`
- Emulator management/health: `http://localhost:5300`

> Port map overlaps BuildGenix local defaults. Stop the other stack before starting this one, or share a single emulator with both topic sets.

Connection string used by the API (already in `appsettings.Development.json`):

```text
Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true
```

Emulator EULA: set `ACCEPT_EULA=Y` (see Microsoft Service Bus emulator docs).

### 2. Run the API

```bash
dotnet run --project src/AgentHub.WebApi
```

- App: [http://127.0.0.1:18766](http://127.0.0.1:18766)
- Swagger: [http://127.0.0.1:18766/swagger](http://127.0.0.1:18766/swagger)
- Health: [http://127.0.0.1:18766/health](http://127.0.0.1:18766/health)

### 3. Heartbeat connectivity check

```bash
curl -X POST http://127.0.0.1:18766/api/connectivity/heartbeat
```

Flow (self-contained in this API):

1. Endpoint publishes to Service Bus topic `ah-heartbeat-ping`
2. Hosted `HeartbeatPingConsumer` on subscription `ah-api` receives the message
3. Consumer appends `|pong-by=agenthub-api` and publishes to `ah-heartbeat-pong`
4. `AgProcessedEvents` records the message id (**after** pong publish — D-C1)
5. Consumer completes the Service Bus message

## Tests

```bash
dotnet test
```

Ping→pong path is covered without Docker via fakes that assert marker append + pong publish + duplicate skip.

## Configuration

| Section | Purpose |
|---------|---------|
| `TableStorage:ConnectionString` | Azurite / Azure Tables |
| `TableStorage:StoragePrefix` | Optional shared-account table prefix |
| `ServiceBus:*` | Emulator/cloud connection, topic names, pong marker |

## Out of scope (Iteration 0b)

- SDLC agents (PRD, SYSTEM_DESIGN, …) and HELLO sample (later)
- AgentHub.Core / Hosting worker mesh
- Cloud deploy (App Service comes later; Docker is local only)
- BuildGenix tables (never written from this solution)

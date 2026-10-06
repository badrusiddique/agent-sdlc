# BuildGenix API

Independent .NET 10 solution for the BuildGenix orchestration Web API (Iteration 0a).

## Solution layout

| Path | Role |
|------|------|
| `src/BuildGenix.WebApi` | ASP.NET Core Web API: Serilog, OpenAPI/Swagger, health, heartbeat connectivity |
| `src/BuildGenix.Data` | BuildGenix Azure Table repositories only (no AgentHub tables) |
| `tests/BuildGenix.WebApi.Tests` | Unit tests including ping→pong path |
| `docker-compose.yml` | Azurite + Azure Service Bus emulator (+ SQL Edge dependency) |
| `docker/servicebus-config.json` | Emulator topics `bg-heartbeat-ping` / `bg-heartbeat-pong` |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker (for Azurite + Service Bus emulator)

## Run locally

### 1. Start infrastructure

```bash
cd buildgenix-api
cp .env.example .env   # optional; defaults work for local
docker compose up -d
```

- Azurite table endpoint: `http://127.0.0.1:10002`
- Service Bus emulator AMQP: `localhost:5672`
- Emulator management/health: `http://localhost:5300`

Connection string used by the API (already in `appsettings.Development.json`):

```text
Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true
```

Emulator EULA: set `ACCEPT_EULA=Y` (see Microsoft Service Bus emulator docs).

### 2. Run the API

```bash
dotnet run --project src/BuildGenix.WebApi
```

- App: [http://127.0.0.1:18765](http://127.0.0.1:18765)
- Swagger: [http://127.0.0.1:18765/swagger](http://127.0.0.1:18765/swagger)
- Health: [http://127.0.0.1:18765/health](http://127.0.0.1:18765/health)

### 3. Heartbeat connectivity check

```bash
curl -X POST http://127.0.0.1:18765/api/connectivity/heartbeat
```

Flow (self-contained in this API):

1. Endpoint publishes to Service Bus topic `bg-heartbeat-ping`
2. Hosted `HeartbeatPingConsumer` on subscription `bg-api` receives the message
3. Consumer appends `|pong-by=buildgenix-api` and publishes to `bg-heartbeat-pong`
4. `WmProcessedEvents` records the message id (**after** pong publish — D-C1)
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

## Out of scope (Iteration 0a)

- Angular UI
- Agent handlers / AgentHub tables
- Cloud deploy (App Service comes later; Docker is local only)

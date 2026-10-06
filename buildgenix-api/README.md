# BuildGenix API

Independent .NET 10 solution for the BuildGenix orchestration Web API (Iteration 0a).

## Solution layout

| Path | Role |
|------|------|
| `src/BuildGenix.WebApi` | ASP.NET Core Web API: controllers (I1 contract routes), Serilog, OpenAPI/Swagger, health, heartbeat |
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
# Prefer clean volumes if emulator state is stale:
docker compose down -v
docker compose up -d
# wait until curl -s http://127.0.0.1:5300/health shows healthy
```

- Azurite table endpoint: `http://127.0.0.1:10002`
- Service Bus emulator AMQP: `localhost:5672`
- Emulator management/health: `http://127.0.0.1:5300/health`

Compose note: SQL Edge and the Service Bus emulator share one network namespace (`network_mode: service:sqledge`) so the emulator reaches SQL on `127.0.0.1`. This avoids Docker bridge inter-container blocks seen on some VMs (iptables-legacy FORWARD DROP). Do not run AgentHub and BuildGenix compose stacks at the same time (shared host ports).

Connection string used by the API (already in `appsettings.Development.json`):

```text
Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true
```

Emulator EULA: set `ACCEPT_EULA=Y` (see Microsoft Service Bus emulator docs).

Readiness: `GET /health` probes the emulator HTTP health URL when `UseDevelopmentEmulator=true`. `GET /health/live` is process liveness only.

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

## API controllers (contract catalog)

HTTP surface follows Project Context `docs/buildgenix-api-contracts.md` (D-C6). Dummy auth: send `X-User-Id` (D-CL-9).

| Area | Status in this slice |
|------|----------------------|
| Projects CRUD-ish | Wired to Azure Tables |
| Work items create / get / list | Wired; **create does not take `~ACTIVE`** (D-C4b) |
| Intake summary marker `POST …/conversations/{threadId}/summaries` | Commits next `summaryRevision` marker (full chat B1 later) |
| Implement now `POST …/execute` | Requires committed summary revision; lock + D-C5 same-revision no-op |
| pending-actions, home/activity, progress, artifacts, gates, activity, steering, … | Routes present; empty or `501` until B1/B2a/E* persistence |
| Heartbeat | Unchanged (`/api/connectivity/heartbeat`) |

## Tests

```bash
dotnet test
```

Ping→pong path and WI lifecycle (create without lock, Implement lock, D-C5 no-op) are covered with fakes (no Docker required for those tests).

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

---
status: PASS
role: Senior Manual QA
scope: Migration prep — buildgenix catalog + agenthub HEALTHY
date: 2026-10-06
repo: https://github.com/badrusiddique/agent-sdlc
commit: 39673c4e671e2bdedc005a5173c87677099edff9
---

# Migration-prep E2E (local Docker)

Requirements-focused QA (UI mocks / locked decisions), not implementation taste. Layers covered: unit (pre-flight), integration (Docker + API), UAT-style happy path, regression vs B2/minor fixes.

Raw evidence: [`/cursor/stores/self/internal/migration-prep-e2e/`](/cursor/stores/self/internal/migration-prep-e2e/).

## 1. Environment

| Item | Value |
|---|---|
| Commit | `39673c4` (PE Important hardenings) |
| Host | Cloud agent VM; .NET 10.0.401; Docker 29 |
| Sequence | AgentHub HEALTHY first → tear down → BuildGenix catalog (shared ports) |
| AgentHub | `http://127.0.0.1:18766`, Fixture LLM |
| BuildGenix | `http://127.0.0.1:18765` |
| Unit tests | BG **72** passed; AH **23** passed (0 skipped) |

## 2. AgentHub HEALTHY — PASS

| Check | Result |
|---|---|
| Compose `down -v` + `up -d` | SB `"status":"healthy"` |
| `/health` JSON | Healthy `servicebus-emulator` |
| Heartbeat | 202; log pong **before** mark (D-C1) |
| `POST /api/connectivity/healthy` | 202 `task-1791265928:1` |
| Worker | DeliveryCount=1; D-C1 publish then mark |
| Blob D-C3 | `e2e-proj/e2e-wi-1791265928/v1/healthy-result.json` (370 bytes, agent `agenthub-health-agent`) |
| Tables | **only** ActivityLog, EffectLedger, AgProcessedEvents |
| EffectLedger | `llm.healthy` + `artifact.healthy` Status=`Committed` |
| Dedup republish | `already processed; complete no-op`; `/health/live` 200 |

Gateway live path: not run here (needs Consumer key on isolated machine). Config swap documented in migration spoon-feed.

## 3. BuildGenix catalog — PASS

| Check | Result |
|---|---|
| Compose checked-in | SB healthy without ephemeral override |
| `/health` | Healthy JSON emulator check |
| Tables | **10/10** Built BG tables |
| Heartbeat D-C1 | publish then mark |
| Create project / WI | 201; title path OK |
| Execute no summary | **400** `SummaryNotFound` |
| Summary marker | `markerOnly=true`; GET `contentUrl=null` |
| Execute start / noop | `alreadyStarted` false then true |
| Second WI execute | **409** `ActiveConflict` |
| Catalog routes | **46/46** hit; no 500; stubs `501 NotImplemented` where expected |
| Run log unknown | **404** (B2 minor retained) |

## 4. UI mock coverage vs requirements

| Band | Result |
|---|---|
| Mocks 01–15, 21–23 REST | Mapped; juniors doc updated |
| Mocks 16–20 | Design/repo only (not REST) |
| OPEN KPI overview | Still OPEN (no dedicated route); FE aggregate OK |
| SignalR | Not in this API slice (expected) |

Junior shareable map: `docs/junior-ui-endpoint-wiring.md` (repo + Context).

## 5. Findings this pass

| ID | Severity | Notes |
|---|---|---|
| — | Critical | None |
| — | Important | None new after PE hardenings |
| F-MIG-01 | Note | AI Gateway live HEALTHY not exercised on this VM (no Consumer key). Isolated-machine verification required (spoon-feed V8). |
| F-MIG-02 | Note | Service Bus App Service still connection-string based (documented honestly in Azure baby-step guide). |

## 6. Verdict

**PASS** for migration-prep local E2E on `39673c4`. Safe to use spoon-feed integration docs on the new machine with the verification tables in:

- `docs/migration/buildgenix-api-new-machine-integration.md`
- `docs/migration/agenthub-api-new-machine-integration.md`
- `docs/migration/azure-appservice-deploy-baby-steps.md`

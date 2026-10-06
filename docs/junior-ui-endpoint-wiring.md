# Junior guide: UI mock → buildgenix-api endpoint

**Audience:** junior engineers wiring Angular screens to `buildgenix-api`.  
**Source of truth for routes:** `buildgenix-api/src/BuildGenix.WebApi/Contracts/CatalogRoutes.cs` (exactly **46** catalog routes).  
**DTO names:** `buildgenix-api/src/BuildGenix.WebApi/Contracts/ApiDtos.cs`.  
**Problem codes:** `BuildGenix.WebApi.Infrastructure.ApiProblems` (`code` extension on problem+json).  
**Verified:** Docker E2E on commit `39673c4` (2026-10-06). See Context `docs/qa/migration-prep-e2e-2026-10-06.md`.

### Rules (do not guess)

1. Send `X-User-Id: <user>` on every REST call (Phase 0). Missing → API uses `"anonymous"` (not 401).
2. HighLevelSummary / VerboseLogs are **SignalR**, not REST columns.
3. Do **not** call `POST /api/internal/*` from the browser.
4. Create work item ≠ Implement. Only `POST …/execute` takes `~ACTIVE`.
5. If an endpoint returns `501` with `code=NotImplemented`, leave a UI stub / disabled action. Do not invent another REST path.
6. Prefer this page map over inventing parallel endpoints. Catalog has **zero duplicate routes**.

### Wire status legend

| Status | Meaning |
|---|---|
| **wired** | Real persistence / real business outcome |
| **empty** | `200` with empty arrays / zeros (parent exists) |
| **stub 501** | `code=NotImplemented` until a later slice |
| **404 shell** | Parent OK; child resource not found yet |
| **hybrid** | Some fields wired, some 501 |

### Lifecycle cheat sheet

| UI action | Endpoint | Request | Response | Notes |
|---|---|---|---|---|
| Create work item | `POST /api/projects/{projectId}/work-items` | `CreateWorkItemRequest` (+ optional `Idempotency-Key`) | `CreateWorkItemResponse` | **No** `~ACTIVE`. No pipeline. |
| Save draft | *(none)* | — | — | Leave page; messages stay when B1 lands. |
| Commit intake summary marker | `POST …/conversations/{threadId}/summaries` | `ProduceIntakeSummaryRequest?` | `IntakeSummaryRefDto` (`markerOnly=true`) | Unblocks Implement. Full chat body is B1. |
| Implement now | `POST /api/work-items/{workItemId}/execute` | `ExecuteWorkItemRequest { summaryRevision }` | `ExecuteWorkItemResponse` | Only action that takes `~ACTIVE`. |
| Same revision again | same | same revision | `alreadyStarted: true` | D-C5 success no-op. |
| Other WI while locked | same on other WI | — | **409** `code=ActiveConflict` | Includes `activeWorkItemId`. |
| No summary for revision | same | — | **400** `code=SummaryNotFound` | |

### Problem codes you must handle in UI

| HTTP | `code` | When |
|---|---|---|
| 400 | `ValidationFailed` | Missing/invalid fields |
| 400 | `SummaryNotFound` | Execute without committed summary revision |
| 404 | `NotFound` | Missing project/WI/artifact/etc. |
| 409 | `ActiveConflict` | Another WI holds project `~ACTIVE` |
| 409 | `Duplicate` | Idempotent create collision (when applicable) |
| 501 | `NotImplemented` | Route exists; persistence not wired |
| 503 | `ServiceUnavailable` | Heartbeat / dependency publish failed |

---

## Page-by-page (mocks 01–23)

### 01 — Home

| UI | Call | Req / Res | Status |
|---|---|---|---|
| Waiting badge / list | `GET /api/pending-actions?sort=age\|project` | — / `PendingActionListResponse` | empty |
| Projects list | `GET /api/projects` | — / `ProjectListResponse` | wired |
| Open project | `GET /api/projects/{projectId}` | — / `ProjectDetailDto` | wired |
| Recent activity | `GET /api/home/activity` | — / `HomeActivityResponse` | empty |
| + New project | `POST /api/projects` | `CreateProjectRequest` / `CreateProjectResponse` | wired |
| + New WI (global) | pick project → create WI | `CreateWorkItemRequest` / `CreateWorkItemResponse` | wired |

**OPEN (no endpoint):** single KPI overview strip. Aggregate from pending-actions + projects in FE, or wait for a future overview route.

### 02 — Project work items

| UI | Call | Status |
|---|---|---|
| Header | `GET /api/projects/{projectId}` | wired |
| Waiting stats | `GET /api/pending-actions?projectId=` | empty |
| WI table | `GET /api/projects/{projectId}/work-items?statusFilter=` | wired |
| + New WI | `POST …/work-items` | wired |
| Artifacts tab | `GET /api/projects/{projectId}/artifacts` | empty |
| Human actions tab | `GET /api/projects/{projectId}/human-actions` | empty |
| Documents tab | `GET` / `POST /api/projects/{projectId}/documents` | empty / stub 501 |
| Settings | `GET` + `PATCH /api/projects/{projectId}` | hybrid (`settings` → 501) |

### 03–08 — Progress lanes (Intake / PRD / Build / Test / Integrate / Deploy)

| UI | Call | Status |
|---|---|---|
| WI shell | `GET /api/work-items/{id}` | wired |
| Lifecycle bar | `GET …/progress` | empty stages |
| Tokens | `GET …/telemetry` | empty zeros |
| Stage detail | `GET …/progress?stageCode=` | empty |
| Gate decide | `POST …/gates/{gateId}/decisions` | stub 501 |
| Human answer | `POST …/answer` | stub 501 |
| Live commentary | **SignalR** | not REST |

**Do not invent** `stageCode=INTAKE` or Integrate. Intake is pre-Implement; stage codes are `PRD`, `SYSTEM_DESIGN`, `IMPLEMENTATION`, `CODE_REVIEW`, `TESTING`, `DEPLOY_DEV`, `DONE`.

### 09 — Findings / code review

| UI | Call | Status |
|---|---|---|
| Findings update | `PATCH …/findings/{findingId}` | stub 501 / validation |
| Assumptions | `GET …/assumptions` | empty |

### 10 — Activity

| UI | Call | Status |
|---|---|---|
| List | `GET …/activity` | empty |
| Event | `GET …/activity/{eventId}` | 404 shell |
| Run log | `GET …/runs/{runId}/log` | 404 shell |
| Export | `GET …/activity/export` | stub 501 |

### 11 — Approvals pending

| UI | Call | Status |
|---|---|---|
| Cross-project | `GET /api/pending-actions` | empty |
| Gates / approvals | `GET …/gates`, `GET …/approvals` | empty |

### 12–13 — Steering intake / Implement

| UI | Call | Status |
|---|---|---|
| Threads / messages | conversations GETs / POST messages | empty / stub 501 |
| Summary marker | `POST …/summaries` | wired (`markerOnly`) |
| Get summary | `GET …/summaries/{revision}` | wired (`contentUrl=null`, `markerOnly=true`) |
| Implement | `POST …/execute` | wired |

### 14 — Steering proposals / cost

| UI | Call | Status |
|---|---|---|
| Proposals | `GET …/proposals` | empty |
| Decide / bulk | POST proposal decisions | stub 501 |
| Interventions | `GET …/interventions` | empty |
| Cost | `GET …/steering/cost` | empty zeros |

### 15 — Map / demo flows overview

Same as Home + Progress shell. No extra catalog routes.

### 16–20 — Design / repo screenshots

**Not REST consumers.** Use for folder naming / leadership only.

### 21–23 — Sign-off / library / misc

| UI | Call | Status |
|---|---|---|
| Sign-off | `POST …/sign-offs` | stub 501 |
| Library docs | project documents GET/POST | empty / stub 501 |

---

## Redundancy (source of truth)

| Need | Prefer | Also exists | Rule |
|---|---|---|---|
| Waiting on you | `GET /api/pending-actions` | progress `waitingOnYou`, gates list | Badge/home = pending-actions |
| Tokens | `GET …/telemetry` | WI detail brief, activity totals, steering/cost | Progress chrome = telemetry |
| Assumptions | `GET …/assumptions` | intake context, gates | Assumptions tab = assumptions |

---

## Endpoints with no browser UI

- `POST /api/internal/sdlc-results`
- `POST /api/internal/sdlc-events`

AgentHub / orchestration only. Never wire these into Angular.

---

## Minimal Angular call pattern

```ts
// every catalog call
headers: {
  'Content-Type': 'application/json',
  'X-User-Id': currentUserId // Phase 0 dummy
}
```

Handle `problem+json` by reading `error.error.code` (ASP.NET ProblemDetails + extensions).

Companion surface map: [`ui-endpoint-map.md`](./ui-endpoint-map.md).  
Page sketch map: [`ui-mock-endpoint-map.md`](./ui-mock-endpoint-map.md).

---
status: DRAFT
updated: 2026-10-06
scope: UI surface → buildgenix-api endpoints and data (documentation only)
rule: NOT GO. No app code. Routes + DTO usage only (D-C6). Do not invent business rules; OPEN where mocks/schema disagree or are silent. Conflicts with decision-log called out explicitly.
sources:
  - docs/buildgenix-api-contracts.md
  - docs/project-context.md
  - docs/db-schema.md
  - docs/decision-log.md
  - internal/answers-2026-10-06-criticals.md
  - internal/confirms-2026-10-06-final.md
  - media/mockups/
companion: docs/buildgenix-api-contracts.md
---

# UI ↔ endpoint map

**Status: DRAFT. Documentation only. NOT GO.**

One section per primary UI surface. Each lists **endpoints** and **data** the page needs. Live HighLevelSummary / VerboseLogs are **SignalR** (D-6), not REST columns. Auth Phase 0: dummy `X-User-Id` on every call (D-CL-9).

Canonical route + DTO definitions: [`buildgenix-api-contracts.md`](./buildgenix-api-contracts.md).

UI lifecycle labels (D-CL-1): Intake → PRD → System Design → Build/Implementation → Test → Deploy. Stage codes in API data: `PRD`, `SYSTEM_DESIGN`, `IMPLEMENTATION`, `CODE_REVIEW`, `TESTING`, `DEPLOY_DEV` (`DONE` after deploy). Code review is an agent/gate, not a separate top-level UI bar in the clarified list.

---

## Locked WI lifecycle (D-C4 / D-C4b / D-C5 / D-C7)

| UI action | Endpoint | Notes |
|---|---|---|
| Projects **Create work item** | `POST /api/projects/{projectId}/work-items` | Creates WI **record**; navigate to intake chat. **No** `~ACTIVE`. **No** pipeline start |
| Chat / approve intake summary | conversation + summary routes (§7) | Finalize `summaryRevision` |
| **Save draft** | none required | Leave without implementing (D-C7). WI stays draft; messages persist |
| **Implement now** | `POST /api/work-items/{workItemId}/execute` | Starts pipeline. **Only** action that takes `~ACTIVE`. Same `summaryRevision` → success no-op (D-C5). Second in-flight Implement → **409** |

**CONFLICT:** [`db-schema.md`](./db-schema.md) still documents `~ACTIVE` on create. Product authority is **D-C4b**. See contracts §0.1.

---

## Global chrome (all authenticated pages)

| UI element | Endpoints | Data |
|---|---|---|
| Brand / env badge | config / FE env | `Non-prod` etc. (not API) |
| “N waiting on you” badge | `GET /api/pending-actions` (`totalCount`) | Cross-project pending count |
| User display | Phase 0 dummy | `X-User-Id` / static label |
| Home link | n/a | navigation |

**SignalR:** optional global invalidation when pending-actions change (OPEN); otherwise badge refreshes on navigation/poll.

---

## 1. Home

**Mocks:** `01-home-cross-project-waiting.jpg`, `15-map-demo-flows-overview.jpg`  
**Role:** Only cross-project view (waiting-on-you, project list, glance recent activity).

| UI block | Endpoints | Data needed |
|---|---|---|
| KPI strip (Waiting / In flight / Blocked / Projects / Work items / Oldest decision) | `GET /api/pending-actions`; `GET /api/projects` | Counts. **OPEN:** whether a single overview endpoint is required, or FE derives from these two. Schema does not yet write Project denormalized counters |
| WAITING ON YOU list | `GET /api/pending-actions?sort=age\|project` | `PendingActionDto[]` (title, WI display, project name, stage/summary, waiting age) |
| See all waiting | same | `totalCount` + full list / pagination |
| PROJECTS list | `GET /api/projects` | Name, optional rollup string fields |
| Project name → Project | `GET /api/projects/{projectId}` (on navigate) | |
| + New project | `POST /api/projects` | **OPEN:** button greyed in mocks; self-serve vs request-based |
| + New work item (global) | Navigate to project pick then Create WI | `POST /api/projects/{projectId}/work-items` (record only; then intake) |
| RECENT ACTIVITY glance | `GET /api/home/activity` | Actor, description, WI link, relative time |

**Not on Home:** full Activity audit (per-WI tab only).

---

## 2. Project

**Mocks:** `02-project-work-items-list.jpg`  
**Tabs:** Work items, Artifacts, Human actions, Application library, Settings.

### 2.1 Shell / header

| UI block | Endpoints | Data |
|---|---|---|
| Breadcrumb, title, PRJ display | `GET /api/projects/{projectId}` | `name`, `projectId` |
| Stats (WI count, waiting, blocked) | `GET /api/projects/{projectId}`; `GET /api/pending-actions?projectId=` | Counts — OPEN if project detail embeds rollups |
| + New work item | `POST /api/projects/{projectId}/work-items` then intake UI | New WI **record** + intake `conversationThreadId`. Does **not** Implement |
| Application context button | `GET /api/projects/{projectId}/documents` (E2) or navigate to library | **later E2** |

### 2.2 Work items tab

| UI block | Endpoints | Data |
|---|---|---|
| Filters Active/Waiting/Blocked/Complete/All | `GET /api/projects/{projectId}/work-items?statusFilter=` | Filtered list. Multiple **Draft** rows OK while none in flight (D-C4b) |
| Row: title, WI-#, features, lifecycle mini-bar, state, waiting, Open | same | `WorkItemListItemDto` including `lifecycleSummary` when StageStatuses exist |
| Open | `GET /api/work-items/{workItemId}` | Navigate to Progress or intake (status-driven) |

**Policy:** Only one **active in-flight** WI per project (D-7 / D-CL-3 / D-C4b). Mocks with many simultaneous “active” rows are illustrative. Concurrent drafts are allowed.

### 2.3 Artifacts tab (project)

| UI block | Endpoints | Data |
|---|---|---|
| Project artifact index | `GET /api/projects/{projectId}/artifacts` | Planned B3 `ProjectArtifactIndex` |

### 2.4 Human actions tab

| UI block | Endpoints | Data |
|---|---|---|
| Human actions list | `GET /api/projects/{projectId}/human-actions` | Planned B3 |

### 2.5 Application library

| UI block | Endpoints | Data |
|---|---|---|
| Document list / upload | `GET` + `POST /api/projects/{projectId}/documents` | **later E2** |

### 2.6 Settings

| UI block | Endpoints | Data |
|---|---|---|
| Settings form | **Read:** `GET /api/projects/{projectId}` (no dedicated settings GET). **Write:** `PATCH /api/projects/{projectId}` | Settings payload shape **OPEN** (`settings?` opaque until B3 SettingsJson) |

---

## 3. Work item — Progress

**Mocks:** `03`–`08` progress lanes  
**Role:** Lifecycle bar, stage detail (single lane or feature fan-out), waiting-on-you, still-to-come, pause/stop, metrics.

| UI block | Endpoints | Data |
|---|---|---|
| WI shell (title, tabs counts, tokens/runs, loop badge, pending approvals CTA) | `GET /api/work-items/{workItemId}`; `GET /api/work-items/{workItemId}/telemetry` | Detail + tabCounts + telemetryBrief (omit planned fields until B2a) |
| Lifecycle bar | `GET /api/work-items/{workItemId}/progress` | `stages[]` with UI labels mapped from **agent** stage codes only |
| Intake milestone (mock 03, Draft / pre-Implement) | progress GET + WI/conversation state | **OPEN:** API does not return `stageCode=INTAKE`. Recommended interim: FE renders Intake from WI status + conversation/summary; chat/Implement remain on Steering §7 |
| Stage lane detail (Build/Test fan-out in mocks) | same (`stageCode` query) | Sub-steps / features — **OPEN** Phase 0 without Features table |
| Click milestone → artifact | `GET /api/work-items/{workItemId}/artifacts/{artifactId}` (+ content) | Deep-link expands Artifacts |
| WAITING ON YOU — **gate** | progress embed and/or `GET /api/pending-actions?projectId=` | Navigate to artifact + `POST …/gates/{gateId}/decisions` |
| WAITING ON YOU — **HumanAction** | same pending list when `kind=HumanAction` | Composer/submit → `POST /api/work-items/{workItemId}/answer` with `AnswerHumanActionRequest` (**not** a gate decision) |
| STILL TO COME | `progress.stillToCome` | **OPEN** computation rules |
| Pause / Stop | `POST …/pause`, `POST …/stop` | **OPEN** Phase 0. Pause must not silently release `~ACTIVE`; Stop unlock only if mapped to terminal `Failed` |
| Ambient HighLevelSummary | **SignalR** `HighLevelSummary` | Not REST columns |
| Previous/Next WI | list endpoint + client navigation | Project WI list |

**Intake before pipeline:** Create WI + chat + **Implement now** live on Steering intake (§7). Progress Intake lane is display/status only until pipeline starts.

---

## 4. Work item — Artifacts

**Mocks:** `09-artifacts-code-review-findings.jpg`, `14-completion-summary-deployed.jpg`  
**Role:** Collapsible groups by stage/feature/type; approval travels with artifact.

| UI block | Endpoints | Data |
|---|---|---|
| Sidebar PRODUCED tree | `GET /api/work-items/{workItemId}/artifacts?groupBy=stage\|feature\|type` | List items + status, awaitingApproval |
| Artifact detail pane | `GET /api/work-items/{workItemId}/artifacts/{artifactId}` | Metadata, summary, linked gate |
| Body / findings / markdown | `GET …/artifacts/{artifactId}/content` | Blob content (paths per D-C3 when written) |
| Download / View diff | same content (or specialized Blob path inside artifact) | |
| Approve / Reject with feedback | `POST /api/work-items/{workItemId}/gates/{gateId}/decisions` | Must send `artifactId` + `artifactRevision` shown |
| Finding dispositions | `PATCH /api/work-items/{workItemId}/findings/{findingId}` | Planned B3 |
| Completion summary artifact | same artifact GET/content | Deploy-produced `completion-summary.json` (D-C3); outlives WI as artifact |
| Signed off footer | `POST /api/work-items/{workItemId}/sign-offs` | **later E3**; ownership OPEN |
| Raise a change | Steering / new WI flows — **OPEN** Phase 0 | Not a separate invented endpoint |

---

## 5. Work item — Activity

**Mocks:** `10-activity-audit-log.jpg`  
**Role:** Filterable audit; expandable agent logs; token costs.

| UI block | Endpoints | Data |
|---|---|---|
| Filters Everything / Human / Agent runs / Failures / Loops | `GET /api/work-items/{workItemId}/activity?filter=` | `ActivityItemDto[]` |
| Header token/run blurb | activity response aggregates and/or `GET …/telemetry` | |
| Row select → detail | `GET /api/work-items/{workItemId}/activity/{eventId}` | Log preview or artifact snapshot refs |
| Full verbose log for a run | `GET /api/work-items/{workItemId}/runs/{runId}/log` | Recoverable VerboseLogs (D-6) |
| Live verbose while running | **SignalR** `VerboseLog` | |
| Export | `GET /api/work-items/{workItemId}/activity/export` | Format **OPEN** |

**Source rule:** FE reads buildgenix projections only; agenthub `ActivityLog` is not a browser API (D-CL-7).

---

## 6. Work item — Approvals

**Mocks:** `11-approvals-pending-queue.jpg`  
**Role:** Pending queue with previous/next/skip; decided list; decision opens artifact.

| UI block | Endpoints | Data |
|---|---|---|
| Pending list | `GET /api/work-items/{workItemId}/gates` (filter `Pending` client-side) and/or pending-actions for WI | Gate summaries, waiting age |
| Decided list | `GET /api/work-items/{workItemId}/approvals` | Immutable decisions |
| Decision panel summary | gate DTO fields | Title, waiting, short summary — **not** sufficient to decide |
| Read it, then approve | `GET …/artifacts/{id}` + content, then `POST …/gates/{gateId}/decisions` | Artifact revision must match gate shown |
| Reject with feedback | same POST with `Rejected` + `feedback` | Feedback required? **OPEN** |
| Skip for now | **Client-only** reorder | No API unless product persists queue — **OPEN** |
| HumanActionRequired (not a gate) | `POST /api/work-items/{workItemId}/answer` | `AnswerHumanActionRequest.answer`; distinct from gate decisions |
| Assumption confirmation items | gates of assumption kind + `GET …/assumptions` | |
| Expected later | FE from pipeline definition / progress still-to-come | Not separate API |
| Next WI | project WI list + pending-actions | |

---

## 7. Work item — Steering (intake mode)

**Mocks:** `12-intake-conversation-evidence.jpg`, `13-intake-draft-context-panel.jpg`  
**Role:** Draft = intake (status-driven). Conversational intake in buildgenix-api (D-30). Flow locked by D-C4 / D-C4b / D-C5 / D-C7.

| UI block | Endpoints | Data |
|---|---|---|
| Create / open draft WI | `POST /api/projects/{projectId}/work-items` | `workItemId`, intake `threadId` (`I01`). Record only; **no** lock; **no** pipeline. **OPEN:** DRAFT-07 handle vs server display (UI tension #2) |
| Transcript | `GET /api/work-items/{workItemId}/conversations/{threadId}` | Messages + attachments |
| Send message | `POST …/conversations/{threadId}/messages` | User content; assistant reply |
| WORKING / reading commentary | **SignalR** HighLevelSummary + VerboseLog | Also eventually durable in transcript |
| Attach evidence | `POST …/conversations/{threadId}/attachments` | File → Blob |
| Context so far sidebar | `GET …/conversations/{threadId}/context` | Structured fields, features, assumptions, attachments |
| Produce summary (when complete) | `POST …/conversations/{threadId}/summaries` | Summary revision + artifact (`intake-summary.json` / D-C3) |
| Review summary | `GET …/conversations/{threadId}/summaries/{revision}` | Content before Implement |
| **Implement now** (misnamed Create on older mocks) | `POST /api/work-items/{workItemId}/execute` | Binds `summaryRevision`. Takes `~ACTIVE`. Starts pipeline. Same revision → no-op (D-C5) |
| **Save draft** | none | Leave chat; do not call execute (D-C7) |
| Assumptions travel note | context + assumptions list | Shown again at PRD gate (product rule from mock; enforcement OPEN) |

**409 `ActiveConflict`:** only when **Implement now** while another WI holds project `~ACTIVE`. Create WI / Save draft / chat do **not** 409 for active lock.

---

## 8. Work item — Steering (in-flight)

**Mocks:** `21`–`23` steering  
**Role:** In flight = steering. Schema: Interventions/Proposals **later E1**. Conversation resource reused.

| UI block | Endpoints | Data |
|---|---|---|
| In flight / Pause / Stop | WI detail + `POST …/pause` \| `stop` | **OPEN** Phase 0 |
| Feature progress strip | `GET …/progress` | Feature/stage running states |
| Conversation tab | `GET`/`POST …/conversations/{threadId}` (steering thread) | Same conversation resource, different thread |
| Chips | FE static copy into composer | No API |
| Ambient vs responsive commentary | **SignalR** | Dual-layer (D-6); not REST |
| Proposed changes rail | `GET /api/work-items/{workItemId}/proposals` | Cards, cost, impact — **E1** |
| Approve / Discard / Approve both | `POST …/proposals/{id}/decisions`; `POST …/proposals/bulk-decisions` | **E1** |
| Discuss further | `POST …/messages` | Stays in conversation |
| History & cost tab | `GET …/interventions`; `GET …/steering/cost`; `GET …/conversations` | Table + rollup + earlier threads — **E1** |
| Watch without typing | SignalR + progress GET | |

---

## 9. Completion summary

**Mocks:** `14-completion-summary-deployed.jpg`  
**Role:** Deploy-produced artifact; endpoints, local run, usage, change links, sign-off. Lives under Artifacts.

| UI block | Endpoints | Data |
|---|---|---|
| Select completion summary in tree | `GET …/artifacts` find by key/stage Deploy | Artifact list item (`completion-summary.json` per D-C3) |
| Sections (delivered, where it runs, local, usage, what changed) | `GET …/artifacts/{id}` + `…/content` | Assembled Markdown/JSON from Deploy agent — section schema owned by producer, not invented here |
| Download / Share | content URL | Share mechanism **OPEN** |
| Signed off | `POST …/sign-offs` | **E3** / UI tension #4 ownership |
| Raise a change | OPEN (steering or new WI) | |

Phase 0 Deploy may be CLI placeholder returning a URL (brain dump) — completion summary richness may be minimal until later.

---

## 10. Cross-cutting realtime

| Concern | Mechanism | REST recovery |
|---|---|---|
| HighLevelSummary | SignalR hub join per `workItemId` | Progress, activity, ClientEvents projections |
| VerboseLogs | SignalR while run live | `GET …/runs/{runId}/log` |
| Pending badge | optional SignalR or refresh | `GET /api/pending-actions` |
| Gate raised / WI completed | prefer state changed via summary or refresh | `GET` WI + gates |

---

## 11. What the UI must never call

| Anti-pattern | Reason |
|---|---|
| Direct agenthub table/HTTP for ActivityLog | Ownership D-CL-7; buildgenix consumes events and exposes projections |
| Approve from Approvals summary alone | Mock: open artifact; gate decision quotes revision |
| Call **Implement** while another WI holds `~ACTIVE` without handling 409 | D-7 / D-CL-3 / D-C4b |
| Treat Create WI or Save draft as pipeline start | D-C4 / D-C7 |
| Treat Create WI as acquiring the active lock | D-C4b |
| Treating FlightDeck mock labels (Integrate, etc.) as API stage codes | D-CL-1 placeholders |

---

## 12. OPEN questions (UI ↔ API)

Resolved items removed.

**Resolved:**

| Topic | Resolution |
|---|---|
| Draft vs `~ACTIVE` | **D-C4b** — multiple drafts OK; lock only on Implement |
| Save draft button | **D-C7** — leave without pipeline; no dedicated API required |
| Implement same revision | **D-C5** — success no-op |
| Dummy auth / SignalR dual channel | **D-CL-9** / **D-6** |

**Still OPEN:**

1. **Draft handle:** Is `DRAFT-07` a client-only label until `POST` work-items, or a server-allocated display form before ULID?
2. **Home KPIs:** Dedicated overview DTO vs FE aggregation from `pending-actions` + `projects` (same OPEN in both docs)?
3. **New project** enabled in Phase 0 API while UI unsettled?
4. **Feature fan-out** on Progress/Approvals without Features entity in Phase 0?
5. **Test acceptance granularity** (reject-all vs per-feature) — Approvals mock OPEN; do not invent endpoints for per-feature gates until decided.
6. **Skip** persistence for approval queue?
7. **Steering in Phase 0:** Conversation-only watch mode vs full proposals E1?
8. **Completion summary** writer and sign-off process (UI tension #4)?
9. **Pause/Stop** allowed states, SB markers, and `~ACTIVE` retention?
10. **Application library / Settings** field lists still undefined.
11. Exact **Draft** status string for list filters once B1 lands? (Until then create may return `Queued`.)
12. **Same WI + different summaryRevision** while pipeline non-terminal (see contracts Execute matrix).
13. Progress **Intake** milestone: FE-only vs future projection (recommended FE-only interim).

---

## 13. Quick matrix (surface → primary endpoints)

| Surface | Primary endpoints |
|---|---|
| Home | `pending-actions`, `projects`, `home/activity` |
| Project work items | `projects/{id}`, `projects/{id}/work-items`, `POST …/work-items` (create record), `pending-actions?projectId=` |
| Project artifacts / human actions / library / settings | `projects/{id}/artifacts`, `…/human-actions`, `…/documents`, `PATCH projects/{id}` |
| Progress | `work-items/{id}`, `…/progress`, `…/telemetry`, SignalR, gates decisions, **`…/answer`** for HumanAction |
| Artifacts | `…/artifacts`, `…/artifacts/{id}`, `…/content`, gates decisions, findings PATCH |
| Activity | `…/activity`, `…/activity/{eventId}`, `…/runs/{runId}/log`, export, SignalR |
| Approvals | `…/gates`, `…/approvals`, `…/gates/{gateId}/decisions`, artifact GET, **`…/answer`** when kind=HumanAction |
| Steering intake | conversations messages/attachments/context/summaries; **Implement** = `POST …/execute`; **Save draft** = leave (no route) |
| Steering in-flight | conversations + proposals/interventions/cost (E1), pause/stop, SignalR |
| Completion | artifact content (+ sign-offs E3) |
| Global chrome | `pending-actions` + dummy auth header |

**Coverage rule (D-C6):** Every surface above has routes + DTOs named in the companion catalog. Later-E surfaces are listed for completeness, not Phase 0 commit.

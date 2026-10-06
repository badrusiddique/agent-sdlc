# UI mock → endpoints (page by page)

**Audience:** junior engineers wiring Angular screens to `buildgenix-api`.  
**Scope:** one section per file in `media/mockups/` (01–23).  
**Auth (Phase 0):** send `X-User-Id: <user>` on every REST call.  
**Realtime:** HighLevelSummary and VerboseLogs are **SignalR**, not REST columns.

Canonical contracts: `docs/buildgenix-api-contracts.md` (agent store) / controllers under `buildgenix-api`.  
Surface-level map (grouped by UI shell): `docs/ui-endpoint-map.md` in the Project agent store.  
**Full junior wiring (DTO + wire status + problem codes):** [`junior-ui-endpoint-wiring.md`](./junior-ui-endpoint-wiring.md).

Intake summary GET returns `markerOnly=true` and `contentUrl=null` until B1 writes the blob (do not treat BlobPath on the POST ref as a downloadable URL yet).

### Work-item lifecycle (remember this)

| UI action | Endpoint | Notes |
|---|---|---|
| Create work item | `POST /api/projects/{projectId}/work-items` | Creates record only. No `~ACTIVE`. No pipeline. |
| Save draft | *(none)* | Leave the page. Messages stay. |
| Implement now | `POST /api/work-items/{workItemId}/execute` | Only action that takes `~ACTIVE`. Same `summaryRevision` → success no-op. |

---

## 01 — Home (cross-project waiting)

**File:** `01-home-cross-project-waiting.jpg`  
**Route idea:** `/` or `/home`

| What you see | Call | Why |
|---|---|---|
| “N waiting on you” badge | `GET /api/pending-actions` | `totalCount` |
| WAITING ON YOU list | `GET /api/pending-actions?sort=age\|project` | Pending rows (title, WI, project, age) |
| PROJECTS list | `GET /api/projects` | Name + optional rollups |
| Open a project | `GET /api/projects/{projectId}` | After navigate |
| RECENT ACTIVITY | `GET /api/home/activity` | Glance feed |
| + New project | `POST /api/projects` | May be disabled in UI; API exists |
| + New work item (global) | pick project → `POST /api/projects/{projectId}/work-items` | Then go to intake chat |

---

## 02 — Project work items list

**File:** `02-project-work-items-list.jpg`  
**Route idea:** `/projects/{projectId}` (Work items tab)

| What you see | Call | Why |
|---|---|---|
| Project title / breadcrumb | `GET /api/projects/{projectId}` | Name, id |
| Waiting / blocked stats | same + `GET /api/pending-actions?projectId=` | Counts |
| WI table + filters | `GET /api/projects/{projectId}/work-items?statusFilter=` | Active / Waiting / Blocked / Complete / All |
| Open row | `GET /api/work-items/{workItemId}` | Progress or intake by status |
| + New work item | `POST /api/projects/{projectId}/work-items` | Record + intake thread; **not** Implement |

Other project tabs (same shell):

| Tab | Calls |
|---|---|
| Artifacts | `GET /api/projects/{projectId}/artifacts` |
| Human actions | `GET /api/projects/{projectId}/human-actions` |
| Application library | `GET` / `POST /api/projects/{projectId}/documents` (later) |
| Settings | `GET /api/projects/{projectId}` + `PATCH /api/projects/{projectId}` |

**Policy:** many **Draft** rows OK; only one **in-flight** WI per project.

---

## 03 — Progress: Intake lane

**File:** `03-work-item-progress-intake-lane.jpg`  
**Route idea:** `/work-items/{workItemId}/progress`

| What you see | Call | Why |
|---|---|---|
| WI header (title, tabs, tokens) | `GET /api/work-items/{workItemId}` + `GET …/telemetry` | Shell |
| Lifecycle bar | `GET /api/work-items/{workItemId}/progress` | Stages |
| Intake milestone | progress + WI/conversation status | No `stageCode=INTAKE` in API; FE derives from draft/pre-Implement |
| Chat / Implement | see mocks **12–13** (Steering intake) | Create + execute live there |

---

## 04 — Progress: PRD lane

**File:** `04-work-item-progress-prd-lane.jpg`

| What you see | Call | Why |
|---|---|---|
| Same shell + lifecycle | `GET …/work-items/{id}`, `GET …/progress`, `GET …/telemetry` | |
| PRD stage detail | `GET …/progress?stageCode=PRD` (or filter client-side) | Lane content |
| Waiting gate → decide | open artifact then `POST …/gates/{gateId}/decisions` | Always quote `artifactId` + `artifactRevision` |
| HumanAction waiting | `POST …/answer` with `AnswerHumanActionRequest` | **Not** a gate decision |
| Live commentary | **SignalR** `HighLevelSummary` | |

---

## 05 — Progress: Build fan-out

**File:** `05-work-item-progress-build-fanout.jpg`

| What you see | Call | Why |
|---|---|---|
| Shell + bar | same as 03/04 | |
| Feature fan-out under Build/Implementation | `GET …/progress` (`stageCode` IMPLEMENTATION) | Feature rows OPEN in Phase 0 without Features table |
| Click artifact | `GET …/artifacts/{artifactId}` (+ `/content`) | Deep-link to Artifacts tab |
| Pause / Stop | `POST …/pause`, `POST …/stop` | Phase 0 semantics OPEN |

---

## 06 — Progress: Test fan-out

**File:** `06-work-item-progress-test-fanout.jpg`

Same pattern as **05**, stage `TESTING`:

- `GET …/progress`
- Gates / HumanAction via pending list → `…/gates/…/decisions` or `…/answer`
- SignalR for live summary

---

## 07 — Progress: Integrate lane

**File:** `07-work-item-progress-integrate-lane.jpg`

Mock label “Integrate” is a **FlightDeck placeholder**. Map UI to agent stages from the clarified lifecycle (no separate Integrate stage code). Same Progress endpoints as 03–06 (`GET …/progress`, telemetry, gates).

---

## 08 — Progress: Deploy lane

**File:** `08-work-item-progress-deploy-lane.jpg`

| What you see | Call | Why |
|---|---|---|
| Deploy stage | `GET …/progress` (`DEPLOY_DEV` / done) | |
| Completion summary teaser | `GET …/artifacts` → find completion summary | Full page = mock **14** |
| Waiting / approve | gates + artifact content + `POST …/gates/{gateId}/decisions` | |

---

## 09 — Artifacts: code review findings

**File:** `09-artifacts-code-review-findings.jpg`  
**Route idea:** `/work-items/{workItemId}/artifacts`

| What you see | Call | Why |
|---|---|---|
| PRODUCED tree | `GET …/artifacts?groupBy=stage\|feature\|type` | Sidebar |
| Detail pane | `GET …/artifacts/{artifactId}` | Metadata + linked gate |
| Body / findings markdown | `GET …/artifacts/{artifactId}/content` | Blob |
| Approve / Reject | `POST …/gates/{gateId}/decisions` | Include revision shown |
| Finding dispositions | `PATCH …/findings/{findingId}` | Planned later |

---

## 10 — Activity audit log

**File:** `10-activity-audit-log.jpg`  
**Route idea:** `/work-items/{workItemId}/activity`

| What you see | Call | Why |
|---|---|---|
| Filterable list | `GET …/activity?filter=` | Everything / Human / Agent / Failures / Loops |
| Row detail | `GET …/activity/{eventId}` | Preview / refs |
| Full run log | `GET …/runs/{runId}/log` | Recoverable VerboseLogs |
| Live verbose | **SignalR** `VerboseLog` | While run live |
| Export | `GET …/activity/export` | Format OPEN |

Do **not** call agenthub ActivityLog from the browser.

---

## 11 — Approvals pending queue

**File:** `11-approvals-pending-queue.jpg`  
**Route idea:** `/work-items/{workItemId}/approvals`

| What you see | Call | Why |
|---|---|---|
| Pending queue | `GET …/gates` (Pending) and/or pending-actions for WI | Cards |
| Decided list | `GET …/approvals` | Immutable history |
| Read then decide | `GET …/artifacts/{id}` + `/content` then `POST …/gates/{gateId}/decisions` | Never approve from summary alone |
| HumanAction card | `POST …/answer` | Distinct from gates |
| Skip for now | client reorder only | No API unless product persists queue |
| Assumptions | `GET …/assumptions` + assumption-kind gates | |

---

## 12 — Intake conversation + evidence

**File:** `12-intake-conversation-evidence.jpg`  
**Route idea:** `/work-items/{workItemId}/steering` (draft)

| What you see | Call | Why |
|---|---|---|
| Open / create draft | `POST /api/projects/{projectId}/work-items` | Returns `workItemId` + intake `threadId` |
| Transcript | `GET …/conversations/{threadId}` | Messages |
| Send message | `POST …/conversations/{threadId}/messages` | User + assistant |
| Attach evidence | `POST …/conversations/{threadId}/attachments` | File → Blob |
| WORKING commentary | **SignalR** HighLevelSummary + VerboseLog | Also lands in transcript later |
| Produce summary | `POST …/conversations/{threadId}/summaries` | `summaryRevision` |
| Review summary | `GET …/conversations/{threadId}/summaries/{revision}` | Before Implement |
| **Implement now** | `POST /api/work-items/{workItemId}/execute` | Takes `~ACTIVE`; starts pipeline |
| **Save draft** | none | Just leave |

`409 ActiveConflict` only on **Implement** when another WI holds the project lock.

---

## 13 — Intake draft context panel

**File:** `13-intake-draft-context-panel.jpg`

Same screen family as **12**. Extra calls for the sidebar:

| What you see | Call |
|---|---|
| Context so far | `GET …/conversations/{threadId}/context` |
| Assumptions / features / attachments | from context (+ assumptions list) |

Implement / Save draft rules identical to mock **12**.

---

## 14 — Completion summary (deployed)

**File:** `14-completion-summary-deployed.jpg`  
**Lives under Artifacts** (not a separate API resource).

| What you see | Call | Why |
|---|---|---|
| Find completion summary | `GET …/artifacts` | Key / Deploy stage (`completion-summary.json`) |
| Sections | `GET …/artifacts/{id}` + `…/content` | Producer-owned JSON/Markdown |
| Download / share | content URL | Share mechanism OPEN |
| Signed off | `POST …/sign-offs` | Later E3 |
| Raise a change | OPEN (steering or new WI) | No invented endpoint |

---

## 15 — Map / demo flows overview

**File:** `15-map-demo-flows-overview.jpg`  
**Kind:** design / navigation overview (not a product page).

Use for orientation. Runtime data for the Home-like glance is the same as mock **01**: `pending-actions`, `projects`, `home/activity`.

---

## 16 — What changed from v2

**File:** `16-v3-what-changed-from-v2.jpg`  
**Kind:** design changelog. **No endpoints.**

---

## 17 — Open design tensions

**File:** `17-open-nine-design-tensions.jpg`  
**Kind:** design notes. **No endpoints.** Track OPEN items in the contracts / UI map, not in new routes.

---

## 18 — Repo folder: buildgenix-api

**File:** `18-repo-folder-buildgenix-api.jpg`  
**Kind:** repo layout slide. **No endpoints.**

---

## 19 — Repo folder: agenthub-api

**File:** `19-repo-folder-agenthub-api.jpg`  
**Kind:** repo layout slide. **No endpoints.** Browser never calls agenthub for UI data.

---

## 20 — Repo folder: buildgenix-web

**File:** `20-repo-folder-buildgenix-web.jpg`  
**Kind:** repo layout slide. **No endpoints.**

---

## 21 — Steering: conversation + proposals

**File:** `21-steering-conversation-proposals.jpg`  
**Route idea:** `/work-items/{workItemId}/steering` (in-flight)

| What you see | Call | Why |
|---|---|---|
| Status / Pause / Stop | WI detail + `POST …/pause` \| `…/stop` | Phase 0 OPEN |
| Feature strip | `GET …/progress` | Running stages |
| Conversation | `GET`/`POST …/conversations/{threadId}` | Steering thread (same resource, different thread) |
| Proposed changes rail | `GET …/proposals` | **E1** |
| Approve / Discard / bulk | `POST …/proposals/{id}/decisions`, `POST …/proposals/bulk-decisions` | **E1** |
| Ambient commentary | **SignalR** | Dual-layer (HighLevelSummary + VerboseLog) |

---

## 22 — Steering: history and cost

**File:** `22-steering-history-and-cost.jpg`

| What you see | Call | Why |
|---|---|---|
| Interventions table | `GET …/interventions` | **E1** |
| Cost rollup | `GET …/steering/cost` | **E1** |
| Earlier threads | `GET …/conversations` | List threads |

---

## 23 — Steering: chips and commentary

**File:** `23-steering-chips-and-commentary.jpg`

| What you see | Call | Why |
|---|---|---|
| Suggestion chips | FE static copy into composer | No API |
| Watch without typing | SignalR + `GET …/progress` | |
| Send follow-up | `POST …/conversations/{threadId}/messages` | |

---

## Quick cheat sheet

| Mock | Primary endpoints |
|---|---|
| 01 Home | `GET /api/pending-actions`, `GET /api/projects`, `GET /api/home/activity` |
| 02 Project list | `GET /api/projects/{id}`, `GET …/work-items`, `POST …/work-items` |
| 03–08 Progress | `GET …/work-items/{id}`, `GET …/progress`, `GET …/telemetry`, gates/`answer`, SignalR |
| 09 Artifacts | `GET …/artifacts`, `…/{id}`, `…/content`, `POST …/gates/…/decisions` |
| 10 Activity | `GET …/activity`, `…/{eventId}`, `…/runs/{runId}/log`, SignalR |
| 11 Approvals | `GET …/gates`, `GET …/approvals`, decisions + artifact GET, `POST …/answer` |
| 12–13 Intake | conversations CRUD/summaries; **Implement** = `POST …/execute`; Save draft = leave |
| 14 Completion | artifact GET + content (+ sign-offs later) |
| 15 Overview | same as Home (design map) |
| 16–20 Design/repo | none |
| 21–23 Steering in-flight | conversations + proposals/interventions/cost (E1) + SignalR |

### Never do

- Call agenthub tables/HTTP from the browser for activity.
- Approve from the Approvals card without opening the artifact.
- Treat Create WI or Save draft as pipeline start.
- Ignore `409` on Implement when another WI is active.

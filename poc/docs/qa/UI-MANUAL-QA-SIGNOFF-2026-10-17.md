# UI Manual QA Sign-off — End-user Studio (Docker resources)

**Date:** 2026-10-17  
**UI:** [http://127.0.0.1:43182](http://127.0.0.1:43182)  
**Backend tip:** topic agents on `main-azure`  
**Evidence:** `poc/docs/qa/evidence/ui-manual-qa-2026-10-17/`

## Result

| Metric | Value |
|--------|-------|
| Cases | **8/8 PASS** |
| Checks | **all green** (consolidated JSON) |
| Path | Interactive HITL Studio → `/start` → topic workers → Compose/Azurite/Gitea |
| LLM | `Fixture` (unchanged) |

## Cases (end-user chip / typed request)

| Case | Gates | Deploy smoke | Notes |
|------|-------|--------------|-------|
| Calc_Add | 6 HITL approves | `/api/add` → 5 | Docs + Logs verified |
| Hello_LLM | 6 | `/api/thought`, `/api/hello` | fe-be-llm |
| Hello_no_LLM | 6 | `/api/hello` | |
| Todo | 6 | `/api/todos` | |
| MultiWI_Add → Subtract | 6+6 | add + subtract | **Same projectId** |
| Rewrite_net8 | 6 | add + `runtime:net10` | Baseline fixture |
| RouteBack | 7 (IA_REVIEW×2) | add | `simulateTaRouteback` + RouteBack event |

## Production-grade fixes during UI QA

1. **Agent fail callback** — topic worker now REST-fails BuildGenix when dispatcher returns no result (no more silent hang).
2. **Interactive RouteBack probe** — remove `taRoutebackProbe` from `LiveArtifacts` + enforce max RouteBacks (was infinite loop on UI path).
3. **Studio examples** — Hello (no LLM), Todo, RouteBack probe chips; `simulateTaRouteback` wired through API client.

## Constraints / 9 Qs covered (UI path)

| Constraint | Evidence |
|------------|----------|
| Every agent = BackgroundService + SB topic | AgentHub logs: PRD/SD/IA/TA/Deploy listening; chain publishes |
| AgentHub → BuildGenix = REST only | `/api/poc/pipeline/agent-callback` |
| Between agents = SB topics | enqueue + chain, no BG stage loop |
| No LLM connectivity change | Fixture |
| Docker resources | Compose deploys + Azurite blobs + Gitea PRs |
| Q1/Q6 FE+BE matrix | All 8 UI cases |
| Q2/Q4/Q5 SD spoon-feed + agent LLM | techSpec artifacts in Docs; decision logs |
| Q3/Q8 topic workers / event handoff | HITL enqueue after approve |
| Q7 dual-mode seams | Still Gitea+Compose local; AzDO/App Service factories intact |
| Q9 Decision + activity | decisionLog / GateApproved / Finalized on every case |

## 6-persona (UI)

**APPROVE (local POC)** — PM/Architect/BE/FE/DevOps/QA: interactive Studio proves end-user gates, docs, logs, and live Docker URLs. Cloud Gate C/D still CONDITIONAL.

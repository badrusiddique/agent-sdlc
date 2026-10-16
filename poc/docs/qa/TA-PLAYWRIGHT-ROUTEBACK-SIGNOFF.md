# Manual E2E Sign-off — Testing Agent (Playwright + RouteBack)

**Date:** 2026-10-16  
**Branch tip:** `main-greenfield` (commit after this doc)  
**Environment:** Local Docker — Gitea, Azurite, AgentHub `:43180`, BuildGenix `:43181`, compose deploys  

## Verdict

**PASS** — TA Playwright FE e2e, HTTP smoke, TA→IA RouteBack, versioned artifacts, sandbox URL share, DEPLOY_SANDBOX stub. Ready for other-machine port of Ia/ then Ta/.

## Cases verified

| Case | Result | Notes |
|---|---|---|
| Happy path Calculator → DONE | PASS | Trail through TA_* + DEPLOY_SANDBOX; Playwright **green**; FE `/` + `/api/add` |
| RouteBack probe (`simulateTaRouteback`) | PASS | v1 concerns → IA_CODEGEN → v2 → TA green; final add→5 |
| Named versions | PASS | `PRD_V1.json`, `TA_RESULTS_V1.json`, `SANDBOX_URL_V1.json`, `LATEST.json` |
| Audit | PASS | 19 transitions; deployedUrl; `AuditLog_V{n}.json` |
| Unit tests | PASS | Pipeline.Tests **56/56** |

## Evidence

- `/tmp/bgx-ta-e2e/happy-run.json`, `routeback-run.json`, `SUMMARY.json`  
- Blobs under `/tmp/bgx-poc-blobs/{projectId}/{workItemId}/`

## Structure shipped

| Folder | Role |
|---|---|
| `Ta/` | Smoke, Playwright, verify runner, concerns |
| `Deploy/` | Sandbox URL stub |
| `Artifacts/` | Versioned paths + LATEST/state-context writer |
| `Ia/`, `Prd/`, `Sd/` | Unchanged behaviour (IA still regenerates on RouteBack) |

## Other machine

Do **not** change LLM Gateway settings. Port `Ia/` first, then `Ta/`, then enhance Prd/Sd using this layout as reference.

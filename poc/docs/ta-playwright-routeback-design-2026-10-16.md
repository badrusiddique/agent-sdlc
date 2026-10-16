# Testing Agent — Playwright + IA RouteBack (design)

**Branch:** `main-greenfield`  
**Date:** 2026-10-16  
**Assumptions (per user):** PRD/SD gates always auto-approve for this POC slice. Do not change Azure LLM wiring.

## Goals

1. TA writes **Playwright** FE automation against the IA-deployed URL (plus keep HTTP smoke).
2. On red TA: **Completed + RouteBack → IA** (not Failed). IA reads concerns, regenerates/fixes, re-deploys, then TA runs again.
3. Each iteration bumps **artifact revision** (`v1` → `v2` …) and named copies (`PRD_V1.json`, `TA_RESULTS_V2.json`, …). Agents always follow **LATEST**.
4. Crash-safe **state-context.json** under the work-item artifact root.
5. **AuditLog** (and `AuditLog_V{n}.json`) under the same tree for verbose UI.
6. End of TA: publish **sandbox URL** to BuildGenix (work item + artifact). **DEPLOY_SANDBOX** stub announces URL and waits (auto-approve).
7. Folder layout: `Prd/`, `Sd/`, `Ia/`, `Ta/`, `Deploy/` for portability to another machine agent-by-agent.

## Stage strip (POC)

`PRD → SYSTEM_DESIGN → IA_* → TA_GENERATE → TA_VERIFY → TA_REPORT → TA_MERGE → DEPLOY_SANDBOX → DONE`

## RouteBack algorithm

```
TA_VERIFY red:
  write ta-concerns.json (+ versioned copy)
  AgentResult Status=Completed, outputs signal.kind=RouteBack, routeBackTo=IA_CODEGEN
  orchestrator: if routebackCount < Max(2):
    ArtifactRevision++
    BlobPrefix = {pid}/{wid}/v{rev}/
    write LATEST.json + state-context.json
    jump stageIndex to IA_CODEGEN
    pass artifacts["taConcerns"]
  else: fail pipeline with concerns exhausted
```

## Playwright

Under mono `tests/e2e/playwright/`: config + specs that open deployed URL, click primary FE control, assert API payload in `#out` / list. Runner prefers Playwright; HTTP smoke always runs as hard gate if Playwright browsers unavailable.

## Other-machine note

Do not change LLM Gateway settings. Move Ia/ then Ta/ then enhance Prd/Sd from this code as reference.

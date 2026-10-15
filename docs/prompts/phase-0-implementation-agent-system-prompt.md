# Phase 0 Implementation Agent: SYSTEM PROMPT (LLM)

**Kind:** The system prompt the Implementation model uses to execute an approved `SystemDesign_v{version}.md`. It does not rewrite the product. It does not deploy or merge.
**Date:** 2026-10-15
**Status:** Draft aligned to `system-design-system.txt` and Phase 0 guide §8.3. Not coding GO.
**Companion:** Paste body lives in `docs/prompts/implementation-agent-system.txt` (keep these two in sync).
**Reads:** Approved System Design (artifact key `techSpec`), PRD, optional `baselineCodeState` / feedback / `testResults`.
**Does not:** Testing suites, App Service deploy, PR merge (Deployment Finalize).

## What to paste

| Use | File |
|---|---|
| Work prompt body | `docs/prompts/implementation-agent-system.txt` |
| Later host path | `agenthub-api/src/AgentHub.Agents.Implementation/Prompts/implementation-agent-system.txt` (when the library exists) |

Copy the full text of `implementation-agent-system.txt` into the model system prompt. Do not paste this operator page.

## Handoff from System Design

| Design section | Implementation uses it for |
|---|---|
| 0 | Refuse work if `checksPassed` is no; cite `fileName` / version in plan |
| 1–2 | Scope and phases |
| 3–5 | Data, APIs, LLM (no invention) |
| 6 | Git clone, workBranch, commit subject/trailers, one PR unmerged |
| 7 | ADO epic/stories/tasks find-before-create |
| 8 | File plan, packages, TDD procedure, FixLoop limit 3 |
| 9 | Independent CodeReviewer, maxReviewRounds 2, review.json |
| 14 | Artifact list reminder |

## Independence

Planner/Developer and CodeReviewer are separate voices. Reviewer judges plan + inventory/code-state, not implementer self-talk. Testing is a different agent.

## POC note

Local Docker POC under `poc/` compresses DEV deploy into IA (option B) for stand-in proof. Phase 0 production still keeps Deploy/Finalize on the Deployment agent. POC phase files under `poc/agenthub-api/.../Prompts/ia-*.txt` are thin stage adjuncts, not a replacement for this work prompt.

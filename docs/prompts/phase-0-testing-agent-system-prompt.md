# Phase 0 Testing Agent: SYSTEM PROMPT (LLM)

**Kind:** The system prompt the Testing model uses to prove `context.repo.headCommit` against `SystemDesign_v{version}.md` sections 10 and 11.
**Date:** 2026-10-15
**Status:** Draft aligned to `system-design-system.txt` and Phase 0 guide §8.4. Not coding GO.
**Companion:** Paste body lives in `docs/prompts/testing-agent-system.txt` (keep these two in sync).
**Reads:** System Design (`techSpec`), `review` (evidence only), `headCommit`, `pullRequestId`.
**Does not:** Implement, deploy, or merge. Red tests are `Completed` + `RouteBack` to Implementation.

## What to paste

| Use | File |
|---|---|
| Work prompt body | `docs/prompts/testing-agent-system.txt` |
| Later host path | `agenthub-api/src/AgentHub.Agents.Testing/Prompts/testing-agent-system.txt` (when the library exists) |

## Handoff from System Design

| Design section | Testing uses it for |
|---|---|
| 10 | Clone SHA, suites, acceptanceStart, Fixture rules, browser=none, RouteBack rules |
| 11 | Acceptance check table (nine columns); skip `retired=true` |
| 12 | Traceability cross-check (orphan detection) |
| 0 / 1 | PRD bar and whether design was approvable |

## Independence

Ignore implementer/reviewer cheerleading. `review.json` passed is not acceptance green. Derive cases from section 11 + PRD criterion ids.

## Live URL

Phase 0 Testing runs **before** Deploy, so `deployedUrl` is not required. If a task carries `deployedUrl` (POC option B after IA deploy), optional HTTP smoke may mirror section 11 paths against that base URL without inventing bodies. Merge remains Deployment Finalize in Phase 0 (POC `TA_MERGE` is a local stand-in only).

## POC note

`poc/.../Prompts/ta-*.txt` and persona `persona-qa-*.txt` are thin adjuncts for the local Docker proof. Prefer this work prompt when standing up the production Testing agent.

---
status: APPROVE-WITH-CONDITIONS
gate: 5-persona delta (Manual QA + Gateway + Guardrails Phase 0)
date: 2026-10-06
scope: Delta after P-GATE-MIGRATION-PREP — not a re-litigation of I0–I2
---

# Design review gate — DELTA Manual QA / Gateway / Guardrails — 2026-10-06

**Overall verdict: APPROVE WITH CONDITIONS**

All five: **APPROVE WITH CONDITIONS**. No BLOCK.

## Per-persona

| Persona | Verdict | Review |
|---|---|---|
| Solution Architect | APPROVE WITH CONDITIONS | `internal/reviews/delta-manual-qa-guardrails-architect.md` |
| Principal Engineer | APPROVE WITH CONDITIONS | `internal/reviews/delta-manual-qa-guardrails-principal-engineer.md` |
| Senior QA | APPROVE WITH CONDITIONS | `internal/reviews/delta-manual-qa-guardrails-qa.md` |
| DBA / Data Engineer | APPROVE WITH CONDITIONS | `internal/reviews/delta-manual-qa-guardrails-dba-data.md` |
| DevOps | APPROVE WITH CONDITIONS | `internal/reviews/delta-manual-qa-guardrails-devops.md` |

Brief: `internal/review-brief-delta-manual-qa-guardrails-2026-10-06.md`

## Conditions cleared in this merge

| ID | Condition | Status |
|---|---|---|
| PE-1 | Dispatcher test: guardrail block → DeadLetter+Failed without Abandon | **Cleared** — `ProcessAsync_guardrail_block_fails_without_retry_at_delivery_1` |
| PE-2 | Evaluate before truncate | **Cleared** — handler evaluates raw completion then truncates |
| QA-1 | Live Ollama path logs guardrail Allow | **Cleared** — `internal/manual-e2e-ollama/agenthub/guardrails-live.log` |
| DevOps-1 | Spoon-feed notes 5.x `max_completion_tokens` | **Cleared** — `docs/agenthub-api-new-machine-integration.md` |

## Conditions deferred (explicit)

| ID | Owner | Note |
|---|---|---|
| ACS-1 | DevOps / PE | Wire Azure Content Safety Prompt Shields behind `IGuardrailPipeline` on other machine (config + DAC) |
| CAT-1 | Product | Catalog items 9–18, 20, 22–23 remain phased per `plans/agent-guardrails-design.md` |
| GW-LIVE | QA | Live Gateway 20/20 still needs OPEN-I2-6 Consumer ID on isolated machine |

## Package decision (recorded)

**Not GuardrailsAI runtime.** Always-on C# `LocalGuardrailPipeline` + Azure AI Content Safety when configured. GuardrailsAI Hub = checklist inspiration only. **P-GUARDRAILS-PHASE0**.

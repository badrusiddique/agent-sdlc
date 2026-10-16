# Six-persona review — PRD + System Design folder extract (like IA)

**Date:** 2026-10-16  
**Branch tip base:** `main-greenfield`  
**Ask:** Move PRD and System Design background-process code into separate folders matching the `Ia/` / `Ta/` pattern; check prompts; no side effects on greenfield / Azure / Manual QA contracts.

## Context

Today `PipelineAgentHandler` owns `RunPrdAsync` / `RunSystemDesignAsync` plus `DefaultPrd` / `DefaultTechSpec` (~100 lines of deliveryMode-aware JSON).  
`Ia/` already holds codegen, rewrite, feature-increment, git/deploy. `Ta/` holds smoke helpers. PRD/SD have no folder.

## Proposed change (structural only)

| Folder | Contents | Stays in handler |
|---|---|---|
| `Prd/` | `PrdDocumentBuilder` (fallback `prd.json` + `prd.md` body) | `RunPrdAsync` step orchestration + LLM call (same as IA stages call `Ia.*`) |
| `Sd/` | `TechSpecBuilder` (fallback `techspec.json` + `system-design.md` body) | `RunSystemDesignAsync` step orchestration + LLM call |

**Invariant:** Artifact paths, JSON field names (`deliveryMode`, `stories`, `apis`, …), blob keys (`prd`, `prdMd`, `techSpec`, `techSpecMd`), stage constants, persona mapping (Product / Architect), and dual-mode Azure seams are unchanged.

## Prompt review

| Prompt | Needs change? | Why |
|---|---|---|
| `Prompts/pipeline-system.txt` `===STAGE:PRD===` | **Yes (align)** | Stage JSON schema omits `deliveryMode` / `projectMode` / `targetFramework` / `baselineSourcePath` that builders + Manual QA already require |
| `Prompts/pipeline-system.txt` `===STAGE:SYSTEM_DESIGN===` | **Yes (align)** | Same gap for `deliveryMode`, `targetFramework`, `appKind` |
| `Prompts/persona-product.txt` | No (already mentions deliveryMode) | Keep |
| `Prompts/persona-architect.txt` | **Light touch** | Echo deliveryMode / primary API rules so Architect JSON matches TechSpecBuilder |
| `docs/prompts/system-design-system.txt` + phase-0 SD page | No | Already has mode / deliveryMode / baselineSourcePath (Phase 0 Markdown authority) |
| `docs/prompts/implementation-agent-system.txt` / TA | No | Already consume deliveryMode from PRD/design |
| `docs/prompts/sd-ia-ta-prompt-alignment-*.md` | Optional note | Document POC thin stage JSON now mirrors greenfield fields |

## Six-persona verdicts

| Persona | Verdict | Conditions |
|---|---|---|
| **Solution Architect** | APPROVE | Keep dependency direction: `Prd`/`Sd` may call `Ia.DeliveryModeClassifier` / `RequestDrivenCodegen.Classify` / feature helpers; do not move DeliveryMode out of `Ia/` this slice (avoids churn). Do not change Phase 0 SystemDesign_v Markdown contract. |
| **Principal Engineer** | APPROVE | Pure extract + prompt schema align. Add focused unit tests on builder JSON for Basic / Rewrite / FeatureIncrement. Full Pipeline test suite must stay green. No DI / factory / Azure provider changes. |
| **Senior QA** | APPROVE | Manual QA contracts (blob names, deliveryMode values, trail) must be bit-compatible. Prefer unit tests over re-running full Docker matrix unless builders change field names. Verify `prd.json` / `techspec.json` shapes with a Fixture LLM fallback path. |
| **DBA / Data** | APPROVE | No table/blob schema rename. Same keys under `{projectId}/{workItemId}/v1/`. |
| **DevOps** | APPROVE | No compose / AzDO / App Service code paths touched. No env var changes. |
| **Product Manager** | APPROVE | PRD still Product-owned; stories + acceptance remain. Prompt schema update is documentation of existing fallback behaviour, not a new product feature. |

**Overall: APPROVE** — proceed with folder extract + thin POC prompt alignment only.

## Side-effect guardrails

1. Do not edit `Ia/*` behaviour, Azure clients, LocalComposeHost, or TA smoke logic except if compile requires `using`.  
2. Do not change Orchestrator intake / BuildGenix API.  
3. Do not rename stages or artifact keys.  
4. Do not expand Generic into free-form LLM codegen.  
5. After change: `dotnet test` Pipeline.Tests all green; spot-check builder unit tests for three delivery modes.

## Out of scope

- Full Phase 0 Product agent system prompt (none today beyond POC persona).  
- Moving `RunPrdAsync` / `RunSystemDesignAsync` into separate hosted services / queues.  
- Relocating `DeliveryMode` enum out of `Ia/`.

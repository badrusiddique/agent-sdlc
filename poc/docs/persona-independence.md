# Persona independence (prompts + context)

Each SDLC step runs as a **separate persona** with:

1. Its own **system prompt** under `Prompts/persona-*.txt`
2. A **filtered evidence pack** (`BuildPersonaContextAsync`) — not a shared chat memory of prior agent rationalizations

| Stage | Persona | Sees | Must not see / inherit |
|---|---|---|---|
| PRD | Product | User request | Design, code, tests |
| SYSTEM_DESIGN | Architect | User request + PRD | Code, review, QA |
| IA_PLAN | Planner | PRD + system design | Codegen hints, reviews |
| IA_CODEGEN | Developer | PRD + approved plan | Reviewer/QA voice |
| IA_REVIEW | Reviewer | Plan + `code-state` / file inventory | Developer LLM narrative / self-praise |
| TA_GENERATE | QA Automation | PRD + techspec APIs + deployed URL | Reviewer “passed”, implementer unit results as gospel |
| TA_REPORT | QA Lead | PRD bar + `ta-results.json` + URL | Softening from implementer |

This keeps Code Review and Automation Test design independent of Implementation authorship.

Production work prompts (same rigor as System Design):

- `docs/prompts/system-design-system.txt` (orders in `SystemDesign_v{n}.md`)
- `docs/prompts/implementation-agent-system.txt` (executes design §§6–9; no deploy/merge)
- `docs/prompts/testing-agent-system.txt` (executes design §§10–11; RouteBack on red; no merge)

Alignment note: `docs/prompts/sd-ia-ta-prompt-alignment-2026-10-15.md`. Local POC option B (IA deploy + TA merge) is a stand-in; Phase 0 keeps Deploy/Finalize on the Deployment agent.

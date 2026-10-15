# Six-persona verification — request-driven IA codegen + Graphify (2026-10-15)

**Scope:** Replace template-only IA codegen with runtime request-driven generation; build `code-state` via `graphify extract . --code-only` after review.  
**Personas:** SA, PE, QA, DBA, DevOps, PM.  
**Gate folder:** store `internal/reviews/ia-request-driven-graphify-gate-2026-10-15/`.

## Evidence

| Check | Result |
|---|---|
| Pipeline unit tests | 40/40 |
| Calculator `verify-ia.sh` | Completed; `/api/add?a=2&b=3` → 5 |
| Todo pipeline | Completed; `/api/todos` → `[]` |
| Hello without LLM | Completed; `/api/hello` → Hello World |
| code-state / code-graph | Present; `command: graphify extract . --code-only`, `mode: request-driven` |

## Sign-off

| Persona | Verdict |
|---|---|
| Solution Architect | APPROVE WITH CONDITIONS |
| Principal Engineer | APPROVE WITH CONDITIONS |
| Senior QA | APPROVE WITH CONDITIONS |
| DBA / Data | APPROVE WITH CONDITIONS |
| DevOps | APPROVE WITH CONDITIONS |
| Product Manager | APPROVE WITH CONDITIONS |

**Overall: APPROVE WITH CONDITIONS** — ship on `main-azure`; do not push to `main`. Full free-form LLM codegen and ACR builder rebuild remain follow-ups.

## Gate-found fixes folded in

- TA `DeployedSmokeGenerator` now picks primary API from the same request classifier as codegen (Todo/Hello no longer smoke `/api/add`).
- Classifier treats “without llm” / “no llm” as non-LLM hello.

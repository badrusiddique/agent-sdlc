# Request-driven IA codegen + Graphify code-state

## What changed

| Before | After |
|---|---|
| Copy fixed `Templates/fe-be` (calculator-shaped) | **Generate at runtime** from the user request (`RequestDrivenCodegen`); template folders + `TemplateMaterializer` removed |
| `code-state.json` = file inventory from materializer | **`graphify extract . --code-only`** → `code-graph.json` + normalised `code-state.json` |
| After codegen | After **IA_REVIEW**, before PR |

## App shapes (classified from request / stack)

- **Calculator** — add API (verify scripts still use this)
- **Todo** — todo list API
- **Hello** — hello endpoint
- **HelloLlm** — hello + `/api/thought` (`fe-be-llm` / LLM wording)
- **Generic** — minimal hello API for other asks

This is still a **runtime scaffold** keyed off the request (not a single calculator template). Full free-form LLM file writing is the next step when a real model is wired; Fixture stays deterministic.

## Graphify

Locked command (same as production IA prompt):

```bash
graphify extract . --code-only
```

Install: `pip install graphifyy` (CLI name remains `graphify`). Also baked into `agenthub-ia-builder`.

## Push target

IA opens/updates a **feature-branch PR** (`IA_PUSH_DEPLOY`). It does **not** push to `main`. TA merges after green smoke (`TA_MERGE`).

## TA smoke

`DeployedSmokeGenerator` uses the same request classifier so live smoke hits `/api/add`, `/api/todos`, `/api/hello`, or `/api/thought` — not a hard-coded calculator.

## Six-persona

See [`ia-request-driven-six-persona-2026-10-15.md`](ia-request-driven-six-persona-2026-10-15.md) (overall AWC on `main-azure`).

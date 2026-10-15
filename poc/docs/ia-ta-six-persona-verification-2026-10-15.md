# Six-persona verification — IA + TA + independent personas (2026-10-15)

**Scope:** Pre-phase-0 under `poc/` — Implementation Agent, Testing Agent, persona-isolated prompts/context, local Docker (Azurite + Gitea + Compose), BuildGenix studio UI.  
**Personas:** Solution Architect, Principal Engineer, Senior QA, DBA/Data, DevOps, Product Manager.

## Evidence pack (this VM)

| Check | Result | Proof |
|---|---|---|
| `./scripts/seed-gitea.sh` | Pass | `agenthub-azurite` + `agenthub-gitea` Up; repo `buildgenix/app-monorepo` |
| `./scripts/verify-ta.sh` | Pass (exit 0) | Trail through `TA_MERGE`; Gitea PR #9 `merged=True`; `/api/add` → 5 |
| Pipeline unit tests | Pass | 2 existing + persona independence suite |
| Studio UI e2e (Playwright) | Pass | Calculator → all gates approved → **Completed**; screenshots under `/opt/cursor/artifacts/screenshots/` |
| Persona prompts shipped | Pass | Seven `Prompts/persona-*.txt` + `BuildPersonaContextAsync` filtered packs |

Deployed smoke (API verify): `http://127.0.0.1:34309` — `/health` ok, add→5.  
UI run deployed: `http://127.0.0.1:44137` (Completed in studio).

---

## Independence model (cross-cutting)

Each SDLC step is a **separate persona DNA**:

| Stage | Persona | Filtered evidence | Explicitly excluded |
|---|---|---|---|
| PRD | Product | User request | Design, code, tests |
| SYSTEM_DESIGN | Architect | Request + PRD | Code, review, QA |
| IA_PLAN | Planner | PRD + system design | Codegen / review voice |
| IA_CODEGEN | Developer | PRD + approved plan | Reviewer / QA |
| IA_REVIEW | Reviewer | Plan + `code-state` inventory | Developer LLM narrative |
| TA_GENERATE | QA Automation | PRD + techspec + deployed URL | Reviewer “passed” cheerleading |
| TA_REPORT | QA Lead | PRD bar + `ta-results.json` + URL | Softening from implementer |

Docs: [`persona-independence.md`](persona-independence.md). Unit guardrails: `PersonaIndependenceTests`.

---

## 1. Solution Architect — APPROVE

- Stage order matches option B: IA plan→codegen→review→push/deploy, then TA generate→verify→report→merge.
- Personas are not a shared chat memory; each call uses its own system prompt + filtered pack (`PipelineAgentHandler`).
- Git/PR and deploy remain ports (`Ia:Git`, `LocalComposeHost`) so Azure can replace stand-ins without rewriting stage IDs.
- **Condition:** Fixture LLM locally proves plumbing; live model variance is out of this verify pass.

## 2. Principal Engineer — APPROVE

- Distinct `persona-*.txt` files; Reviewer text forbids trusting implementer narrative; QA Automation forbids treating review as gospel.
- `verify-ta.sh` green after persona rebuild; mono folder has `frontend/`, `backend/`, `tests/e2e/`.
- Studio strip shows IA + TA labels through Done; Playwright approved every gate including `IA_REVIEW` and `TA_REPORT`.
- **Condition:** Full browser (Playwright) automation of the *generated* app FE is still future work; TA POC smoke is HTTP against deployed URL.

## 3. Senior QA — APPROVE

- Live URL automation: `ta-results.json` status=`green` (HealthOk + Add).
- Interactive UI path exercised human gates (not only `/run` auto-approve).
- Merge only after report gate; Gitea PR closed merged.
- Independence: TA cases derived from PRD/techspec/URL path, not from IA review summary as source of truth.
- **Condition:** Fixture LLM summaries are deterministic; independence is enforced by context filtering + prompts, not by divergent fixture prose.

## 4. DBA / Data Engineer — APPROVE

- Blob paths `{projectId}/{workItemId}/v1/` populated: `prd.json`, `implementation-plan.md`, `code-state.json`, `review.json`, `ta-results.json`, `merge.json`, AuditLog.
- Azurite (Docker) still the storage stand-in; File blob provider root `/tmp/bgx-poc-blobs` used in Development.
- Single-writer POC assumption holds (agent → AH blobs; BuildGenix → WorkItems/events/audit).

## 5. DevOps — APPROVE

- Docker: Azurite, Gitea, per-WI Compose deploy with rewritten host ports.
- APIs: AgentHub `:43180`, BuildGenix `:43181`, studio `:43182`.
- Builder image optional (`agenthub-ia-builder:local`); host SDK fallback verified previously.
- **Condition:** Managed Identity / real App Service not emulated locally — see Azure guide next.

## 6. Product Manager — APPROVE

- Requester path: describe Calculator → approve gates → live URL + Completed in studio.
- Documents / Logs tabs available; deployed banner shows URL.
- Persona independence addresses “review rubber-stamping implementer” and “tests copying implementer claims” risks called out in product intent.

---

## Sign-off

| Persona | Verdict |
|---|---|
| Solution Architect | APPROVE |
| Principal Engineer | APPROVE |
| Senior QA | APPROVE |
| DBA / Data Engineer | APPROVE |
| DevOps | APPROVE |
| Product Manager | APPROVE |

**Overall:** Local Docker + UI e2e + persona isolation are green. Proceed to Azure resource integration per [`azure-integration-next-steps.md`](azure-integration-next-steps.md).

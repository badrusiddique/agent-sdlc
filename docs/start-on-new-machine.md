# Start on the new machine — short guide

**Repo:** `https://github.com/badrusiddique/agent-sdlc` · branch `main` (≥ `fbb71b1`)

Detailed step-by-step: [`other-machine-proceed-instructions.md`](./other-machine-proceed-instructions.md).

## Order

1. Clone/pull `agent-sdlc` `main` (≥ `fbb71b1`). Skim `docs/what-you-asked-completion-report.md` + `docs/manual-e2e-means-actual.md`.
2. **BuildGenix merge:** open inno-tech BG boilerplate → paste prompt from [`buildgenix-api-new-machine-integration.md`](./buildgenix-api-new-machine-integration.md) → fill **V1–V13**.
3. **AgentHub merge:** open inno-tech AH boilerplate → paste prompt from [`agenthub-api-new-machine-integration.md`](./agenthub-api-new-machine-integration.md) → include **Core/Guardrails** → Fixture plumbing then **AI Gateway Manual QA** → fill **V1–V11**.
4. **Azure:** give DevOps [`azure-appservice-deploy-baby-steps.md`](./azure-appservice-deploy-baby-steps.md) (zip deploy, not Docker).
5. **UI juniors:** [`junior-ui-endpoint-wiring.md`](./junior-ui-endpoint-wiring.md).

## Stop rules

- No inventing endpoints or MessageIds.
- Not done until verification tables are PASS on the new machine.
- Isolated machine = **Gateway only** (no Ollama). Manual QA ≠ Fixture.
- Guardrails = in-process C# Phase 0; Azure Content Safety later optional; **no GuardrailsAI Python**.
- Do not start I3 / System Design agent from this track.

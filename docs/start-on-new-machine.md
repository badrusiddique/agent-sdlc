# Start on the new machine — short guide

Canonical detail also in Project Context: `docs/migration/start-on-new-machine.md`.

**Repo:** `https://github.com/badrusiddique/agent-sdlc` · branch `main` (≥ `f7840c4`)

## Order

1. Clone/pull `agent-sdlc` `main`. Read `docs/what-you-asked-completion-report.md`.
2. **BuildGenix merge:** open inno-tech BG boilerplate → follow / paste prompt from [`buildgenix-api-new-machine-integration.md`](./buildgenix-api-new-machine-integration.md) → fill **V1–V13**.
3. **AgentHub merge:** open inno-tech AH boilerplate → follow [`agenthub-api-new-machine-integration.md`](./agenthub-api-new-machine-integration.md) → Fixture then **AI Gateway** → fill **V1–V9**.
4. **Azure:** give DevOps [`azure-appservice-deploy-baby-steps.md`](./azure-appservice-deploy-baby-steps.md) (zip deploy, not Docker).
5. **UI juniors:** [`junior-ui-endpoint-wiring.md`](./junior-ui-endpoint-wiring.md).

## Stop rules

- No inventing endpoints or MessageIds.
- Not done until verification tables are PASS on the new machine.
- Isolated machine = Gateway only (no Ollama).
- Do not start I3 / System Design agent from this track.

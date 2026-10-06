# Manual QA E2E (Ollama) — 2026-10-06

**Definition locked:** [`manual-e2e-means-actual.md`](./manual-e2e-means-actual.md)  
**Raw artifacts:** store `internal/manual-e2e-ollama/{agenthub,buildgenix,gateway-review}/`

## Summary

| Surface | Stack | Verdict |
|---|---|---|
| AgentHub HEALTHY | Azurite + SB emulator + **Ollama** `llama3.2:1b` + real worker | **PASS** |
| BuildGenix lifecycle/catalog | Azurite + SB emulator + real host | **PASS** |
| AI Gateway client vs `ai-gateway.md` | Static review + unit tests; live Gateway on other machine | **Chat rules OK** after `max_completion_tokens` fix |

## AgentHub highlights

- Real Ollama joke in blob (`llmProvider":"Ollama"`), not Fixture string.
- D-C1: publish Completed → then mark AgProcessedEvents.
- CorrelationId `corr-1791267662` on run start.
- Dedup no-op; fault abandon×5 → Failed/DLQ; `/health/live` stayed 200.

## BuildGenix highlights

- `BG_LIFECYCLE_ASSERT_OK`; catalog `SMOKE_ROUTES_OK 15 FAIL5xx 0`.
- Edge paths: noop, ActiveConflict, SummaryNotFound exercised.

## Gateway fix for other machine

`GatewayLlmClient` now sends `max_completion_tokens` for aliases `5.1` / `5.4` per `docs/ai-gateway.md` §3. `GatewayBodyRulesTests` 5/5 green. Live Gateway call still to be verified on the isolated machine.

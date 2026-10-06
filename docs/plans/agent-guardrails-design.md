---
status: APPROVED-FOR-PHASE-0-SEAM
updated: 2026-10-06
authority: Badru GO 2026-10-06 + brain dump (prompt injection / groundedness / codegen / jailbreak)
rule: Stack stays .NET 10 + Azure. GuardrailsAI Hub is a checklist inspiration, not a runtime dependency.
---

# Agent guardrails design

## Package / platform decision (locked)

| Option | Verdict | Why |
|---|---|---|
| **[GuardrailsAI](https://guardrailsai.com/hub)** as NuGet/runtime | **No** | Python-first hub/validators; does not fit locked stack (.NET 10 + Azure only). Keep Hub as a **catalog of check ideas**, not a shipped dependency. |
| **Azure AI Content Safety** (Prompt Shields, text/PII APIs) | **Yes — cloud / isolated machine** | First-party Azure; .NET SDK (`Azure.AI.ContentSafety`); Prompt Shields for jailbreak + direct/indirect injection on user text and documents (PRD/wiki/repo as data). Aligns with App Service + `DefaultAzureCredential`. |
| **In-process C# validators** in agenthub | **Yes — always on (local + cloud)** | Deterministic: length caps, content-type, secrets regex, ban list, schema shape, system-prompt leakage, path-escape (later). Works with Ollama offline; no Python sidecar. |
| Custom LLM-as-judge only | **No as primary** | Costly, non-deterministic; optional later for groundedness/provenance, not the hard stop. |

**Runtime shape:**

```text
TaskMessage / wiki / PRD / repo text
        │
        ▼
 IGuardrailPipeline.EvaluateInputAsync  ──► local C# rules (always)
        │                                 └► Azure Content Safety Prompt Shields (when configured)
        ▼
 ILlmClient.CompleteAsync
        │
        ▼
 IGuardrailPipeline.EvaluateOutputAsync ──► local C# rules (always)
        │                                 └► optional Azure output/PII scan when configured
        ▼
 EffectLedger + blob write (only if Allow)
```

Every verdict: `Allow | Block | RepairHint` + reason codes + **CorrelationId** logged (ActivityLog + Serilog).

Local default: `Guardrails:ContentSafety:Enabled=false` (no Azure call). Isolated machine: enable Content Safety endpoint via config/Key Vault.

## Brain-dump catalog (1–23) — phasing

| # | Area | Phase 0 (HEALTHY seam now) | Later (production agents / DevOps) |
|---|---|---|---|
| 1 | Prompt injection / jailbreak on user+docs | Local heuristic ban-list + **interface** for Prompt Shields | Wire Azure Prompt Shields on Gateway machine |
| 2 | PII before model | Basic pattern detectors (email/SSN-ish) optional | Azure PII / richer detectors |
| 3 | Secrets before model | Regex (key/token/connection string shapes) | Align with org secret scanner |
| 4 | Length + content-type in code | **Yes** | — |
| 5 | Schema/semantic on agent output | HEALTHY: non-empty, no `#` headings, max len | Per-agent JSON schema |
| 6 | Secrets on artifacts before blob | **Yes** (same scanner) | Generated files / commits |
| 7 | Ban list | **Yes** (shared list) | Expand per agent |
| 8 | System-prompt leakage | **Yes** (output must not contain system prompt body) | — |
| 9 | Provenance / groundedness | Doc only | Wiki-trace checks for SD/PRD agents |
| 10–14 | Generated code checks | Doc only | IMPLEMENTATION agent |
| 15–17 | Actions / permissions | Doc only (SB roles / ADO allowlist / human deploy) | App Service + ADO wiring |
| 18 | Token/cost budget | Doc only | Per WI/run hard stop |
| 19 | Max repairs/retries/timeouts | Exists today | Keep |
| 20 | Global concurrency | ADR-033 | Keep |
| 21 | Verdict + correlation id | **Yes** | — |
| 22 | Rates in verbose logs | Log counts per run | Dashboard later |
| 23 | Red-team golden set via Gateway | Doc / QA later | Other machine |

## HEALTHY Phase 0 coding GO (this slice)

1. `AgentHub.Core` abstractions: `IGuardrailPipeline`, `GuardrailVerdict`, reason codes.
2. `AgentHub.Agents.Healthy` (or small Core helpers): local input/output validators used by `HealthyAgentHandler`.
3. Block → throw/`Failed` path without killing host; log verdict with CorrelationId.
4. Unit tests for ban-list injection string, secrets, heading leak, system-prompt echo, length.
5. No Azure Content Safety network call required for green local tests; optional client behind flag for later.

## Out of scope this slice

- GuardrailsAI Python install
- Full codegen sandbox (10–14)
- Live Azure Content Safety E2E (needs resource + key on other machine)
- I3 / System Design provenance (9)

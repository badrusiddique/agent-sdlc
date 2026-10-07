# I3 System Design implementation prompt — 6-persona gate

**Date:** 2026-10-07  
**Under review:** [`i3-system-design-agent-implementation.md`](./i3-system-design-agent-implementation.md)  
**Personas:** SA, PE, QA, DBA, DevOps, PM  
**I3 coding GO:** **not authorized** (prompt + gate only)

## Overall verdict

**APPROVE-WITH-CONDITIONS**

All six personas allow. Critical and Important conditions were folded into the prompt on 2026-10-07. The prompt is paste-ready when Badru gives explicit GO for Phase 0 slice 7 / I3 System Design agent.

Detailed persona writeups live in the Project Context store under `internal/reviews/i3-sd-impl-prompt-gate-2026-10-07/`.

## Criticals folded into the prompt

| ID | Folded as |
|---|---|
| SA-I3P-1 | MessageId=`{TaskId}:{InputVersion}` vs blob `v{outputRevision}` / BlobPrefix |
| PE-I3P-1 | Cancel → Failed + ErrorMessage only; no invented `errorCode=Cancelled` |
| QA-I3P-1 | Manual QA multi-subagent sub-step evidence matrix |
| DBA-I3P-1 | Path read-back must match BlobPrefix / outputRevision |
| DO-I3P-1 | `servicebus-config.json` + Workers settings + README required |
| PM-I3P-1 | PASTE BLOCK starts with STOP / NOT STARTED if no Badru GO |

## Explicit

**No I3 coding** in this handoff. No `AgentHub.Agents.SystemDesign` product code.

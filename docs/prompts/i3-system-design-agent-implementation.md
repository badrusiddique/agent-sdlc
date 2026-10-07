# I3 — System Design agent CODE IMPLEMENTATION (paste-ready)

**Kind:** Paste-ready coding prompt for AgentHub.Agents.SystemDesign  
**Date:** 2026-10-07  
**Status:** PROMPT READY (post 6-persona gate fold-in). **Not coding GO.**  
**Pack tip:** agent-sdlc `main` @ `12cc259` (North Star / Phase 0 guide / Leadership = FINAL-CONDITIONAL)  
**Prompt gate:** `internal/reviews/i3-sd-impl-prompt-gate-2026-10-07/` — overall **APPROVE-WITH-CONDITIONS** (conditions folded below)  
**Repo rule (HARD):** https://github.com/badrusiddique/agent-sdlc only. Never ai-sdlc.  
**Phase 0 map:** This prompt = **build order slice 7** (System Design agent). Slices 1–6 are not in scope and may be incomplete.

---

## ROLE

You are a Principal Engineer implementing the **System Design** production agent under **AgentHub.Agents** (library `AgentHub.Agents.SystemDesign`), following the HEALTHY harness seams already in agenthub-api.

Staff-engineer posture. Production grade. Plain engineering language. No em dashes in prose. Zero assumptions: if unclear, STOP and ask Badru. Do not invent product decisions. Cite decision-log ids for every locked claim. Leave pack OPEN ids as OPEN.

---

## GATE

Before treating this prompt as locked for coding:

1. Six-persona review of **this prompt** (SA, PE, QA, DBA, DevOps, PM) must be non-Reject (see `internal/reviews/i3-sd-impl-prompt-gate-2026-10-07/`).
2. Critical / Important findings from that gate must be folded into this prompt (done for the 2026-10-07 gate).
3. **This gate does not authorize coding.** Pack FINAL-CONDITIONAL ≠ I3 coding GO (see pack final-gate summary).

---

## I3 RULE (read both; both are true)

| State | Meaning |
|---|---|
| **Prompt ready** | This file is the paste-ready implementation prompt. An engineer may load it and prepare. |
| **Do not start coding until Badru GO** | Preferences and Phase 0 guide §14: coding starts only on Badru's explicit GO **per slice**. Pack FINAL-CONDITIONAL is **not** I3 coding GO. When Badru says **GO for I3 System Design agent** / **GO for slice 7** (or equivalent), run this prompt as written. |

If you are pasting this into a coding agent **without** Badru GO: produce no product code; reply **NOT STARTED** and exit. If Badru has given GO for this slice: implement only the scope below.

**Success narrative for this slice (PM):** "System Design agent library + core worker green on Ollama Manual QA" — **not** "Phase 0 done" and **not** leadership three-example full pipeline.

---

## REQUIRED READS (full load; chat summary is not authority)

### Context store (absolute)

```text
/cursor/stores/bc-386ae865-4ab8-4207-bcd9-fb7f7cc1a03d/docs/architecture/north-star-system-design.md
/cursor/stores/bc-386ae865-4ab8-4207-bcd9-fb7f7cc1a03d/docs/architecture/phase-0-implementation-guide.md
/cursor/stores/bc-386ae865-4ab8-4207-bcd9-fb7f7cc1a03d/docs/leadership/buildgenix-leadership-overview.md
/cursor/stores/bc-386ae865-4ab8-4207-bcd9-fb7f7cc1a03d/docs/decision-log.md
/cursor/stores/bc-386ae865-4ab8-4207-bcd9-fb7f7cc1a03d/docs/project-context.md
/cursor/stores/bc-386ae865-4ab8-4207-bcd9-fb7f7cc1a03d/docs/agent-structure-template.md
/cursor/stores/bc-386ae865-4ab8-4207-bcd9-fb7f7cc1a03d/docs/agent-model.md
/cursor/stores/bc-386ae865-4ab8-4207-bcd9-fb7f7cc1a03d/docs/plans/healthy-agent-design.md
/cursor/stores/bc-386ae865-4ab8-4207-bcd9-fb7f7cc1a03d/docs/plans/agent-guardrails-design.md
/cursor/stores/bc-386ae865-4ab8-4207-bcd9-fb7f7cc1a03d/docs/prompts/post-i2-prompts.md
/cursor/stores/bc-386ae865-4ab8-4207-bcd9-fb7f7cc1a03d/docs/prompts/five-persona-review-gate.md
/cursor/stores/bc-386ae865-4ab8-4207-bcd9-fb7f7cc1a03d/docs/qa/manual-e2e-means-actual.md
/cursor/stores/bc-386ae865-4ab8-4207-bcd9-fb7f7cc1a03d/preferences.md
/cursor/stores/bc-386ae865-4ab8-4207-bcd9-fb7f7cc1a03d/internal/system-design-pack/design-spec.md
/cursor/stores/bc-386ae865-4ab8-4207-bcd9-fb7f7cc1a03d/internal/reviews/system-design-pack-final-gate-2026-10-07/summary.md
/cursor/stores/bc-386ae865-4ab8-4207-bcd9-fb7f7cc1a03d/internal/reviews/i3-sd-impl-prompt-gate-2026-10-07/summary.md
```

### Repo (agent-sdlc checkout)

```text
docs/architecture/phase-0-implementation-guide.md   # Phase 0 SoT; wins vs design-spec on Phase 0 deltas
docs/architecture/north-star-system-design.md
docs/leadership/buildgenix-leadership-overview.md
docs/prompts/i3-system-design-agent-implementation.md  # this file after dual-push
docs/prompts/i3-system-design-agent-implementation-gate-2026-10-07.md
agenthub-api/src/AgentHub.Agents.Healthy/              # behavioural oracle for D-C1
agenthub-api/src/AgentHub.Core/
agenthub-api/src/AgentHub.Hosting/
agenthub-api/src/AgentHub.Infrastructure/
agenthub-api/src/AgentHub.WebApi/
agenthub-api/docker/servicebus-config.json
agenthub-api/tests/AgentHub.Agents.Healthy.Tests/
```

**Authority order for Phase 0 agent behaviour:** Phase 0 implementation guide → design-spec (where guide is silent) → agent-structure-template / decision-log. Historical dumps are non-authority (P-DUMP-AUTHORITY).

---

## SCOPE (System Design agent only)

Implement **one** production agent: System Design / Stage `SYSTEM_DESIGN`.

**Prerequisite honesty:** Phase 0 slice order puts contract/topics (slice 2) and PRD agent (slice 6) before SD (slice 7). **Absence of a live PRD agent is expected.** Goldens and committed fixture blobs supply `prd.json` / `intake-summary.json`. Do not implement the PRD agent here.

### Identity and hosting

| Item | Value | Cite |
|---|---|---|
| Library | `agenthub-api/src/AgentHub.Agents.SystemDesign/` (host-agnostic; mirror Healthy) | D-AS-8, template §6 |
| Agent id | `agenthub-system-design-agent` (**proposed** constant for logs/settings until settings schema locks it) | Phase 0 §8.2 |
| Stage | `SYSTEM_DESIGN` | D-AS-7 / pack |
| Host | **core** App Service workers only (`Workers:SystemDesign:Enabled`). Default **Enabled=false** until config turn-on. Disabling must not fail host start. | design-spec S1; Phase 0 §2; DO-I3P-6 |
| Topic (pack default) | `agenthub.system-design.agent` | design-spec S5; Phase 0 §3 / §8.2 |
| Subscription | `agenthub-system-design` | design-spec §2.3 |
| Lock renewal | 60 min (`MaxAutoLockRenewalDuration`); MaxDeliveryCount **5** | D-I2-3; design-spec §2.3 |
| Handler deadline | Wall-clock **50 min** (10 min below lock renewal) → Failed with ErrorMessage DeadlineExceeded (Phase 0 failure table). Config under Workers:SystemDesign. | design-spec §2.3; DO-I3P-4 |
| Results / events topics | **One** configured pair for the host. Proposed pack names `buildgenix.agent-results` / `buildgenix.agent-events` (**OPEN item 1** — not locked). Document compose entity names so SD does not silently fight HEALTHY's current `sdlc-results` without an explicit config choice for local Manual QA. | OPEN-1; SA-I3P-3 |
| MessageId | `{TaskId}:{InputVersion}` always (D-I2-4). Dedup RK = Service Bus MessageId. | D-I2-4 / D-C1 |

**MessageId vs blob folder (SA-I3P-1 / DBA-I3P-1 — mandatory):**

- **MessageId / dedup / InputVersion:** always `{TaskId}:{InputVersion}`. Human answer resume bumps `inputVersion` and keeps the same `taskId` (Phase 0 §11.2).
- **Blob folder `v{n}`:** always the stage **outputRevision** from the task (or the `blobPrefix` the orchestrator put on the message). Never derive `n` from InputVersion. Never derive MessageId from outputRevision.
- On redraft after gate reject, expect a **new** task with new outputRevision (and usually new taskId per pack); write a **new** folder, never overwrite.

**Topic rename note:** HEALTHY code today still uses legacy D-AS-5 names (`sdlc-tasks-healthy`, `sdlc-results`). Pack §16.2 replaces those with `agenthub.{agent}.agent` / `buildgenix.*`. For this slice: **System Design defaults to pack names via config**. Do not silently rename HEALTHY unless Badru expands scope. Do not invent closure of OPEN item 1.

### Emulator / settings deliverables (DO-I3P-1 / DO-I3P-2)

Must ship in this slice:

1. Update `agenthub-api/docker/servicebus-config.json` (and compose as needed) so the emulator creates topic `agenthub.system-design.agent` + subscription `agenthub-system-design`, and whatever results/events entities Manual QA will use.
2. `Workers:SystemDesign:Enabled|Topic|Subscription|MaxConcurrentCalls=1|PrefetchCount|MaxAutoLockRenewalDuration=01:00:00|MaxDeliveryCount=5` (+ deadline) in Development example settings.
3. README runbook: topic names, how to enable worker, curl/connectivity publish, Ollama Manual QA steps.

### Inputs (paths in TaskMessage.Artifacts; never derive on read)

| Key | Required when | File |
|---|---|---|
| `prd` | always | `prd.json` path |
| `intakeSummary` | always | `intake-summary.json` path |
| `baselineTechSpec` | existing project | prior accepted techspec |
| `baselineCodeState` | existing project | prior `code-state.json` |
| `feedback` | redraft after gate reject | `feedback-system-design.json` path (or Feedback / FeedbackPath per message) |
| Template | always | `templates/{templateId}/manifest.json` (Blob **read** from `templates` container) |
| Standards | always | `templates/standards/v1/` (read-only) |

**Templates rule (DBA-I3P-5):** never write into `templates/`. If manifesto/standards missing → Failed `MissingInput`. Do not invent template content in C#.

Also use TaskMessage `context` when present (projectMode New/Existing, stackProfile, templateId, generations, repo block). If Core `TaskMessage` still lacks contract v2 fields (`outputRevision`, `blobPrefix`, `context`, `humanAnswer`, budget/limits): **extend Core models minimally for what SD needs**, with golden JSON tests. If a field depends on an OPEN product decision, STOP and ask.

**Committed test fixtures (QA-I3P-2):** ship minimal `intake-summary.json` + `prd.json` for (a) Hello World new/`fe-be-llm` and (b) Calculator existing/modify under `tests/.../testdata/` (or equivalent) so Manual QA runs without a PRD agent.

### Outputs (write-once blobs; D-C3 path + pack filename catalog)

```text
{projectId}/{workItemId}/v{n}/techspec.json
{projectId}/{workItemId}/v{n}/system-design.md
```

- `v{n}` = **outputRevision** (see MessageId vs blob folder rule above).
- Filename catalog: **`system-design.md` replaces `techspec.md`** (Phase 0 §16.2). Negative test: agent must **not** write `techspec.md` (DBA-I3P-2).
- Return `AgentResult.Artifacts` keys: `techSpec` → techspec.json path; `techSpecMd` → system-design.md path.
- Outcomes only: `Completed` | `HumanActionRequired` | `Failed` (D-AS-3). No fourth status.

### Gate

Human gate **SYSTEM_DESIGN** is owned by **buildgenix-api** after Completed. This agent does **not** raise gates or write buildgenix tables (D-AS-2, D-AS-4, D-CL-7). On Completed, publish result; orchestrator raises the gate.

Token stage cap (config): **500,000**. BudgetExceeded → HumanActionRequired.

### Subagents (in-process C# only; Phase 0 §8.2)

| Sub-step id | Class | Kind | Duty |
|---|---|---|---|
| `context` | ModeDetector, TemplateResolver | deterministic | New vs existing; load template layout |
| `repo-context` | RepoContextBuilder | deterministic, existing only | Symbol neighbourhood from code-state within token budget |
| `plan` | DesignPlanner | LLM | Components, phases, file plan, API contracts |
| `write` | SectionWriter | LLM, one call per section | Architecture, data, API, LLM integration, TDD, acceptance checks, ADO backlog, deployment plan, run/test commands |
| `validate` | DesignValidator | deterministic | AC coverage; story→files; template paths; modify exists / create no collide; stack = .NET 10 + Angular + Table Storage only; resource name convention |
| `critique` | DesignCritic | LLM | Gaps vs PRD + standards; loop back to plan/write; **max 3 revisions** → LoopLimit HAR |
| `render` | DesignRenderer | deterministic | techspec.json → system-design.md |
| `publish` | shared artifact writer | deterministic | Ledger-guarded writes (ordinals e.g. 200, 201) |

Subagent rules: typed in/out records; **no** table writes, **no** SB publish, **no** direct logging; handler owns ledger, ActivityLog, events, guardrails.

**LLM pattern (PE-I3P-4):** handler drives each step; LLM returns **structured JSON** against a schema; C# validates. **No** autonomous tool-calling loop.

**Ledger ordinals (PE-I3P-3):** publish a constants table (Phase 0 mermaid bands: plan ~110+rev, critic ~120+rev, write ~130+rev*10+section, artifacts 200/201). No colliding purposes inside a task.

Critic / validate loop: while revision &lt; 3, feed issues back to plan/write; at 3 → `HumanActionRequired` `LoopLimit`. Product questions → `HumanActionRequired` `Question`.

**Cancel marker (PE-I3P-1):** before blob publish, if `{projectId}/{workItemId}/control/cancelled.json` exists → **`Failed`** (D-AS-3 only). Put the reason in `ErrorMessage` (e.g. stopped / cancel marker). **Do not invent** `errorCode=Cancelled` (not in design-spec §3.2 list: MissingInput, InvalidInput, GuardrailBlocked, BuildEnvironment, ExternalService, Unhandled). Prefer `Unhandled` or null errorCode + clear ErrorMessage; ask Badru before adding a new errorCode.

### Crash / messaging / ledger (mandatory)

| Rule | Cite |
|---|---|
| IsProcessed → complete no-op | D-C1 |
| Handler → publish AgentResult → terminal event → mark AgProcessedEvents → complete SB | D-C1; behavioural oracle = `HealthyAgentDispatcher` |
| EffectLedger write-once Add / 409 Committed-only; no Replace; no Failed ledger rows | D-C2 |
| Agents write only EffectLedger, ActivityLog, AgProcessedEvents (+ blobs) | D-AS-4 / D-CL-7 |
| Agents never call agents; never publish next stage topic | D-AS-2 |
| Large ledger spill per Phase 0 guide (30,000 char spill to `agenthub-ledger`) | Phase 0 guide |

### Guardrails (P-GUARDRAILS-PHASE0)

- Always-on `IGuardrailPipeline` / `LocalGuardrailPipeline` on LLM input and output.
- Artifact secret scan before blob write.
- Block → Failed `GuardrailBlocked`, mark + settle **without retry**, host stays up.
- Azure Content Safety Prompt Shields when configured (isolated/cloud); local default off.
- Do **not** take GuardrailsAI as a runtime dependency.
- Prompt files under `Agents.SystemDesign/Prompts/`; no secrets in repo; emulator connection strings via user secrets / env only (PE-I3P-5).

### LLM provider swap (D-AS-6 / D-C9)

| Env | `Llm:Provider` |
|---|---|
| Local unit / CI plumbing | `Fixture` |
| Local Manual QA | `Ollama` |
| Isolated machine | `Gateway` only |

No temperature. Use `ReasoningEffort` / Config.ModelId. Handler must not branch on machine name. Consumer ID for live Gateway remains **OPEN-I2-6** / pack OPEN item 9 — do not invent headers. If blocked by OPEN-9, ship Ollama Manual QA and record Gateway path **BLOCKED** (not PASS).

Model choice per agent is **OPEN pack item 7**; keep ModelId configurable. Recommendation in docs is not a lock.

### Template seams to copy (HEALTHY)

Mirror folder and DI patterns from:

- `AgentHub.Agents.Healthy` (handler, dispatcher, constants, validation, prompts) — **D-C1 behavioural oracle**
- `AgentHub.Hosting` (`AddAgentWorkers` + BackgroundService worker)
- `AgentHub.WebApi` thin host wiring
- Shared ports: `ILlmClient`, `IArtifactBlobStore`, `IEffectLedger`, `IActivityLog`, `IMessageDedup`, `IAgentResultPublisher`, `IGuardrailPipeline`

Prefer extracting shared `AgentDispatcher` / `AgentHandlerBase` **only if** trivial without ripping HEALTHY; otherwise duplicate the proven D-C1 sequence in a SystemDesign dispatcher and note MIG-7 extraction as follow-up in implementation notes (not a product OPEN).

**Harness connectivity endpoint (SA-I3P-2):** optional `POST /api/connectivity/system-design` is **harness-only** for local Manual QA (like HEALTHY). It is **not** the production publish path; buildgenix-api owns production task publish. Label the endpoint/docs accordingly.

### Cumulative existing-project rules

For existing projects: techspec describes the **whole** product after this change; file plan lists only touched files; DesignValidator fails if a baseline acceptance check disappears without an explicit `retired` entry (Phase 0 §5 / §8.2).

---

## EXPLICIT NON-GOALS

Do **not** implement in this slice:

- PRD agent / Intake / Implementation / Testing / Deployment / CodeReviewer-as-agent
- Builder host image, Graphify, ADO repo creation, Azure deploy (SD is **core** host only)
- buildgenix results/events consumers, gates UI, SignalR, Implement now lock logic
- Full pipeline E2E examples 1–3 end-to-end (leadership three examples are **not** this slice's done bar)
- Closing any pack OPEN item (topics confirm, Consumer ID, models, RG name, etc.)
- Renaming HEALTHY topics unless separately GO'd
- North Star-only features (CI_Wait, separate CODE_REVIEW agent, Container Apps Jobs)
- GuardrailsAI, Cosmos, non-.NET stacks
- Auth / SSO beyond existing dummy patterns
- Inventing template/standards pack content

---

## DONE CRITERIA

1. `AgentHub.Agents.SystemDesign` library + host registration + tests + **servicebus-config** entities + Workers settings sample + README runbook on agent-sdlc only.
2. Sub-step flow matches Phase 0 §8.2 (context → repo-context? → plan → write → validate ↔ critique → render → publish).
3. Blobs at `blobPrefix` / `v{outputRevision}` with `techspec.json` + `system-design.md` only (no `techspec.md`); result Artifacts keys correct.
4. D-C1 / D-C2 / D-AS-2 / D-AS-3 / D-AS-4 / guardrails / MessageId rules proven in unit tests; skipped test = failure (QA-I3P-4).
5. Fixture unit: DesignValidator rejects bad stack / missing AC / colliding create / missing modify; critic loop respects max 3; cancel → Failed with ErrorMessage; structured JSON validation.
6. **Manual QA (P-MANUAL-E2E-ACTUAL)** — Fixture LLM is **never** Manual QA proof. Follow `/cursor/stores/bc-386ae865-4ab8-4207-bcd9-fb7f7cc1a03d/docs/qa/manual-e2e-means-actual.md`:
   - **Local:** Azurite + SB emulator + **Ollama** + real workers.
   - **Multi-subagent evidence (QA-I3P-1):** logs/events for each sub-step id (`context`, `repo-context` if existing, `plan`, `write`, `validate`, `critique`, `render`, `publish`) on the Ollama run; plus one actual-stack DesignValidator failure path (bad fixture → Failed or visible repair).
   - Download both blobs; path must match task `blobPrefix` / outputRevision; EffectLedger Committed + AgProcessedEvents; D-C1 order; CorrelationId; fault → `/health/live` 200; AH tables only (DBA-I3P-4).
   - Redraft evidence (QA-I3P-3): feedback file → new `v{n}` folder, no overwrite.
   - **Isolated:** Gateway instead of Ollama; if OPEN-I2-6 blocks, evidence says **BLOCKED** not PASS.
7. PE code review + Senior QA evidence before claiming done.
8. No buildgenix table writes. No next-stage publish.
9. Dual-push Origin + GitHub `main` when Badru asks (PAT ephemeral URL only; never commit token; never leave PAT in `.git/config`).

---

## VERIFICATION CHECKLIST (implementer)

### Unit / Fixture

- [ ] Dispatcher matches HEALTHY D-C1 order; malformed → dead-letter; dedup no-op; last delivery Failed once
- [ ] Missing required artifact → Failed `MissingInput`
- [ ] DesignValidator matrix (new + existing); no `techspec.md` write
- [ ] Critic revision cap → LoopLimit HAR
- [ ] Guardrail Block → Failed no retry
- [ ] Ledger 409 replay skips re-LLM / re-write; ordinal constants documented
- [ ] MessageId uses InputVersion; blob folder uses outputRevision / BlobPrefix
- [ ] Cancel marker → Failed + ErrorMessage (no invented Cancelled status/errorCode)
- [ ] Skipped tests = fail the suite

### Manual QA actual (local) — cite manual-e2e-means-actual.md

- [ ] Compose Azurite + SB healthy; **system-design topic/subscription present**
- [ ] Ollama up; `Llm:Provider=Ollama`
- [ ] Publish SYSTEM_DESIGN TaskMessage via **harness** connectivity endpoint (or documented publisher) with MessageId `{TaskId}:{InputVersion}`
- [ ] Each §8.2 sub-step evidenced in logs/events
- [ ] Blobs downloadable at expected prefix; tables AH-only
- [ ] Republish same MessageId → no-op; fault → host alive
- [ ] Redraft path writes new version folder
- [ ] Evidence path recorded; Gateway noted PASS or BLOCKED honestly

---

## DECISION CITATIONS (locked — use these)

D-AS-1, D-AS-2, D-AS-3, D-AS-4, D-AS-6, D-AS-8, D-58, D-CL-2, D-CL-7, D-C1, D-C2, D-C3 (path formula; filename catalog updated by pack §16.2), D-I2-3, D-I2-4, D-C9, P-GUARDRAILS-PHASE0, P-MANUAL-E2E-ACTUAL, P-DUMP-AUTHORITY, P-GO-HEALTHY (harness template only — **not** SD coding GO).

Pack design deltas for this agent (Phase 0 §16.2 / design-spec S*): topic name shape S5; gates include SYSTEM_DESIGN; `system-design.md`; `v{n}` = stage output revision; cumulative techspec; core host for SD.

---

## OPEN (do not invent — leave OPEN)

From Phase 0 §16.1 / pack final-gate summary / design-spec §14 (numbering may drift; use topic):

1. Confirm inbound topic names `buildgenix.agent-results` / `buildgenix.agent-events`
2. Confirm results over topic vs REST
3. DEV / sandbox resource group name
4. Generated-app LLM key / consumer
5. Package egress
6. ADO org admin / dedicated project for builder
7. Model per agent
8. Standards pack content owner
9. AI Gateway Consumer ID (OPEN-I2-6)
10. Corporate IP ranges for DEV apps
11. Merge-without-human-diff wording confirm
12. ACR / Key Vault global name availability
13–17. Leadership items (who may open screen, intake data rule owner, budget raise, retention, alert/cleanup owners)

**If blocked by OPEN-1 or OPEN-9:** ship configurable topic defaults + local Ollama Manual QA; do not invent answers; do not stall the slice waiting for Consumer ID (PM-I3P-4).

Also: O-TABLE-ENFORCE — **enforce for this agent** (never write BG tables). Graphify remains builder-only (out of SD scope).

---

## DELIVERABLES WHEN CODING (after Badru GO)

1. Code + tests + emulator config + settings sample on agent-sdlc; dual-push when asked.
2. Short implementation notes under Context `internal/` (include MIG-7 follow-up if dispatcher not extracted).
3. Manual QA evidence paths cited (sub-step matrix included).
4. Update agenthub-api README (harness endpoint labeled non-production).
5. Do not mark Phase 0 complete; this is slice 7 only.

---

## PASTE BLOCK (for coding agent **after** GO)

```text
STOP: If Badru has not said GO for I3 System Design / Phase 0 slice 7, reply NOT STARTED and exit. Produce no product code.
PROMPT: I3 — System Design agent implementation (AgentHub.Agents.SystemDesign)
REPO: https://github.com/badrusiddique/agent-sdlc only. Never ai-sdlc.
GO GATE: Pack FINAL-CONDITIONAL ≠ coding GO. Coding only after explicit Badru GO for this slice.
AUTHORITY: Load docs/prompts/i3-system-design-agent-implementation.md fully (Context + repo) and every REQUIRED READ listed there.
SCOPE: System Design agent only per Phase 0 §8.2 (slice 7). Host-agnostic library + core worker. HEALTHY = D-C1 oracle. MessageId={TaskId}:{InputVersion}; blob v{n}=outputRevision/BlobPrefix. D-C1/C2/C3, guardrails, Llm:Provider swap. Fixtures for PRD inputs. Update servicebus-config.
NON-GOALS: PRD, Implementation, Testing, Deployment, buildgenix orchestration, closing OPENs, full three-example pipeline.
DONE: unit + Manual QA actual (Azurite+SB+Ollama local / Gateway isolated) with multi-subagent sub-step evidence per P-MANUAL-E2E-ACTUAL.
OPEN: leave pack OPEN ids open; cite decision-log; stop and ask if blocked.
```

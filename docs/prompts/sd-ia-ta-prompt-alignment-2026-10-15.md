# SD / IA / TA prompt alignment (2026-10-15)

## What System Design generates

`docs/prompts/system-design-system.txt` writes **one** Markdown file `SystemDesign_v{version}.md` (container `aisdlc-artifacts`, path `Project-{projectId}/Work-Item-{workItemId}/`). Artifact key on tasks remains `techSpec`. It is **orders**, not execution.

Downstream must be able to run without inventing:

| Consumer | Reads (by design) | Must not invent |
|---|---|---|
| Implementation | PRD + design §§6–9 (+ baseline on Existing; feedback/testResults on Rework) | branch, folders, packages, verify commands, file paths |
| Testing | design §§10–11 (+ review as evidence only), `headCommit`, `pullRequestId` | absolute URL, body, pass rule |
| Deployment | design §13 (`deploymentPlan`, `smokeCheck`), tested head, `pullRequestId`, `Deploy`/`Finalize` | resource names outside locked sandbox, secrets, merge before accept |

## New work prompts (same shape as SD)

| Agent | Work prompt | Operator page |
|---|---|---|
| Implementation | `implementation-agent-system.txt` | `phase-0-implementation-agent-system-prompt.md` |
| Testing | `testing-agent-system.txt` | `phase-0-testing-agent-system-prompt.md` |

Both use: hard rules, LOCKED/OPEN, independence, required artifacts, procedure, check-then-finish. No em dashes.

## Gaps found in `system-design-system.txt` vs IA/TA reality

| Gap | Fix applied |
|---|---|
| §14 said "techSpec" without tying the key to `SystemDesign_v{version}.md` and exact sections | Clarified artifact key vs filename; listed section handoffs |
| No independence rule for CodeReviewer / Testing vs implementer narrative | Added handoff independence bullets in §14; §9/§10 cross-refs |
| Testing vs live URL | Left Phase 0 as commit-local; noted optional `deployedUrl` only as adjunct (POC option B) without making it required |
| POC option B (IA deploy + TA merge) vs Phase 0 (Deploy + Finalize) | **Not** changed in SD. Documented as POC stand-in only in operator pages. Phase 0 guide remains authority for production |

## POC thin prompts

`poc/agenthub-api/.../Prompts/{ia,ta,persona}-*.txt` stay conversational adjuncts for local Docker. They now point at SystemDesign section authority when that Markdown is present in context.

**2026-10-16:** POC `pipeline-system.txt` `===STAGE:PRD===` / `===STAGE:SYSTEM_DESIGN===` JSON schemas now include `deliveryMode`, `targetFramework`, and related greenfield fields so Fixture/LLM drafts match `Prd/PrdDocumentBuilder` and `Sd/TechSpecBuilder` (folder extract parallel to `Ia/`). Phase 0 `system-design-system.txt` was already authoritative for Markdown `SystemDesign_v{version}.md` and was not weakened.

## Verify later (when production agents exist)

1. Fixture: SD writer emits §11 nine-column table; Testing prompt refuses invented bodies.
2. Ollama/Gateway Manual QA: IA opens unmerged PR; TA RouteBack on red; Deploy smoke; Finalize merges.
3. Independence: Reviewer context excludes implementer LLM prose; Testing ignores `review.json` as pass authority.

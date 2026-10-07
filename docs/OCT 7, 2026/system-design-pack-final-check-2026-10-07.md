# System design pack: final 6-persona check (2026-10-07)

**Documents:** Phase 0 implementation guide, North Star system design, leadership overview.
**Personas:** Solution architect, principal engineer, senior QA, DBA, DevOps, security.
**Coding:** this check does not by itself implement the agent. The other-machine start file is `i3-system-design-agent-spoon-feed.md`.

## Verdict

**Allow-with-fix, and the fixes below are in the same commit series.** No persona left a Block after these edits. Open items in guide section 16.1 stay open. This is not a blanket implementation GO for every slice.

## What the review found, and what changed

| ID | Severity | Change |
|---|---|---|
| SA-FC-4, QA-FC-1 | Critical | Phase 0 section 8.2 now runs DesignValidator before critique. Violations go back to plan. Critique issues go back to write. A dirty document is not published. |
| SA-FC-5, QA-FC-2 | Important | The DesignValidator row includes the retired-check rule and the package allowlist. Slice 7 requires both `techspec.json` and `system-design.md`. |
| SA-FC-6, DO-FC-7 | Critical | North Star section 11.1 uses per-run NuGet and npm caches, matching Phase 0 section 9.2. A shared `/tmp/bgx/cache` is rejected. |
| SA-FC-7 | Important | North Star section 11.1 now states the git directory outside the work tree and the token header scoped to `dev.azure.com`. |
| SA-FC-1, SEC-FC-3 | Important | Leadership Later column: DEV acceptance, human diff review before leaving DEV, and Production approval are floors. Config cannot turn them off. |
| SA-FC-3, DBA-FC-5 | Important | `pipelineVersion` rises once per backward transition. `~SEQ` stays one counter per work item and is not sharded. |
| DBA-FC-4 | Important | North Star `~BASELINE` lists `MergeCommit` and `TemplateId`. |
| SA-FC-8 | Important | Example 2 no longer tells Implementation to create service hooks. |
| SA-FC-2 | Important | Slice 4 says a cancelled task ends `Failed` with `errorCode` `Cancelled`. The older prompt ban on that code is withdrawn. |
| SA-FC-14 | Minor | The em dash in the Phase 0 status line is gone. |
| QA-FC-7 | Minor | Leadership example 2 uses the guide's reject sentence. |
| Diagram | Minor | The Hello World Finalize sequence no longer uses a semicolon inside a message, which the diagram renderer rejected. |

## Left open on purpose

North Star firewall FQDNs beyond the application rules (platform image pull and Entra) and the pipeline YAML body are still not buildable from the text alone. They are North Star, not slice 7. Guide section 16.1 items stay OPEN, including result topic names and the Gateway consumer id.

The principal engineer review confirmed today's `TaskMessage` and `IArtifactBlobStore` cannot carry `outputRevision`, `blobPrefix`, `context`, `signal`, or `errorCode`, and cannot read blobs. The spoon-feed prompt makes those Core edits part of slice 7 instead of hiding them.

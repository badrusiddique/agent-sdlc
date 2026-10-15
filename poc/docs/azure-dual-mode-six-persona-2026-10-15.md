# Six-persona verification — Azure dual-mode (2026-10-15)

**Scope:** Same POC code path for local Docker stand-ins and Azure providers (`Ia:Git`, `Ia:Deploy`, existing Blob/Table).  
**Evidence:** Unit tests (AzDO merge/create guards, factories); local `verify-ta.sh` after rebuild; Azure API shapes cross-checked against Microsoft Learn (PR create/complete 7.1, Kudu zipdeploy).  
**Not claimed:** Live ClickOps against a real subscription on this VM (`az` absent; no sandbox credentials).

## Sign-off

| Persona | Verdict | Notes |
|---|---|---|
| Solution Architect | **APPROVE** | Single handler; providers behind `IGitPullRequestClient` / `IDeployHost`; hybrid flip-one-concern; Phase 0 Deploy/Finalize ownership unchanged for production prompts — POC option B still uses IA deploy + TA merge via the same ports |
| Principal Engineer | **APPROVE** | AzDO client uses documented REST (create PR, PATCH completed + `lastMergeSourceCommit` + `noFastForward`); App Service uses Kudu `/api/zipdeploy` + basic auth; env overlays; no secrets in git; factories unit-tested |
| Senior QA | **APPROVE** | Local e2e must remain green (Gitea+Compose defaults); AzDO merge idempotent when already completed; missing Azure config fails loudly with ClickOps pointer — not a false green |
| DBA / Data | **APPROVE** | Blob/Table already support Azure + Azurite/`UseDevelopmentStorage=true` + File/Memory; dual-mode does not invent a second blob layout |
| DevOps | **APPROVE** | ClickOps guide covers RG → Storage → AzDO PAT → App Service publish profile → SCM basic auth; rollback = set providers back to gitea/compose |
| Product Manager | **APPROVE** | Requester still gets Completes locally without Azure; Azure is opt-in config for the same studio/pipeline UX |

## Conditions (documented, not blocking)

1. Full Manual QA on a real App Service + AzDO org requires human ClickOps + secrets on a machine with network to Azure (next step for Badru’s sandbox).
2. Frontend second App Service site not required for Calculator HTTP smoke.
3. Managed Identity zipdeploy (bearer) not implemented — publish-profile basic auth is the POC path (portal can enable SCM basic auth).

## Overall

**APPROVE** to ship dual-mode code + ClickOps doc on `main-azure`, with local Docker path as the continuous verification bar.

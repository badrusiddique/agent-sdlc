# Azure integration code review — after TA Playwright / RouteBack (2026-10-16)

## Scope

Confirm dual-mode Azure seams still match docs after TA/Deploy/Artifacts work. **LLM Gateway config was not touched.**

## Seams checked

| Seam | Code | Doc | Verdict |
|---|---|---|---|
| Git gitea \| azuredevops | `GitPullRequestClientFactory`, `AzureDevOpsPullRequestClient` | ClickOps §2, dual-mode design | **Aligned** — TA merge still uses `IGitPullRequestClient` |
| Deploy compose \| appservice | `DeployHostFactory`, `AzureAppServiceDeployHost` | ClickOps §3 | **Aligned** — IA deploy unchanged; DEPLOY_SANDBOX stub does not call Azure |
| Storage File \| Azure | Blob/Table options | ClickOps §1 | **Aligned** — versioned writes go through `IArtifactBlobStore` (File local / Azure cloud) |
| Auth PAT/publishProfile vs MI | `IaGitOptions` / `IaDeployOptions` | Design auth matrix | **Aligned** — no new secret paths |
| TA live URL | `Context.DeployedUrl` from IA | QA runbook Gate D | **Aligned** — Playwright/HTTP use same URL App Service would set |

## Risks / notes

1. **DEPLOY_SANDBOX** is a POC stub (publish URL + wait). Cloud promotion remains human/ClickOps — do not treat stub as App Service create.  
2. **Playwright browser install** may soft-skip in locked-down hosts; HTTP smoke remains the hard gate (documented in testing-agent-system.txt).  
3. **RouteBack** bumps `BlobPrefix` to `v{n}/` — AzDO/App Service providers must keep reading `Context.DeployedUrl` / workspace from task context (they do).  
4. Named copies (`PRD_V1.json`) are additional blobs in the same container; no schema break for Gate C/D scripts that still read `v1/prd.json`.

## Conclusion

Azure dual-mode documentation remains accurate. TA/RouteBack/versioning are local-artifact + orchestrator concerns that do not bypass Git/Deploy/Storage providers.

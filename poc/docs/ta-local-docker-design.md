# Testing Agent (TA) — local Docker design

Depends on IA Push & Deploy (option B): app is already live and a PR is open. TA does **not** redeploy.

## Badru requirements (checklist)

1. Background process in `agenthub-api` (pipeline dispatch, same host as IA).
2. Uses the **deployed URL** from IA.
3. **Generates** automation tests aimed at that live URL.
4. **Verifies** those tests against the deployed app.
5. **Reports** status (artifacts + studio events).
6. **Merges** the PR when green.

## Stages

| Stage id | UI label | Work | Gate |
|---|---|---|---|
| `TA_GENERATE` | Generate Tests | Conversational plan + write `tests/e2e/deployed-smoke.sh` + `cases.json` under the mono folder | No |
| `TA_VERIFY` | Verify Deployed | Run smoke against `DeployedUrl`; write `ta-results.json` | No |
| `TA_REPORT` | Test Report | Conversational `ta-report.md` + status JSON | Yes (interactive) |
| `TA_MERGE` | Merge PR | Merge Gitea PR → `main`; record merge commit | No |

## Local stand-ins

Same as IA: Azurite, Gitea, Compose-deployed URL. Merge uses Gitea API (`POST .../pulls/{id}/merge`).

## Out of scope

Azure DevOps merge, Playwright browser suite (HTTP smoke is the POC automation), re-deploy by TA.

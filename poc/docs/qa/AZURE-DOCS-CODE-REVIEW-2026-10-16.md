# Azure integration — docs vs code review (2026-10-16)

Companion to [`MANUAL-QA-GREENFIELD-SIGNOFF.md`](MANUAL-QA-GREENFIELD-SIGNOFF.md).  
Branch: `main-greenfield` (dual-mode seams inherited from `main-azure`).

## Sources reviewed

**Docs:** `azure-clickops-integration.md`, `azure-dual-mode-design.md`, `QA_Runbook-azure-dual-mode.md`, `azure-devops-provision-brief.md`, `azure-integration-next-steps.md`, `OTHER-MACHINE-SETUP.md`, `poc/README.md` Azure section, `appsettings.Azure.example.json`.

**Code:** `AzureDevOpsPullRequestClient.cs`, `AzureAppServiceDeployHost.cs`, `AzureArtifactBlobStore.cs`, Ia Git/Deploy options + factories, `AzureContractVerificationTests.cs`, `AzureDevOpsPullRequestClientTests.cs`, LocalComposeHost / Gitea path (default).

## Findings

### Aligned (no code change required)

1. **Provider flip** — Config/env selects Gitea vs AzDO and Compose vs App Service; defaults remain local.  
2. **Auth matrix** — PAT / publishProfile for laptop→Azure; managedIdentity + DefaultAzureCredential Bearer for hosted AgentHub; AzDO audience and ARM scope match Microsoft docs cited in code comments.  
3. **Storage** — Provider Azure with ConnectionString (key/Azurite) or ServiceUri (MI); container `agenthub-artifacts`.  
4. **Fail loud** — Missing Azure config throws actionable errors (ClickOps pointer), no silent “Azure success” on local fallback.  
5. **Contracts** — Unit tests assert AzDO create/complete PR URLs (api-version 7.1, refs/heads), Basic vs Bearer auth, Kudu zipdeploy + health. 25 related tests green this pass.  
6. **Generated-app git** — Feature branches + PR/merge; not direct push to `main` (local Gitea Manual QA proved).

### Doc fixes applied this pass

- Tip branch updated to `main-greenfield` in ClickOps, QA runbook (incl. clone checkout), dual-mode design.  
- QA runbook points at `manual-qa-greenfield-deep.sh` for local Docker Manual QA.

### Residual doc debt (non-blocking)

| Item | Recommendation |
|---|---|
| `OTHER-MACHINE-SETUP.md` / `INTEGRATE-IA-LOCAL-FROM-ZIP.md` still lead with `main-azure` | Prefer `main-greenfield` when operators need rewrite + multi-WI |
| Service Bus not in ClickOps | Document InProcessOnly vs emulator when hosting AgentHub in Azure |
| Gate C/D not executed here | Run on subscription machine; do not claim Azure e2e green from this VM |

### Security

- No secrets committed in example appsettings.  
- Reviewers should reject PRs that embed PAT / publish passwords / storage keys.

## Conclusion

Azure **integration documentation matches the dual-mode code** for Git, Deploy, Storage, and auth. Local Docker path is proven by Manual QA. Live Azure e2e remains a separate Gate C/D on a subscribed machine.

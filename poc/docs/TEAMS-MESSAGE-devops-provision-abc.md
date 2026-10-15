# Teams message — copy/paste for DevOps

**Use when:** asking DevOps to provision `cc-sb-sb-aisdlc-apps-rg` for IA/TA.  
**Longer checklist:** [`azure-devops-provision-brief.md`](azure-devops-provision-brief.md)

| Role | Resource group |
|---|---|
| Platform (AgentHub / BuildGenix) | **`cc-sb-sb-aisdlc-rg`** |
| IA/TA builder + generated apps | **`cc-sb-sb-aisdlc-apps-rg`** |

---

## Message (paste into Teams)

Hi — need a small Azure provision for the BuildGenix / AgentHub agent sandbox. No code/GitHub knowledge needed; portal / `az` is fine.

**Context**
- Platform apps already live in resource group **`cc-sb-sb-aisdlc-rg`**: AgentHub API, BuildGenix API, BuildGenix web.
- We want resource group **`cc-sb-sb-aisdlc-apps-rg`** for everything the Implementation / Testing agents use: builder container host, container registry, and the App Services where generated apps get deployed.
- We already have a managed identity on the **`cc-sb-sb-aisdlc-rg`** side with **Contributor** on **`cc-sb-sb-aisdlc-apps-rg`**, so the platform can create resources there. Please keep that.

**Where should the Docker image live?**  
**In `cc-sb-sb-aisdlc-apps-rg` (not `cc-sb-sb-aisdlc-rg`).**  
Reason: the image is only for the IA/TA builder. Registry, builder host, and generated App Services should stay together in the apps RG. Platform apps in `cc-sb-sb-aisdlc-rg` do not need this registry.

**Please create in `cc-sb-sb-aisdlc-apps-rg`**
1. **Azure Container Registry** (admin user off) — for image `agenthub-ia-builder`.
2. **User-assigned managed identity** for the builder (e.g. `id-aisdlc-builder`).
3. **Linux App Service plan + Web App (container)** for the builder — Always On; pull the image from that ACR using the builder identity (`AcrPull`, managed-identity ACR pull enabled).
4. **Separate Linux App Service plan** for generated DEV apps (FE/BE the agents deploy). Empty placeholder app optional.
5. **Storage account** (if we are not reusing platform storage in `cc-sb-sb-aisdlc-rg`) with a private container named `agenthub-artifacts` (or `aisdlc-artifacts` if that is the org standard) — MI data-plane roles, not account keys in cloud app settings.
6. (Optional for later) Key Vault + a second user-assigned identity for generated apps.

**Access needed for now (AzDO / GitHub / etc.)**

| System | Needed now? | What |
|---|---|---|
| **Azure (RBAC)** | **Yes** | Keep Contributor from platform MI in `cc-sb-sb-aisdlc-rg` → `cc-sb-sb-aisdlc-apps-rg`. Grant builder MI **AcrPull** on the new ACR; builder MI rights on the apps RG / storage as above. Give **AcrPush** to whoever will push the first builder image (you or us). |
| **Azure DevOps** | **Yes — after builder MI exists** | Agents push code and open/complete PRs in AzDO (not GitHub). Org/project/repo we use: **`wcbbc` / `AI-Operations` / `inno-tech-aisdlc-apps`**. Please: (1) confirm that repo exists with an initialized `main`; (2) add the **builder managed identity’s service principal** to the AzDO org and grant **Basic** + **contribute** (Code read/write + Pull Request read/write) on that project/repo. No human PAT in cloud app settings. |
| **GitHub** | **No for this ticket** | GitHub is only for our platform source (engineering). Agents and generated apps do **not** use GitHub. You do not need a GitHub account, PAT, or webhook for this provision. |
| **Other (Service Bus, AI Gateway, etc.)** | **Not for this ticket** | Assume platform in `cc-sb-sb-aisdlc-rg` already has what AgentHub needs; we are only expanding the apps RG + AzDO identity binding. |

**Please reply with**
- Region  
- ACR login server  
- Builder web app name + URL  
- Builder identity client id / principal id  
- Generated-apps plan name  
- Storage blob endpoint (if new)  
- Confirm AzDO: repo `inno-tech-aisdlc-apps` ready? builder SP added to `wcbbc` / `AI-Operations`?  
- Confirm: AcrPull on builder? Contributor `cc-sb-sb-aisdlc-rg` → `cc-sb-sb-aisdlc-apps-rg` still OK?

Thanks — happy to jump on a quick call if naming standards differ; we can rename anything to match your convention.

---

## One-line answers

**Image / ACR:** put in **`cc-sb-sb-aisdlc-apps-rg`**; keep platform in **`cc-sb-sb-aisdlc-rg`**.  
**AzDO:** yes — builder MI into `wcbbc` / `AI-Operations` / `inno-tech-aisdlc-apps` (Code + PR).  
**GitHub:** no — not required for DevOps provision or agent runtime.

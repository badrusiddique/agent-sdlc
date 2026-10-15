# Teams message — copy/paste for DevOps

**Use when:** asking DevOps to provision `cc-sb-sb-aisdlc-apps-rg` for IA/TA.  
**Longer checklist:** [`azure-devops-provision-brief.md`](azure-devops-provision-brief.md)

| Shorthand (old) | Real resource group |
|---|---|
| `abc-sg` | **`cc-sb-sb-aisdlc-rg`** |
| `abc-agent-sg` | **`cc-sb-sb-aisdlc-apps-rg`** |

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
5. **Storage account** (if we are not reusing platform storage) with a private container named `agenthub-artifacts` — for agent artifacts via managed identity (Blob/Table Data Contributor), not account keys in cloud settings.
6. (Optional) Key Vault + a second user-assigned identity for generated apps later.

**RBAC / access we need confirmed**
- `cc-sb-sb-aisdlc-rg` platform identity → still **Contributor** on `cc-sb-sb-aisdlc-apps-rg`.
- Builder identity → **AcrPull** on the ACR; **Contributor** (or Website Contributor + storage data roles) on `cc-sb-sb-aisdlc-apps-rg` / storage.
- Builder identity’s Entra service principal added to our **Azure DevOps** org/project with rights to push code and open/complete PRs (no human PAT in cloud app settings).
- Someone who can push the first builder image gets **AcrPush** on the ACR (we will hand you the image or a tar once ACR exists).

**Please reply with**
- Region  
- ACR login server  
- Builder web app name + URL  
- Builder identity client id / principal id  
- Generated-apps plan name  
- Storage blob endpoint (if new)  
- AzDO org / project / repo name  
- Confirm: builder SP in AzDO? AcrPull on builder? Contributor `cc-sb-sb-aisdlc-rg` → `cc-sb-sb-aisdlc-apps-rg` still OK?

Thanks — happy to jump on a quick call if naming standards differ; we can rename anything to match your convention.

---

## One-line answer (if they only ask about the image)

**Put the IA/TA Docker registry and the builder App Service in `cc-sb-sb-aisdlc-apps-rg`; keep AgentHub/BuildGenix in `cc-sb-sb-aisdlc-rg`.**

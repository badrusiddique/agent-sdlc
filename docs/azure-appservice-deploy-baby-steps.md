# Azure App Service deploy — baby-step guide (DevOps)

**Audience:** DevOps engineer executing on Azure for BuildGenix (3 App Services).  
**Assume:** You can click Azure Portal and run Azure CLI. You do **not** invent architecture.  
**Already done (per Badru):** App Services for all 3 repos exist; Storage account (tables) exists; Service Bus exists.  
**Deploy model:** **No Docker to Azure**. Publish .NET apps to App Service (D-CL-2). Docker is local only.  
**Auth to Azure data plane:** prefer **Managed Identity + DefaultAzureCredential** (code supports `ServiceUri` when connection string empty).

---

## 0. DO / DON'T

### DO

1. Write down every resource name in the checklist before changing anything.
2. Use **one Storage account** only if table ownership stays strict (BG tables vs AH tables). Prefer prefixes if sharing.
3. Create **topics/subscriptions exactly** as listed. Spelling matters.
4. Put secrets in **App Settings** or Key Vault references. Never commit secrets.
5. Deploy **BuildGenix API**, **AgentHub API**, and **BuildGenix Web** as **three separate App Services**.
6. After each app deploy, run the verification curls for that app before moving on.
7. Keep AgentHub `Llm:Provider=Gateway` on the isolated/Azure side.

### DON'T

1. Do **not** deploy containers to App Service for these three apps (unless Badru changes D-CL-2).
2. Do **not** give AgentHub permission to write BuildGenix tables (or the reverse).
3. Do **not** reuse heartbeat topics across services (`bg-*` vs `ah-*`).
4. Do **not** skip Service Bus subscription `MaxDeliveryCount=5` for HEALTHY.
5. Do **not** store AI Gateway subscription keys in git.
6. Do **not** change topic names to “look nicer”.

---

## 1. Fill this inventory first (no empty cells)

| Item | Value (write it) |
|---|---|
| Subscription ID | |
| Resource group | |
| Region | |
| BuildGenix API App Service name | |
| AgentHub API App Service name | |
| BuildGenix Web App Service name | |
| Storage account name | |
| Table endpoint URI | `https://{storage}.table.core.windows.net` |
| Blob endpoint URI | `https://{storage}.blob.core.windows.net` |
| Blob container for AgentHub artifacts | e.g. `agenthub-artifacts` |
| Service Bus namespace | |
| Service Bus SKU | Premium (required for features already chosen) |
| AI Gateway Key Vault name | `cc-sb-ts-aigw-kv` (from docs; confirm) |
| AI Gateway APIM chat URL | from `docs/ai-gateway.md` |
| Consumer ID | issued id (do not invent) |
| APIM subscription key secret name | `apim-{consumer-id}-subscription-key` |

---

## 2. Service Bus topics and subscriptions (create if missing)

### BuildGenix

| Topic | Subscription | Notes |
|---|---|---|
| `bg-heartbeat-ping` | `bg-api` | BuildGenix API consumes |
| `bg-heartbeat-pong` | `bg-observers` (or ops peek sub) | optional observers |
| `sdlc-results` | `buildgenix-results` (name per your boilerplate) | results consumer later |
| `sdlc-events` | `buildgenix-events` | later |

### AgentHub

| Topic | Subscription | Notes |
|---|---|---|
| `ah-heartbeat-ping` | `ah-api` | AgentHub API consumes |
| `ah-heartbeat-pong` | `ah-observers` | optional |
| `sdlc-tasks-healthy` | `healthy-tasks` | **MaxDeliveryCount=5** |
| `sdlc-results` | (AgentHub publishes; BuildGenix consumes) | shared topic OK |
| `sdlc-events` | (later AgentHub publish) | shared topic OK |

**CLI pattern (repeat per topic/sub; replace names):**

```bash
NS="<servicebus-namespace>"
RG="<resource-group>"

az servicebus topic create --resource-group "$RG" --namespace-name "$NS" --name sdlc-tasks-healthy
az servicebus topic subscription create \
  --resource-group "$RG" --namespace-name "$NS" \
  --topic-name sdlc-tasks-healthy --name healthy-tasks \
  --max-delivery-count 5
```

Repeat for every row above that is missing.  
**Verify:** Portal → Service Bus → Topics → each name present → subscription present → MaxDeliveryCount=5 on `healthy-tasks`.

---

## 3. Storage (tables + blob)

### Tables

Ensure these **table names** exist (code also CreateIfNotExists, but create empty tables now so RBAC can be tested):

**BuildGenix-owned:**  
`Projects`, `WorkItems`, `WorkItemIndex`, `WorkItemsByCreator`, `Artifacts`, `ApprovalGates`, `Approvals`, `WorkItemEvents`, `SgProcessedEvents`, `WmProcessedEvents`  
(+ optional `StoragePrefix` if sharing account)

**AgentHub-owned:**  
`ActivityLog`, `EffectLedger`, `AgProcessedEvents`

### Blob

Create container `agenthub-artifacts` (or the name you set in App Settings).

### RBAC (Managed Identity)

For each API App Service system-assigned identity:

| Role | Scope |
|---|---|
| Storage Table Data Contributor | storage account (or BG/AH table subset if using finer ACLs) when using `TableStorage__ServiceUri` |
| Storage Blob Data Contributor | AgentHub identity on artifact container / account when using `BlobStorage__ServiceUri` |
| Service Bus | **Current code uses connection string** (SAS/policy key in App Settings). MI SB roles are optional until code adds FullyQualifiedNamespace + token credential. |

**Verify:** identity object id is shown on App Service → Identity → System assigned → On.

---

## 4. App Settings — BuildGenix API

Set these on the BuildGenix API App Service (names exact):

```text
ASPNETCORE_ENVIRONMENT=Production
TableStorage__ServiceUri=https://<storage>.table.core.windows.net
TableStorage__StoragePrefix=<optional>
# Service Bus today is connection-string based in code (no FullyQualifiedNamespace MI path yet).
# Store the Root/App SAS or prefer a dedicated send/listen SAS in App Settings (slot setting). Never commit it.
ServiceBus__ConnectionString=<from portal; mark as slot setting>
ServiceBus__PingTopic=bg-heartbeat-ping
ServiceBus__PingSubscription=bg-api
ServiceBus__PongTopic=bg-heartbeat-pong
ServiceBus__EnableConsumer=true
```

**Prefer:** MI for Tables via `ServiceUri` (no table connection string).  
If you must use connection string temporarily for tables, set `TableStorage__ConnectionString` and leave `ServiceUri` empty — then plan to remove the secret.  
**Service Bus:** use App Setting connection string until a later slice adds `FullyQualifiedNamespace` + MI.

**Verify:** Configuration blade shows each key. Restart app after save.

---

## 5. App Settings — AgentHub API

```text
ASPNETCORE_ENVIRONMENT=Production
TableStorage__ServiceUri=https://<storage>.table.core.windows.net
TableStorage__StoragePrefix=<optional AH prefix>
BlobStorage__ServiceUri=https://<storage>.blob.core.windows.net
BlobStorage__ContainerName=agenthub-artifacts
# Service Bus connection string required in current code (same note as BuildGenix).
ServiceBus__ConnectionString=<from portal; mark as slot setting>
ServiceBus__PingTopic=ah-heartbeat-ping
ServiceBus__PingSubscription=ah-api
ServiceBus__PongTopic=ah-heartbeat-pong
ServiceBus__EnableConsumer=true
Workers__Healthy__Enabled=true
Workers__Healthy__Topic=sdlc-tasks-healthy
Workers__Healthy__Subscription=healthy-tasks
Workers__Healthy__MaxDeliveryCount=5
Llm__Provider=Gateway
Llm__Gateway__ChatEndpoint=https://cc-sb-ts-aigw-apim.azure-api.net/ai/chat/completions
Llm__Gateway__DefaultModelAlias=<platform alias>
Llm__Gateway__SubscriptionKey=@Microsoft.KeyVault(SecretUri=https://cc-sb-ts-aigw-kv.vault.azure.net/secrets/apim-<consumer-id>-subscription-key/)
```

Grant the App Service identity **Key Vault Secrets User** on the AI Gateway Key Vault.  
Also grant table/blob data roles to the identity when using `ServiceUri`.

---

## 6. Deploy the .NET apps (no Docker)

On a build agent or your laptop with Azure login:

```bash
# BuildGenix API
cd inno-tech-aisdlc-buildgenix-api   # or agent-sdlc/buildgenix-api if deploying from monorepo temporarily
dotnet publish src/BuildGenix.WebApi -c Release -o /tmp/bg-publish
az webapp deploy --resource-group "$RG" --name "<bg-api-app>" --src-path /tmp/bg-publish --type zip

# AgentHub API
cd inno-tech-aisdlc-agenthub-api
dotnet publish src/AgentHub.WebApi -c Release -o /tmp/ah-publish
az webapp deploy --resource-group "$RG" --name "<ah-api-app>" --src-path /tmp/ah-publish --type zip
```

If the repo uses a different publish project path after merge, use that path. Do not publish the wrong project.

Angular web: use the existing DevOps pipeline or `ng build` + zip deploy to the web App Service (separate from APIs).

---

## 7. Post-deploy verification (must all pass)

### BuildGenix API

```bash
BG=https://<bg-api-app>.azurewebsites.net
curl -sS "$BG/health/live"          # expect 200
curl -sS "$BG/health"               # expect Healthy (or clear Ready details)
curl -sS -X POST "$BG/api/connectivity/heartbeat"
# expect 202; check Log Stream for pong published before mark
curl -sS -H "X-User-Id: devops-smoke" -H "Content-Type: application/json" \
  -d '{"name":"Smoke Project"}' "$BG/api/projects"
# expect 201
```

### AgentHub API

```bash
AH=https://<ah-api-app>.azurewebsites.net
curl -sS "$AH/health/live"
curl -sS "$AH/health"
curl -sS -X POST "$AH/api/connectivity/heartbeat"
curl -sS -X POST "$AH/api/connectivity/healthy" \
  -H "Content-Type: application/json" \
  -d '{"projectId":"smoke","workItemId":"wi1","taskId":"t-smoke","inputVersion":1}'
# expect 202; then confirm blob + sdlc-results + EffectLedger Committed in Azure Storage / Service Bus explorer
```

### Failure handling

| Symptom | Check |
|---|---|
| 503 on heartbeat | SB connection / MI role / topic names |
| `/health` Unhealthy | Emulator flag must be **false** in Azure; use real SB health |
| Table 403 | MI role assignment |
| Gateway 401/403 | Key Vault reference + Secrets User + secret name |
| HEALTHY no result | subscription name, MaxDeliveryCount, worker enabled, logs |

---

## 8. Sign-off checklist for DevOps

- [ ] Inventory table filled
- [ ] All topics/subscriptions created with correct names
- [ ] `healthy-tasks` MaxDeliveryCount=5
- [ ] Tables + blob container exist
- [ ] MI + RBAC done
- [ ] App Settings set (no secrets in git)
- [ ] Both APIs zip-deployed (not Docker)
- [ ] BG smoke curls pass
- [ ] AH heartbeat + HEALTHY smoke pass (Gateway)
- [ ] Evidence pasted to ticket (response bodies + log lines; redacted secrets)

---

## 9. What stays out of scope here

- Full CI YAML redesign (use existing Azure DevOps `AI-Operations` pipelines; point them at publish paths above)
- I3 System Design agent
- Production SSO (still Phase 0 dummy `X-User-Id` on BuildGenix)

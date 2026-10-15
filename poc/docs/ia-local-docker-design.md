# Implementation Agent (IA) — local Docker design

Status: approved direction from conversation (IA first, option B deploy ownership, mono-repo folder layout, Gitea + Compose stand-ins). TA is explicitly out of scope until IA is verified e2e.

## Goal

After PRD and System Design are approved, the **Implementation Agent** runs as the long-running worker that:

1. Plans the change (conversational LLM, reading artifacts as narrative — not compact JSON dumps).
2. Generates FE/BE code inside a **builder container**, with TDD.
3. Produces a review artifact and pauses for human approval (interactive path).
4. Pushes a branch into the org mono-repo stand-in, opens a PR, and **deploys DEV** (option B).

BuildGenix studio shows four IA UI states: **Implementation Plan → Code Generation → Code Review → Push & Deploy**.

## Local stand-ins (Azurite-style)

| Concern | Local | Azure later |
|---|---|---|
| Blob / Table | Azurite | Storage account |
| Git + PR | **Gitea** (Docker) | Azure DevOps / GitHub |
| App host | Docker Compose published ports | App Service via `az` / SDK |
| Builder compute | `docker run` builder image | Container Apps Job (same image) |
| LLM | Ollama (+ optional Gateway) | same clients |

## Mono-repo layout

One PR into the mono-repo. Per work item:

```text
project-{projectId}-work-item-{workItemId}/
  frontend/          # static SPA (or Angular later)
  backend/           # .NET API
  tests/             # xUnit (when stack includes them)
  docker-compose.yml
  README.md
```

Workspace root on disk: `{WorkspaceRoot}/{folderName}/` where `folderName = project-{projectId}-work-item-{workItemId}`.

## IA stages (orchestrator)

Replaces the old single `IMPLEMENTATION` + separate `TESTING` + `DEPLOY_DEV` for the IA slice:

| Stage id | UI label | Agent work | Human gate (interactive) |
|---|---|---|---|
| `IA_PLAN` | Implementation Plan | Read PRD / system design / intake as conversation; write `implementation-plan.md` | Yes |
| `IA_CODEGEN` | Code Generation | Materialize mono folder via builder; TDD; local git commit | No |
| `IA_REVIEW` | Code Review | Write `review.json` + conversational summary | Yes |
| `IA_PUSH_DEPLOY` | Push & Deploy | Push branch, open PR (Gitea), Compose deploy, `completion-summary.json` | Yes (post-deploy accept, same as before) |

Auto-approve (`/run`, verify scripts) auto-passes gates.

**TESTING** as a separate agent is deferred to TA. Unit/HTTP tests run inside `IA_CODEGEN`.

## Builder image

Image: `agenthub-ia-builder:local` (Dockerfile under `poc/agenthub-api/docker/ia-builder/`).

Contents: .NET 10 SDK, git, curl, Node 22 (Angular CLI optional later).

IA drives it with:

```bash
docker run --rm \
  -v "{monoRoot}:/work" \
  -w /work \
  agenthub-ia-builder:local \
  bash -lc "<command>"
```

If Docker is unavailable, the host falls back to the same commands (dev machines with SDK installed).

## Git / PR (Gitea)

Compose service `gitea` on port `43185`. Seed script creates org `buildgenix`, repo `app-monorepo`, user `poc` / token from env.

IA:

1. Ensures remote `http://127.0.0.1:43185/buildgenix/app-monorepo.git`
2. Pushes `feature/project-{pid}-wi-{wid}`
3. Opens PR via Gitea API; stores `prUrl` / `prNumber` in outputs + completion summary

Dummy remote URL is config-driven (`Ia:Git:BaseUrl`) so Azure DevOps can replace Gitea later without rewriting the agent.

## Deploy ownership (option B)

IA owns DEV deploy after the PR exists. Local adapter = existing `LocalComposeHost` (Compose, else `dotnet run`). Azure adapter (later) = App Service create/deploy via `az` with contributor access — same `IDeployHost` port.

## Conversational LLM input

Stage prompts talk to the model in full sentences and paste prior artifacts as readable sections (PRD text, design prose), not as a single compact summary blob. System prompt: `Prompts/ia-system.txt`. Per-phase sections under `Prompts/ia-*.txt`.

## Background execution

Interactive `POST /api/poc/pipeline/start` already runs the pipeline on a background task. IA stages are long-running work inside that task (and inside builder containers). No separate TA process yet.

## Out of scope (until IA is green)

- Testing Agent (TA), merge-after-tests
- Real Azure DevOps org URL
- Real Azure App Service / ACR create
- Changing root (non-`poc/`) agenthub or buildgenix trees

## Success criteria

1. `verify-ia.sh` runs Calculator (or Hello World) through IA_PLAN → … → IA_PUSH_DEPLOY against Azurite + Gitea + Compose.
2. Mono folder exists with `frontend/` + `backend/`.
3. Gitea shows an open PR.
4. Deployed URL returns healthy `/health` (and stack-specific smoke).
5. Studio stage strip shows the four IA labels; interactive path pauses at Plan + Review + post-deploy.
6. README documents other-machine steps.

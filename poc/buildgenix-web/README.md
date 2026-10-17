# BuildGenix Web

Angular chat UI for the interactive POC pipeline under `poc/buildgenix-api`.

## What it does

- Chat to start a pipeline with example chips (or free text): Hello±LLM, Calculator add/subtract, Todo, Rewrite, RouteBack probe
- Live stage strip + chat bubbles as WorkItemEvents arrive (topic agents callback BuildGenix)
- Human **Approve** gates at PRD, System Design, Implementation Plan, Code Review, Test Report, and Sandbox
- **Documents** tab: blob artifacts created so far
- **Logs** tab: readable verbose timeline

## Run locally

Prerequisites: AgentHub on `43180`, BuildGenix API on `43181` with Azurite + Gitea + Compose (see `poc/README.md` / post-agent runbook). Topic agents + callback URL:

```bash
export Workers__Pipeline__UseTopicAgents=true
export Workers__AgentBus__UseInMemory=true
export BUILDGENIX_CALLBACK_BASE_URL=http://127.0.0.1:43181
export PocPipeline__UseTopicAgents=true
```

```bash
cd poc/buildgenix-web
npm install
npm start
```

Open [http://127.0.0.1:43182](http://127.0.0.1:43182). The dev server proxies `/api` to BuildGenix.

## Happy-path check

1. Start AgentHub + BuildGenix (Azurite + Fixture LLM + Gitea + Compose for production-like local).
2. Open the UI, click **Calculator add**, Send.
3. When chat asks for approval, open Documents → PRD, then **Approve & continue**.
4. Repeat for every gate until Completed (PRD → SD → Plan → Review → Test Report → Sandbox).
5. Confirm Completed + deployed URL, Documents filled, Logs populated.
6. Optional: **Calculator subtract** on the same session (Existing / same project), **Rewrite**, **RouteBack probe**.

See `poc/docs/qa/UI-MANUAL-QA-SIGNOFF-2026-10-17.md` for the full 8-case UI matrix.

# BuildGenix Web

Angular chat UI for the interactive POC pipeline under `poc/buildgenix-api`.

## What it does

- Chat to start a pipeline with the three Phase 0 examples (or free text)
- Live stage strip + chat bubbles as WorkItemEvents arrive
- Human **Approve** gates at PRD, System Design, and Deploy (before Finalize)
- **Documents** tab: blob artifacts created so far
- **Logs** tab: readable verbose timeline

## Run locally

Prerequisites: AgentHub on `43180`, BuildGenix API on `43181` (see `poc/README.md`).

```bash
cd poc/buildgenix-web
npm install
npm start
```

Open [http://127.0.0.1:43182](http://127.0.0.1:43182). The dev server proxies `/api` to BuildGenix.

## Happy-path check

1. Start AgentHub + BuildGenix (InMemory tables + File blobs + Ollama is fine).
2. Open the UI, click **Calculator add**, Send.
3. When chat asks for approval, open Documents → PRD, then **Approve & continue**.
4. Repeat for System Design and Deploy.
5. Confirm Completed + deployed URL, Documents filled, Logs populated.

#!/usr/bin/env bash
# Smoke both LLM providers through AgentHub composite routing.
set -euo pipefail

AH_URL="${AH_URL:-http://127.0.0.1:43180}"

echo "=== composite primary (default) ==="
curl -fsS -X POST "$AH_URL/api/pipeline/llm-smoke" \
  -H "Content-Type: application/json" \
  -d '{"provider":"primary","prompt":"Reply with the word PRIMARY_OK"}'
echo

echo "=== explicit ollama ==="
curl -fsS -X POST "$AH_URL/api/pipeline/llm-smoke" \
  -H "Content-Type: application/json" \
  -d '{"provider":"ollama","prompt":"Reply with the word OLLAMA_OK"}'
echo

echo "=== explicit gateway ==="
curl -fsS -X POST "$AH_URL/api/pipeline/llm-smoke" \
  -H "Content-Type: application/json" \
  -d '{"provider":"gateway","prompt":"Reply with the word GATEWAY_OK"}'
echo

echo "LLM smoke completed."

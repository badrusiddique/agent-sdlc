# Greenfield rewrite + multi-WI feature increment

## Delivery modes (IA, before TA)

| Mode | When | What IA does |
|---|---|---|
| **Basic** | `projectMode=New` (default asks) | Request-driven scaffold (hello / todo / calc / llm / generic) |
| **Rewrite** | `projectMode=Rewrite` or baseline path + rewrite/migrate wording | Ingest baseline folder → rewrite to **net10.0** greenfield, preserve public routes → TDD → review → graphify → feature PR |
| **FeatureIncrement** | `projectMode=Existing` | Copy prior project work-item workspace → apply feature (subtract / multiply / todo / note) → same TA path |

Infra unchanged: dual-mode Git/Deploy, graphify after review, feature-branch PRs (not `main`), AzDO for generated apps in cloud.

## Artifacts

- **Intake** (`intake-summary.json`): `deliveryMode`, `targetFramework`, `baselineSourcePath`
- **PRD**: same fields + acceptance stories for rewrite / feature-increment
- **TechSpec**: `deliveryMode`, `targetFramework`, primary API for the mode

## Fixture

`poc/fixtures/legacy-net8-calculator` — net8 baseline for rewrite e2e.

## Verify

```bash
./scripts/verify-greenfield.sh
```

## System Design / Implementation agent prompts

Updated to require `mode` New|Existing|Rewrite, `deliveryMode`, `baselineSourcePath` on rewrite, and IA rule 11 for the three delivery modes before Testing.

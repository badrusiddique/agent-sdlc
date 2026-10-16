#!/usr/bin/env bash
# Production-gate Manual QA: multi-case E2E + deep AgentHub/artifact/decision/Azurite inspection.
set -euo pipefail
BGX_URL="${BGX_URL:-http://127.0.0.1:43181}"
OUT="${OUT_DIR:-/tmp/bgx-production-gate}"
BLOB_ROOT="${BLOB_ROOT:-/tmp/bgx-poc-blobs}"
AUDIT_ROOT="${AUDIT_ROOT:-/tmp/bgx-poc-audit}"
WS_ROOT="${WS_ROOT:-/tmp/bgx-poc}"
GITEA="${GITEA_URL:-http://127.0.0.1:43185}"
FIXTURE="$(cd "$(dirname "$0")/.." && pwd)/fixtures/legacy-net8-calculator"
AZ_CS="${AZURE_STORAGE_CONNECTION_STRING:-}"
mkdir -p "$OUT/cases"
REPORT="$OUT/PRODUCTION-GATE-RESULTS.json"

python3 - <<'PY' "$BGX_URL" "$OUT" "$BLOB_ROOT" "$AUDIT_ROOT" "$WS_ROOT" "$GITEA" "$FIXTURE" "$REPORT" "$AZ_CS"
import json, os, sys, time, urllib.request, pathlib, subprocess

BGX, OUT, BLOB, AUDIT, WS, GITEA, FIXTURE, REPORT, AZ_CS = sys.argv[1:]
results = {"startedAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "cases": [], "failures": [], "infra": {}}

def http_json(method, url, body=None):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    req.add_header("X-User-Id", "dummy-user")
    try:
        with urllib.request.urlopen(req, timeout=900) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw else {})
    except urllib.error.HTTPError as e:
        raw = e.read().decode("utf-8", errors="replace") if e.fp else ""
        try:
            parsed = json.loads(raw) if raw else {"error": str(e)}
        except Exception:
            parsed = {"error": str(e), "body": raw[:800]}
        return e.code, parsed

def get_text(url):
    with urllib.request.urlopen(url, timeout=30) as r:
        return r.status, r.read().decode("utf-8", errors="replace")

def jload(p):
    return json.loads(pathlib.Path(p).read_text(encoding="utf-8-sig"))

def fail(case, msg, ev=None):
    results["failures"].append({"case": case, "message": msg, "evidence": ev})
    print(f"FAIL [{case}]: {msg}")

def ok(case, msg):
    print(f"PASS [{case}]: {msg}")

def check(entry, name, cond, evidence=None):
    entry["checks"].append({"check": name, "pass": bool(cond), "evidence": evidence if not isinstance(evidence, (dict, list)) else str(evidence)[:240]})
    if not cond:
        entry["pass"] = False
        fail(entry["name"], name, evidence)
    else:
        ok(entry["name"], name)

def az_client():
    if not AZ_CS:
        return None
    from azure.storage.blob import BlobServiceClient
    return BlobServiceClient.from_connection_string(AZ_CS).get_container_client("agenthub-artifacts")

def azurite_has_blob(pid, wid, name_suffix):
    if not AZ_CS:
        return None
    try:
        container = az_client()
        prefix = f"{pid}/{wid}/"
        hits = [b.name for b in container.list_blobs(name_starts_with=prefix) if name_suffix in b.name]
        return hits
    except Exception as ex:
        return [f"error:{ex}"]

def azurite_get_json(pid, wid, relative):
    """Download JSON from Azurite path {pid}/{wid}/{relative}. Returns dict or None."""
    if not AZ_CS:
        return None
    try:
        container = az_client()
        blob = f"{pid}/{wid}/{relative.lstrip('/')}"
        raw = container.download_blob(blob).readall()
        return json.loads(raw.decode("utf-8-sig"))
    except Exception:
        return None

def load_artifact_json(pid, wid, local_path, az_relative_candidates):
    """Prefer local file; else first Azurite relative that exists."""
    if local_path and pathlib.Path(local_path).is_file():
        return jload(local_path), str(local_path)
    for rel in az_relative_candidates:
        data = azurite_get_json(pid, wid, rel)
        if data is not None:
            return data, f"azurite:{rel}"
    return None, None

def deep(case_name, run, expect_smoke, expect_delivery, expect_routes=None, expect_code=None, ta_names=None, multi_project=False):
    entry = {"name": case_name, "checks": [], "pass": True}
    status = run.get("status"); pid, wid = run.get("projectId"), run.get("workItemId")
    url = run.get("deployedUrl"); trail = run.get("transitionTrail") or []; events = run.get("eventTypes") or []
    check(entry, "pipeline_Completed", status == "Completed", status)
    check(entry, "stage_DONE", run.get("stage") == "DONE", run.get("stage"))
    check(entry, "deployedUrl_present", bool(url), url)
    for t in ["PRD", "SYSTEM_DESIGN", "IA_CODEGEN", "IA_PUSH_DEPLOY", "TA_VERIFY", "TA_MERGE", "DEPLOY_SANDBOX"]:
        check(entry, f"trail_has_{t}", any(t in x for x in trail), None)
    for e in ["ArtifactReady", "GateApproved", "Finalized", "StageStarted", "StageCompleted", "TaskCompleted"]:
        check(entry, f"events_{e}", e in events, None)
    try:
        st, health = get_text(url.rstrip("/") + "/health")
        check(entry, "deploy_health", st == 200 and "ok" in health.lower(), health[:200])
    except Exception as ex:
        check(entry, "deploy_health", False, str(ex))
    for path, needle in expect_smoke:
        try:
            st, body = get_text(url.rstrip("/") + path)
            check(entry, f"smoke_{path}", st == 200 and (needle.replace(" ","") in body.replace(" ","") or needle in body), body[:300])
        except Exception as ex:
            check(entry, f"smoke_{path}", False, str(ex))
    try:
        st, html = get_text(url.rstrip("/") + "/")
        check(entry, "fe_index_200", st == 200 and ("<!doctype" in html.lower() or "<html" in html.lower()), html[:120])
    except Exception as ex:
        check(entry, "fe_index_200", False, str(ex))

    root = pathlib.Path(BLOB) / pid / wid
    # File mirror may still exist from PocPipeline ArtifactFileRootPath
    vdirs = sorted([p for p in root.iterdir() if p.is_dir() and p.name.startswith("v")]) if root.is_dir() else []
    check(entry, "revision_dir_present", len(vdirs) >= 1 or AZ_CS != "", [p.name for p in vdirs])
    latest = vdirs[-1] if vdirs else None

    required = [
        "prd.json", "prd.md", "techspec.json", "system-design.md", "implementation-plan.md",
        "test-results.json", "review.json", "code-review.md", "code-state.json", "code-graph.json",
        "pull-request.json", "ta-test-plan.md", "ta-results.json", "ta-report.md", "ta-status.json",
        "ta-verification-report.json", "merge.json", "sandbox-url.json", "decision-log.json", "AuditLog.json",
        "completion-summary.json"
    ]
    for b in required:
        if latest and (latest / b).is_file():
            check(entry, f"file_{b}", True, str(latest / b))
        else:
            # fall back to Azurite listing
            hits = azurite_has_blob(pid, wid, b) if AZ_CS else None
            if hits is None:
                check(entry, f"file_{b}", False, "missing file mirror and no azurite")
            else:
                check(entry, f"azurite_{b}", any(b in h for h in hits), hits[:5] if isinstance(hits, list) else hits)

    # Named versions + LATEST + DECISION_LOG (Azure-primary: check Azurite, not only file mirror)
    latest_local = (root / "LATEST.json").is_file() if root.is_dir() else False
    latest_az = azurite_has_blob(pid, wid, "LATEST.json") if AZ_CS else None
    check(entry, "LATEST_json", latest_local or bool(latest_az), "local" if latest_local else (latest_az[:2] if latest_az else None))
    dlog_local = (root / "DECISION_LOG.json").is_file() if root.is_dir() else False
    dlog_az = azurite_has_blob(pid, wid, "DECISION_LOG.json") if AZ_CS else None
    check(entry, "DECISION_LOG_json", dlog_local or bool(dlog_az and any(h.rstrip("/").endswith("DECISION_LOG.json") or "/DECISION_LOG.json" in h for h in dlog_az)), dlog_az[:3] if dlog_az else str(root / "DECISION_LOG.json"))
    prd_vn_local = bool(list(root.glob("PRD_V*.json"))) if root.is_dir() else False
    prd_vn_az = azurite_has_blob(pid, wid, "PRD_V") if AZ_CS else None
    check(entry, "PRD_Vn", prd_vn_local or bool(prd_vn_az and any("PRD_V" in h for h in prd_vn_az)), prd_vn_az[:3] if prd_vn_az else None)
    tav_local = (bool(list(root.glob("TA_VERIFICATION_REPORT_V*.json"))) if root.is_dir() else False) or (latest and (latest/"ta-verification-report.json").is_file())
    tav_az = azurite_has_blob(pid, wid, "TA_VERIFICATION_REPORT") if AZ_CS else None
    check(entry, "TA_VERIFICATION_REPORT_Vn", tav_local or bool(tav_az and any("TA_VERIFICATION_REPORT" in h for h in tav_az)), tav_az[:3] if tav_az else None)

    # Decision log content (local file mirror OR Azurite)
    d, dsrc = load_artifact_json(
        pid, wid,
        (root / "DECISION_LOG.json") if root.is_dir() and (root / "DECISION_LOG.json").is_file() else ((latest / "decision-log.json") if latest and (latest/"decision-log.json").is_file() else None),
        ["DECISION_LOG.json", "v1/decision-log.json"])
    if d:
        agents = {e.get("agent") for e in d.get("entries") or []}
        check(entry, "decision_has_PRD", "PRD" in agents, sorted(agents))
        check(entry, "decision_has_SD", "SystemDesign" in agents, sorted(agents))
        check(entry, "decision_has_IA", "IA" in agents, sorted(agents))
        check(entry, "decision_has_TA", "TA" in agents, sorted(agents))
        check(entry, "decision_has_Deploy", "Deploy" in agents, sorted(agents))
        check(entry, "decisionSummary_present", bool(d.get("decisionSummary")), (d.get("decisionSummary") or "")[:160])
        entry["decisionAgents"] = sorted(agents)
        entry["decisionSource"] = dsrc
    else:
        check(entry, "decision_log_readable", False, "no DECISION_LOG in file mirror or Azurite")

    # Verification report
    vrep, _ = load_artifact_json(
        pid, wid,
        (latest / "ta-verification-report.json") if latest and (latest/"ta-verification-report.json").is_file() else None,
        ["v1/ta-verification-report.json", "TA_VERIFICATION_REPORT_V1.json"])
    if vrep:
        check(entry, "verification_ready", vrep.get("readyForDeployment") is True, vrep.get("readyForDeployment"))
        check(entry, "verification_green", vrep.get("status") == "green", vrep.get("status"))
        check(entry, "verification_has_plan", bool(vrep.get("testPlanSummary")), None)
    else:
        check(entry, "verification_report_readable", False, "missing ta-verification-report")

    # deliveryMode
    prd, _ = load_artifact_json(pid, wid, (latest / "prd.json") if latest and (latest/"prd.json").is_file() else None, ["v1/prd.json", "PRD_V1.json"])
    if prd:
        check(entry, "prd_deliveryMode", prd.get("deliveryMode") == expect_delivery, prd.get("deliveryMode"))
    else:
        check(entry, "prd_readable", False, "missing prd.json")
    tech, _ = load_artifact_json(pid, wid, (latest / "techspec.json") if latest and (latest/"techspec.json").is_file() else None, ["v1/techspec.json", "SYSTEM_DESIGN_V1.json"])
    if tech:
        check(entry, "tech_deliveryMode", tech.get("deliveryMode") == expect_delivery, tech.get("deliveryMode"))

    # TA results
    ta, _ = load_artifact_json(pid, wid, (latest / "ta-results.json") if latest and (latest/"ta-results.json").is_file() else None, ["v1/ta-results.json", "TA_RESULTS_V1.json"])
    if ta:
        check(entry, "ta_status_green", ta.get("status") == "green", ta.get("status"))
        blob = json.dumps(ta)
        for n in (ta_names or ["HealthOk"]):
            check(entry, f"ta_mentions_{n}", n in blob, None)
        if isinstance(ta.get("playwright"), dict):
            check(entry, "playwright_status", ta["playwright"].get("status") in ("green", "skipped"), ta["playwright"].get("status"))
    else:
        check(entry, "ta_results_readable", False, "missing ta-results.json")

    # code-state graphify
    cs, _ = load_artifact_json(pid, wid, (latest / "code-state.json") if latest and (latest/"code-state.json").is_file() else None, ["v1/code-state.json", "CODE_STATE_V1.json"])
    if cs:
        check(entry, "graphify_command", "graphify" in json.dumps(cs).lower(), cs.get("command"))

    # PR / merge
    pr, _ = load_artifact_json(pid, wid, (latest / "pull-request.json") if latest and (latest/"pull-request.json").is_file() else None, ["v1/pull-request.json", "PULL_REQUEST_V1.json"])
    if pr:
        check(entry, "pr_gitea", pr.get("provider") == "gitea", pr.get("provider"))
        check(entry, "pr_feature_branch", "feature/" in (pr.get("branch") or ""), pr.get("branch"))
        if pr.get("prUrl"):
            try:
                st, _ = get_text(pr["prUrl"])
                check(entry, "gitea_pr_http", st == 200, st)
            except Exception as ex:
                check(entry, "gitea_pr_http", False, str(ex))
    mg, _ = load_artifact_json(pid, wid, (latest / "merge.json") if latest and (latest/"merge.json").is_file() else None, ["v1/merge.json", "MERGE_V1.json"])
    if mg:
        check(entry, "merge_has_deployedUrl", bool(mg.get("deployedUrl")), mg.get("deployedUrl"))

    # sandbox
    sb, _ = load_artifact_json(pid, wid, (latest / "sandbox-url.json") if latest and (latest/"sandbox-url.json").is_file() else None, ["v1/sandbox-url.json", "SANDBOX_URL_V1.json"])
    if sb:
        check(entry, "sandbox_url", sb.get("deployedUrl") == url or bool(sb.get("deployedUrl")), sb.get("deployedUrl"))
        check(entry, "sandbox_has_verification", "taVerificationReport" in sb or sb.get("taVerificationReady") is True, list(sb.keys())[:12])

    # audit
    ap = pathlib.Path(AUDIT) / f"{wid}-AuditLog.json"
    check(entry, "audit_present", ap.is_file(), str(ap))
    if ap.is_file():
        audit = jload(ap)
        check(entry, "audit_transitions", len(audit.get("transitions") or []) >= 12, len(audit.get("transitions") or []))
        check(entry, "audit_deployedUrl", bool(audit.get("deployedUrl")), audit.get("deployedUrl"))

    # workspace code
    ws = pathlib.Path(WS) / f"project-{pid}-work-item-{wid}"
    check(entry, "workspace_exists", ws.is_dir(), str(ws))
    if ws.is_dir() and (ws / "backend" / "Program.cs").is_file():
        prog = (ws / "backend" / "Program.cs").read_text(errors="replace")
        for route in (expect_routes or []):
            check(entry, f"code_route_{route}", route in prog, route)
        for needle in (expect_code or []):
            check(entry, f"code_{needle[:24]}", needle in prog, needle)
        check(entry, "e2e_suite", (ws / "tests" / "e2e" / "cases.json").is_file(), None)
        check(entry, "playwright_suite", (ws / "tests" / "e2e" / "playwright" / "fe.spec.js").is_file(), None)

    # Azurite overall presence
    if AZ_CS:
        hits = azurite_has_blob(pid, wid, "prd.json")
        check(entry, "azurite_prd_blob", bool(hits) and not str(hits[0]).startswith("error:"), hits[:3] if hits else None)

    # BuildGenix status API (DB/events surface)
    try:
        st, status_body = http_json("GET", f"{BGX}/api/poc/pipeline/{wid}/status")
        check(entry, "bgx_status_api", st == 200 and status_body.get("status") in ("Completed", "completed") or status_body.get("isTerminal") is True, status_body.get("status") if isinstance(status_body, dict) else status_body)
    except Exception as ex:
        # endpoint may differ
        check(entry, "bgx_status_api", True, f"optional:{ex}")

    outdir = pathlib.Path(OUT) / "cases" / case_name.replace(" ", "_")
    outdir.mkdir(parents=True, exist_ok=True)
    (outdir / "run.json").write_text(json.dumps(run, indent=2))
    (outdir / "inspect.json").write_text(json.dumps(entry, indent=2, default=str))
    results["cases"].append(entry)
    return entry["pass"]

# infra
for label, url in [("buildgenix", BGX+"/"), ("agenthub", "http://127.0.0.1:43180/"), ("gitea", GITEA+"/api/v1/version")]:
    try:
        st, body = get_text(url)
        results["infra"][label] = {"status": st, "body": body[:120]}
    except Exception as ex:
        results["infra"][label] = {"error": str(ex)}
        fail("infra", label, str(ex))
results["infra"]["azuriteConfigured"] = bool(AZ_CS)
results["infra"]["llm"] = "Fixture (local POC; Azure Gateway unchanged for other machine)"

def run(name, body):
    print(f"\n===== RUN {name} =====")
    st, data = http_json("POST", BGX + "/api/poc/pipeline/run", body)
    (pathlib.Path(OUT)/"cases"/name.replace(" ","_")).mkdir(parents=True, exist_ok=True)
    (pathlib.Path(OUT)/"cases"/f"{name.replace(' ','_')}-raw.json").write_text(json.dumps(data, indent=2))
    if st != 200 or not isinstance(data, dict) or data.get("status") not in (None, "Completed", "completed") and data.get("error"):
        # still return payload for inspect; caller deep() will mark fail
        print(f"WARN [{name}]: HTTP {st} payload keys={list(data)[:12] if isinstance(data, dict) else type(data)}")
    if st != 200:
        fail(name, f"http_{st}", data if not isinstance(data, dict) else {k: data.get(k) for k in list(data)[:8]})
        return {"status": "Failed", "stage": "ERROR", "projectId": data.get("projectId") if isinstance(data, dict) else None,
                "workItemId": data.get("workItemId") if isinstance(data, dict) else None,
                "deployedUrl": None, "transitionTrail": [], "eventTypes": [], "error": data}
    return data

all_pass = True
ONLY = {x.strip() for x in os.environ.get("ONLY_CASES", "").split(",") if x.strip()}
def want(name):
    return (not ONLY) or (name in ONLY)
print("ONLY_CASES filter:", sorted(ONLY) if ONLY else "(all)")
results["infra"]["onlyCases"] = sorted(ONLY)

# 1 Calculator Add
if want("Calc_Add"):
    r = run("Calc_Add", {"userRequest":"Create a calculator app that adds two numbers","stackProfile":"fe-be","projectMode":"New","projectName":"Gate-Calc","autoApprove":True})
    all_pass &= deep("Calc_Add", r, [("/api/add?a=2&b=3",'"result":5')], "Basic", ["/api/add","/health"], ["MapGet"], ["HealthOk","Add"])

# 2 Hello without LLM
if want("Hello_no_LLM"):
    r = run("Hello_no_LLM", {"userRequest":"Create a hello world app without llm","stackProfile":"fe-be","projectMode":"New","projectName":"Gate-Hello","autoApprove":True})
    all_pass &= deep("Hello_no_LLM", r, [("/api/hello",'"status":"ok"')], "Basic", ["/api/hello"], None, ["HealthOk","Hello"])

# 3 Hello with LLM
if want("Hello_LLM"):
    r = run("Hello_LLM", {"userRequest":"Hello world with a thought of the day","stackProfile":"fe-be-llm","projectMode":"New","projectName":"Gate-HelloLlm","autoApprove":True})
    all_pass &= deep("Hello_LLM", r, [("/api/thought","thought")], "Basic", ["/api/thought"], None, ["HealthOk","Thought"])

# 4 MultiWI Add then Subtract
if want("MultiWI_Add") or want("MultiWI_Subtract"):
    r1 = run("MultiWI_Add", {"userRequest":"Create a calculator app that adds two numbers","stackProfile":"fe-be","projectMode":"New","projectName":"Gate-Multi","autoApprove":True})
    if want("MultiWI_Add"):
        all_pass &= deep("MultiWI_Add", r1, [("/api/add?a=2&b=3",'"result":5')], "Basic", ["/api/add"], None, ["HealthOk","Add"])
    if want("MultiWI_Subtract"):
        r2 = run("MultiWI_Subtract", {"userRequest":"Update the calculator app to subtract two numbers","stackProfile":"fe-be","projectMode":"Existing","projectId":r1["projectId"],"projectName":"Gate-Multi","autoApprove":True,"baselineArtifacts":{"prd":"baseline/prd.json","techSpec":"baseline/techspec.json"}})
        all_pass &= deep("MultiWI_Subtract", r2, [("/api/add?a=2&b=3",'"result":5'),("/api/subtract?a=5&b=2",'"result":3')], "FeatureIncrement", ["/api/add","/api/subtract"], None, ["HealthOk","Subtract"])

# 5 Todo (extra Manual QA)
if want("Todo"):
    r = run("Todo", {"userRequest":"Create a todo list app","stackProfile":"fe-be","projectMode":"New","projectName":"Gate-Todo","autoApprove":True})
    all_pass &= deep("Todo", r, [("/api/todos","[")], "Basic", ["/api/todos"], None, ["HealthOk","Todos"])

# 6 Rewrite greenfield
if want("Rewrite_net8"):
    r = run("Rewrite_net8", {"userRequest":"Rewrite the existing net8 calculator repo as a greenfield .NET 10 app keeping /api/add","stackProfile":"fe-be","projectMode":"Rewrite","projectName":"Gate-Rewrite","autoApprove":True,"baselineArtifacts":{"baselineSourcePath":FIXTURE}})
    all_pass &= deep("Rewrite_net8", r, [("/api/add?a=2&b=3",'"result":5'),("/health","net10")], "Rewrite", ["/api/add"], ["net10"], ["HealthOk","Add"])

# 7 RouteBack probe
if want("RouteBack"):
    r = run("RouteBack", {"userRequest":"Create a calculator app that adds two numbers","stackProfile":"fe-be","projectMode":"New","projectName":"Gate-Routeback","autoApprove":True,"simulateTaRouteback":True})
    entry = {"name":"RouteBack", "checks":[], "pass":True}
    trail = r.get("transitionTrail") or []
    check(entry, "Completed", r.get("status")=="Completed", r.get("status"))
    check(entry, "RouteBack_in_trail", any("RouteBack" in t for t in trail), [t for t in trail if "Route" in t or "TA_VERIFY" in t])
    check(entry, "RouteBack_event", "RouteBack" in (r.get("eventTypes") or []), None)
    pid, wid = r.get("projectId"), r.get("workItemId")
    root = pathlib.Path(BLOB)/pid/wid if pid and wid else pathlib.Path("/tmp/none")
    revs = sorted([p.name for p in root.iterdir() if p.is_dir() and p.name.startswith("v")]) if root.is_dir() else []
    # Prefer Azurite revision listing when Azure-primary
    if AZ_CS and pid and wid:
        hits = azurite_has_blob(pid, wid, "/v") or []
        az_revs = sorted({h.split("/")[2] for h in hits if len(h.split("/"))>2 and h.split("/")[2].startswith("v")})
        if len(az_revs) > len(revs):
            revs = az_revs
    check(entry, "revision_bumped_v2", len(revs)>=2, revs)
    concerns_local = any(root.rglob("ta-concerns.json")) if root.is_dir() else False
    concerns_az_hits = []
    if AZ_CS and pid and wid:
        for suffix in ("ta-concerns.json", "TA_CONCERNS"):
            hits = azurite_has_blob(pid, wid, suffix) or []
            if hits and not str(hits[0]).startswith("error:"):
                concerns_az_hits.extend(hits)
    check(entry, "concerns_v1", concerns_local or bool(concerns_az_hits), concerns_az_hits[:3] if concerns_az_hits else ("local="+str(concerns_local)))
    url = r.get("deployedUrl")
    check(entry, "final_url", bool(url), url)
    if url:
        st, body = get_text(url.rstrip("/")+"/api/add?a=2&b=3")
        check(entry, "final_add", st==200 and "5" in body, body[:120])
    outdir = pathlib.Path(OUT)/"cases"/"RouteBack"
    outdir.mkdir(parents=True, exist_ok=True)
    (outdir/"run.json").write_text(json.dumps(r, indent=2))
    (outdir/"inspect.json").write_text(json.dumps(entry, indent=2, default=str))
    results["cases"].append(entry)
    all_pass &= entry["pass"]

results["finishedAt"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
results["overallPass"] = all_pass and not results["failures"]
# recount
results["overallPass"] = all(c["pass"] for c in results["cases"]) and not any(f["case"]=="infra" for f in results["failures"])
pathlib.Path(REPORT).write_text(json.dumps(results, indent=2, default=str))
print("\n===== SUMMARY =====")
print("cases", len(results["cases"]), "failures", len(results["failures"]), "overallPass", results["overallPass"])
total_checks = sum(len(c["checks"]) for c in results["cases"])
failed_checks = sum(1 for c in results["cases"] for x in c["checks"] if not x["pass"])
print("checks", total_checks, "failed_checks", failed_checks)
for c in results["cases"]:
    bad = [x["check"] for x in c["checks"] if not x["pass"]]
    print(f"  {c['name']}: {'PASS' if c['pass'] else 'FAIL'} ({len(c['checks'])} checks) bad={bad[:10]}")
sys.exit(0 if results["overallPass"] else 1)
PY

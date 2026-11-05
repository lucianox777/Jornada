#!/usr/bin/env python3
"""V9 parallel orchestrator helpers: fail-closed artifact preflight, executor
invocation contract, evidence manifest and status report.

Never dispatches workflows, never starts SQL, never generates corpora, never
reads TEST. It only inspects declared references, downloaded artifacts and
read-only GitHub API data supplied as JSON.
"""
import argparse
import hashlib
import importlib.util
import json
import re
import shlex
import sys
from pathlib import Path

SHA64 = re.compile(r"[0-9a-f]{64}\Z")
SHA40 = re.compile(r"[0-9a-f]{40}\Z")
REFERENCES = Path("Solution/data/reference/v9-frozen-artifacts/references.json")
EXECUTOR = Path("Solution/data/reference/v9-tf-seed43-frozen-transfer/executor.json")
V2V3_WORKFLOW = Path(".github/workflows/v9-frozen-joint-v3-independent-physical.yml")
STATES = ("EXECUTED", "TECHNICALLY_APPROVED", "STATISTICALLY_ELIGIBLE", "CERTIFIED", "OPERATIONALLY_AUTHORIZED")


def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def load(path):
    return json.loads(Path(path).read_bytes())


# ---------------------------------------------------------------- references

def audit_references(repo):
    """Offline: every declared SHA is complete, repeated SHAs agree, executor and
    workflows reference only complete declared SHAs (no 62-char truncation)."""
    repo = Path(repo)
    ref = load(repo / REFERENCES)
    if ref.get("schema_version") != "V9_FROZEN_ARTIFACT_REFERENCES_V1":
        raise ValueError("Unknown reference schema")
    declared = set()
    for key, art in ref["artifacts"].items():
        if type(art.get("run_id")) is not int or art["run_id"] < 1 or not SHA40.fullmatch(art.get("head_sha", "")):
            raise ValueError(f"{key}: run id and 40-hex head SHA required")
        if not art.get("name") or not art.get("files"):
            raise ValueError(f"{key}: artifact name and files required")
        seen = set()
        for f in art["files"]:
            path = f.get("path", "")
            if not path or path.startswith("/") or ".." in Path(path).parts or path in seen:
                raise ValueError(f"{key}: invalid or duplicate ZIP-internal path {path!r}")
            seen.add(path)
            if f["sha256"] == "CONTENT_BOUND":
                if not isinstance(f.get("content"), dict) or not f["content"]:
                    raise ValueError(f"{key}: content-bound file needs declared content")
                continue
            if not SHA64.fullmatch(f["sha256"]):
                raise ValueError(f"{key}: incomplete or non-hex SHA-256 for {path}")
            name_sha = Path(path).name.split(".")[0]
            if path.endswith(".fitted.json") and name_sha != f["sha256"]:
                raise ValueError(f"{key}: fitted file name and SHA differ for {path}")
            declared.add(f["sha256"])
    for name, value in ref["models"].items():
        if not SHA64.fullmatch(value) or value not in declared:
            raise ValueError(f"model {name} not a declared complete artifact SHA")
    executor = load(repo / EXECUTOR)
    src = ref["artifacts"]["seed42-source"]
    files = {f["path"]: f["sha256"] for f in src["files"]}
    if (executor["SourceRunId"] != src["run_id"] or executor["SourceCommitSha"] != src["head_sha"]
            or executor["SourceArtifactName"] != src["name"]
            or files.get("plan-tf.json") != executor["SourcePlanTfSha256"]
            or files.get("study-tf/study.json") != executor["SourceStudySha256"]
            or any(files.get(f"study-tf/{a['SnapshotSha256']}.fitted.json") != a["SnapshotSha256"]
                   for a in executor["Arms"])):
        raise ValueError("executor.json and central references disagree")
    receipt = ref["artifacts"]["v3-trial-metadata"]["files"][0]["content"]
    if (receipt["sourceSnapshotSha256"] != ref["models"]["V2_ONE_MF_original"]
            or receipt["derivedTrialSnapshotSha256"] != ref["models"]["V3_trial"]
            or receipt["originalTrainStudySha256"] != executor["SourceStudySha256"]):
        raise ValueError("Derivation receipt declaration disagrees with models")
    problems = []
    for wf in sorted((repo / ".github" / "workflows").glob("v9-*.yml")):
        text = wf.read_text(encoding="utf-8")
        for token in set(re.findall(r"(?<![0-9a-f])[0-9a-f]{41,80}(?![0-9a-f])", text)):
            if len(token) != 64:
                problems.append(f"{wf.name}: hex token of length {len(token)}: {token[:12]}…")
            elif any(d != token and (d.startswith(token[:16])) for d in declared):
                problems.append(f"{wf.name}: SHA {token[:12]}… collides with a different declared SHA")
        for token in set(re.findall(r"(?<![0-9a-f])[0-9a-f]{20,63}(?![0-9a-f])", text)):
            if len(token) != 40 and any(d.startswith(token) or token in d for d in declared):
                problems.append(f"{wf.name}: truncated declared SHA {token[:12]}… ({len(token)} chars)")
    if problems:
        raise ValueError("Workflow SHA references invalid: " + "; ".join(sorted(problems)))
    return {"status": "REFERENCES_OK", "declared_sha256": len(declared), "artifacts": sorted(ref["artifacts"])}


def verify_artifact(repo, key, root):
    """After download: declared files exist at their REAL ZIP-internal paths,
    are non-empty and match SHA-256 or declared content. No root assumption."""
    ref = load(Path(repo) / REFERENCES)
    art = ref["artifacts"][key]
    root = Path(root)
    present = sorted(str(p.relative_to(root)) for p in root.rglob("*") if p.is_file())
    report = {"artifact": key, "name": art["name"], "files": [], "present": present}
    for f in art["files"]:
        path = root / f["path"]
        if not path.is_file():
            near = [p for p in present if Path(p).name == Path(f["path"]).name]
            raise ValueError(f"{key}: {f['path']} missing; same name found at {near or 'nowhere'}")
        if path.stat().st_size == 0:
            raise ValueError(f"{key}: {f['path']} is empty")
        if f["sha256"] == "CONTENT_BOUND":
            content = load(path)
            for k, v in f["content"].items():
                if content.get(k) != v:
                    raise ValueError(f"{key}: {f['path']} field {k} differs")
            actual = sha256(path)
        else:
            actual = sha256(path)
            if actual != f["sha256"]:
                raise ValueError(f"{key}: SHA-256 mismatch for {f['path']}")
        report["files"].append({"path": f["path"], "sha256": actual, "bytes": path.stat().st_size})
    return report


# ---------------------------------------------------- executor call contract

def extract_invocations(workflow_text, flag):
    """Return argument lists of every `dotnet run ... -- <flag> args` in the workflow."""
    calls = []
    joined = re.sub(r"\\\n\s*", " ", workflow_text)
    for line in joined.splitlines():
        if flag in line and "dotnet run" in line:
            tokens = shlex.split(line.strip())
            calls.append(tokens[tokens.index(flag) + 1:])
            calls[-1] = [t for t in calls[-1] if t not in (">", "/dev/null")]
    return calls


def invocation_contract(repo, source, trial):
    """Positive test: resolve the EXACT arguments the workflow passes to the
    pair preflight and comparison, and prove every input path exists with the
    declared bytes. Returns the resolved preflight argv for real execution."""
    repo = Path(repo)
    ref = load(repo / REFERENCES)
    text = (repo / V2V3_WORKFLOW).read_text(encoding="utf-8")
    env = {"$PWD": str((repo / "Solution").resolve()), "$SOURCE_DIR": str(Path(source).resolve()),
           "$TRIAL_DIR": str(Path(trial).resolve())}

    def resolve(arg):
        for k, v in env.items():
            arg = arg.replace(k, v)
        return arg

    receipt_path = ref["artifacts"]["v3-trial-metadata"]["files"][0]["path"]
    if f'mv -- "$TRIAL_DIR/{receipt_path}"' not in text or f'test -f "$TRIAL_DIR/{receipt_path}"' not in text:
        raise ValueError("Workflow does not normalize the receipt from its declared ZIP-internal path")
    for art in ("v3-trial-model", "v3-trial-metadata"):
        if ref["artifacts"][art]["name"].replace(ref["artifacts"][art]["head_sha"], "$TRIAL_SHA") not in text:
            raise ValueError(f"Workflow downloads a different artifact than declared for {art}")
    pre = extract_invocations(text, "--v9-preflight-frozen-v2-v3-pair")
    cmp_ = extract_invocations(text, "--v9-compare-frozen-v2-v3-independent")
    if len(pre) != 1 or len(cmp_) != 1 or len(pre[0]) != 5 or len(cmp_[0]) != 7:
        raise ValueError("Workflow must call pair preflight (5 args) and comparison (7 args) exactly once")
    if pre[0] != cmp_[0][:5]:
        raise ValueError("Preflight and comparison receive different model inputs")
    models = ref["models"]
    expected_tail = [f"{models['V2_ONE_MF_original']}.fitted.json", f"{models['V3_trial']}.fitted.json",
                     "derivation-receipt.json"]
    resolved = [resolve(a) for a in pre[0]]
    if [Path(p).name for p in resolved[2:]] != expected_tail:
        raise ValueError("Workflow model arguments differ from central references")
    for p in resolved:
        if not Path(p).is_file() or Path(p).stat().st_size == 0:
            raise ValueError(f"Executor input missing or empty: {p}")
    if sha256(resolved[2]) != models["V2_ONE_MF_original"] or sha256(resolved[3]) != models["V3_trial"]:
        raise ValueError("Executor model bytes differ from declared SHA")
    return {"preflight_argv": resolved, "comparison_inputs_match_preflight": True}


# ------------------------------------------------------ evidence and status

def physical_evidence(runs, workflow_path, sha, branch, require_mode=None):
    """Only finished same-SHA evidence counts. The probe accepts PR auto 30k
    when BOTH the physical job and its stable success gate passed, never a
    skipped job or a generic green PR workflow. Other workflows remain manual.
    """
    def valid(r):
        if (r.get("path") != workflow_path or r.get("head_sha") != sha
                or r.get("head_branch") != branch or r.get("conclusion") != "success"
                or r.get("status") not in (None, "completed")):
            return False
        if r.get("event") == "workflow_dispatch":
            return require_mode is None or r.get("mode") == require_mode
        return (r.get("event") == "pull_request"
                and workflow_path.endswith("/v9-physical-seed42-probe.yml")
                and require_mode in (None, "execute_30k")
                and r.get("mode") == "auto_30k"
                and r.get("physical_job") == "success"
                and r.get("stable_gate") == "success"
                and r.get("same_repository_branch") is True)

    hits = [r for r in runs if valid(r)]
    return sorted(hits, key=lambda r: r["id"])[-1] if hits else None


def front_status(front, checks, runs):
    """Derive the five distinct states. Green jobs never imply eligibility,
    certification or authorization; failures are never reported as success."""
    sha, branch = front["sha"], front["branch"]
    failed = sorted(c["name"] for c in checks if c.get("conclusion") not in ("success", "skipped", "neutral")
                    and c.get("status") == "completed" and c["name"] not in front.get("physical_check_names", []))
    pending = sorted(c["name"] for c in checks if c.get("status") != "completed")
    evidence = {}
    for need in front.get("physical", []):
        evidence[need["id"]] = physical_evidence(runs, need["workflow"], sha, branch, need.get("mode"))
    missing = [k for k, v in evidence.items() if v is None]
    not_implemented = bool(front.get("not_implemented"))
    executed = (bool(evidence) and not missing) or (not front.get("physical") and not not_implemented
                                                    and not failed and not pending)
    technical = not failed and not pending and not missing and not not_implemented
    blockers = list(front.get("scientific_blockers", []))
    if not_implemented:
        blockers.insert(0, "NOT_IMPLEMENTED: no executable campaign exists yet")
    if failed:
        blockers.append("CI failing: " + ", ".join(failed))
    if pending:
        blockers.append("CI pending: " + ", ".join(pending))
    for k in missing:
        blockers.append(f"verified physical evidence missing at exact SHA: {k}")
    return {
        "front": front["id"], "pr": front.get("pr"), "branch": branch, "sha": sha,
        "ci": "FAILED" if failed else "PENDING" if pending else "GREEN",
        "physical": {k: (v["id"] if v else None) for k, v in evidence.items()},
        "EXECUTED": executed or (not front.get("physical") and technical),
        "TECHNICALLY_APPROVED": technical,
        "STATISTICALLY_ELIGIBLE": front.get("statistical_state", "NO_ELIGIBLE_CONFIGURATION"),
        "CERTIFIED": False, "OPERATIONALLY_AUTHORIZED": False,
        "historical_evidence": front.get("historical_evidence", []),
        "merge_ready": None if not front.get("pr") else technical and not front.get("scientific_blockers"),
        "blockers": blockers,
    }


def report(status_rows):
    lines = ["## V9 — orquestração paralela (somente leitura; nenhum 30k disparado)", "",
             "EXECUTADO ≠ APROVADO TECNICAMENTE ≠ ELEGÍVEL ESTATISTICAMENTE ≠ CERTIFICADO ≠ AUTORIZADO OPERACIONALMENTE", "",
             "| Frente | PR | SHA | CI | Físico | Executado | Aprov. técnica | Estatística | Certificado | Autorizado | Merge |",
             "|---|---|---|---|---|---|---|---|---|---|---|"]
    for r in status_rows:
        phys = ", ".join(f"{k}={v or 'FALTA'}" for k, v in r["physical"].items()) or "não exigido"
        lines.append(f"| {r['front']} | {r['pr'] or '—'} | `{r['sha'][:8]}` | {r['ci']} | {phys} | "
                     f"{'sim' if r['EXECUTED'] else 'não'} | {'sim' if r['TECHNICALLY_APPROVED'] else 'não'} | "
                     f"{r['STATISTICALLY_ELIGIBLE']} | não | não | {'n/a (sem PR)' if r['merge_ready'] is None else 'pronto' if r['merge_ready'] else 'bloqueado'} |")
    lines += ["", f"Aprovadas tecnicamente: {sum(r['TECHNICALLY_APPROVED'] for r in status_rows)} de {len(status_rows)}; "
              f"bloqueadas: {sum(bool(r['blockers']) for r in status_rows)}.", ""]
    for r in status_rows:
        for h in r.get("historical_evidence", []):
            lines.append(f"- **{r['front']}** evidência histórica reutilizável: run {h['run_id']} ({h['conclusion']}): {h['scope']}")
    for r in status_rows:
        if r["blockers"]:
            lines.append(f"- **{r['front']}**: " + "; ".join(r["blockers"]))
    return "\n".join(lines) + "\n"


def _gh(path):
    import os, subprocess
    out = subprocess.run(["gh", "api", f"repos/{os.environ['GITHUB_REPOSITORY']}/{path}"],
                         check=True, text=True, capture_output=True).stdout
    return json.loads(out)


def collect(fronts_path, results_dir=None):
    """Read-only GitHub API collection. Never POSTs; tokens in the orchestrator
    have actions: read only, so a dispatch would be refused anyway."""
    spec = load(fronts_path)
    master = _gh("branches/master")["commit"]["sha"]
    fronts, checks = [], {}
    for f in spec["fronts"]:
        f = dict(f)
        if f.get("pr"):
            pr = _gh(f"pulls/{f['pr']}")
            if pr["state"] != "open":
                f["scientific_blockers"] = list(f.get("scientific_blockers", [])) + [f"PR state {pr['state']}"]
            f["sha"], f["branch"] = pr["head"]["sha"], pr["head"]["ref"]
        else:
            f["sha"], f["branch"] = master, "master"
        fronts.append(f)
        checks[f["id"]] = [{"name": c["name"], "status": c["status"], "conclusion": c["conclusion"]}
                           for c in _gh(f"commits/{f['sha']}/check-runs?per_page=100")["check_runs"]]
    shas = {f["sha"] for f in fronts}
    runs = []
    for wf in sorted({n["workflow"] for f in fronts for n in f.get("physical", [])}):
        probe = wf.endswith("/v9-physical-seed42-probe.yml")
        events = ("workflow_dispatch", "pull_request") if probe else ("workflow_dispatch",)
        for event in events:
            for r in _gh(f"actions/workflows/{Path(wf).name}/runs?event={event}&per_page=100")["workflow_runs"]:
                if r["head_sha"] not in shas or r.get("event") != event:
                    continue
                mode = None
                physical = None
                stable = None
                if probe:
                    jobs = {j["name"]: (j.get("status"), j.get("conclusion"))
                            for j in _gh(f"actions/runs/{r['id']}/jobs?per_page=100")["jobs"]}
                    physical = "success" if jobs.get("V9 full physical 30k SQL TRAIN (when impacted)") == ("completed", "success") else None
                    stable = "success" if jobs.get("SQL físico 30k clean — FS blind Rank-Resolve TF0") == ("completed", "success") else None
                    if event == "workflow_dispatch":
                        mode = "execute_30k" if physical else "preflight"
                    else:
                        mode = "auto_30k" if physical and stable else "incomplete_auto_30k"
                # Automatic PR evidence must originate in this repository,
                # not an untrusted fork with an identical displayed branch name.
                from_same_repo = (event == "pull_request"
                                  and (r.get("head_repository") or {}).get("full_name") == __import__("os").environ["GITHUB_REPOSITORY"])
                runs.append({"id": r["id"], "path": wf, "event": event, "head_sha": r["head_sha"],
                             "head_branch": r["head_branch"], "conclusion": r["conclusion"],
                             "status": r.get("status"), "mode": mode,
                             "physical_job": physical, "stable_gate": stable,
                             "same_repository_branch": from_same_repo,
                             "started": r.get("run_started_at"), "updated": r.get("updated_at")})
    results = {}
    if results_dir:
        for path in sorted(Path(results_dir).rglob("*.json")):
            raw = load(path)
            results[path.name] = aggregate_only(raw)
            if isinstance(raw, dict) and "original" in raw and "v3Trial" in raw:
                results[path.name]["pairedIdentityClustered"] = paired_v2_v3(raw)
    return {"generated_from": {"master": master}, "fronts": fronts, "checks": checks, "runs": runs, "results": results}


def paired_v2_v3(raw):
    """Identity-clustered paired contrast V3 - V2 using the existing analyzer;
    only aggregate numbers are returned (no base identities)."""
    spec = importlib.util.spec_from_file_location(
        "v9_transfer_paired", Path(__file__).resolve().parent / "v9-tf-seed43-frozen-transfer-physical.py")
    tool = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(tool)
    arm = lambda key: {"Evaluation": {"Metric": raw[key]["Metric"]}}
    if not raw["original"]["Metric"].get("IdentityOutcomes"):
        return {"status": "IDENTITY_OUTCOMES_NOT_IN_ARTIFACT"}
    return tool.paired(arm("original"), arm("v3Trial"))


def aggregate_only(obj, depth=0):
    """Keep scalars and small nested objects; drop lists (identity outcomes,
    samples) so no per-identity data reaches the report."""
    if isinstance(obj, dict) and depth < 4:
        return {k: aggregate_only(v, depth + 1) for k, v in obj.items()
                if not isinstance(v, list) and k not in ("IdentityOutcomes",)}
    return obj if not isinstance(obj, (dict, list)) else None


def main():
    p = argparse.ArgumentParser(description=__doc__)
    sub = p.add_subparsers(dest="cmd", required=True)
    a = sub.add_parser("audit-references"); a.add_argument("--repo", default=".")
    v = sub.add_parser("verify-artifact"); v.add_argument("--repo", default="."); v.add_argument("--key", required=True); v.add_argument("--root", required=True)
    c = sub.add_parser("invocation-contract"); c.add_argument("--repo", default="."); c.add_argument("--source", required=True); c.add_argument("--trial", required=True); c.add_argument("--argv-out", required=True)
    k = sub.add_parser("collect"); k.add_argument("--fronts", required=True); k.add_argument("--results-dir"); k.add_argument("--output", required=True)
    s = sub.add_parser("status"); s.add_argument("--input", required=True); s.add_argument("--manifest", required=True); s.add_argument("--summary", required=True)
    x = p.parse_args()
    if x.cmd == "audit-references":
        print(json.dumps(audit_references(x.repo)))
    elif x.cmd == "verify-artifact":
        print(json.dumps(verify_artifact(x.repo, x.key, x.root)))
    elif x.cmd == "collect":
        Path(x.output).write_text(json.dumps(collect(x.fronts, x.results_dir), indent=2) + "\n", encoding="utf-8")
    elif x.cmd == "invocation-contract":
        out = invocation_contract(x.repo, x.source, x.trial)
        Path(x.argv_out).write_text("\n".join(out["preflight_argv"]) + "\n", encoding="utf-8")
        print(json.dumps(out))
    else:
        data = load(x.input)
        rows = [front_status(f, data["checks"].get(f["id"], []), data["runs"]) for f in data["fronts"]]
        manifest = {"schema": "V9_ORCHESTRATOR_EVIDENCE_MANIFEST_V1", "generated_from": data.get("generated_from"),
                    "fronts": rows, "evidence_runs": data["runs"], "results": data.get("results", {}),
                    "states_are_distinct": list(STATES), "physical_dispatch_performed": False}
        Path(x.manifest).write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        Path(x.summary).write_text(report(rows), encoding="utf-8")
        print(report(rows))


if __name__ == "__main__":
    sys.exit(main())

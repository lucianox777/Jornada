#!/usr/bin/env python3
"""Fail-closed DT-12 branch cleanup.

Dry-run validates that the versioned candidate set is still safe.
Destructive execution additionally requires an explicit authorization marker.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import urllib.error
import urllib.parse
import urllib.request

AUTH_PHRASE = "DELETE_DT12_170_BRANCHES"
DEFAULT_CANDIDATE = "Solution/docs/DT12_Branch_Delete_Candidates_20261001.csv"
DEFAULT_AUTHORIZATION = "Solution/docs/evidence/DT12_Branch_Delete_Authorization_20261001.json"
EXCLUDED_REFERENCE_FILES = {
    "Solution/docs/DT12_Branches_20260926.csv",
    "Solution/docs/DT12_Branch_Delete_Candidates_20261001.csv",
    "Solution/docs/DT12_Branch_DryRun_20261001.csv",
    "Solution/docs/DT12_DryRun_20261001.md",
    "Solution/docs/DT12_Inventario_Branches_NOTA_RELEASE_20260926.md",
}
MAX_SCAN_BYTES = 2_000_000


class CleanupError(RuntimeError):
    pass


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def load_candidates(path: Path) -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8") as fh:
        rows = list(csv.DictReader(fh))
    if not rows:
        raise CleanupError("candidate file is empty")
    required = {"branch", "sha", "classification", "ancestor", "reasons", "active_refs"}
    missing = required.difference(rows[0])
    if missing:
        raise CleanupError(f"candidate file missing columns: {sorted(missing)}")
    names: set[str] = set()
    for row in rows:
        name = row["branch"].strip()
        sha = row["sha"].strip()
        if not name or name == "master":
            raise CleanupError(f"invalid candidate branch: {name!r}")
        if name in names:
            raise CleanupError(f"duplicate candidate branch: {name}")
        names.add(name)
        if not re.fullmatch(r"[0-9a-f]{40}", sha):
            raise CleanupError(f"invalid SHA for {name}: {sha}")
        if row["classification"].strip() != "CANDIDATE_DELETE":
            raise CleanupError(f"candidate {name} is not CANDIDATE_DELETE")
        if row["ancestor"].strip().lower() != "true":
            raise CleanupError(f"candidate {name} is not marked ancestor of master")
        if row["reasons"].strip() or row["active_refs"].strip():
            raise CleanupError(f"candidate {name} has blockers in versioned dry-run")
    return rows


def validate_candidate_contract(path: Path, expected_sha256: str, expected_count: int) -> list[dict[str, str]]:
    actual = sha256_file(path)
    if actual.lower() != expected_sha256.lower():
        raise CleanupError(f"candidate SHA256 mismatch: expected {expected_sha256}, got {actual}")
    rows = load_candidates(path)
    if len(rows) != expected_count:
        raise CleanupError(f"candidate count mismatch: expected {expected_count}, got {len(rows)}")
    return rows


def api_request(repository: str, endpoint: str, token: str, method: str = "GET"):
    url = f"https://api.github.com/repos/{repository}/{endpoint.lstrip('/')}"
    req = urllib.request.Request(
        url,
        method=method,
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "jornada-dt12-cleanup",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            data = response.read()
            return json.loads(data) if data else None
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        raise CleanupError(f"GitHub API {method} {endpoint} failed: HTTP {exc.code}: {body[:500]}") from exc


def paged(repository: str, endpoint: str, token: str) -> list[dict]:
    result: list[dict] = []
    page = 1
    separator = "&" if "?" in endpoint else "?"
    while True:
        batch = api_request(repository, f"{endpoint}{separator}per_page=100&page={page}", token)
        if not isinstance(batch, list):
            raise CleanupError(f"unexpected GitHub API payload for {endpoint}")
        result.extend(batch)
        if len(batch) < 100:
            return result
        page += 1
        if page > 50:
            raise CleanupError(f"pagination safety limit exceeded for {endpoint}")


def current_state(repository: str, token: str) -> tuple[dict[str, dict], set[str], dict[str, str]]:
    branches = paged(repository, "branches", token)
    open_prs = paged(repository, "pulls?state=open", token)
    tags = api_request(repository, "git/matching-refs/tags/", token)
    if not isinstance(tags, list):
        raise CleanupError("unexpected tags payload")

    branch_map = {
        b["name"]: {
            "sha": b["commit"]["sha"],
            "protected": bool(b.get("protected", False)),
        }
        for b in branches
    }
    open_heads = {
        p["head"]["ref"]
        for p in open_prs
        if p.get("head") and p["head"].get("repo", {}).get("full_name") == repository
    }
    tag_heads = {
        t["object"]["sha"]: t["ref"].removeprefix("refs/tags/")
        for t in tags
        if t.get("object", {}).get("sha")
    }
    return branch_map, open_heads, tag_heads


def scan_active_references(root: Path, branch_names: set[str]) -> dict[str, list[str]]:
    pattern = re.compile("|".join(sorted((re.escape(x) for x in branch_names), key=len, reverse=True)))
    hits: dict[str, list[str]] = {}
    for path in root.rglob("*"):
        if not path.is_file() or ".git" in path.parts:
            continue
        rel = path.relative_to(root).as_posix()
        if rel in EXCLUDED_REFERENCE_FILES or rel.startswith("Solution/docs/archive/"):
            continue
        try:
            if path.stat().st_size > MAX_SCAN_BYTES:
                continue
            text = path.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        found = set(pattern.findall(text))
        for name in found:
            hits.setdefault(name, []).append(rel)
    return hits


def compute_blockers(
    candidates: list[dict[str, str]],
    branches: dict[str, dict],
    open_heads: set[str],
    tag_heads: dict[str, str],
    active_refs: dict[str, list[str]],
) -> dict[str, list[str]]:
    blockers: dict[str, list[str]] = {}
    for row in candidates:
        name = row["branch"]
        expected_sha = row["sha"]
        reasons: list[str] = []
        state = branches.get(name)
        if state is None:
            reasons.append("branch_missing")
        else:
            if state["sha"] != expected_sha:
                reasons.append(f"sha_changed:{state['sha']}")
            if state["protected"]:
                reasons.append("protected")
        if name in open_heads:
            reasons.append("open_pr")
        if expected_sha in tag_heads:
            reasons.append(f"tag_head:{tag_heads[expected_sha]}")
        for rel in active_refs.get(name, []):
            reasons.append(f"active_reference:{rel}")
        if reasons:
            blockers[name] = reasons
    return blockers


def load_authorization(path: Path, expected_sha256: str, expected_count: int) -> dict:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise CleanupError(f"authorization marker not found: {path}") from exc
    if data.get("authorized") is not True:
        raise CleanupError("authorization marker must contain authorized=true")
    if data.get("phrase") != AUTH_PHRASE:
        raise CleanupError("authorization phrase mismatch")
    if data.get("candidate_sha256") != expected_sha256:
        raise CleanupError("authorization candidate_sha256 mismatch")
    if data.get("candidate_count") != expected_count:
        raise CleanupError("authorization candidate_count mismatch")
    if not data.get("authorized_at") or not data.get("authorized_by"):
        raise CleanupError("authorization marker must identify authorized_at and authorized_by")
    return data


def ref_endpoint(branch: str) -> str:
    return "git/ref/heads/" + urllib.parse.quote(branch, safe="/")


def delete_candidates(repository: str, token: str, candidates: list[dict[str, str]]) -> list[str]:
    deleted: list[str] = []
    for row in sorted(candidates, key=lambda x: x["branch"]):
        name = row["branch"]
        expected = row["sha"]
        current = api_request(repository, ref_endpoint(name), token)
        current_sha = current["object"]["sha"]
        if current_sha != expected:
            raise CleanupError(
                f"TOCTOU guard blocked {name}: expected {expected}, current {current_sha}; "
                f"{len(deleted)} branches had already been deleted"
            )
        api_request(repository, ref_endpoint(name), token, method="DELETE")
        deleted.append(name)
    return deleted


def write_report(path: Path | None, payload: dict) -> None:
    if path is None:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository", required=True)
    parser.add_argument("--candidate-file", default=DEFAULT_CANDIDATE)
    parser.add_argument("--expected-sha256", required=True)
    parser.add_argument("--expected-count", type=int, required=True)
    parser.add_argument("--authorization-file", default=DEFAULT_AUTHORIZATION)
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--report")
    args = parser.parse_args(argv)

    candidate_path = Path(args.candidate_file)
    candidates = validate_candidate_contract(candidate_path, args.expected_sha256, args.expected_count)
    token = os.environ.get("GITHUB_TOKEN")
    if not token:
        raise CleanupError("GITHUB_TOKEN is required")

    branches, open_heads, tag_heads = current_state(args.repository, token)
    active_refs = scan_active_references(Path.cwd(), {r["branch"] for r in candidates})
    blockers = compute_blockers(candidates, branches, open_heads, tag_heads, active_refs)
    report = {
        "repository": args.repository,
        "candidate_count": len(candidates),
        "candidate_sha256": args.expected_sha256,
        "blockers": blockers,
        "execute": args.execute,
        "deleted": [],
    }
    if blockers:
        write_report(Path(args.report) if args.report else None, report)
        sample = list(blockers.items())[:10]
        raise CleanupError(f"preflight blocked {len(blockers)} candidates; sample={sample}")

    if not args.execute:
        write_report(Path(args.report) if args.report else None, report)
        print(f"DT-12 dry-run OK: {len(candidates)} candidates, zero blockers")
        return 0

    load_authorization(Path(args.authorization_file), args.expected_sha256, args.expected_count)

    # Re-read all remote state immediately before the destructive loop.
    branches2, open_heads2, tag_heads2 = current_state(args.repository, token)
    active_refs2 = scan_active_references(Path.cwd(), {r["branch"] for r in candidates})
    blockers2 = compute_blockers(candidates, branches2, open_heads2, tag_heads2, active_refs2)
    if blockers2:
        report["blockers"] = blockers2
        write_report(Path(args.report) if args.report else None, report)
        raise CleanupError(f"second preflight blocked {len(blockers2)} candidates")

    deleted = delete_candidates(args.repository, token, candidates)
    report["deleted"] = deleted
    write_report(Path(args.report) if args.report else None, report)
    print(f"DT-12 cleanup completed: deleted {len(deleted)} branches")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except CleanupError as exc:
        print(f"DT-12 cleanup blocked: {exc}", file=sys.stderr)
        raise SystemExit(2)

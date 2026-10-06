#!/usr/bin/env python3
"""Delete only stale branch refs whose current HEAD is the exact head of a merged PR.

The authorization file freezes a merged-at cutoff. Runtime preflight excludes protected
branches, open PR heads, direct tag heads and branch names still referenced by tracked
text files. Every deletion revalidates both the branch SHA and the merged PR.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import sys
import urllib.error
import urllib.parse
import urllib.request

AUTH_PHRASE = "DELETE_MERGED_BRANCHES_20261006"
DEFAULT_AUTHORIZATION = "Solution/docs/evidence/DT12_Merged_Branch_Delete_Authorization_20261006.json"
MAX_SCAN_BYTES = 2_000_000
EXCLUDED_REFERENCE_FILES = {
    DEFAULT_AUTHORIZATION,
    "Solution/docs/DT12_Branch_Delete_Candidates_20261001.csv",
    "Solution/docs/DT12_Branch_DryRun_20261001.csv",
    "Solution/docs/DT12_DryRun_20261001.md",
    "Solution/docs/DT12_Inventario_Branches_NOTA_RELEASE_20260926.md",
}


class CleanupError(RuntimeError):
    pass


def parse_time(value: str) -> datetime:
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except (TypeError, ValueError) as exc:
        raise CleanupError(f"invalid ISO timestamp: {value!r}") from exc
    if parsed.tzinfo is None:
        raise CleanupError("authorization timestamp must include timezone")
    return parsed.astimezone(timezone.utc)


def load_authorization(path: Path) -> dict:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise CleanupError(f"authorization unavailable/invalid: {exc}") from exc
    if data.get("authorized") is not True:
        raise CleanupError("authorization must contain authorized=true")
    if data.get("phrase") != AUTH_PHRASE:
        raise CleanupError("authorization phrase mismatch")
    if not data.get("authorized_by"):
        raise CleanupError("authorization must identify authorized_by")
    parse_time(data.get("authorized_at"))
    return data


def api_request(repository: str, endpoint: str, token: str, method: str = "GET"):
    url = f"https://api.github.com/repos/{repository}/{endpoint.lstrip('/')}"
    req = urllib.request.Request(
        url,
        method=method,
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "jornada-dt12-merged-cleanup",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            payload = response.read()
            return json.loads(payload) if payload else None
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


def current_state(repository: str, token: str):
    branches = paged(repository, "branches", token)
    open_prs = paged(repository, "pulls?state=open", token)
    closed_prs = paged(repository, "pulls?state=closed&sort=updated&direction=desc", token)
    tags = api_request(repository, "git/matching-refs/tags/", token)
    if not isinstance(tags, list):
        raise CleanupError("unexpected tags payload")
    branch_map = {
        b["name"]: {"sha": b["commit"]["sha"], "protected": bool(b.get("protected", False))}
        for b in branches
    }
    open_heads = {
        p["head"]["ref"]
        for p in open_prs
        if p.get("head") and p["head"].get("repo", {}).get("full_name") == repository
    }
    direct_tag_heads = {
        t["object"]["sha"]
        for t in tags
        if t.get("object", {}).get("type") == "commit" and t["object"].get("sha")
    }
    return branch_map, open_heads, direct_tag_heads, closed_prs


def select_merged_candidates(
    repository: str,
    branches: dict[str, dict],
    open_heads: set[str],
    direct_tag_heads: set[str],
    closed_prs: list[dict],
    cutoff: datetime,
) -> list[dict]:
    selected: dict[str, dict] = {}
    for pr in closed_prs:
        merged_at_raw = pr.get("merged_at")
        if not merged_at_raw:
            continue
        merged_at = parse_time(merged_at_raw)
        if merged_at > cutoff:
            continue
        head = pr.get("head") or {}
        base = pr.get("base") or {}
        head_repo = (head.get("repo") or {}).get("full_name")
        name = head.get("ref")
        sha = head.get("sha")
        if head_repo != repository or base.get("ref") != "master" or not name or not sha or name == "master":
            continue
        state = branches.get(name)
        if not state or state["protected"] or state["sha"] != sha:
            continue
        if name in open_heads or sha in direct_tag_heads:
            continue
        candidate = {
            "branch": name,
            "sha": sha,
            "pr_number": int(pr["number"]),
            "merged_at": merged_at_raw,
        }
        previous = selected.get(name)
        if previous is None or parse_time(previous["merged_at"]) < merged_at:
            selected[name] = candidate
    return sorted(selected.values(), key=lambda x: x["branch"])


def scan_active_references(root: Path, branch_names: set[str]) -> dict[str, list[str]]:
    if not branch_names:
        return {}
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
        for name in set(pattern.findall(text)):
            hits.setdefault(name, []).append(rel)
    return hits


def ref_endpoint(branch: str) -> str:
    return "git/ref/heads/" + urllib.parse.quote(branch, safe="/")


def delete_ref_endpoint(branch: str) -> str:
    return "git/refs/heads/" + urllib.parse.quote(branch, safe="/")


def revalidate_candidate(repository: str, token: str, candidate: dict, cutoff: datetime) -> None:
    current = api_request(repository, ref_endpoint(candidate["branch"]), token)
    if current["object"]["sha"] != candidate["sha"]:
        raise CleanupError(f"{candidate['branch']}: branch SHA changed")
    pr = api_request(repository, f"pulls/{candidate['pr_number']}", token)
    if not pr.get("merged_at") or parse_time(pr["merged_at"]) > cutoff:
        raise CleanupError(f"{candidate['branch']}: merged PR evidence changed")
    if pr.get("base", {}).get("ref") != "master":
        raise CleanupError(f"{candidate['branch']}: merged PR base is no longer master")
    head = pr.get("head") or {}
    if head.get("ref") != candidate["branch"] or head.get("sha") != candidate["sha"]:
        raise CleanupError(f"{candidate['branch']}: merged PR head evidence changed")
    if (head.get("repo") or {}).get("full_name") != repository:
        raise CleanupError(f"{candidate['branch']}: merged PR head repository changed")


def write_report(path: Path | None, payload: dict) -> None:
    if path is None:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository", required=True)
    parser.add_argument("--authorization-file", default=DEFAULT_AUTHORIZATION)
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--report")
    args = parser.parse_args(argv)

    token = os.environ.get("GITHUB_TOKEN")
    if not token:
        raise CleanupError("GITHUB_TOKEN is required")

    authorization = load_authorization(Path(args.authorization_file))
    cutoff = parse_time(authorization["authorized_at"])
    branches, open_heads, direct_tag_heads, closed_prs = current_state(args.repository, token)
    candidates = select_merged_candidates(
        args.repository, branches, open_heads, direct_tag_heads, closed_prs, cutoff
    )
    references = scan_active_references(Path.cwd(), {c["branch"] for c in candidates})
    deletable = [c for c in candidates if c["branch"] not in references]
    skipped = [
        {"branch": c["branch"], "reasons": [f"active_reference:{p}" for p in references[c["branch"]]]}
        for c in candidates
        if c["branch"] in references
    ]

    report = {
        "repository": args.repository,
        "authorization": {
            "authorized_at": authorization["authorized_at"],
            "authorized_by": authorization["authorized_by"],
        },
        "candidate_count": len(candidates),
        "deletable_count": len(deletable),
        "skipped": skipped,
        "deleted": [],
        "execute": args.execute,
    }
    if not args.execute:
        write_report(Path(args.report) if args.report else None, report)
        print(f"DT-12 merged-branch dry-run: {len(deletable)}/{len(candidates)} deletable")
        return 0

    for candidate in deletable:
        revalidate_candidate(args.repository, token, candidate, cutoff)
        api_request(args.repository, delete_ref_endpoint(candidate["branch"]), token, method="DELETE")
        report["deleted"].append(candidate["branch"])

    write_report(Path(args.report) if args.report else None, report)
    print(
        f"DT-12 merged-branch cleanup completed: {len(report['deleted'])} deleted; "
        f"{len(skipped)} preserved by active-reference guard"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except CleanupError as exc:
        print(f"DT-12 merged-branch cleanup blocked: {exc}", file=sys.stderr)
        raise SystemExit(2)

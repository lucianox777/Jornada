#!/usr/bin/env python3
"""Read-only audit of master branch protection and active repository rulesets.

Unknown permissions or API failures are NOT evidence of protection.
No API writes, no GitHub Actions dispatch, no merge.
"""
import argparse
import json
import os
import urllib.error
import urllib.request

REQUIRED = frozenset({"unit-runtime", "security-guards", "deterministic-build"})


def request_json(repo, endpoint, token):
    req = urllib.request.Request(
        "https://api.github.com/repos/" + repo + endpoint,
        headers={"Accept": "application/vnd.github+json",
                 "Authorization": "Bearer " + token,
                 "X-GitHub-Api-Version": "2022-11-28"},
    )
    with urllib.request.urlopen(req, timeout=15) as response:
        return json.load(response)


def evaluate(protection, rulesets):
    """Return an audit, never infer protection from workflow definitions."""
    sources = []
    if isinstance(protection, dict):
        checks = protection.get("required_status_checks") or {}
        if isinstance(checks, dict):
            # GitHub exposes both legacy contexts and app-bound checks. A
            # partial legacy list must not hide required checks in the latter.
            contexts = set(checks.get("contexts") or [])
            contexts.update(item["context"] for item in (checks.get("checks") or [])\n                            if isinstance(item, dict) and isinstance(item.get("context"), str))
            sources.append(("branch_protection", contexts))
    for ruleset in rulesets:
        if ruleset.get("enforcement") != "active":
            continue
        # A ruleset's list metadata does not expose rules or full ref conditions.
        # Never infer master coverage from its name or enforcement alone.
    return {"verified": any(REQUIRED <= contexts for _, contexts in sources),
            "required_contexts": sorted(REQUIRED),
            "verified_sources": [name for name, contexts in sources if REQUIRED <= contexts],
            "rulesets_require_manual_scope_review": bool(rulesets),\n            "classic_strict_setting": (protection.get("required_status_checks") or {}).get("strict")\n            if isinstance(protection, dict) else None,\n            "scope": "classic_required_checks_only_not_full_merge_protection"}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", default="lucianox777/Jornada")
    args = parser.parse_args()
    token = os.environ.get("GITHUB_TOKEN")
    if not token:
        raise SystemExit("UNKNOWN: GITHUB_TOKEN missing; protection not verified")
    try:
        protection = request_json(args.repo, "/branches/master/protection", token)
        rulesets = request_json(args.repo, "/rulesets", token)
    except (urllib.error.HTTPError, urllib.error.URLError, TimeoutError, ValueError) as exc:
        raise SystemExit("UNKNOWN: protection/ruleset API inaccessible (" + type(exc).__name__ + ")")
    result = evaluate(protection, rulesets)
    print(json.dumps(result, sort_keys=True))
    if not result["verified"]:
        raise SystemExit("UNVERIFIED: required checks not proven by classic protection; rulesets need full inspection")


if __name__ == "__main__":
    main()

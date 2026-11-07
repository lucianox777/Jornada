#!/usr/bin/env python3
"""Read-only, provenance-pinned download of two GitHub Actions V9 30k artifacts.

GitHub token is sent ONLY to api.github.com. A 302 signed storage URL is
opened separately WITHOUT credentials. Never logs token/URL/headers/contents.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import sys
import urllib.error
import urllib.request

REPO = "lucianox777/Jornada"
WORKFLOW = ".github/workflows/v9-physical-seed42-probe.yml"
PHYSICAL = "V9 full physical 30k SQL TRAIN (when impacted)"
RUNS = [
    ("parallel", 42412346250, 13590595625,
     "77fae0d9e2c1603c40e26a13987cd1a160ca4295"),
    ("serial", 42414220551, 13590738893,
     "8c40aab2a16f6130e6f8cd9ecf0735cf62fd6ff5"),
]


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def api_request(path, token, redirect=False):
    uri = "https://api.github.com/repos/" + REPO + "/" + path
    req = urllib.request.Request(uri, headers={
        "Authorization": "Bearer " + token,
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28"})
    if redirect:
        try:
            urllib.request.build_opener(NoRedirect).open(req, timeout=35)
        except urllib.error.HTTPError as err:
            if err.code not in (302, 301, 307):
                raise ValueError("GitHub artifact request not authorized")
            url = err.headers.get("Location", "")
            if not url.startswith("https://") or "api.github.com" in url:
                raise ValueError("Unexpected artifact redirect")
            return url
        raise ValueError("GitHub artifact must return a redirect")
    with urllib.request.urlopen(req, timeout=30) as response:
        data = response.read(2_000_001)
        if len(data) > 2_000_000:
            raise ValueError("Oversized GitHub metadata")
        return json.loads(data)


def download_one(dest, token, label, run_id, artifact_id, expected_sha):
    run = api_request(f"actions/runs/{run_id}", token)
    if not (run.get("head_sha") == expected_sha and run.get("event") == "push"
            and run.get("head_branch") == "master"
            and run.get("status") == "completed"
            and run.get("conclusion") == "success"
            and run.get("path") == WORKFLOW
            and run.get("head_repository", {}).get("full_name") == REPO):
        raise ValueError("Unverified physical-run identity")
    jobs = api_request(f"actions/runs/{run_id}/jobs?per_page=100", token).get("jobs", [])
    if not any(j.get("name") == PHYSICAL and j.get("conclusion") == "success"
               and j.get("status") == "completed" for j in jobs):
        raise ValueError("Physical job has not succeeded")
    artifacts = api_request(f"actions/runs/{run_id}/artifacts?per_page=100", token).get("artifacts", [])
    name = f"v9-tf-45-physical-{run_id}-{expected_sha}"
    matching = [x for x in artifacts if x.get("id") == artifact_id
                and x.get("name") == name and x.get("expired") is False
                and 0 < x.get("size_in_bytes", 0) < 1_300_000_000]
    if len(matching) != 1:
        raise ValueError("Missing exact unexpired physical-run artifact")
    url = api_request(f"actions/artifacts/{artifact_id}/zip", token, redirect=True)
    with urllib.request.urlopen(urllib.request.Request(url), timeout=150) as source:
        with dest.open("xb") as file:
            copied = 0
            while True:
                part = source.read(2 * 1024 * 1024)
                if not part:
                    break
                copied += len(part)
                if copied > 1_300_000_000:
                    raise ValueError("Downloaded artifact larger than approved bound")
                file.write(part)
    if dest.stat().st_size < 10_000_000:
        raise ValueError("Physical evidence archive suspiciously small")
    print("PASS: verified and downloaded " + label + " SHA-bound physical 30k artifact")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--directory", type=Path, required=True)
    args = p.parse_args()
    token = os.environ.get("GITHUB_TOKEN")
    if not token:
        raise SystemExit("No read-only GitHub Actions token")
    if os.environ.get("GITHUB_REPOSITORY") != REPO:
        raise SystemExit("Unexpected repository; fail closed")
    args.directory.mkdir(exist_ok=False, parents=True)
    try:
        for label, run, artifact, sha in RUNS:
            download_one(args.directory / (label + ".zip"), token,
                         label, run, artifact, sha)
    except Exception:
        # Do not reveal pre-signed artifact URLs, auth headers or error bodies.
        raise SystemExit("BLOCK: physical artifact provenance/download failed")


if __name__ == "__main__":
    main()

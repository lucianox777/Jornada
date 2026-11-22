#!/usr/bin/env python3
"""Read-only GitHub Actions artifact preservation for the already finished V9 campaign #38.

No workflows, SQL, TRAIN/TEST, model activation, or GitHub writes. Copies ZIPs
to a caller-owned durable filesystem; never claims that filesystem is a NAS.
Source: V9_Standard_Linear_100k_Resultado_Run_44970012048.md (22/11/2026).
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import urllib.error
import urllib.parse
import urllib.request
import zipfile

REPO = "lucianox777/Jornada"
RUN_ID = 44970012048
RUN_ATTEMPT = 1
CODE_SHA = "30c71a0db1bdeac6ba536db5e428ade4d5c70c9c"
WINNER_SHA = "6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee"
# Expected SHA-256 is of the GitHub Actions downloadable ZIP, not of its members.
ARTIFACTS = {
    14732438742: ("prepare", "ba954dd0c3711a5df55055d76f4dc8a625a1118ccce21f4ce56e05336836b1ad"),
    14742152579: ("train", "22029b40ab51e6892f8071fae94d8c2c2d662348ae6dd3484614d95456ac9690"),
    14741794442: ("audit", "e1abc3e5d733836a7360a2511cb94a196ccac015407a6bdb97d447a0dd29bb41"),
    14742472448: ("validation", "64e2e8375b5a71f8697e0703496728552a0698998e2585b89439893246b59684"),
    14742611497: ("validation-status", "1a4da4fcca339f1dd27a1aa2ed0f29cc09d056f9ecf221798880d957b5e2dc0b"),
    14742492418: ("validation-corpus", "c2d682e4d90518d3584a1ed3f2a63aa8448ad183b1baec68feae284a7118bfc1"),
    14742452561: ("winner-operational", "24a890243dff4d988515ac17532ee3720bff0c0b01261aa36c9e0d74426d5de7"),
    14742233807: ("independent-test", "dc1f211549a61cfed6a1b5ee81b5e84580ef2eec6ad803b5425a449a489b3344"),
    14742890165: ("test-corpus", "e828414061da54e46dfbacb161bc8e62a073be967d25cb2678b5137578b521d2"),
    14742477968: ("closeout", "c2f703e0b0f155e6ae4a9c7e4e7f258f1c6b89803c803152942716834da89929"),
    14742621957: ("comparative-report", "9659e8593fbab3d5b21d1ab57189ed4bce26c0ce8fd9d17647dbee35761dead0"),
    14742084383: ("prepare-reference", "5f0f0d1b4f6e2bbe7831650eea167fb0b75b2f2d423085992c77ddf9e4e69880"),
    14742267026: ("preaudit-handoff", "7cb9b20f0602abf8a588a2dd09ec2d4422fcebcf7408cbbb51bf80511d68dced"),
    14742257307: ("validation-handoff", "643b59545a938b63d8dc0cfc9df1abbf0195f9620e0e95ac5d96ebf513251cc6"),
}


class SafeArtifactRedirectHandler(urllib.request.HTTPRedirectHandler):
    """Keep GitHub bearer credentials off cross-origin artifact ZIP redirects."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        target = urllib.parse.urlsplit(newurl)
        source = urllib.parse.urlsplit(req.full_url)
        if (target.scheme != "https" or not target.hostname
                or target.username is not None or target.password is not None):
            raise ValueError("artifact redirect target must be HTTPS without credentials")
        redirected = super().redirect_request(req, fp, code, msg, headers, newurl)
        if redirected is not None and (target.hostname, target.port) != (source.hostname, source.port):
            # urllib.request.HTTPRedirectHandler copies Authorization by default.
            # GitHub artifact ZIPs redirect to a separately hosted signed URL.
            redirected.remove_header("Authorization")
            redirected.remove_header("X-GitHub-Api-Version")
        return redirected


def api_json(url, token):
    request = urllib.request.Request(url, headers={
        "Authorization": "Bearer " + token,
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "jornada-v9-campaign38-preserver",
    })
    with urllib.request.build_opener(SafeArtifactRedirectHandler()).open(
            request, timeout=60) as response:
        return json.load(response)


def download_verified(artifact_id, expected_sha, destination, token):
    url = f"https://api.github.com/repos/{REPO}/actions/artifacts/{artifact_id}/zip"
    request = urllib.request.Request(url, headers={
        "Authorization": "Bearer " + token,
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "jornada-v9-campaign38-preserver",
    })
    digest = hashlib.sha256()
    # Never create the final path until hash and CRC checks have passed.
    fd, temporary = tempfile.mkstemp(prefix=".v9-artifact-", dir=destination)
    try:
        with os.fdopen(fd, "wb") as output:
            with urllib.request.build_opener(SafeArtifactRedirectHandler()).open(
                    request, timeout=120) as source:
                while chunk := source.read(1024 * 1024):
                    output.write(chunk)
                    digest.update(chunk)
            output.flush()
            os.fsync(output.fileno())
        if digest.hexdigest() != expected_sha:
            raise ValueError(f"artifact {artifact_id}: SHA256 mismatch; fail closed")
        with zipfile.ZipFile(temporary) as archive:
            if not archive.infolist() or archive.testzip() is not None:
                raise ValueError(f"artifact {artifact_id}: invalid ZIP/CRC")
            members = [x.filename for x in archive.infolist() if not x.is_dir()]
            if not members:
                raise ValueError(f"artifact {artifact_id}: no file members")
            if artifact_id == 14742452561:
                winner = [x for x in members if x.endswith(WINNER_SHA + ".operational.json")]
                if len(winner) != 1:
                    raise ValueError("winner package: expected exactly one pinned operational.json")
                with archive.open(winner[0]) as model:
                    h = hashlib.sha256()
                    while chunk := model.read(1024 * 1024):
                        h.update(chunk)
                if h.hexdigest() != WINNER_SHA:
                    raise ValueError("winner package: internal operational.json SHA256 mismatch")
        return temporary, members
    except BaseException:
        Path(temporary).unlink(missing_ok=True)
        raise


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--destination", required=True, type=Path,
                        help="Existing private durable directory; never use a public repository")
    parser.add_argument("--execute", action="store_true",
                        help="Explicitly download and verify; default only prints inventory")
    args = parser.parse_args(argv)
    inventory = {"repository": REPO, "run_id": RUN_ID, "attempt": RUN_ATTEMPT,
                 "code_sha": CODE_SHA, "winner_sha256": WINNER_SHA,
                 "artifact_count": len(ARTIFACTS),
                 "artifacts": [{"id": i, "role": name, "zip_sha256": digest}
                               for i, (name, digest) in ARTIFACTS.items()]}
    if not args.execute:
        print(json.dumps(inventory, indent=2))
        print("DRY RUN: no download, no preservation performed.", file=sys.stderr)
        return 0
    token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")
    if not token:
        parser.error("GH_TOKEN or GITHUB_TOKEN required; never pass token as CLI argument")
    # Inspect the supplied path BEFORE resolve(): resolve() erases a symlink
    # at the destination itself, defeating the intended non-symlink guard.
    raw_destination = args.destination.expanduser()
    if raw_destination.is_symlink():
        parser.error("destination must not be a symlink")
    destination = raw_destination.resolve()
    if not destination.is_dir():
        parser.error("destination must be an existing non-symlink private directory")
    if destination.stat().st_mode & 0o077:
        parser.error("destination must not be readable or writable by group/others (chmod 700)")
    if any(destination.iterdir()):
        parser.error("destination must be empty; refuse overwrite/partial preservation")
    run = api_json(f"https://api.github.com/repos/{REPO}/actions/runs/{RUN_ID}", token)
    if (run.get("head_sha") != CODE_SHA or run.get("run_attempt") != RUN_ATTEMPT
            or run.get("conclusion") != "success"):
        raise ValueError("run provenance mismatch; fail closed")
    # Check every pinned artifact still belongs to this run and has not expired.
    remote = api_json(
        f"https://api.github.com/repos/{REPO}/actions/runs/{RUN_ID}/artifacts?per_page=100", token)
    found = {x["id"]: x for x in remote.get("artifacts", [])}
    if not set(ARTIFACTS).issubset(found) or any(found[i].get("expired") for i in ARTIFACTS):
        raise ValueError("required artifacts missing/expired; fail closed")
    inventory["run_url"] = run.get("html_url")
    inventory["preservation_status"] = "IN_PROGRESS"
    # Failures leave already verified ZIPs available for controlled recovery,
    # but no COMPLETE manifest. Never mistake partial copying for preservation.
    for artifact_id, (role, expected) in ARTIFACTS.items():
        temporary, members = download_verified(artifact_id, expected, destination, token)
        final = destination / f"{artifact_id}-{role}.zip"
        os.replace(temporary, final)
        inventory["artifacts"][list(ARTIFACTS).index(artifact_id)]["members"] = members
        print(f"VERIFIED {artifact_id} {role}", file=sys.stderr)
    inventory["preservation_status"] = "COPIED_AND_VERIFIED_LOCAL_NOT_NAS_CERTIFIED"
    manifest = destination / "campaign38-preservation-manifest.json"
    with manifest.open("x", encoding="utf-8") as stream:
        json.dump(inventory, stream, indent=2, ensure_ascii=False)
        stream.write("\n")
        stream.flush()
        os.fsync(stream.fileno())
    print(str(manifest))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

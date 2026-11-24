#!/usr/bin/env python3
"""Fail-closed Git-mode premerge audit for directly invoked local Bash scripts.

Use tracked Git modes rather than filesystem modes so checkout behavior cannot
mask a PR that accidentally replaces a 100755 script with 100644.
Explicit "bash script.sh" is allowed to call a 100644 file; executing
"script.sh" directly is not. No script is executed during this scan.
"""
import argparse
import os
from pathlib import Path
import re
import subprocess
import tempfile

LOCAL = re.compile(
    r'(?:(?:\./)?(?:Solution/)?scripts/|'
    r'\$(?:\{[A-Za-z_][A-Za-z_0-9]*\}|[A-Za-z_][A-Za-z_0-9]*)/'
    r'|(?:\./))([A-Za-z_0-9.-]+\.sh)(?![A-Za-z_0-9.-])'
)
INTERPRETER = re.compile(
    r'(?:^|[\s;&|({])(?:bash|sh|source|\.|cat|test|grep|'
    r'printf|echo|chmod|stat|cp|mv|readlink|python3?)\s+["\']?$'
)
SHELL = re.compile(r'\.sh$')


def tracked_modes(root: Path) -> dict[str, str]:
    process = subprocess.run(
        ["git", "-C", str(root), "ls-files", "--stage", "-z"],
        check=True, capture_output=True)
    result = {}
    for item in process.stdout.split(b"\0"):
        if not item:
            continue
        meta, path = item.decode("utf-8").split("\t", 1)
        mode, _sha, stage = meta.split(" ")
        if stage != "0":
            raise ValueError("unmerged Git index entry in executable audit")
        result[path] = mode
    return result


def check(root: Path) -> list[str]:
    modes = tracked_modes(root)
    scripts = root / "Solution" / "scripts"
    workflows = root / ".github" / "workflows"
    if not scripts.is_dir() or not workflows.is_dir():
        raise ValueError("missing scripts/workflows directory")
    targets = list(workflows.glob("*.yml")) + list(workflows.glob("*.yaml"))
    targets += list(scripts.glob("*.sh"))
    if not targets:
        raise ValueError("no executable command surfaces found")
    direct = set()
    for file in targets:
        if not file.is_file() or file.is_symlink():
            raise ValueError(f"invalid command surface: {file}")
        for number, line in enumerate(file.read_text(encoding="utf-8").splitlines(), 1):
            raw = line.lstrip()
            if not raw or raw.startswith("#"):
                continue
            for match in LOCAL.finditer(line):
                name = match.group(1)
                before = line[:match.start()]
                # A path passed to an explicit interpreter or a file operation
                # is data, not direct shell execution.
                if INTERPRETER.search(before):
                    continue
                # Paths mentioned in assignment strings or doc comments do
                # not alter execution mode. Conservative if mixed with shell.
                if re.search(r"(?:^|[\s;])(?:export\s+)?[A-Za-z_]\w*=$", before):
                    continue
                candidate = "Solution/scripts/" + name
                if candidate not in modes:
                    raise ValueError(
                        f"untracked direct script {candidate} at "
                        f"{file.relative_to(root)}:{number}")
                direct.add(candidate)
    if not direct:
        raise ValueError("no directly invoked shell script discovered")
    return sorted(f"{path}: Git mode {modes[path]} (required 100755)"
                  for path in direct if modes[path] != "100755")


def self_test():
    with tempfile.TemporaryDirectory(prefix="jornada-direct-mode-") as location:
        root = Path(location)
        (root / ".github" / "workflows").mkdir(parents=True)
        scripts = root / "Solution" / "scripts"
        scripts.mkdir(parents=True)
        (root / ".github/workflows/check.yml").write_text(
            "jobs:\n  validate:\n    steps:\n"
            "      - run: ./Solution/scripts/direct.sh\n"
            "      - run: bash ./Solution/scripts/interpreted.sh\n")
        (scripts / "direct.sh").write_text(
            "#!/bin/bash\n\"$script_dir/sibling.sh\"\n")
        (scripts / "sibling.sh").write_text("#!/bin/bash\nexit 0\n")
        (scripts / "interpreted.sh").write_text("#!/bin/bash\nexit 0\n")
        subprocess.run(["git", "-C", str(root), "init", "-q"], check=True)
        subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
        failures = check(root)
        assert len(failures) == 2 and "direct.sh" in str(failures) \
            and "sibling.sh" in str(failures), failures
        os.chmod(scripts / "direct.sh", 0o755)
        os.chmod(scripts / "sibling.sh", 0o755)
        subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
        assert check(root) == [], "direct executable scripts must pass"
        assert tracked_modes(root)["Solution/scripts/interpreted.sh"] == "100644"
    print("DIRECT_EXECUTABLE_GIT_MODE_SELF_TEST: PASS (negative+positive)")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    if args.repo is None:
        parser.error("--repo required outside self-test")
    problems = check(args.repo.resolve())
    if problems:
        raise SystemExit("DIRECT_EXECUTABLE_GIT_MODE_GATE: FAIL\n" +
                         "\n".join(problems))
    print("DIRECT_EXECUTABLE_GIT_MODE_GATE: PASS")


if __name__ == "__main__":
    main()

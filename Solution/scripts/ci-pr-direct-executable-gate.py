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


def command_lines(file: Path):
    """Scan only actual YAML run commands (not 'paths:' filters or labels)."""
    lines = file.read_text(encoding="utf-8").splitlines()
    if file.suffix not in (".yml", ".yaml"):
        yield from enumerate(lines, 1)
        return
    active_indent = None
    run_step = re.compile(r"^(\s*)(?:-\s+)?run:\s*(.*)$")
    for number, line in enumerate(lines, 1):
        m = run_step.match(line)
        if m:
            active_indent = len(m.group(1)) if m.group(2) in ("|", ">", "|-", ">-") else None
            if active_indent is None:
                yield number, m.group(2)
            continue
        if active_indent is not None and line.strip():
            indent = len(line) - len(line.lstrip())
            if indent > active_indent:
                yield number, line
            else:
                active_indent = None


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
    # Include every tracked shell program in the repo, including install/,
    # database/ and container-test/, not only Solution/scripts/.
    other_shells = sorted(
        path for path in modes
        if path.endswith(".sh") and not path.startswith("Solution/scripts/"))
    direct = {}
    for file in targets:
        if not file.is_file() or file.is_symlink():
            raise ValueError(f"invalid command surface: {file}")
        for number, line in command_lines(file):
            raw = line.lstrip()
            if not raw or raw.startswith("#"):
                continue
            # Only the EXECUTABLE TOKEN of each shell command is direct.
            # Paths passed to bash/source, cp, echo, sed, python, PowerShell
            # Get-Content, or assigned as strings are NOT direct calls.
            for segment in re.split(r"[;&|]+", line):
                command = segment.strip()
                for _ in range(6):
                    prefix = re.match(
                        r"^(?:if|then|do|else|!|exec|env|time|sudo)\s+",
                        command)
                    if prefix is None:
                        break
                    command = command[prefix.end():]
                command = re.sub(
                    r"^(?:[A-Za-z_]\w*=\S+\s+)+", "", command)
                if not command:
                    continue
                first = command.split(None, 1)[0]
                if re.match(r"^[A-Za-z_]\w*=", first):
                    continue  # pure shell assignment, not a command
                match = LOCAL.search(first)
                candidate = ("Solution/scripts/" + match.group(1)) if match else None
                if candidate is None:
                    # Exact other tracked paths, including variable-prefixed
                    # paths like "$ROOT/install/container-test/entrypoint.sh".
                    # Never classify a basename alone from a different tree.
                    token = first.strip("\\\"'")
                    for other in other_shells:
                        relative = other.removeprefix("Solution/")
                        if (token == relative or token == other
                            or token.endswith("/" + relative)
                            or token.endswith("/" + other)):
                            candidate = other
                            break
                if candidate is None:
                    continue
                if candidate not in modes:
                    raise ValueError(
                        f"untracked directly invoked script {candidate} at "
                        f"{file.relative_to(root)}:{number}")
                direct.setdefault(candidate, f"{file.relative_to(root)}:{number}")
    if not direct:
        raise ValueError("no directly invoked shell script discovered")
    return sorted(f"{path}: Git mode {modes[path]} (required 100755); "
                  f"direct call {direct[path]}"
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
            "      - run: ./Solution/install/install-direct.sh\n"
            "      - run: bash ./Solution/scripts/interpreted.sh\n"
            "      - run: source \"$ROOT/scripts/interpreted.sh\"\n"
            "  on:\n    pull_request:\n      paths:\n"
            "        - \"Solution/scripts/interpreted.sh\"\n")
        installer = root / "Solution" / "install"
        installer.mkdir()
        (installer / "install-direct.sh").write_text("#!/bin/bash\\nexit 0\\n")
        (scripts / "direct.sh").write_text(
            "#!/bin/bash\n\"$script_dir/sibling.sh\"\n"
            "DRAFT=\"$ROOT/scripts/interpreted.sh\"\n"
            "echo \"# ./scripts/interpreted.sh\"\n"
            "bash \"$ROOT/scripts/interpreted.sh\"\n")
        (scripts / "sibling.sh").write_text("#!/bin/bash\nexit 0\n")
        (scripts / "interpreted.sh").write_text("#!/bin/bash\nexit 0\n")
        subprocess.run(["git", "-C", str(root), "init", "-q"], check=True)
        subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
        failures = check(root)
        assert len(failures) == 3 and "direct.sh" in str(failures) \
            and "sibling.sh" in str(failures) \
            and "install-direct.sh" in str(failures), failures
        os.chmod(scripts / "direct.sh", 0o755)
        os.chmod(scripts / "sibling.sh", 0o755)
        os.chmod(installer / "install-direct.sh", 0o755)
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

#!/usr/bin/env python3
"""Prove native/optional-Python V9 SQL startup with fake Docker (no physical SQL)."""
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
STARTER = ROOT / "scripts/v9-tf-start-disposable-sql.sh"
SHA = "a" * 40


class V9SqlStartupModesTests(unittest.TestCase):
    @staticmethod
    def _run(mode, *, wrong_sha=False):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            tools = root / "tools"
            tools.mkdir()
            events = root / "events"
            gh_env = root / "github-env"
            gh_env.touch()
            (tools / "openssl").write_text("""#!/bin/sh
printf '000000000000000000000000000000000000000000000000\n'
""")
            (tools / "docker").write_text("""#!/bin/sh
printf 'docker %s\n' "$*" >> "$TOOL_EVENTS"
if [ "$1" = run ]; then
  previous=
  for arg in "$@"; do
    if [ "$previous" = --env-file ]; then
      if grep -q '^MSSQL_MEMORY_LIMIT_MB=' "$arg"; then
        echo 'sql-cap-present' >> "$TOOL_EVENTS"
      else
        echo 'sql-cap-absent' >> "$TOOL_EVENTS"
      fi
    fi
    previous=$arg
  done
  echo fake-container-id
elif [ "$1" = exec ]; then
  exit 0
else
  exit 2
fi
""")
            (tools / "bash").write_text("""#!/bin/sh
echo "guard $*" >> "$TOOL_EVENTS"
[ "$2" = before-sql ] || exit 2
mkdir -p "$(dirname "$3")"
echo '{"status":"PASS_ADMISSION_ONLY_NOT_FIT_PROOF"}' > "$3"
""")
            (tools / "python3").write_text(f"""#!/bin/sh
if [ "$1" = - ]; then exec '{sys.executable}' "$@"; fi
echo "python-budget $*" >> "$TOOL_EVENTS"
output=; github_env=
while [ "$#" -gt 0 ]; do
  case "$1" in
    --output) shift; output=$1 ;;
    --github-env) shift; github_env=$1 ;;
  esac
  shift
done
[ -n "$output" ] && [ -n "$github_env" ] || exit 2
mkdir -p "$(dirname "$output")"
echo '{{"status":"PASS_ADMISSION_ONLY_NOT_FIT_PROOF","stage":"plan-sql","sqlMemoryLimitMb":2048,"sqlContainerMemoryMb":3072}}' > "$output"
printf 'V9_SQL_MEMORY_LIMIT_MB=2048\nV9_SQL_CONTAINER_MEMORY_MB=3072\n' >> "$github_env"
""")
            for exe in tools.iterdir():
                exe.chmod(0o700)
            env = os.environ.copy()
            env.update({
                "PATH": str(tools) + os.pathsep + env.get("PATH", ""),
                "GITHUB_ENV": str(gh_env),
                "GITHUB_RUN_ID": "112233",
                "GITHUB_RUN_ATTEMPT": "1",
                "GITHUB_SHA": SHA,
                "EXACT_SHA": ("b" * 40 if wrong_sha else SHA),
                "GITHUB_REF": "refs/heads/master",
                "GITHUB_EVENT_NAME": "workflow_dispatch",
                "GITHUB_ACTOR": "po",
                "GITHUB_REPOSITORY_OWNER": "po",
                "TOOL_EVENTS": str(events),
                "RUNNER_TEMP": str(root),
            })
            for key in ("V9_SQL_MEMORY_LIMIT_MB", "V9_SQL_CONTAINER_MEMORY_MB",
                        "V9_100K_DISABLE_PYTHON_SQL_BUDGET"):
                env.pop(key, None)
            if mode is not None:
                env["V9_100K_DISABLE_PYTHON_SQL_BUDGET"] = mode
            work = root / "work"
            work.mkdir()
            proc = subprocess.run(["/bin/bash", str(STARTER)], cwd=work,
                                  env=env, capture_output=True, text=True, timeout=20)
            return proc, events.read_text() if events.exists() else "", gh_env.read_text()

    def test_default_native_skips_python_and_caps(self):
        proc, events, env = self._run("true")
        self.assertEqual(proc.returncode, 0, proc.stderr)
        self.assertIn("guard ", events)
        self.assertNotIn("python-budget", events)
        self.assertIn("sql-cap-absent", events)
        self.assertNotIn("--memory ", events)
        self.assertNotIn("V9_SQL_MEMORY_LIMIT_MB=", env)
        self.assertIn("::add-mask::", proc.stdout)

    def test_python_opt_in_sets_sql_and_docker_caps_before_start(self):
        proc, events, env = self._run("false")
        self.assertEqual(proc.returncode, 0, proc.stderr)
        self.assertNotIn("guard ", events)
        self.assertIn("python-budget", events)
        self.assertIn("--memory 3072m", events)
        self.assertIn("sql-cap-present", events)
        self.assertIn("V9_SQL_MEMORY_LIMIT_MB=2048", env)
        self.assertIn("::add-mask::", proc.stdout)

    def test_unset_switch_preserves_legacy_30k_startup(self):
        proc, events, env = self._run(None)
        self.assertEqual(proc.returncode, 0, proc.stderr)
        self.assertNotIn("guard ", events)
        self.assertNotIn("python-budget", events)
        self.assertIn("sql-cap-absent", events)
        self.assertNotIn("--memory ", events)

    def test_sha_mismatch_rejected_before_container_and_secret_generation(self):
        proc, events, env = self._run("false", wrong_sha=True)
        self.assertNotEqual(proc.returncode, 0)
        self.assertIn("unauthorized", proc.stderr)
        self.assertNotIn("docker ", events)
        self.assertNotIn("::add-mask::", proc.stdout)
        self.assertNotIn("JORNADA_SQL_SA_PASSWORD=", env)


if __name__ == "__main__":
    unittest.main()

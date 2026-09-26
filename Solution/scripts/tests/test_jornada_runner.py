"""DT-11: regressões sem Docker, SQL Server ou PowerShell instalados."""
import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

SCRIPTS = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("jornada_dt11", SCRIPTS / "jornada-runner.py")
runner = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = runner
spec.loader.exec_module(runner)


class TestSafeRunner(unittest.TestCase):
    def test_three_namespaces_are_fixed(self):
        for profile, database in runner.DBS.items():
            with self.subTest(profile=profile):
                job = runner.plan(profile, "db", "status")
                self.assertEqual(job["database"], database)
                self.assertFalse(job["destructive"])

    def test_profile_crossing_and_shared_clean_are_refused(self):
        for profile, operation in (("local", "e2e"), ("e2e", "synthetic"),
                                   ("synthetic", "test"), ("e2e", "cluster-status")):
            with self.subTest(profile=profile, operation=operation):
                with self.assertRaises(ValueError):
                    runner.plan(profile, operation)
        for action in (None, "down", "clean", "drop"):
            with self.assertRaises(ValueError):
                runner.plan("local", "db", action)

    def test_all_resetting_operations_require_authorization(self):
        for profile, operation, action in (("local", "db", "reset"),
                                           ("e2e", "e2e", None),
                                           ("synthetic", "synthetic", None),
                                           ("local", "ddl-upgrade", None)):
            with self.subTest(profile=profile, operation=operation):
                job = runner.plan(profile, operation, action)
                self.assertTrue(job["destructive"])
                with patch.object(runner.subprocess, "run") as sub:
                    with self.assertRaises(ValueError):
                        runner.execute(job)
                    sub.assert_not_called()

    def test_dry_run_does_not_call_scripts_or_read_secrets(self):
        job = runner.plan("e2e", "e2e")
        with patch("builtins.print") as output, patch.object(runner.subprocess, "run") as sub:
            self.assertEqual(runner.execute(job, dry_run=True), 0)
            sub.assert_not_called()
            payload = json.loads(output.call_args.args[0])
            self.assertEqual(payload["database"], "JornadaE2E")
            self.assertFalse(payload["authorized"])

    def test_bash_delegation_uses_explicit_profile(self):
        for profile, db in runner.DBS.items():
            job = runner.plan(profile, "db", "status")
            with patch.object(runner.shutil, "which", return_value="/usr/bin/bash"):
                def fake_run(argv, cwd, env, check):
                    self.assertEqual(env["JORNADA_SQL_DATABASE_OVERRIDE"], db)
                    self.assertEqual(cwd, runner.ROOT)
                    self.assertTrue(argv[1].endswith("local-db.sh"))
                    return type("Result", (), {"returncode": 0})()
                with patch.object(runner.subprocess, "run", side_effect=fake_run):
                    self.assertEqual(runner.execute(job), 0)

    def test_powershell_synthetic_requires_separate_configuration(self):
        with tempfile.TemporaryDirectory() as d:
            previous = runner.ROOT
            runner.ROOT = Path(d)
            try:
                config = runner.ROOT / ".env.synthetic.local"
                config.write_text("JORNADA_SQL_DATABASE=JornadaLocal\n", encoding="utf-8")
                job = runner.plan("synthetic", "synthetic", shell="powershell")
                with patch.dict(os.environ, {"JORNADA_LOCAL_ENV_FILE": ""}):
                    with patch.object(runner.shutil, "which", return_value="pwsh"):
                        with self.assertRaises(ValueError):
                            runner.execute(job, allow_reset=True)
                        config.write_text("JORNADA_SQL_DATABASE=JornadaSyntheticDev\n", encoding="utf-8")
                        with patch.object(runner.subprocess, "run", return_value=type("R", (), {"returncode": 0})()) as sub:
                            self.assertEqual(runner.execute(job, allow_reset=True), 0)
                            self.assertEqual(sub.call_args.kwargs["env"]["JORNADA_LOCAL_ENV_FILE"], str(config))
            finally:
                runner.ROOT = previous

    def test_specialized_bash_scripts_pin_e2e_and_synthetic(self):
        e2e = (SCRIPTS / "local-e2e.sh").read_text(encoding="utf-8")
        synthetic = (SCRIPTS / "local-synthetic-calibration.sh").read_text(encoding="utf-8")
        local_db = (SCRIPTS / "local-db.sh").read_text(encoding="utf-8")
        self.assertIn('DB="JornadaE2E"', e2e)
        self.assertIn('JORNADA_SQL_DATABASE_OVERRIDE="$DB" "$ROOT/scripts/local-db.sh" reset', e2e)
        self.assertIn('DB="JornadaSyntheticDev"', synthetic)
        self.assertIn('JORNADA_SQL_DATABASE_OVERRIDE="$DB" "$ROOT/scripts/local-db.sh" up --no-synthetic-corpus', synthetic)
        self.assertIn('DATABASE_OVERRIDE=', local_db)

    def test_cli_refuses_implicit_e2e_reset(self):
        self.assertEqual(runner.main(["--profile", "e2e", "e2e"]), 2)


if __name__ == "__main__":
    unittest.main(verbosity=2)

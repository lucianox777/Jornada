#!/usr/bin/env python3
"""Regressão isolada do bootstrap DEV: não necessita Docker nem SQL."""
import contextlib
import importlib.util
import io
import os
from pathlib import Path
import re
import stat
import tempfile
import unittest
from unittest.mock import patch


HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("local_env_bootstrap", HERE / "local_env_bootstrap.py")
assert SPEC and SPEC.loader
bootstrap = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(bootstrap)

EXAMPLE = (
    "# Exemplo público, não usar como credencial implícita.\n"
    "JORNADA_SQL_SA_PASSWORD=Public_Example_Only!\n"
    "JORNADA_SQL_PORT=14333\n"
    "JORNADA_SQL_DATABASE=JornadaLocal\n"
)


class LocalEnvBootstrapTests(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory(prefix="jornada-env-bootstrap-")
        self.addCleanup(self.dir.cleanup)
        self.root = Path(self.dir.name)
        self.template = self.root / ".env.example"
        self.output = self.root / ".env"
        self.template.write_text(EXAMPLE, encoding="utf-8")

    def test_creates_unique_non_public_password_with_sql_complexity_and_no_secret_stdout(self):
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            self.assertTrue(bootstrap.ensure_local_env(self.template, self.output))
        generated = self.output.read_text(encoding="utf-8")
        password = re.search(r"(?m)^JORNADA_SQL_SA_PASSWORD=(.+)$", generated).group(1)
        self.assertRegex(password, r"^Jd!A9_[a-f0-9]{48}$")
        self.assertNotIn(password, output.getvalue())
        self.assertNotIn("Public_Example_Only!", generated)
        self.assertIn("JORNADA_SQL_DATABASE=JornadaLocal", generated)
        self.assertNotIn("\r", generated)
        if os.name == "posix":
            self.assertEqual(stat.S_IMODE(self.output.stat().st_mode), 0o600)

        second = self.root / ".env.2"
        self.assertTrue(bootstrap.ensure_local_env(self.template, second))
        self.assertNotEqual(generated, second.read_text(encoding="utf-8"))

    def test_existing_env_remains_byte_for_byte_unchanged_even_with_volume_guard(self):
        existing = b"JORNADA_SQL_SA_PASSWORD=custom_value!A1\r\nJORNADA_SQL_DATABASE=OutroBanco\r\n"
        self.output.write_bytes(existing)
        with patch.object(bootstrap, "_assert_no_existing_sql_volume") as guard:
            self.assertFalse(bootstrap.ensure_local_env(
                self.template, self.output, check_docker_volume=True
            ))
        guard.assert_not_called()
        self.assertEqual(self.output.read_bytes(), existing)

    def test_existing_volume_blocks_new_password_before_any_file_write(self):
        with patch.object(bootstrap.subprocess, "run") as run:
            run.return_value.returncode = 0
            run.return_value.stdout = "other_volume\njornada_sql_data\n"
            with self.assertRaisesRegex(RuntimeError, "Recupere o .env original"):
                bootstrap.ensure_local_env(
                    self.template, self.output, check_docker_volume=True
                )
        self.assertFalse(self.output.exists())

    def test_docker_failure_blocks_bootstrap_instead_of_assuming_absent_volume(self):
        with patch.object(bootstrap.subprocess, "run") as run:
            run.return_value.returncode = 1
            run.return_value.stdout = ""
            with self.assertRaisesRegex(RuntimeError, "Não foi possível consultar"):
                bootstrap.ensure_local_env(
                    self.template, self.output, check_docker_volume=True
                )
        self.assertFalse(self.output.exists())

    def test_volume_missing_allows_bootstrap(self):
        with patch.object(bootstrap.subprocess, "run") as run:
            run.return_value.returncode = 0
            run.return_value.stdout = "unrelated\n"
            self.assertTrue(bootstrap.ensure_local_env(
                self.template, self.output, check_docker_volume=True
            ))
        self.assertTrue(self.output.is_file())

    def test_missing_or_duplicate_template_password_fails_without_creating_file(self):
        for invalid in ("JORNADA_SQL_PORT=14333\n", EXAMPLE + "JORNADA_SQL_SA_PASSWORD=another\n"):
            with self.subTest(invalid=invalid):
                self.template.write_text(invalid, encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "exatamente uma vez"):
                    bootstrap.ensure_local_env(self.template, self.output)
                self.assertFalse(self.output.exists())


if __name__ == "__main__":
    unittest.main()

#!/usr/bin/env python3
"""No Docker needed: exercise real run/Popen call sites with synthetic credentials."""
from __future__ import annotations

import importlib.util
import os
from pathlib import Path
import subprocess
import unittest
from unittest import mock

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("coordination_lock_probe", HERE / "coordination-lock-probe.py")
assert SPEC and SPEC.loader
probe = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(probe)

SYNTHETIC = "Jornada_TestOnly_Mock!2026"
ROOT = Path("/synthetic-jornada-root")


class CredentialTransportTests(unittest.TestCase):
    def assert_safe_command(self, cmd: list[str], env: dict[str, str]) -> None:
        self.assertIn("exec", cmd)
        self.assertEqual(cmd[cmd.index("-e") + 1], "SQLCMDPASSWORD")
        self.assertFalse(any(SYNTHETIC in arg for arg in cmd))
        self.assertFalse(any(arg.startswith("SQLCMDPASSWORD=") for arg in cmd))
        self.assertEqual(env["SQLCMDPASSWORD"], SYNTHETIC)

    def test_scalar_subprocess_run_inherits_secret_without_argv(self) -> None:
        prior = os.environ.get("SQLCMDPASSWORD")
        completed = subprocess.CompletedProcess([], 0, stdout="1\n", stderr="")
        with mock.patch.object(probe.subprocess, "run", return_value=completed) as run:
            result = probe.sqlcmd(ROOT, "JornadaLocal", SYNTHETIC, "SELECT 1")
        self.assertIs(result, completed)
        self.assert_safe_command(run.call_args.args[0], run.call_args.kwargs["env"])
        self.assertEqual(run.call_args.kwargs["cwd"], ROOT)
        self.assertEqual(os.environ.get("SQLCMDPASSWORD"), prior)

    def test_holder_popen_inherits_secret_without_argv(self) -> None:
        prior = os.environ.get("SQLCMDPASSWORD")
        holder = mock.Mock()
        holder.poll.side_effect = [None, 0]
        holder.communicate.return_value = ("", "")
        holder.returncode = 0
        with (
            mock.patch.object(probe.subprocess, "Popen", return_value=holder) as popen,
            mock.patch.object(probe, "scalar", side_effect=["1", "1"]) as scalar,
            mock.patch.object(probe.time, "monotonic", side_effect=[0.0, 0.1, 1.5]),
        ):
            result = probe.probe(ROOT, "JornadaLocal", SYNTHETIC, "Jornada.Pipeline.Corpus", 2000)
        self.assertTrue(result["contentionConfirmed"])
        self.assertEqual(result["lockResult"], 1)
        self.assertGreaterEqual(result["waitMilliseconds"], 1000)
        self.assertEqual(scalar.call_count, 2)
        self.assert_safe_command(popen.call_args.args[0], popen.call_args.kwargs["env"])
        self.assertEqual(popen.call_args.kwargs["cwd"], ROOT)
        holder.kill.assert_not_called()
        self.assertEqual(os.environ.get("SQLCMDPASSWORD"), prior)


if __name__ == "__main__":
    unittest.main()

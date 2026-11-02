#!/usr/bin/env python3
"""No SQL and no physical corpus: test TF dispatch's exact-SHA and pin gates."""
import importlib.util
import json
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "v9-tf-30k-dispatch-gate.py"
spec = importlib.util.spec_from_file_location("v9_tf_dispatch", SCRIPT)
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)

SHA = "f" * 40
REF = "refs/heads/feat/v9-csharp-consolidation-20261102"
PIN = json.dumps({
    "PinnedIbgeReferenceVersionId": 1,
    "PinnedIbgePublishedSha256": "a" * 64,
    "PinnedIbgeMaterializedFrequencySha256": "b" * 64,
    "PinnedIbgeBootstrap": {
        "Provenance": "SINTETICO_IBGE", "People": 30000,
        "CorpusFingerprint": "c" * 64, "GeneratorVersion": "JORNADA_SYNTH_CORPUS_CSHARP_V1",
        "GeneratorSeed": 42, "GenerationHypothesesFingerprint": "d" * 64
    }
})


class TfDispatchGateTests(unittest.TestCase):
    def test_preflight_accepts_exact_branch_but_does_not_execute(self):
        report, pinned = gate.validate("preflight", SHA, SHA, SHA, REF)
        self.assertEqual(report["physical_evaluations_expected"], 135)
        self.assertFalse(report["physical_executed"])
        self.assertIsNone(pinned)

    def test_execution_requires_real_pinned_reference(self):
        with self.assertRaises(ValueError):
            gate.validate("execute_30k", SHA, SHA, SHA, REF)
        report, pinned = gate.validate("execute_30k", SHA, SHA, SHA, REF, PIN)
        self.assertEqual(pinned["PinnedIbgeBootstrap"]["People"], 30000)
        self.assertFalse(report["certified"])

    def test_stale_branch_or_checkout_or_bad_ref_fails(self):
        for actual, remote, ref in ((SHA, "e" * 40, REF), ("a" * 40, SHA, REF),
                                    (SHA, SHA, "refs/tags/v9")):
            with self.subTest(actual=actual, remote=remote, ref=ref):
                with self.assertRaises(ValueError):
                    gate.validate("execute_30k", SHA, actual, remote, ref, PIN)

    def test_noncanonical_or_duplicate_pinned_fields_rejected(self):
        for invalid in ('{"PinnedIbgeReferenceVersionId":1,"PinnedIbgeReferenceVersionId":2}',
                        PIN.replace("SINTETICO_IBGE", "UNVERIFIED"),
                        PIN.replace('"People": 30000', '"People": 30001')):
            with self.subTest(invalid=invalid[:45]):
                with self.assertRaises(ValueError):
                    gate.validate("execute_30k", SHA, SHA, SHA, REF, invalid)


if __name__ == "__main__":
    unittest.main()

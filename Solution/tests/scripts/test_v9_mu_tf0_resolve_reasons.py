"""Fail-closed regression for aggregate diagnostic Resolve reason accounting."""
import importlib.util
from pathlib import Path
import unittest

HERE = Path(__file__).resolve()
SPEC = importlib.util.spec_from_file_location(
    "mu_tf0_reasons", HERE.parents[2] / "scripts" / "v9-mu-tf0-controlled-audit.py")
audit = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(audit)


def row():
    return {
        "trueAccepted": 6306, "falseAccepted": 0,
        "conflicts": 610, "unresolved": 2324,
        "allSelectionObservations": 9240,
        "resolveReasonCounts": {
            "RESOLVIDO": 6306,
            "DOIS_CANDIDATOS_ACIMA_T_LINKAGE": 600,
            "MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE": 10,
            "ABAIXO_T_LINKAGE": 4,
            "SEM_CANDIDATO_NO_RULESET_BLOCKING": 2320,
        },
    }


class TestResolveReasonAccounting(unittest.TestCase):
    def test_reconciles_normal_conflict_and_abstention_totals(self):
        self.assertEqual(audit.check_reasons(row())["RESOLVIDO"], 6306)

    def test_fail_closed_incomplete_or_modified_metrics(self):
        for mutate in (
            lambda r: r.pop("resolveReasonCounts"),
            lambda r: r["resolveReasonCounts"].update(RESOLVIDO=6305),
            lambda r: r["resolveReasonCounts"].update({"DOIS_CANDIDATOS_ACIMA_T_LINKAGE": 0}),
            lambda r: r["resolveReasonCounts"].update({"CONFLITO_FICTICIO": 610}),
            lambda r: r.update(allSelectionObservations=9241),
            lambda r: r.update(unresolved=2323),
            lambda r: r["resolveReasonCounts"].update(RESOLVIDO=-1),
            lambda r: r["resolveReasonCounts"].update(RESOLVIDO=6306.0),
            lambda r: r["resolveReasonCounts"].update(RESOLVIDO=None),
        ):
            with self.subTest(mutate=mutate):
                x = row()
                mutate(x)
                with self.assertRaises(ValueError):
                    audit.check_reasons(x)

    def test_declared_conflict_kernel_categories_are_exhaustive(self):
        for reason in audit.CONFLICT_REASONS:
            x = row()
            x["resolveReasonCounts"] = {"RESOLVIDO": 6306,
                reason: 610, "ABAIXO_T_LINKAGE": 2324}
            self.assertEqual(audit.check_reasons(x)[reason], 610)


if __name__ == "__main__":
    unittest.main()

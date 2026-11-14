"""Static fail-closed contract for the full-schema V9 readiness preflight.

These tests do not execute SQL, RunAsync or an independent TEST campaign.
"""
from pathlib import Path
import re
import unittest


SOLUTION = Path(__file__).resolve().parents[2]
SQL_PATH = SOLUTION / "database" / "testing" / "20261114_V9_Operational_Schema_Readonly_Preflight.sql"
DECISION_PATH = SOLUTION / "docs" / "Decisao_GOV_V9_TEST_Esquema_Operacional_RunAsync_Gold_Serving_20261114.md"


class V9RealSchemaPreflightContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.sql = SQL_PATH.read_text(encoding="utf-8")
        cls.decision = DECISION_PATH.read_text(encoding="utf-8")

    def test_rejects_non_harness_before_schema_probe(self):
        sql = self.sql
        self.assertIn("IF DB_NAME() <> N'JornadaHarness'", sql)
        self.assertIn("THROW 52620", sql)
        self.assertLess(sql.index("THROW 52620"), sql.index("DECLARE @required"))

    def test_requires_true_canonical_schema_and_trusted_model_linkage(self):
        sql = self.sql
        for object_name in (
            "identidade.modelo_linkage",
            "identidade.modelo_linkage_v9_operacional",
            "identidade.linkage_run",
            "identidade.linkage_run_item",
            "identidade.linkage_resultado",
            "identidade.v_vinculo_corrente",
            "silver.pessoa_observacao",
            "identidade.blocking_chave",
            "gold.pessoa",
            "serving.registro_integrado",
        ):
            self.assertIn(object_name, sql)
        for token in ("sys.foreign_key_columns", "is_not_trusted=0",
                      "sys.check_constraints", "sys.triggers",
                      "ck_modelo_linkage_v9_operacional_contract",
                      "tr_modelo_linkage_v9_operacional_append_only",
                      "OBJECT_DEFINITION"):
            self.assertIn(token, sql)

    def test_preflight_neither_mutates_operational_tables_nor_certifies(self):
        sql = self.sql
        for verb in ("INSERT", "UPDATE", "DELETE", "MERGE", "TRUNCATE", "ALTER", "DROP", "EXEC"):
            self.assertIsNone(
                re.search(r"\b" + verb + r"\s+(?:INTO\s+)?(?:identidade|gold|serving|silver)\.", sql, re.I),
                f"forbidden persistent write or DDL in read-only preflight: {verb}")
        for token in ("SCHEMA_PREFLIGHT_PASS_NOT_RUNTIME_PARITY",
                      "runasync_status", "NOT_EXECUTED", "gold_serving_parity",
                      "NOT_MEASURED", "independent_test_100k", "BLOCKED",
                      "publication_authorized"):
            self.assertIn(token, sql)

    def test_scope_preserves_one_winner_publication_off_and_test_lock(self):
        doc = self.decision
        for token in ("#1488", "#1478", "TF4/81", "Publish=false",
                      "CONCLUIDO_SEM_PUBLICACAO",
                      "V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED",
                      "TEST 100k", "Gold", "Serving", "RunAsync",
                      "NÃO COMPROVADOS"):
            self.assertIn(token, doc)


    def test_real_schema_ci_uses_canonical_ddl_and_rejects_both_negative_cases(self):
        workflow = (SOLUTION.parent / ".github" / "workflows" /
                    "v9-real-schema-preflight.yml").read_text(encoding="utf-8")
        for expected in (
            "pull_request:", "Jornada_Fase1_v3.70.sql",
            "20261113_V9_Operational_Frozen_Model_Conversion.sql",
            "20261114_V9_Operational_Schema_Readonly_Preflight.sql",
            "v9-tf-start-disposable-sql.sh", "CREATE DATABASE JornadaHarness",
            "grep -q '52620'", "grep -q '52621'",
            "SCHEMA_PREFLIGHT_PASS_NOT_RUNTIME_PARITY",
            "runasync=NOT_EXECUTED", "gold_serving=NOT_MEASURED",
            "independent_test_100k=BLOCKED",
            "publication_authorized=false", "docker rm -f",
            "if: always()", "contents: read"):
            self.assertIn(expected, workflow)
        for forbidden in (
            "v9-v3-four-name-100k", "test_required: true",
            "prepare_sealed_test", "JornadaLocal"):
            self.assertNotIn(forbidden, workflow)


if __name__ == "__main__":
    unittest.main()

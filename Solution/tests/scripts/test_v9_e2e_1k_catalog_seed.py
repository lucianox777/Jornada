import hashlib
import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("catalog", ROOT / "scripts/v9-e2e-1k-catalog-seed.py")
CAT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CAT)


class CatalogSeedTests(unittest.TestCase):
    def test_exact_reviewed_seed_without_person_fixture(self):
        raw = (ROOT / "database/Jornada_Seed_Dev.sql").read_bytes()
        sql = CAT.derive(raw)
        self.assertEqual(hashlib.sha256(sql.encode()).hexdigest(), "a976dfda5ef42e15120d1318f80fd3274f54c85310567e5081fab6f73126f728")
        self.assertIn("MERGE ref.gestor", sql)
        self.assertIn("INSERT controle.credencial_api", sql)
        self.assertNotIn("INSERT gold.pessoa", sql)
        self.assertNotIn("INSERT silver.pessoa_observacao", sql)
        self.assertNotIn("INSERT identidade.linkage_run", sql)
        self.assertNotIn(CAT.LEGACY, sql)
        self.assertEqual(sql.count("V9_E2E_CATALOG_REQUIRES_EMPTY_ATTEMPT"), 2)
        self.assertIn("Jornada.V9.DevAttempt", sql)
        self.assertTrue(sql.endswith("COMMIT TRANSACTION;\n"))

    def test_seed_drift_rejected(self):
        raw = (ROOT / "database/Jornada_Seed_Dev.sql").read_bytes()
        with self.assertRaisesRegex(ValueError, "SEED_DRIFT"):
            CAT.derive(raw + b"\n")


if __name__ == "__main__":
    unittest.main()

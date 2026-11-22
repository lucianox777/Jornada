"""Nonpublishing synthetic Apoio ZIP contract; not an HTTP ingestion proof."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[3]
class ApoioZipNonpublishBoundary(unittest.TestCase):
    def test_fixture_and_cli_exist(self):
        base = ROOT / "Produtos/ApoioSecretarias"
        self.assertTrue((base / "executar_cli.py").is_file())
        self.assertTrue((base / "exemplos/entrada").is_dir())
        self.assertTrue((base / "exemplos/perfil.sintetico.json").is_file())
    def test_documented_default_and_zip_shape(self):
        s = (ROOT / "Produtos/ApoioSecretarias/README.md").read_text()
        for marker in ["enviar=false", "--nao-enviar", "manifest.json",
                       "pessoas.jsonl", "registros.jsonl", "Idempotency-Key"]:
            self.assertIn(marker, s)

if __name__ == "__main__":
    unittest.main()

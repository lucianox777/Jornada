"""Bidirectional Resultado.Api route contract regression."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
import json

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("resultado_gate", ROOT / "scripts/resultado-openapi-contract-gate.py")
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


class ResultadoOpenApiTests(unittest.TestCase):
    def test_real_contract_matches_code(self):
        self.assertEqual([], gate.validate(ROOT / "src/Jornada.Resultado.Api/Program.cs", ROOT / "openapi/resultado-v1.openapi.json"))

    def test_missing_contract_route_fails(self):
        original = json.loads((ROOT / "openapi/resultado-v1.openapi.json").read_text())
        del original["paths"]["/health"]
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "openapi.json"
            path.write_text(json.dumps(original))
            self.assertTrue(any("ausente do contrato" in error for error in gate.validate(ROOT / "src/Jornada.Resultado.Api/Program.cs", path)))

    def test_extra_contract_route_fails(self):
        original = json.loads((ROOT / "openapi/resultado-v1.openapi.json").read_text())
        original["paths"]["/api/v1/extra"] = {"get": {"responses": {"200": {"description": "extra"}}}}
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "openapi.json"
            path.write_text(json.dumps(original))
            self.assertTrue(any("ausente do código" in error for error in gate.validate(ROOT / "src/Jornada.Resultado.Api/Program.cs", path)))


if __name__ == "__main__":
    unittest.main()

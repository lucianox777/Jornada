"""Regression boundary for Resultado.Api; not a runtime E2E proof."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
class ResultadoApiBoundaryContract(unittest.TestCase):
    def test_protected_route_and_delegated_authority(self):
        s = (ROOT / "src/Jornada.Resultado.Api/Program.cs").read_text()
        for marker in ['app.MapGet("/api/v1/ingestao/resultados/{nomeArquivo}"',
                       'RequireAuthorization("jornada.ingestao.status")',
                       "credential.Type != AccessCredentialType.GESTOR",
                       "repository.FindLatestAsync(credential.PublicCode.Trim(), parsed, ct)",
                       "if (response.StatusCode != HttpStatusCode.OK)",
                       "http.Items[CandidateItem] = candidate;"]:
            self.assertIn(marker, s)
    def test_filename_and_sql_scope(self):
        s = (ROOT / "src/Jornada.Resultado.Api/Program.cs").read_text()
        for marker in ['EndsWith(".zip"', "value.Length > 260",
                       "g.codigo=@gestor", "b.nome_arquivo=@nomeArquivo"]:
            self.assertIn(marker, s)
    def test_openapi_gate_does_not_yet_cover_resultado(self):
        gate = (ROOT / "scripts/openapi-contract-gate.py").read_text()
        self.assertNotIn("Jornada.Resultado.Api", gate)

if __name__ == "__main__":
    unittest.main()

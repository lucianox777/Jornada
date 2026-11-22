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
    def test_resultado_has_separate_bidirectional_openapi_gate(self):
        gate = (ROOT / "scripts/resultado-openapi-contract-gate.py").read_text()
        self.assertIn('src/Jornada.Resultado.Api/Program.cs', gate)
        self.assertIn('openapi/resultado-v1.openapi.json', gate)

    def test_authorization_must_precede_detail_and_never_fallback_to_sql_identity(self):
        s = (ROOT / "src/Jornada.Resultado.Api/Program.cs").read_text()
        verifier = s[s.index("internal sealed class ResultadoAccessVerifier"):s.index("internal sealed record ResultadoOptions")]
        self.assertLess(verifier.index("credential.Type != AccessCredentialType.GESTOR"),
                        verifier.index("repository.FindLatestAsync("))
        self.assertLess(verifier.index("if (response.StatusCode != HttpStatusCode.OK)"),
                        verifier.index("http.Items[CandidateItem] = candidate;"))
        self.assertLess(verifier.index("http.Items[CandidateItem] = candidate;"),
                        verifier.index("JornadaAccessVerification.Accepted("))
        self.assertIn('request.Headers.TryAddWithoutValidation("X-Jornada-Access-Key", credential.AccessKey)', verifier)
        self.assertIn('request.Headers.TryAddWithoutValidation("X-Jornada-Gestor", credential.PublicCode.Trim())', verifier)
        self.assertNotIn("Results.Ok(", verifier)
        route = s[s.index('app.MapGet("/api/v1/ingestao/resultados/'):s.index('if (operationalSql is not null)')]
        self.assertIn('http.HttpContext.Items[ResultadoAccessVerifier.CandidateItem]!', route)
        self.assertIn('.RequireAuthorization("jornada.ingestao.status")', route)

    def test_sql_delivery_lookup_is_tenant_scoped_and_parameterized(self):
        s = (ROOT / "src/Jornada.Resultado.Api/Program.cs").read_text()
        sql = s[s.index('FindLatestDeliverySql = """'):s.index('CountDetailedItemsSql = """')]
        self.assertIn("JOIN ref.gestor g ON g.gestor_id=e.gestor_id", sql)
        self.assertIn("g.codigo=@gestor", sql)
        self.assertIn("b.nome_arquivo=@nomeArquivo", sql)
        self.assertIn("ORDER BY e.recebido_em DESC,e.entrega_id DESC", sql)
        method = s[s.index("public async Task<ResultadoCandidate?> FindLatestAsync"):s.index("public async Task<object> GetDetailAsync")]
        self.assertIn('AddParameter(command, "@gestor", DbType.String, gestor, 30)', method)
        self.assertIn('AddParameter(command, "@nomeArquivo", DbType.String, nomeArquivo, 260)', method)

if __name__ == "__main__":
    unittest.main()

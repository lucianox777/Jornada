"""Negative regressions for the Gate 6 source boundary (no database/network)."""
import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "check-segregation.py"
spec = importlib.util.spec_from_file_location("segregation_guard", SCRIPT)
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)


class SegregationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.write("Solution/Jornada.sln", 'Project = "Jornada.Contracts"')
        self.write("Solution/ApoioSecretarias/SolucaoApoioSecretarias.sln",
                   'Project = "Jornada.Integrador.CSharp"')
        self.write("Solution/src/Jornada.Contracts/Jornada.Contracts.csproj",
                   '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup /></Project>')
        self.write("Solution/config/governance/schema-approvals.json",
                   json.dumps({"contracts": [{"path":
                       "config/contracts/gestores/SMADS/pessoa/v5/pessoa.schema.json"}]}))
        self.write("Solution/ApoioSecretarias/config/governance/schema-approvals.SEHAB.json",
                   json.dumps({"contracts": [{"path": path}
                       for path in sorted(guard.EXPECTED_SUPPORT)]}))
        self.write("Solution/database/migrations/20260921_Pessoa_V5_Contrato_371.sql",
                   "INSERT INTO ref.gestor_pessoa_versao VALUES (N'SMADS');\n")
        schema = '{"type":"object"}\n'
        digest = hashlib.sha256(schema.encode("utf-8")).hexdigest()
        self.write("Solution/config/contracts/registros/AA01/v1/registro.schema.json", schema)
        self.write("Solution/ApoioSecretarias/config/contracts/registros/AA01/v1/registro.schema.json", schema)
        self.write("Solution/ApoioSecretarias/config/governance/factual-schema-sources.json",
                   json.dumps({"contracts": [{
                       "natureza": "BENEFICIO", "codigoTipo": "AA01", "tipoVersao": 1,
                       "path": "config/contracts/registros/AA01/v1/registro.schema.json",
                       "canonicalSource": "Solution/config/contracts/registros/AA01/v1/registro.schema.json",
                       "sha256": digest}]}))


    def write(self, name: str, content: str):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def assert_rejects(self, phrase: str):
        findings = guard.audit(self.root)
        self.assertTrue(any(phrase in problem for problem in findings), findings)

    def test_isolated_layout_passes(self):
        self.assertEqual([], guard.audit(self.root))

    def test_runtime_sehab_copy_must_match_support(self):
        self.write("Solution/ApoioSecretarias/config/contracts/gestores/SEHAB/pessoa/v5/pessoa.schema.json", "{}")
        self.write("Solution/config/contracts/gestores/SEHAB/pessoa/v5/pessoa.schema.json", "{\"different\":true}")
        self.assert_rejects("Cópia runtime SEHAB divergente")

    def test_runtime_sehab_copy_is_allowed_when_identical(self):
        self.write("Solution/ApoioSecretarias/config/contracts/gestores/SEHAB/pessoa/v5/pessoa.schema.json", "{}")
        self.write("Solution/config/contracts/gestores/SEHAB/pessoa/v5/pessoa.schema.json", "{}")
        self.assertEqual([], guard.audit(self.root))

    def test_principal_inventory_contamination_fails(self):
        self.write("Solution/config/governance/schema-approvals.json",
                   json.dumps({"contracts": [{"path":
                       "config/contracts/gestores/SEHAB/pessoa/v5/pessoa.schema.json"}]}))
        self.assert_rejects("Inventário principal")

    def test_support_inventory_omission_fails(self):
        self.write("Solution/ApoioSecretarias/config/governance/schema-approvals.SEHAB.json",
                   json.dumps({"contracts": []}))
        self.assert_rejects("titularidade exata")

    def test_main_solution_reference_fails(self):
        self.write("Solution/Jornada.sln", 'Project = "Jornada.Integrador.CSharp"')
        self.assert_rejects("inclui integrador")

    def test_product_project_reference_fails(self):
        self.write("Solution/src/Jornada.Contracts/Jornada.Contracts.csproj",
                   '<Project><ItemGroup><ProjectReference '
                   'Include="../../ApoioSecretarias/clients/Jornada.Integrador.CSharp/a.csproj" />'
                   '</ItemGroup></Project>')
        self.assert_rejects("referência externa proibida")

    def test_migration_registration_fails_but_comments_are_allowed(self):
        self.write("Solution/database/migrations/20260921_Pessoa_V5_Contrato_371.sql",
                   "-- SEHAB só no apoio\n/* N'SEHAB' histórico */\n"
                   "INSERT INTO ref.gestor_pessoa_versao VALUES (N'SMADS');")
        self.assertEqual([], guard.audit(self.root))
        self.write("Solution/database/migrations/20260921_Pessoa_V5_Contrato_371.sql",
                   "INSERT INTO ref.gestor_pessoa_versao VALUES (N'SEHAB');")
        self.assert_rejects("Migration principal registra")

    def test_test_only_external_schema_link_does_not_break_boundary(self):
        self.write("Solution/tests/Jornada.Integration.Tests/Jornada.Integration.Tests.csproj",
                   '<Project><ItemGroup><None '
                   'Include="../../ApoioSecretarias/config/contracts/gestores/SEHAB/v5.json" />'
                   '</ItemGroup></Project>')
        self.assertEqual([], guard.audit(self.root))


    def test_factual_schema_drift_in_support_is_rejected(self):
        self.write("Solution/ApoioSecretarias/config/contracts/registros/AA01/v1/registro.schema.json",
                   '{"type":"array"}\n')
        self.assert_rejects("Schema factual divergente")

    def test_canonical_factual_schema_drift_is_rejected(self):
        self.write("Solution/config/contracts/registros/AA01/v1/registro.schema.json",
                   '{"type":"array"}\n')
        self.assert_rejects("Schema factual divergente")


if __name__ == "__main__":
    unittest.main()

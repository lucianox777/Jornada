import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2] / "src" / "Jornada.Linkage.Runner"


class SilverSourceAuthTests(unittest.TestCase):
    def test_core_fields_and_link_are_authenticated(self):
        code = (ROOT / "V9OperationalSilverSourceAuthentication.cs").read_text(encoding="utf-8")
        for required in ("IsolationLevel.Snapshot", "silver.pessoa_observacao",
                         "ref.gestor", "identidade.vinculo_fonte",
                         "gold.pessoa", "v.ativo = 1", "v.status = N'RESOLVIDO'",
                         "source.Observation.CpfAusenteMotivo",
                         "source.Observation.NomeCompleto",
                         "source.Observation.DataNascimento",
                         "source.Observation.NomeMae", "source.Manager",
                         "RUNNER_V9_SOURCE_AUTH_ATTRIBUTE_MISMATCH",
                         "RUNNER_V9_SOURCE_AUTH_AMBIGUOUS_LINK",
                         "ResolutionAttributes is { Count: > 0 }"):
            self.assertIn(required, code)
        self.assertNotIn("seenGold", code)
        self.assertNotIn("TOP (", code)
        self.assertNotIn("NOLOCK", code)


if __name__ == "__main__":
    unittest.main()

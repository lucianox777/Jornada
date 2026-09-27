import contextlib
import io
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "preparador"))
from preparador import preparar

class Tests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.dest = Path(self.temp.name)
        self.args = SimpleNamespace(
            csv=ROOT / "preparador/fixtures/SEHAB/pessoas.csv",
            mapeamento=ROOT / "preparador/mapeamentos/sehab.synthetic.example.json",
            manifest=ROOT / "preparador/fixtures/SEHAB/manifest.json",
            schema=ROOT / "config/contracts/gestores/SEHAB/pessoa/v5/pessoa.schema.json",
            saida=self.dest, registros=None)

    def test_envelope_and_determinism(self):
        first = preparar(self.args)
        self.assertEqual(first, preparar(self.args))
        with zipfile.ZipFile(first) as z:
            self.assertEqual(z.namelist(), ["manifest.json", "pessoas.jsonl", "registros.jsonl"])
            self.assertEqual(len(z.read("pessoas.jsonl").splitlines()), 2)
            self.assertEqual(z.read("registros.jsonl"), b"")

    def test_v4_synthetic_fixture_for_dev_receiver(self):
        self.args.mapeamento = ROOT / "preparador/mapeamentos/sehab.synthetic.v4.example.json"
        self.args.manifest = ROOT / "preparador/fixtures/SEHAB/manifest.v4.json"
        self.args.schema = ROOT / "config/contracts/gestores/SEHAB/pessoa/v4/pessoa.schema.json"
        archive = preparar(self.args)
        with zipfile.ZipFile(archive) as z:
            people = z.read("pessoas.jsonl").splitlines()
            self.assertEqual(len(people), 2)
            self.assertIn(b'"pessoaSchemaVersao":4', z.read("manifest.json"))

    def test_no_inferred_missing_cpf_reason(self):
        source = self.dest / "missing.csv"
        source.write_text("id;codigo;nome;nascimento;cpf;cpf_ausente_motivo;mae\n"
                          "P1;C1;Pessoa Sintetica;1990-01-01;;;\n", encoding="utf-8")
        self.args.csv = source
        with self.assertRaisesRegex(ValueError, "motivo"):
            preparar(self.args)

    def test_schema_hash_is_required(self):
        modified = self.dest / "wrong.schema.json"
        modified.write_bytes(self.args.schema.read_bytes() + b" ")
        self.args.schema = modified
        with self.assertRaisesRegex(ValueError, "SHA-256"):
            preparar(self.args)

if __name__ == "__main__":
    unittest.main()

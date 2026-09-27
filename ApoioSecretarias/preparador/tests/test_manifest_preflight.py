"""Preflight rejects malformed manifests before writing deterministic ZIPs (synthetic DEV only)."""
import hashlib
import json
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "preparador"))
from preparador import preparar


class ManifestPreflightTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.work = Path(self.temp.name)
        self.output = self.work / "output"
        self.source_manifest = json.loads(
            (ROOT / "preparador/fixtures/SEHAB/manifest.json").read_text(encoding="utf-8")
        )
        self.args = SimpleNamespace(
            csv=ROOT / "preparador/fixtures/SEHAB/pessoas.csv",
            mapeamento=ROOT / "preparador/mapeamentos/sehab.synthetic.example.json",
            manifest=self.work / "manifest.json",
            schema=ROOT / "config/contracts/gestores/SEHAB/pessoa/v5/pessoa.schema.json",
            saida=self.output,
            registros=None,
        )

    def manifest(self, **changes):
        document = dict(self.source_manifest)
        document.update(changes)
        self.args.manifest.write_text(
            json.dumps(document, ensure_ascii=False), encoding="utf-8"
        )
        return document

    def rejects(self, expected):
        with self.assertRaisesRegex(ValueError, expected):
            preparar(self.args)
        self.assertEqual(list(self.output.glob("*.zip")) if self.output.exists() else [], [])

    def test_canonical_manifest_still_produces_stable_zip(self):
        self.manifest()
        path = preparar(self.args)
        self.assertEqual(path, preparar(self.args))
        self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), path.stem.rsplit("_", 1)[-1])
        with zipfile.ZipFile(path) as archive:
            self.assertEqual(archive.namelist(), ["manifest.json", "pessoas.jsonl", "registros.jsonl"])

    def test_only_people_may_omit_factual_context(self):
        self.manifest(natureza=None, codigoTipo=None, tipoVersao=None)
        path = preparar(self.args)
        self.assertTrue(path.is_file())

    def test_facts_require_all_three_factual_fields(self):
        self.manifest(natureza=None, codigoTipo=None, tipoVersao=None)
        facts = self.work / "registros.jsonl"
        facts.write_text('{"idPessoaEntrega":"SINTETICO"}\n', encoding="utf-8")
        self.args.registros = facts
        self.rejects("registros.jsonl.*exige")

    def test_partial_factual_context_rejected_even_without_facts(self):
        self.manifest(codigoTipo=None)
        self.rejects("informados juntos")

    def test_invalid_factual_metadata_rejected(self):
        for changes, expected in (
            ({"natureza": "DESCONHECIDA"}, "natureza inválida"),
            ({"codigoTipo": "A1"}, "codigoTipo"),
            ({"tipoVersao": 0}, "tipoVersao"),
            ({"tipoVersao": True}, "tipoVersao"),
        ):
            with self.subTest(changes=changes):
                self.manifest(**changes)
                self.rejects(expected)

    def test_formato_must_be_v2_integer(self):
        for version in (1, 3, "2", True, None):
            with self.subTest(version=version):
                self.manifest(formatoVersao=version)
                self.rejects("formatoVersao deve ser 2")

    def test_person_schema_version_matches_mapping(self):
        self.manifest(pessoaSchemaVersao=4)
        self.rejects("pessoaSchemaVersao")

    def test_origin_code_matches_receiver_length_and_alphabet(self):
        for origin in ("sehab", "", "X" * 81, "OUTRA/ORIGEM"):
            with self.subTest(origin=origin):
                self.manifest(codigoSistemaOrigem=origin)
                self.rejects("codigoSistemaOrigem")
        self.manifest(codigoSistemaOrigem="X")
        self.assertTrue(preparar(self.args).is_file())

    def test_reference_requires_valid_datetime_and_explicit_offset(self):
        for stamp in ("2026-09-27", "2026-09-27T08:00:00",
                      "2026-02-30T08:00:00-03:00", "invalid"):
            with self.subTest(timestamp=stamp):
                self.manifest(dataReferencia=stamp)
                self.rejects("dataReferencia")

    def test_optional_person_base_code_is_validated(self):
        self.manifest(codigoBasePessoaOrigem="BASE-1")
        self.assertTrue(preparar(self.args).is_file())
        # Falhas subsequentes não podem alterar o ZIP já produzido.
        before = list(self.output.glob("*.zip"))
        self.manifest(codigoBasePessoaOrigem="PESSOA / CPF")
        with self.assertRaisesRegex(ValueError, "codigoBasePessoaOrigem"):
            preparar(self.args)
        self.assertEqual(before, list(self.output.glob("*.zip")))

    def test_duplicate_manifest_keys_are_rejected(self):
        self.args.manifest.write_text(
            '{"formatoVersao":2,"formatoVersao":3}', encoding="utf-8"
        )
        self.rejects("campo duplicado")

    def test_oversize_manifest_is_rejected_before_packaging(self):
        self.manifest(observacao="X" * (64 * 1024))
        self.rejects("excede 64 KiB")

    def test_invalid_json_types_are_rejected(self):
        self.args.manifest.write_text("[]", encoding="utf-8")
        self.rejects("objeto JSON")
        self.args.manifest.write_bytes(b"\xff")
        self.rejects("UTF-8")


if __name__ == "__main__":
    unittest.main()

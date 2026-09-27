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


    def test_factual_records_preflight_and_hash_binding(self):
        self.manifest()
        self.args.registro_schema = ROOT / "config/contracts/registros/AA01/v1/registro.schema.json"
        facts = self.work / "registros.jsonl"
        valid = {"idPessoaEntrega": "P-001", "codigoRegistroOrigem": "SYN-AA01-001",
                 "operacao": "INCLUSAO", "dataInicioConcessao": "2026-09-01",
                 "situacaoVigencia": "VIGENTE", "valorConcedido": 12.5}
        self.args.registros = facts

        def write(*rows):
            facts.write_text("".join(json.dumps(row, ensure_ascii=False) + "\n"
                                     for row in rows), encoding="utf-8")
        write(valid)
        built = preparar(self.args)
        with zipfile.ZipFile(built) as archive:
            self.assertEqual(json.loads(archive.read("registros.jsonl").splitlines()[0]), valid)

        self.output = self.work / "rejected"
        self.args.saida = self.output
        self.args.registro_schema = None
        self.rejects("--registro-schema")
        self.args.registro_schema = ROOT / "config/contracts/registros/AA01/v1/registro.schema.json"

        for row, expected in (
            ({**valid, "valorConcedido": "R$ 12"}, "contrato factual"),
            ({**valid, "codigoTipo": "OUTRO"}, "contrato factual"),
            ({**valid, "idPessoaEntrega": "INEXISTENTE"}, "sem pessoa"),
            ({**valid, "dataInicioConcessao": "2026-02-30"}, "contrato factual"),
        ):
            with self.subTest(row=row):
                write(row)
                self.rejects(expected)

        self.manifest(tipoVersao=2)
        write(valid)
        self.rejects("contrato factual não reconhecido")
        self.manifest()
        self.args.registro_schema = self.work / "schema-adulterado.json"
        self.args.registro_schema.write_bytes(
            (ROOT / "config/contracts/registros/AA01/v1/registro.schema.json").read_bytes() + b" ")
        self.rejects("fora do contrato versionado")
        self.args.registro_schema = ROOT / "config/contracts/registros/AA01/v1/registro.schema.json"
        facts.write_text('{"idPessoaEntrega":"P-001","idPessoaEntrega":"P-002"}\n', encoding="utf-8")
        self.rejects("chave duplicada")
        facts.write_text('{"idPessoaEntrega":"P-001","valorConcedido":NaN}\n', encoding="utf-8")
        self.rejects("constante JSON inválida")
        facts.write_bytes(b"\xff")
        self.rejects("não é UTF-8")

    def test_empty_facts_preserve_person_only_mode(self):
        self.manifest()
        self.args.registros = self.work / "empty.jsonl"
        self.args.registros.write_bytes(b"")
        self.args.registro_schema = None
        self.assertTrue(preparar(self.args).is_file())


if __name__ == "__main__":
    unittest.main()

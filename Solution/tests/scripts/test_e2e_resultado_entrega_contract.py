#!/usr/bin/env python3
"""Adversarial offline contract for Resultado.Api DEV E2E receipt, issue #1853."""
import copy
import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "resultado_receipt", ROOT / "scripts/e2e-resultado-entrega.py")
MOD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MOD)
SHA = "a" * 64
NAME = "ENTREGA_SEHAB_SEHAB_v2_" + SHA + ".zip"
ID = "11111111-1111-4111-8111-111111111111"


def fixture():
    return {
        "nomeArquivoConsultado": NAME,
        "entregasEncontradasParaOMesmoNome": 2,
        "finalizado": True,
        "entrega": {"entregaId": ID, "gestor": "SEHAB", "nomeArquivo": NAME,
                    "status": "PROCESSADA", "payloadSha256": SHA.upper(),
                    "bytesRecebidos": 4096},
        "processamento": {
            "lotes": [{"loteId": "22222222-2222-4222-8222-222222222222",
                       "loteSeq": 1, "loteTotal": 1,
                       "status": "PROCESSADO", "qtdPessoas": 1, "qtdRegistros": 1}],
            "resumoItens": [
                {"classeItem": "PESSOA", "resultado": "INCLUIDO",
                 "quantidadeDetalhada": 1, "quantidadeConsolidada": 0,
                 "totalObservado": 1},
                {"classeItem": "REGISTRO", "resultado": "RETRANSMITIDO",
                 "quantidadeDetalhada": 1, "quantidadeConsolidada": 0,
                 "totalObservado": 1}],
            "itensDetalhadosDisponiveis": 2, "itensConsolidados": 0,
            "detalheItemAItemIntegral": True}
    }


class ResultadoDevReceiptTests(unittest.TestCase):
    def test_valid_detail_and_retention(self):
        self.assertEqual(MOD.check_detail(fixture(), NAME, ID, SHA, 4096),
                         {"payloadSha256Verified": True, "payloadBytesVerified": True,
                          "lotesProcessados": 1, "resumoItensConsistente": True})
        retained = fixture()
        for row in retained["processamento"]["resumoItens"]:
            row["quantidadeConsolidada"] = row["quantidadeDetalhada"]
            row["quantidadeDetalhada"] = 0
        retained["processamento"]["itensDetalhadosDisponiveis"] = 0
        retained["processamento"]["itensConsolidados"] = 2
        retained["processamento"]["detalheItemAItemIntegral"] = False
        self.assertTrue(MOD.check_detail(retained, NAME, ID, SHA, 4096)["resumoItensConsistente"])

    def test_exact_local_zip_identity_and_size_are_required(self):
        content = b"synthetic-fixture-archive-bytes"
        sha = hashlib.sha256(content).hexdigest()
        with tempfile.TemporaryDirectory() as root:
            package = Path(root) / ("ENTREGA_SEHAB_SEHAB_v2_" + sha + ".zip")
            package.write_bytes(content)
            self.assertEqual(MOD.read_payload_identity(package, package.name),
                             (sha, len(content)))
            with self.assertRaises(ValueError):
                MOD.read_payload_identity(package, "outro.zip")
            package.write_bytes(b"tampered")
            with self.assertRaises(ValueError):
                MOD.read_payload_identity(package, package.name)
            package.unlink()
            with self.assertRaises(ValueError):
                MOD.read_payload_identity(package, package.name)

        for bad in (4095, 4097, 0, True, "4096", None):
            with self.subTest(size=bad):
                with self.assertRaises(ValueError):
                    MOD.check_detail(fixture(), NAME, ID, SHA, bad)

    def test_wrong_delivery_hash_scope_or_payload_fails(self):
        for key, bad in (("entregaId", "33333333-3333-4333-8333-333333333333"),
                         ("nomeArquivo", "outro.zip"), ("gestor", "SMADS"),
                         ("status", "PENDENTE"), ("payloadSha256", "b" * 64),
                         ("bytesRecebidos", 0), ("bytesRecebidos", 4095),
                         ("bytesRecebidos", 4097), ("bytesRecebidos", True)):
            with self.subTest(key=key):
                obj = fixture()
                obj["entrega"][key] = bad
                with self.assertRaises(ValueError):
                    MOD.check_detail(obj, NAME, ID, SHA, 4096)
        for digest in ("invalid", "b" * 64):
            with self.subTest(digest=digest):
                with self.assertRaises(ValueError):
                    MOD.check_detail(fixture(), NAME, ID, digest, 4096)

    def test_incomplete_duplicate_or_noninteger_lot_fails(self):
        for key, bad in (("qtdPessoas", 2), ("qtdRegistros", True),
                         ("qtdRegistros", -1), ("status", "PENDENTE")):
            with self.subTest(key=key):
                obj = fixture()
                obj["processamento"]["lotes"][0][key] = bad
                with self.assertRaises(ValueError):
                    MOD.check_detail(obj, NAME, ID, SHA, 4096)
        obj = fixture()
        obj["processamento"]["lotes"].append(copy.deepcopy(obj["processamento"]["lotes"][0]))
        with self.assertRaises(ValueError):
            MOD.check_detail(obj, NAME, ID, SHA, 4096)

    def test_lot_sequence_is_complete_and_consistent(self):
        valid = fixture()
        first = valid["processamento"]["lotes"][0]
        first["loteTotal"] = 2
        first["qtdRegistros"] = 0
        second = copy.deepcopy(first)
        second["loteId"] = "33333333-3333-4333-8333-333333333333"
        second["loteSeq"] = 2
        second["qtdPessoas"] = 0
        second["qtdRegistros"] = 1
        valid["processamento"]["lotes"].append(second)
        self.assertEqual(MOD.check_detail(valid, NAME, ID, SHA, 4096)["lotesProcessados"], 2)

        for field, bad in (("loteSeq", 0), ("loteSeq", True),
                           ("loteSeq", 2), ("loteTotal", 0),
                           ("loteTotal", False), ("loteTotal", 2),
                           ("loteTotal", 100000000)):
            with self.subTest(field=field, bad=bad):
                obj = fixture()
                obj["processamento"]["lotes"][0][field] = bad
                with self.assertRaises(ValueError):
                    MOD.check_detail(obj, NAME, ID, SHA, 4096)
        for field, bad in (("loteSeq", 1), ("loteTotal", 3),
                           ("loteTotal", "2")):
            with self.subTest(second_lot_field=field, bad=bad):
                obj = copy.deepcopy(valid)
                obj["processamento"]["lotes"][1][field] = bad
                with self.assertRaises(ValueError):
                    MOD.check_detail(obj, NAME, ID, SHA, 4096)
        missing = copy.deepcopy(valid)
        missing["processamento"]["lotes"][1]["loteSeq"] = 3
        missing["processamento"]["lotes"][0]["loteTotal"] = 3
        missing["processamento"]["lotes"][1]["loteTotal"] = 3
        with self.assertRaises(ValueError):
            MOD.check_detail(missing, NAME, ID, SHA, 4096)

    def test_summary_class_totals_must_reconcile_with_processed_lots(self):
        # A valid per-row sum and retention total cannot mask an extra item.
        for row_index in (0, 1):
            with self.subTest(extra_item_in_row=row_index):
                obj = fixture()
                row = obj["processamento"]["resumoItens"][row_index]
                row["quantidadeDetalhada"] += 1
                row["totalObservado"] += 1
                obj["processamento"]["itensDetalhadosDisponiveis"] += 1
                with self.assertRaisesRegex(ValueError, "diverge das contagens"):
                    MOD.check_detail(obj, NAME, ID, SHA, 4096)

        for classe in ("DESCONHECIDA", "REGISTRO"):
            with self.subTest(classe=classe):
                obj = fixture()
                obj["processamento"]["resumoItens"][0]["classeItem"] = classe
                with self.assertRaises(ValueError):
                    MOD.check_detail(obj, NAME, ID, SHA, 4096)

    def test_ambiguous_processing_or_retention_fails(self):
        for key, bad in (("quantidadeDetalhada", 2),
                         ("quantidadeConsolidada", True), ("totalObservado", 3)):
            with self.subTest(key=key):
                obj = fixture()
                obj["processamento"]["resumoItens"][0][key] = bad
                with self.assertRaises(ValueError):
                    MOD.check_detail(obj, NAME, ID, SHA, 4096)
        for key, bad in (("itensDetalhadosDisponiveis", 1),
                         ("itensConsolidados", 1), ("detalheItemAItemIntegral", False)):
            with self.subTest(key=key):
                obj = fixture()
                obj["processamento"][key] = bad
                with self.assertRaises(ValueError):
                    MOD.check_detail(obj, NAME, ID, SHA, 4096)
        obj = fixture()
        obj["processamento"]["resumoItens"].append(
            copy.deepcopy(obj["processamento"]["resumoItens"][1]))
        with self.assertRaises(ValueError):
            MOD.check_detail(obj, NAME, ID, SHA, 4096)


if __name__ == "__main__":
    unittest.main()

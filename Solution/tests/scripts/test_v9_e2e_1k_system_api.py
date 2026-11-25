import copy
import importlib.util
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[2] / "scripts" / "v9-e2e-1k-system-api.py"
spec = importlib.util.spec_from_file_location("system_api", SOURCE)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class ResultadoCensusTests(unittest.TestCase):
    def setUp(self):
        self.delivery = dict(fileName="synthetic.zip", deliveryId="synthetic-delivery",
                             manager="SEHAB", zipSha256="A"*64, zipBytes=123,
                             versions=3, facts=2)
        self.detail = dict(nomeArquivoConsultado="synthetic.zip",
                           entregasEncontradasParaOMesmoNome=1, finalizado=True,
                           entrega=dict(entregaId="synthetic-delivery", gestor="SEHAB",
                                        nomeArquivo="synthetic.zip", status="PROCESSADA",
                                        payloadSha256="a"*64, bytesRecebidos=123),
                           processamento=dict(lotes=[dict(loteId="00000000-0000-4000-8000-000000000001",
                                                           loteSeq=1, loteTotal=1, status="PROCESSADO",
                                                           qtdPessoas=3, qtdRegistros=2)],
                           resumoItens=[dict(classeItem="PESSOA", resultado="NOVO", quantidadeDetalhada=3,
                                            quantidadeConsolidada=0, totalObservado=3),
                                       dict(classeItem="REGISTRO", resultado="NOVO", quantidadeDetalhada=2,
                                            quantidadeConsolidada=0, totalObservado=2)],
                           itensDetalhadosDisponiveis=5, itensConsolidados=0, detalheItemAItemIntegral=True))

    def test_exact_delivery_and_census(self):
        self.assertEqual(m.check_detail(self.detail, self.delivery), 1)

    def test_wrong_bytes_or_count_fails(self):
        for field, value in (("payloadSha256", "b"*64), ("bytesRecebidos", 124)):
            d = copy.deepcopy(self.detail)
            d["entrega"][field] = value
            with self.assertRaises(ValueError):
                m.check_detail(d, self.delivery)

    def test_wrong_lot_or_item_total_fails(self):
        d = copy.deepcopy(self.detail)
        d["processamento"]["lotes"][0]["qtdRegistros"] = 1
        with self.assertRaises(ValueError):
            m.check_detail(d, self.delivery)
        d = copy.deepcopy(self.detail)
        d["processamento"]["resumoItens"][0]["totalObservado"] = 2
        with self.assertRaises(ValueError):
            m.check_detail(d, self.delivery)


if __name__ == "__main__":
    unittest.main()

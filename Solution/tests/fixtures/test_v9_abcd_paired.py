import importlib.util
import unittest
from pathlib import Path

SOURCE = Path(__file__).resolve().parents[2] / "scripts" / "v9-abc-paired-blocking.py"
spec = importlib.util.spec_from_file_location("v9_paired", SOURCE)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

def observation(number, person, name, phonetic, manager, split="TEST", cpf=""):
    return {"observacao_id": str(number), "base_person_id": person,
            "particao": split, "gestor": manager, "cpf": cpf,
            "nome_normalizado": name, "nome_fonetico": phonetic,
            "nome_mae_normalizado": "ANA", "nome_mae_fonetico": "AN",
            "data_nascimento": "1980-01-01"}

class PairedABCDTests(unittest.TestCase):
    def test_phonetic_gain_and_e0_nonregression(self):
        rows = [observation(1, "P1", "MARIA", "MR", "G1"),
                observation(2, "P1", "MARYA", "MR", "G2")]
        result = module.evaluate(rows)["TEST"]
        self.assertEqual(result["m_eligible"], 1)
        self.assertEqual(result["arms"]["A0"]["m_retrieved"], 0)
        self.assertEqual(result["arms"]["A1"]["m_retrieved"], 1)
        self.assertEqual(result["paired_phonetic"]["A"]["m_only_on"], 1)
        for arm in module.ARMS:
            self.assertGreaterEqual(result["arms"][arm + "+E0"]["m_retrieved"],
                                    result["arms"][arm]["m_retrieved"])

    def test_cpf_and_partition_fail_closed(self):
        rows = [observation(1, "P1", "MARIA", "MR", "G1"),
                observation(2, "P1", "MARIA", "MR", "G2", cpf="123")]
        self.assertEqual(module.evaluate(rows)["TEST"]["m_eligible"], 0)
        rows[1]["cpf"] = ""
        rows[1]["particao"] = "TRAIN"
        with self.assertRaises(ValueError):
            module.evaluate(rows)

if __name__ == "__main__":
    unittest.main()

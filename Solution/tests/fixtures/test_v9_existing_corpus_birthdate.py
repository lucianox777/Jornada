import csv
import importlib.util
import json
import pathlib
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("adapter", ROOT / "scripts/v9-existing-corpus-to-memory.py")
adapter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(adapter)

class ExistingCorpusBirthDate(unittest.TestCase):
    def test_real_csv_column_maps_to_birth_date(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = pathlib.Path(directory)
            source = folder / "observations.csv"
            with source.open("w", encoding="utf-8", newline="") as handle:
                writer = csv.DictWriter(handle, fieldnames=adapter.REQUIRED)
                writer.writeheader()
                writer.writerow({"observacao_id":"a1","base_person_id":"p1",
                                 "particao":"TRAIN","gestor":"G1","nome":"ANA SILVA",
                                 "nome_mae":"MARIA SILVA","data_nascimento":"1990-05-17","cpf":""})
                writer.writerow({"observacao_id":"a2","base_person_id":"p1",
                                 "particao":"TRAIN","gestor":"G2","nome":"ANA SILVA",
                                 "nome_mae":"MARIA SILVA","data_nascimento":"1990-05-17","cpf":""})
            report = adapter.convert(source, folder / "out")
            rows = [json.loads(line) for line in
                    (folder / "out/v9-observations-train.jsonl").read_text().splitlines()]
            self.assertEqual(report["rows_by_split"]["TRAIN"], 2)
            self.assertEqual([r["birth_date"] for r in rows], ["1990-05-17"] * 2)
            self.assertEqual([r["birth_year"] for r in rows], ["1990"] * 2)
            self.assertTrue(all(r["gestor"] in {"G1","G2"} for r in rows))

if __name__ == "__main__":
    unittest.main()

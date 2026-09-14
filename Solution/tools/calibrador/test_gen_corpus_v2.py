import random
import unittest
from datetime import date

import gen_corpus_v2 as g


class CorpusV2Tests(unittest.TestCase):
    def test_cpf_has_valid_check_digits(self):
        rng = random.Random(42)
        for _ in range(1000):
            cpf = g.cpf_valid(rng)
            self.assertEqual(11, len(cpf))
            d = [int(x) for x in cpf]
            s1 = sum(x * w for x, w in zip(d[:9], range(10, 1, -1)))
            dv1 = 0 if 11 - (s1 % 11) >= 10 else 11 - (s1 % 11)
            s2 = sum(x * w for x, w in zip(d[:10], range(11, 1, -1)))
            dv2 = 0 if 11 - (s2 % 11) >= 10 else 11 - (s2 % 11)
            self.assertEqual([dv1, dv2], d[-2:])

    def test_cns_provisional_is_structurally_valid(self):
        rng = random.Random(43)
        for _ in range(1000):
            cns = g.cns_valid(rng)
            self.assertEqual(15, len(cns))
            self.assertIn(cns[0], "789")
            weighted = sum(int(x) * w for x, w in zip(cns, range(15, 0, -1)))
            self.assertEqual(0, weighted % 11)

    def test_invalid_check_digit_breaks_cns_rule(self):
        rng = random.Random(44)
        cns = g.cns_valid(rng)
        invalid = g.invalidate_check_digit(cns)
        weighted = sum(int(x) * w for x, w in zip(invalid, range(15, 0, -1)))
        self.assertNotEqual(0, weighted % 11)

    def test_empirical_m_excludes_missing_pairs(self):
        rows = [
            dict(base_person_id="P1", nome="A", nome_mae="M", data_nascimento=date(2000,1,1), evaluation_weight=1),
            dict(base_person_id="P1", nome="A", nome_mae=None, data_nascimento=date(2000,1,1), evaluation_weight=1),
            dict(base_person_id="P1", nome="B", nome_mae="M", data_nascimento=None, evaluation_weight=1),
        ]
        m = g.empirical_m(rows)
        self.assertEqual(3, m["NOME"]["eligible_pairs"])
        self.assertEqual(1, m["NOME_MAE"]["eligible_pairs"])
        self.assertEqual(1, m["NASCIMENTO"]["eligible_pairs"])
        self.assertAlmostEqual(1/3, m["NOME"]["m_exact_empirical"], places=6)
        self.assertEqual(1.0, m["NOME_MAE"]["m_exact_empirical"])
        self.assertEqual(1.0, m["NASCIMENTO"]["m_exact_empirical"])

    def test_cns_scenarios_never_change_base_person_truth(self):
        rng = random.Random(45)
        people = []
        for i in range(50):
            people.append({
                "base_person_id": f"P{i}",
                "cns": g.cns_valid(rng),
                "data_nascimento": date(1940 + i, 1, 1),
            })
        before = [p["base_person_id"] for p in people]
        g.apply_cns_scenarios(people, rng, .1, .1, .1)
        self.assertEqual(before, [p["base_person_id"] for p in people])
        self.assertTrue(any(p["cns_scenario"] != "CLEAN" for p in people))


if __name__ == "__main__":
    unittest.main()

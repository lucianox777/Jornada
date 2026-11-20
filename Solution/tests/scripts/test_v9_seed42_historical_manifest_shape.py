#!/usr/bin/env python3
"""Static regression guard for the seed42 manifest compatibility in the isolated V9 generator.

This does not replace physical byte-for-byte corpus/gate verification.
"""
import re
import unittest
from pathlib import Path

MATERIALIZER = (Path(__file__).resolve().parents[2] /
                "src/Jornada.Linkage.SyntheticCorpus.Parametrized/SyntheticCorpusMaterializer.cs")


class FrozenSeed42ManifestShapeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = MATERIALIZER.read_text(encoding="utf-8")
        cls.before_demographic = cls.source.split(
            "if (source.Demographic is { } demographic)", 1)[0]

    def test_exact_historical_conditions_all_required(self):
        guard = self.before_demographic.split(
            "// The frozen seed42/30k clean demographic reference", 1)[1]
        guard = guard.split("var historical = JsonNode.Parse", 1)[0]
        for condition in (
            "generation.Options.Seed == 42",
            "generation.Options.People == 30000",
            'generation.Options.ErrorProfile == "clean"',
            "source.Demographic is not null",
            "generation.Options.TrainPercent == 60",
            "generation.Options.ValidationPercent == 20",
            "generation.Options.TestPercent == 20",
            "generation.Options.BrazilianNameErrors is null",
            "generation.Options.StratifiedErrors is null",
        ):
            with self.subTest(condition=condition):
                self.assertIn(condition, guard)
        self.assertEqual(guard.count("if ("), 1)

    def test_only_two_fields_removed_before_demographic_annotation(self):
        code = self.before_demographic.split("var historical = JsonNode.Parse", 1)[1]
        removals = re.findall(r'historical\.Remove\("([^"]+)"\)', code)
        self.assertEqual(removals, ["synthetic_population", "partition_percentages"])
        self.assertIn("JsonSerializer.Serialize(historical, IndentedJsonOptions)", code)
        self.assertIn("synthetic_population = generation.Options.People", self.source)
        self.assertIn("partition_percentages = new", self.source)


if __name__ == "__main__":
    unittest.main()

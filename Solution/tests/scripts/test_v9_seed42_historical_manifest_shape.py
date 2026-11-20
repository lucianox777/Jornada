#!/usr/bin/env python3
"""Static regression guard for the seed42 manifest compatibility in the isolated V9 generator.

This does not replace physical byte-for-byte corpus/gate verification.
"""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MATERIALIZER = (ROOT /
                "src/Jornada.Linkage.SyntheticCorpus.Parametrized/SyntheticCorpusMaterializer.cs")


class IsolatedPartitionContractTests(unittest.TestCase):
    """Read-only structural checks; no corpus or scientific campaign is generated."""

    @classmethod
    def setUpClass(cls):
        project = ROOT / "src/Jornada.Linkage.SyntheticCorpus.Parametrized"
        cls.generator = (project / "SyntheticCorpusGenerator.cs").read_text(encoding="utf-8")
        cls.models = (project / "SyntheticCorpusModels.cs").read_text(encoding="utf-8")
        cls.materializer = (project / "SyntheticCorpusMaterializer.cs").read_text(encoding="utf-8")

    def test_partition_assignment_uses_configured_thresholds(self):
        self.assertIn("split < options.TrainPercent / 100d", self.generator)
        self.assertIn("split < (options.TrainPercent + options.ValidationPercent) / 100d", self.generator)
        self.assertIn('person.Partition = split < options.TrainPercent / 100d ? "TRAIN"', self.generator)
        self.assertIn('? "VALIDATION" : "TEST"', self.generator)
        self.assertIn("var split = random.NextUnitInterval();", self.generator)

    def test_partition_percentages_are_finite_bounded_and_sum_to_100(self):
        for token in (
            "double.IsFinite(TrainPercent)",
            "double.IsFinite(ValidationPercent)",
            "double.IsFinite(TestPercent)",
            "TrainPercent < 0", "ValidationPercent < 0", "TestPercent < 0",
            "TrainPercent > 100", "ValidationPercent > 100", "TestPercent > 100",
            "Math.Abs(TrainPercent + ValidationPercent + TestPercent - 100) > 1e-9",
        ):
            with self.subTest(token=token):
                self.assertIn(token, self.models)

    def test_partition_threshold_boundary_cases(self):
        """Model the two ordered comparisons used by the generator at boundaries."""
        def partition(split, train, validation):
            return ("TRAIN" if split < train / 100
                    else "VALIDATION" if split < (train + validation) / 100
                    else "TEST")

        cases = (
            (0.0, 60, 20, "TRAIN"),
            (0.599999999, 60, 20, "TRAIN"),
            (0.6, 60, 20, "VALIDATION"),
            (0.799999999, 60, 20, "VALIDATION"),
            (0.8, 60, 20, "TEST"),
            (0.999999999, 60, 20, "TEST"),
            (0.0, 0, 0, "TEST"),
            (0.0, 0, 100, "VALIDATION"),
            (0.0, 100, 0, "TRAIN"),
            (0.699999999, 70, 20, "TRAIN"),
            (0.7, 70, 20, "VALIDATION"),
            (0.9, 70, 20, "TEST"),
        )
        for split, train, validation, expected in cases:
            with self.subTest(split=split, train=train, validation=validation):
                self.assertEqual(partition(split, train, validation), expected)

    def test_cli_config_and_explicit_percentage_precedence(self):
        program = (ROOT / "src/Jornada.Linkage.SyntheticCorpus.Parametrized/Program.cs").read_text(encoding="utf-8")
        self.assertIn('Get(values, "calibrator-config")', program)
        self.assertIn('JsonDocument.Parse(File.ReadAllText(calibratorConfigPath))', program)
        self.assertIn('c.GetProperty("sampling").GetProperty(key).GetDouble()', program)
        for cli, config, fallback in (
            ("train-percent", "trainPercent", 60),
            ("validation-percent", "validationPercent", 20),
            ("test-percent", "testPercent", 20),
        ):
            with self.subTest(cli=cli):
                self.assertIn(
                    f'Percent("{cli}", "{config}", {fallback})',
                    program,
                )
        self.assertIn('values.ContainsKey("people") ? GetInt(values, "people", 30_000) : ConfigPeople()', program)
        self.assertIn("People: People()", program)
        self.assertIn("values.ContainsKey(cliKey)", program)
        self.assertIn("? GetDouble(values, cliKey, fallback)", program)
        self.assertIn(": ConfigPercent(configKey, fallback)", program)
        self.assertLess(program.index("options.Validate();"), program.index("return options;"))

    def test_partition_integrity_runtime_harness_is_wired_to_diagnostic_ci(self):
        harness = (ROOT / "tests/PartitionIntegritySmoke/Program.cs").read_text(encoding="utf-8")
        project = (ROOT / "tests/PartitionIntegritySmoke/PartitionIntegritySmoke.csproj").read_text(encoding="utf-8")
        workflow = (ROOT.parent / ".github/workflows/p0-tf-locked-generator-audit.yml").read_text(encoding="utf-8")
        self.assertIn("Jornada.Linkage.SyntheticCorpus.Parametrized.csproj", project)
        self.assertIn("PartitionIntegritySmoke/PartitionIntegritySmoke.csproj", workflow)
        self.assertIn("await ExpectInvalidOptions();", harness)
        self.assertEqual(harness.count("await ExpectRejected("), 16)
        self.assertIn("using Jornada.Contracts;", harness)
        self.assertNotIn("throw new Exception(", harness)

    def test_materializer_rejects_cross_partition_or_orphan_observations_before_io(self):
        for token in (
            "new Dictionary<string, string>(StringComparer.Ordinal)",
            'person.Partition is not ("TRAIN" or "VALIDATION" or "TEST")',
            "!partitionsByPerson.TryAdd(person.BasePersonId, person.Partition)",
            "string.IsNullOrWhiteSpace(person.BasePersonId)",
            "var observationIds = new HashSet<string>(StringComparer.Ordinal)",
            "string.IsNullOrWhiteSpace(observation.ObservationId)",
            "!observationIds.Add(observation.ObservationId)",
            "!partitionsByPerson.TryGetValue(observation.BasePersonId, out var partition)",
            "!string.Equals(observation.Partition, partition, StringComparison.Ordinal)",
            'throw new InvalidDataException("Observação órfã ou com partição divergente da identidade base.")',
        ):
            with self.subTest(token=token):
                self.assertIn(token, self.materializer)
        self.assertLess(
            self.materializer.index("var partitionsByPerson ="),
            self.materializer.index("Directory.CreateDirectory(outputDirectory);"),
        )

    def test_observations_inherit_base_person_partition_and_csv_exports_it(self):
        # Prevent identity leakage across partitions in the isolated generator.
        self.assertIn("Partition = person.Partition,", self.generator)
        self.assertIn("BasePersonId = person.BasePersonId,", self.generator)
        self.assertIn('"base_person_id", "particao", "nome"', self.materializer)
        self.assertIn('"observacao_id", "base_person_id", "particao", "gestor"', self.materializer)
        self.assertIn("person.Partition,", self.materializer)
        self.assertIn("observation.Partition,", self.materializer)

    def test_manifest_partition_metadata_uses_validated_options(self):
        for expected in (
            "synthetic_population = generation.Options.People",
            "train = generation.Options.TrainPercent",
            "validation = generation.Options.ValidationPercent",
            "test = generation.Options.TestPercent",
        ):
            with self.subTest(expected=expected):
                self.assertIn(expected, self.materializer)
        self.assertIn("options.Validate();", (
            ROOT / "src/Jornada.Linkage.SyntheticCorpus.Parametrized/Program.cs"
        ).read_text(encoding="utf-8"))
        self.assertIn("options.Validate();", self.generator)
        self.assertIn("generation.Options.People == 30000", self.materializer)
        self.assertIn('generation.Options.ErrorProfile == "clean"', self.materializer)
        self.assertIn("source.Demographic is not null", self.materializer)

    def test_nonhistorical_manifest_retains_partition_declarations(self):
        self.assertIn('historical.Remove("synthetic_population")', self.materializer)
        self.assertIn('historical.Remove("partition_percentages")', self.materializer)
        self.assertIn("generation.Options.TrainPercent == 60", self.materializer)
        self.assertIn("generation.Options.ValidationPercent == 20", self.materializer)
        self.assertIn("generation.Options.TestPercent == 20", self.materializer)


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

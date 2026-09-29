using System.IO;
using System.Text.Json;
using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class SyntheticEvaluationManifestContractTests
{
    private static readonly string A = new('a', 64);
    private static readonly string B = new('b', 64);
    private static readonly string C = new('c', 64);

    [Test]
    public void RoundTrip_PreservesExplicitProvenanceAndIndependentTruth()
    {
        var manifest = Example();
        var json = SyntheticEvaluationManifestContract.Serialize(manifest);
        var restored = SyntheticEvaluationManifestContract.Deserialize(json);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"contractVersion\""));
            Assert.That(json, Does.Contain("\"externalSourceReference\""));
            Assert.That(json, Does.Contain("\"realCadastreErrorRates\": \"UNAVAILABLE\""));
            Assert.That(restored.SchemaVersion, Is.EqualTo(1));
            Assert.That(restored.Partitions.Select(p => p.Name),
                Is.EquivalentTo(new[] { "TRAIN", "VALIDATION", "TEST" }));
            Assert.That(restored.EffectiveRates.Single().ObservedRate, Is.EqualTo(0.6));
            Assert.That(restored.Truth.DerivedFromMotorDecision, Is.False);
            Assert.That(restored.Truth.AvailableToScorer, Is.False);
            Assert.That(restored.ReservedFamily.State, Is.EqualTo("AWAITING_EXTERNAL_SPECIFICATION"));
            Assert.That(json, Does.EndWith("\n"));
        });
    }

    [Test]
    public void UnknownContractFieldsAndVersions_FailExplicitly()
    {
        var json = SyntheticEvaluationManifestContract.Serialize(Example());
        Assert.Multiple(() =>
        {
            Assert.That(
                () => SyntheticEvaluationManifestContract.Deserialize(
                    json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal)),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(
                () => SyntheticEvaluationManifestContract.Deserialize(
                    json.Replace("\"contractVersion\":", "\"unreviewedField\": true,\n  \"contractVersion\":",
                        StringComparison.Ordinal)),
                Throws.TypeOf<JsonException>());
        });
    }

    [Test]
    public void PartitionLeakageAndTotals_AreRejected()
    {
        var source = Example();
        Assert.Multiple(() =>
        {
            Assert.That(
                () => SyntheticEvaluationManifestContract.Serialize(
                    source with { PartitionMethod = "BY_OBSERVATION_ID" }),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(
                () => SyntheticEvaluationManifestContract.Serialize(
                    source with { Partitions = new[]
                    {
                        new SyntheticManifestPartition("TRAIN", 6, 10),
                        new SyntheticManifestPartition("VALIDATION", 2, 3),
                        new SyntheticManifestPartition("TRAIN", 2, 3)
                    } }),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(
                () => SyntheticEvaluationManifestContract.Serialize(
                    source with { ObservationCount = 17 }),
                Throws.TypeOf<InvalidDataException>());
        });
    }

    [Test]
    public void IncorrectObservedRateOrUnattributedExternalEvidence_Fails()
    {
        var source = Example();
        var rate = source.EffectiveRates.Single();
        Assert.Multiple(() =>
        {
            Assert.That(
                () => SyntheticEvaluationManifestContract.Serialize(
                    source with { EffectiveRates = new[] { rate with { ObservedRate = 0.5 } } }),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(
                () => SyntheticEvaluationManifestContract.Serialize(
                    source with { EffectiveRates = new[]
                    {
                        rate with { ParameterBasis = "EXTERNAL_EVIDENCE", ExternalSourceReference = null }
                    } }),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(
                () => SyntheticEvaluationManifestContract.Serialize(
                    source with { EffectiveRates = new[]
                    {
                        rate with { EligibleCount = 0, ObservedCount = 0, ObservedRate = 0.0 }
                    } }),
                Throws.TypeOf<InvalidDataException>());
        });
    }

    [Test]
    public void SecretiveOrUnreviewedReservedFamily_Fails()
    {
        var source = Example();
        Assert.Multiple(() =>
        {
            Assert.That(
                () => SyntheticEvaluationManifestContract.Serialize(
                    source with { ReservedFamily = source.ReservedFamily with { Seed = 42 } }),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(
                () => SyntheticEvaluationManifestContract.Serialize(
                    source with { ReservedFamily = source.ReservedFamily with
                    {
                        State = "SEALED",
                        Seed = 99,
                        FamilyVersion = "EXTERNAL_V1",
                        ExternalAuthorReference = "independent-review",
                        SpecificationSha256 = B,
                        UsedForThresholdSelection = true
                    } }),
                Throws.TypeOf<InvalidDataException>());
        });
        var sealedManifest = source with
        {
            ReservedFamily = source.ReservedFamily with
            {
                State = "SEALED",
                Seed = 99,
                FamilyVersion = "EXTERNAL_V1",
                ExternalAuthorReference = "independent-review",
                SpecificationSha256 = B
            }
        };
        Assert.DoesNotThrow(() => SyntheticEvaluationManifestContract.Serialize(sealedManifest));
    }

    [Test]
    public void TruthLeakageAndClaimsOfRepresentativeness_Fail()
    {
        var source = Example();
        Assert.Multiple(() =>
        {
            Assert.That(
                () => SyntheticEvaluationManifestContract.Serialize(
                    source with { Truth = source.Truth with { AvailableToScorer = true } }),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(
                () => SyntheticEvaluationManifestContract.Serialize(
                    source with { Evidence = source.Evidence with
                    {
                        PopulationRepresentativeness = "REPRESENTATIVE"
                    } }),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(
                () => SyntheticEvaluationManifestContract.Serialize(
                    source with { Artifacts = new[]
                    {
                        new SyntheticManifestArtifact("PEOPLE", "../escape.csv", A),
                        new SyntheticManifestArtifact("OBSERVATIONS", "observacoes.csv", B),
                        new SyntheticManifestArtifact("GROUND_TRUTH", "gabarito.json", C)
                    } }),
                Throws.TypeOf<InvalidDataException>());
        });
    }

    private static SyntheticEvaluationManifest Example() => new(
        SchemaVersion: SyntheticEvaluationManifestContract.SchemaVersion,
        ContractVersion: SyntheticEvaluationManifestContract.Version,
        GeneratorVersion: "JORNADA_SYNTH_CORPUS_CSHARP_V1",
        RulesetVersion: "JORNADA_SYNTH_CORPUS_V2_RULES_CSHARP_V1",
        RngVersion: "XOSHIRO_TEST",
        Seed: 42,
        ScenarioId: "correlated/provisional",
        PartitionState: "PROVISIONAL",
        PartitionMethod: SyntheticEvaluationManifestContract.PartitionMethod,
        PeopleCount: 10,
        ObservationCount: 16,
        InputFingerprintSha256: A,
        Partitions: new[]
        {
            new SyntheticManifestPartition("TRAIN", 6, 10),
            new SyntheticManifestPartition("VALIDATION", 2, 3),
            new SyntheticManifestPartition("TEST", 2, 3)
        },
        Artifacts: new[]
        {
            new SyntheticManifestArtifact("PEOPLE", "pessoas_verdade.csv", A),
            new SyntheticManifestArtifact("OBSERVATIONS", "observacoes.csv", B),
            new SyntheticManifestArtifact("GROUND_TRUTH", "gabarito.json", C)
        },
        EffectiveRates: new[]
        {
            new SyntheticManifestEffectiveRate(
                "correlated", "G0", "WITH_CPF", "MISSING_MOTHER",
                0.5, 10, 6, 0.6, "EXPLORATORY_ASSUMPTION", null)
        },
        ErrorDependencies: Array.Empty<SyntheticManifestErrorDependency>(),
        Truth: new SyntheticManifestTruthDeclaration(
            "GROUND_TRUTH", "base_person_id", "observacao_id",
            "base_person_id", false, false),
        Evidence: new SyntheticManifestEvidenceDeclaration(
            "UNAVAILABLE", "NOT_CLAIMED", "#31"),
        ReservedFamily: new SyntheticManifestReservedFamily(
            "AWAITING_EXTERNAL_SPECIFICATION", null, null, null, null,
            true, true, false, false));

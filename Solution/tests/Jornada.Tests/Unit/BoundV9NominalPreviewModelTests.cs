using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class BoundV9NominalPreviewModelTests
{
    private static FellegiSunterJointNominalCellV9[] Cells()
    {
        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        return levels.SelectMany(person => levels.Select(mother =>
        {
            var exact = person == CompositeNominalLevelV9.ExactFull
                && mother == CompositeNominalLevelV9.ExactFull;
            return new FellegiSunterJointNominalCellV9(
                person, mother, exact ? 0.2m : 0.8m / 80m,
                exact ? 0.1m : 0.9m / 80m);
        })).ToArray();
    }

    private static V9NominalPreviewBindingManifest Manifest(
        IReadOnlyCollection<FellegiSunterJointNominalCellV9> cells) => new(
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), 1,
            CompositeNominalComparisonV9.ContractVersion,
            FrozenFellegiSunterJointNominalV9.Freeze(
                cells, CompositeNominalComparisonV9.ContractVersion).ContentFingerprint,
            new string('a', 64), new string('b', 64),
            "independent-label-reference", "train-partition-reference",
            "raw-evidence-extractor-v9",
            BoundV9NominalPreviewModel.NoTermFrequencyStatus,
            BoundV9NominalPreviewModel.DiagnosticOnlyStatus);

    private static readonly IReadOnlyDictionary<string, decimal> Parameters =
        new Dictionary<string, decimal>
        {
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.PriorMatchProbability] = 0.01m
        };

    [Test]
    public void ValidBindingUsesSameFsScorerAndOneJointNominalContribution()
    {
        var cells = Cells();
        var bound = BoundV9NominalPreviewModel.Bind(Manifest(cells), cells);
        var result = bound.ScorePreview(Parameters, "ANA SILVA", "ANA SILVA",
            "MARIA SOUZA", "MARIA SOUZA");
        var direct = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
            Parameters, "ANA SILVA", "ANA SILVA", "MARIA SOUZA", "MARIA SOUZA",
            cells, CompositeNominalComparisonV9.ContractVersion);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.Score, Is.EqualTo(direct.Score));
            Assert.That(result.Contributions.Count(c => c.Evidence == "NOMINAL_CONJUNTO_V9"),
                Is.EqualTo(1));
            Assert.That(result.Contributions.Any(c => c.Evidence is "NOME" or "NOME_MAE"),
                Is.False);
            Assert.That(result.Contributions.Single(c => c.Evidence == "NOMINAL_CONJUNTO_V9")
                .LogLikelihoodRatio, Is.EqualTo((decimal)Math.Log(2d)).Within(0.000001m));
            Assert.That(bound.ContentFingerprint, Is.EqualTo(Manifest(cells).ExpectedContentFingerprint));
        }));
    }

    [Test]
    public void BindingRejectsDigestMismatchEvenWithNormalizedCells()
    {
        var cells = Cells();
        var manifest = Manifest(cells) with { ExpectedContentFingerprint = new string('f', 64) };
        Assert.That((Action)(() => BoundV9NominalPreviewModel.Bind(manifest, cells)),
            Throws.InvalidOperationException);
    }

    [Test]
    public void BindingFailsClosedForUncertifiedMetadataAndInventedTf()
    {
        var cells = Cells();
        var manifest = Manifest(cells);
        var invalid = new[]
        {
            manifest with { ModelId = Guid.Empty },
            manifest with { ModelVersion = 0 },
            manifest with { NominalContractVersion = "OTHER" },
            manifest with { FrameFingerprint = "" },
            manifest with { SelectionFingerprint = "" },
            manifest with { LabelingReference = "" },
            manifest with { PartitionReference = "" },
            manifest with { EvidenceGeneration = "" },
            manifest with { TermFrequencyStatus = "TF_CALIBRATED" },
            manifest with { Status = "ACTIVE" },
            manifest with { ExpectedContentFingerprint = "not-a-sha256" }
        };
        foreach (var bad in invalid)
            Assert.That((Action)(() => BoundV9NominalPreviewModel.Bind(bad, cells)),
                Throws.InvalidOperationException);
    }

    [Test]
    public void BindingFreezesSnapshotAgainstCallerMutation()
    {
        var cells = Cells();
        var manifest = Manifest(cells);
        var bound = BoundV9NominalPreviewModel.Bind(manifest, cells);
        var before = bound.ScorePreview(Parameters, "ANA SILVA", "ANA SILVA",
            "MARIA SOUZA", "MARIA SOUZA");
        cells[0] = cells[0] with { M = 0.0001m, U = 0.0001m };
        var after = bound.ScorePreview(Parameters, "ANA SILVA", "ANA SILVA",
            "MARIA SOUZA", "MARIA SOUZA");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(after, Is.EqualTo(before));
            Assert.That(bound.ContentFingerprint, Is.EqualTo(manifest.ExpectedContentFingerprint));
        }));
    }

    [Test]
    public void BindingRequiresComplete81CellSnapshot()
    {
        var cells = Cells();
        var manifest = Manifest(cells);
        Assert.That((Action)(() => BoundV9NominalPreviewModel.Bind(manifest, cells.Skip(1).ToArray())),
            Throws.InvalidOperationException);
    }
}

using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class ProbabilisticV9CandidateRankingPreviewTests
{
    private static readonly DateOnly Birth = new(1980, 5, 6);
    private static readonly Guid ExactId = Guid.Parse("11111111-1111-4111-8111-111111111119");
    private static readonly Guid NearId = Guid.Parse("22222222-2222-4222-8222-222222222229");

    private static readonly IReadOnlyDictionary<string, decimal> Parameters =
        new Dictionary<string, decimal>
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .01m,
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.BirthSingleEvidenceScoring] = 1m,
            ["M_DATA_NASCIMENTO_EXACT"] = .8m,
            ["U_DATA_NASCIMENTO_EXACT"] = .2m,
            ["M_DATA_NASCIMENTO_DIFF"] = .2m,
            ["U_DATA_NASCIMENTO_DIFF"] = .8m
        };

    private static FellegiSunterJointNominalCellV9[] Cells()
    {
        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        return levels.SelectMany(person => levels.Select(mother =>
        {
            var exact = person == CompositeNominalLevelV9.ExactFull
                && mother == CompositeNominalLevelV9.ExactFull;
            return new FellegiSunterJointNominalCellV9(
                person, mother,
                exact ? .40m : .60m / 80m,
                exact ? .05m : .95m / 80m);
        })).ToArray();
    }

    private static IdentityObservation Observation(string? cpf = null) =>
        new(cpf, "SEM_CPF", "Maria Clara Silva", Birth, "Ana Costa");

    private static IReadOnlyList<LinkageCandidate> Candidates() =>
    [
        new LinkageCandidate(NearId, "Maria Sofia Silva", Birth, "ANA COSTA"),
        new LinkageCandidate(ExactId, "Maria Clara Silva", Birth, "Ana Costa")
    ];

    [Test]
    public void PreviewRanksOnlyJointNominalAndBirthInExistingFsAndKeepsDemographicGuard()
    {
        var result = ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview(
            Parameters, Observation(), Candidates(), Cells(),
            CompositeNominalComparisonV9.ContractVersion);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].PessoaUuid, Is.EqualTo(ExactId));
            Assert.That(result[1].PessoaUuid, Is.EqualTo(NearId));
            Assert.That(result[0].LogOdds, Is.GreaterThan(result[1].LogOdds));
            Assert.That(result[0].DemographicExactCollisionRisk, Is.True);
            Assert.That(result[1].DemographicExactCollisionRisk, Is.False);
            Assert.That(result.All(x => x.Score > 0m && x.Score < 1m), Is.True);
        }));
    }

    [Test]
    public void PreviewRankingIsIndependentOfCandidateInputOrderAndIdenticalDuplicates()
    {
        var candidates = Candidates();
        var one = ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview(
            Parameters, Observation(), candidates, Cells(),
            CompositeNominalComparisonV9.ContractVersion);
        var two = ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview(
            Parameters, Observation(),
            [candidates[1], candidates[0], candidates[1]],
            Cells(), CompositeNominalComparisonV9.ContractVersion);
        Assert.That(two, Is.EqualTo(one));
    }

    [Test]
    public void PreviewRejectsCpfObservationsAndConflictingCandidateDuplicates()
    {
        var candidates = Candidates();
        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() =>
                ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview(
                    Parameters, Observation("12345678901"), candidates, Cells(),
                    CompositeNominalComparisonV9.ContractVersion)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview(
                    Parameters, Observation(),
                    [candidates[0], candidates[0] with { NomeCompleto = "Diferente" }],
                    Cells(), CompositeNominalComparisonV9.ContractVersion)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview(
                    Parameters, Observation(), candidates, Cells().Skip(1).ToArray(),
                    CompositeNominalComparisonV9.ContractVersion)),
                Throws.InvalidOperationException);
        }));
    }

    [Test]
    public void EvenWithoutCandidatesInvalidJointSnapshotFailsClosed()
    {
        var invalid = Cells().Skip(1).ToArray();
        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() =>
                ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview(
                    Parameters, Observation(),
                    Array.Empty<LinkageCandidate>(), invalid,
                    CompositeNominalComparisonV9.ContractVersion)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview(
                    Parameters, Observation(),
                    Array.Empty<LinkageCandidate>(), Cells(),
                    "LEGACY_V8_COMPARISON")),
                Throws.InvalidOperationException);
        }));
    }

    [Test]
    public void FrozenSnapshotAndLegacyCollectionPreviewGiveIdenticalScoresPerCandidate()
    {
        var cells = Cells();
        var frozen = FrozenFellegiSunterJointNominalV9.Freeze(
            cells, CompositeNominalComparisonV9.ContractVersion);
        var ranked = ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview(
            Parameters, Observation(), Candidates(),
            cells, CompositeNominalComparisonV9.ContractVersion);
        foreach (var candidate in Candidates())
        {
            var single = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                Parameters, Observation().NomeCompleto, candidate.NomeCompleto,
                Observation().NomeMae, candidate.NomeMae,
                cells, CompositeNominalComparisonV9.ContractVersion,
                2, Birth, candidate.DataNascimento);
            var precompiled = FellegiSunterScoring.CalculateFrozenCompositeNominalV9PreviewWithBreakdown(
                Parameters, Observation().NomeCompleto, candidate.NomeCompleto,
                Observation().NomeMae, candidate.NomeMae,
                frozen, 2, Birth, candidate.DataNascimento);
            var actual = ranked.Single(c => c.PessoaUuid == candidate.PessoaUuid);
            Assert.Multiple((Action)(() =>
            {
                Assert.That(actual.LogOdds, Is.EqualTo(single.Score.LogOdds));
                Assert.That(actual.Score, Is.EqualTo(single.Score.Posterior));
                Assert.That(System.Text.Json.JsonSerializer.Serialize(precompiled),
                    Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(single)));
            }));
        }
    }

    [Test]
    public void PreviewWithNoCandidatesProducesNoInferenceOrAutomatedResolution()
    {
        var noMatches = ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview(
            Parameters, Observation(), Array.Empty<LinkageCandidate>(), Cells(),
            CompositeNominalComparisonV9.ContractVersion);
        Assert.That(noMatches, Is.Empty);
    }
}

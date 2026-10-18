using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Jornada.Linkage.Parameters.Worker;

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

    private static FrozenIbgeNominalTfV9 PinnedTf()
    {
        var entries = new[]
        {
            new NominalTermFrequencyEntry("NOME_PRENOME", "MARIA", 60, 100, .60m),
            new NominalTermFrequencyEntry("NOME_MAE_PRENOME", "ANA", 30, 100, .30m)
        };
        return FrozenIbgeNominalTfV9.BindPublished(
            new PreparedNominalTermFrequency(
                NominalTermFrequencySnapshot.Create(entries), entries, 42),
            "IBGE_PINNED", new string('f', 64), "BLOCKING_PINNED",
            new NominalSyntheticIbgeBootstrapV9(
                CompositeNominalTermFrequencyContractV9.SyntheticIbgeProvenance,
                30_000, "CORPUS_30K", "GENERATOR_V1", 42UL, "HYPOTHESES_V1"));
    }

    [Test]
    public void Ranking_preview_consumes_actual_FS_TF_weight_and_changes_candidate_order()
    {
        // Intentionally neutral 81 joint-cell ratios to isolate TF. Unlike
        // logging exactFirst=false, this asserts actual ranked CandidateScore.
        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        var cells = levels.SelectMany(p => levels.Select(m =>
            new FellegiSunterJointNominalCellV9(p, m, 1m / 81m, 1m / 81m))).ToArray();
        var observation = new IdentityObservation(
            null, "SEM_CPF", "Maria Lima", Birth, "Ana Costa");
        var firstMatch = new LinkageCandidate(
            ExactId, "Maria Costa", Birth, "Ana Costa");
        var nonMatch = new LinkageCandidate(
            NearId, "Zuleica Nogueira", Birth, "Ana Costa");
        var baseline = ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview(
            Parameters, observation, [nonMatch, firstMatch],
            cells, CompositeNominalComparisonV9.ContractVersion);
        Assert.That(CompositeNominalComparisonV9.Classify(
            observation.NomeCompleto, firstMatch.NomeCompleto),
            Is.EqualTo(CompositeNominalLevelV9.ExactFirst));
        var pinned = PinnedTf();
        int applied = 0;
        CompositeNominalTfPreviewV9? Weighted(
            IdentityObservation source, LinkageCandidate target)
        {
            var level = CompositeNominalComparisonV9.Classify(
                source.NomeCompleto, target.NomeCompleto);
            if (level != CompositeNominalLevelV9.ExactFirst)
                return null; // No marginal may be invented for another unit.
            applied++;
            return pinned.CreatePreview(
                CompositeNominalTfSideV9.Person, level,
                source.NomeCompleto, target.NomeCompleto,
                .10m, 1m, .00001m, 1);
        }
        var weighted = ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview(
            Parameters, observation, [firstMatch, nonMatch],
            cells, CompositeNominalComparisonV9.ContractVersion, Weighted,
            positiveTfSide: CompositeNominalTfSideV9.Person);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(applied, Is.EqualTo(1));
            Assert.That(baseline[0].PessoaUuid, Is.EqualTo(ExactId));
            Assert.That(weighted[0].PessoaUuid, Is.EqualTo(NearId));
            Assert.That(weighted[1].PessoaUuid, Is.EqualTo(ExactId));
            Assert.That(weighted[0].LogOdds, Is.GreaterThan(weighted[1].LogOdds));
            Assert.That(weighted[1].LogOdds - baseline[0].LogOdds,
                Is.EqualTo((decimal)Math.Log(1d / 6d)).Within(.00000001m));
        }));
        // A caller cannot pass TF with the wrong comparison unit and have
        // the ranker silently ignore an incompatible positive adjustment.
        Assert.That((Action)(() =>
            ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview(
                Parameters, observation, [nonMatch],
                cells, CompositeNominalComparisonV9.ContractVersion,
                (_, _) => pinned.CreatePreview(
                    CompositeNominalTfSideV9.Person,
                    CompositeNominalLevelV9.ExactFirst,
                    "Maria Lima", "Maria Costa", .10m, 1m, .00001m, 1),
                positiveTfSide: CompositeNominalTfSideV9.Person)),
            Throws.InvalidOperationException);
        // Missing term is not the same as weight TF=0 or BF=1.
        Assert.That((Action)(() =>
            ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview(
                Parameters, observation, [firstMatch],
                cells, CompositeNominalComparisonV9.ContractVersion,
                (_, _) => null,
                positiveTfSide: CompositeNominalTfSideV9.Person)),
            Throws.InvalidOperationException);
        Assert.That((Action)(() =>
            ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview(
                Parameters, observation, [firstMatch],
                cells, CompositeNominalComparisonV9.ContractVersion,
                positiveTfSide: CompositeNominalTfSideV9.Person)),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Full_exact_outscores_high_full_with_equal_birth_mother_and_TF_excluded()
    {
        var obs = new IdentityObservation(
            null, "SEM_CPF", "Maria Clara Silva", Birth, "Ana Costa");
        var exact = new LinkageCandidate(
            ExactId, "Maria Clara Silva", Birth, "Ana Costa");
        var high = new LinkageCandidate(
            NearId, "Maria Clara Silvaa", Birth, "Ana Costa");
        Assert.That(CompositeNominalComparisonV9.Classify(
            obs.NomeCompleto, high.NomeCompleto),
            Is.EqualTo(CompositeNominalLevelV9.HighFull));
        int ineligibleTf = 0;
        CompositeNominalTfPreviewV9? OnlyFirst(
            IdentityObservation source, LinkageCandidate candidate)
        {
            if (CompositeNominalComparisonV9.Classify(
                source.NomeCompleto, candidate.NomeCompleto)
                == CompositeNominalLevelV9.ExactFirst)
                ineligibleTf++;
            return null;
        }
        var ranked = ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview(
            Parameters, obs, [high, exact],
            Cells(), CompositeNominalComparisonV9.ContractVersion, OnlyFirst,
            positiveTfSide: CompositeNominalTfSideV9.Person);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(ineligibleTf, Is.Zero);
            Assert.That(ranked[0].PessoaUuid, Is.EqualTo(ExactId));
            Assert.That(ranked[1].PessoaUuid, Is.EqualTo(NearId));
            Assert.That(ranked[0].LogOdds, Is.GreaterThan(ranked[1].LogOdds));
        }));
    }

    private static ProbabilisticLinkageDecision ResolveV9(
        IdentityObservation source,
        IReadOnlyList<LinkageCandidate> candidates,
        decimal threshold,
        decimal margin,
        IReadOnlyDictionary<string, decimal>? parameters = null,
        IReadOnlyCollection<FellegiSunterJointNominalCellV9>? cells = null,
        Func<IdentityObservation, LinkageCandidate, CompositeNominalTfPreviewV9?>? tf = null,
        CompositeNominalTfSideV9? tfSide = null) =>
        ProbabilisticLinkageDecisions.ResolveCompositeNominalV9Preview(
            Guid.Parse("11111111-1111-4111-8111-111111111190"),
            parameters ?? Parameters,
            source, candidates, cells ?? Cells(),
            CompositeNominalComparisonV9.ContractVersion,
            threshold, margin, tf, tfSide);

    [Test]
    public void Preview_resolve_calls_the_same_final_decision_guards_as_production_without_V9_activation()
    {
        var observation = Observation();
        var ranked = ResolveV9(observation, Candidates(), .10m, .5m);
        var conflicted = ResolveV9(observation, Candidates(), .10m, 3m);
        var tooWeak = ResolveV9(observation, Candidates(), .90m, 0m);
        var empty = ResolveV9(observation, [], .10m, 0m);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(ranked.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
            Assert.That(ranked.PessoaUuidResolvido, Is.EqualTo(ExactId));
            Assert.That(ranked.MelhorCandidatoUuid, Is.EqualTo(ExactId));
            Assert.That(conflicted.Status, Is.EqualTo(ResolutionStatus.CONFLITO));
            Assert.That(conflicted.Motivo, Is.EqualTo("MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE"));
            Assert.That(tooWeak.Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
            Assert.That(tooWeak.Motivo, Is.EqualTo("ABAIXO_T_LINKAGE"));
            Assert.That(empty.Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
            Assert.That(empty.Motivo, Is.EqualTo("SEM_CANDIDATO_NO_RULESET_BLOCKING"));
        }));

        var demographic = Parameters.ToDictionary(x => x.Key, x => x.Value);
        demographic[LinkageParameterCatalog.NonUniqueDemographicExactGuard] = 1m;
        var guarded = ResolveV9(observation, Candidates(), .10m, 0m, demographic);
        Assert.That(guarded.Status, Is.EqualTo(ResolutionStatus.CONFLITO));
        Assert.That(guarded.Motivo, Is.EqualTo("NUCLEO_DEMOGRAFICO_EXATO_NAO_UNICO"));

        demographic.Remove(LinkageParameterCatalog.NonUniqueDemographicExactGuard);
        demographic[LinkageParameterCatalog.DualThresholdConflictGuard] = 1m;
        demographic[LinkageParameterCatalog.DualThresholdConflictFloorV2] = 1m;
        demographic[LinkageParameterCatalog.DualThresholdConflictFloor] = .02m;
        var ambiguous = ResolveV9(observation, Candidates(), .10m, 0m, demographic);
        Assert.That(ambiguous.Status, Is.EqualTo(ResolutionStatus.CONFLITO));
        Assert.That(ambiguous.Motivo, Is.EqualTo("SEGUNDO_CANDIDATO_ACIMA_PISO_CONFLITO"));
    }

    [Test]
    public void Preview_resolve_TF_positive_changes_final_selection_and_refuses_unknown_frequency()
    {
        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        var neutralCells = levels.SelectMany(p => levels.Select(m =>
            new FellegiSunterJointNominalCellV9(p, m, 1m / 81m, 1m / 81m))).ToArray();
        var source = new IdentityObservation(
            null, "SEM_CPF", "Maria Lima", Birth, "Ana Costa");
        var exactFirst = new LinkageCandidate(
            ExactId, "Maria Costa", Birth, "Ana Costa");
        var different = new LinkageCandidate(
            NearId, "Zuleica Nogueira", Birth, "Ana Costa");
        var unadjusted = ResolveV9(source, [different, exactFirst], .02m, 0m,
            cells: neutralCells);
        var pinned = PinnedTf();
        CompositeNominalTfPreviewV9? Frequency(
            IdentityObservation obs, LinkageCandidate candidate)
        {
            var level = CompositeNominalComparisonV9.Classify(
                obs.NomeCompleto, candidate.NomeCompleto);
            return level == CompositeNominalLevelV9.ExactFirst
                ? pinned.CreatePreview(CompositeNominalTfSideV9.Person, level,
                    obs.NomeCompleto, candidate.NomeCompleto, .10m, 1m, .00001m, 1)
                : null;
        }
        var weighted = ResolveV9(source, [exactFirst, different], .02m, 0m,
            cells: neutralCells, tf: Frequency, tfSide: CompositeNominalTfSideV9.Person);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(unadjusted.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
            Assert.That(unadjusted.PessoaUuidResolvido, Is.EqualTo(ExactId));
            Assert.That(weighted.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
            Assert.That(weighted.PessoaUuidResolvido, Is.EqualTo(NearId));
            Assert.That(weighted.PessoaUuidResolvido, Is.Not.EqualTo(unadjusted.PessoaUuidResolvido));
        }));
        Assert.That((Action)(() => ResolveV9(
            source, [exactFirst], .02m, 0m, cells: neutralCells,
            tfSide: CompositeNominalTfSideV9.Person)), Throws.InvalidOperationException);
        Assert.That((Action)(() => ResolveV9(
            source, [exactFirst], .02m, 0m, cells: neutralCells,
            tf: (_, _) => null, tfSide: CompositeNominalTfSideV9.Person)),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Preview_resolve_rejects_invalid_threshold_margin_model_id_and_CPF()
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() => ResolveV9(Observation(), Candidates(), 0m, 0m)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That((Action)(() => ResolveV9(Observation(), Candidates(), .2m, -1m)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That((Action)(() => ResolveV9(Observation("12345678901"), Candidates(), .2m, 0m)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() =>
                ProbabilisticLinkageDecisions.ResolveCompositeNominalV9Preview(
                    Guid.Empty, Parameters, Observation(), Candidates(),
                    Cells(), CompositeNominalComparisonV9.ContractVersion,
                    .2m, 0m)), Throws.TypeOf<ArgumentException>());
        }));
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

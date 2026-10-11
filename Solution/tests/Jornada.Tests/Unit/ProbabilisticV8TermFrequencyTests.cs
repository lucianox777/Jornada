using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class ProbabilisticV8TermFrequencyTests
{
    private static readonly Guid ModelId = Guid.Parse("88000000-0000-4000-8000-0000000000F8");

    [Test]
    public void New_V8_TF_makes_rare_exact_name_stronger_than_common_exact_name()
    {
        var parameters = Parameters(withTf: true);
        var snapshot = Snapshot();
        var model = LinkageModelPolicy.Create(
            ModelId, 9,
            LinkageParameterCatalog.NeutralMissingDecisionEvidenceAlgorithmVersion,
            parameters,
            snapshot);
        var birth = new DateOnly(1980, 5, 6);

        var common = ProbabilisticLinkageDecisions.Resolve(
            model,
            new IdentityObservation(null, "SEM_CPF", "MARIA SILVA", birth, "ANA SOUZA"),
            [new LinkageCandidate(Guid.Parse("11111111-1111-4111-8111-111111111111"),
                "MARIA SILVA", birth, "ANA SOUZA")]);
        var rare = ProbabilisticLinkageDecisions.Resolve(
            model,
            new IdentityObservation(null, "SEM_CPF", "ZULEICA KRAUSE", birth, "ANA SOUZA"),
            [new LinkageCandidate(Guid.Parse("22222222-2222-4222-8222-222222222222"),
                "ZULEICA KRAUSE", birth, "ANA SOUZA")]);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(rare.MelhorScore, Is.GreaterThan(common.MelhorScore));
            Assert.That(rare.Margem, Is.Null);
            Assert.That(common.Margem, Is.Null);
        }));
    }

    [TestCase("MARIA SILVA", "MARIA SILVAA", NameComparisonState.HIGH)]
    [TestCase("MARIA SILVA", "MARIA VIOL", NameComparisonState.MEDIUM)]
    [TestCase("JOSIANE SANTOS", "ELISA SANTOS", NameComparisonState.LOW)]
    public void TF_is_neutral_outside_exact_name_state(
        string observedName,
        string candidateName,
        NameComparisonState expectedState)
    {
        var parameters = Parameters(withTf: true);
        var model = LinkageModelPolicy.Create(
            ModelId, 9,
            LinkageParameterCatalog.NeutralMissingDecisionEvidenceAlgorithmVersion,
            parameters,
            Snapshot());
        var birth = new DateOnly(1980, 5, 6);
        var actualState = IdentityComparison.CompareName(
            observedName,
            candidateName,
            LinkageParameterCatalog.NameComparisonContractForAlgorithm(model.AlgorithmVersion));
        Assert.That(actualState, Is.EqualTo(expectedState), "Fixture deve exercer o estado nominal declarado.");

        var decision = ProbabilisticLinkageDecisions.Resolve(
            model,
            new IdentityObservation(null, "SEM_CPF", observedName, birth, null),
            [new LinkageCandidate(Guid.NewGuid(), candidateName, birth, null)]);
        var raw = FellegiSunterScoring.Calculate(
            parameters,
            expectedState,
            null,
            1,
            birth,
            birth);

        Assert.That(decision.MelhorScore, Is.EqualTo(raw.Posterior));
    }

    [Test]
    public void Missing_published_term_is_neutral_not_zero_frequency()
    {
        var parameters = Parameters(withTf: true);
        var snapshot = Snapshot();
        var model = LinkageModelPolicy.Create(
            ModelId, 9,
            LinkageParameterCatalog.NeutralMissingDecisionEvidenceAlgorithmVersion,
            parameters,
            snapshot);
        var birth = new DateOnly(1980, 5, 6);
        var observation = new IdentityObservation(null, "SEM_CPF", "NAO PUBLICADO", birth, null);
        var candidate = new LinkageCandidate(Guid.NewGuid(), "NAO PUBLICADO", birth, null);

        var decision = ProbabilisticLinkageDecisions.Resolve(model, observation, [candidate]);
        var raw = FellegiSunterScoring.Calculate(
            parameters,
            NameComparisonState.EXACT,
            null,
            1,
            birth,
            birth);

        Assert.That(decision.MelhorScore, Is.EqualTo(raw.Posterior));
    }

    [Test]
    public void Historical_V8_without_TF_remains_replayable()
    {
        var parameters = Parameters(withTf: false);
        parameters[LinkageParameterCatalog.NonUniqueDemographicExactGuard] = 1m;

        Assert.DoesNotThrow((Action)(() => LinkageModelPolicy.Create(
            ModelId, 8,
            LinkageParameterCatalog.NeutralMissingDecisionEvidenceAlgorithmVersion,
            parameters)));
    }

    [Test]
    public void TF_enabled_V8_requires_snapshot_and_rejects_legacy_demographic_guard()
    {
        var parameters = Parameters(withTf: true);
        Assert.That((Func<object?>)(() => LinkageModelPolicy.Create(
                ModelId, 9,
                LinkageParameterCatalog.NeutralMissingDecisionEvidenceAlgorithmVersion,
                parameters)),
            Throws.InvalidOperationException.With.Message.Contains("snapshot nominal"));

        parameters[LinkageParameterCatalog.NonUniqueDemographicExactGuard] = 1m;
        Assert.That((Func<object?>)(() => LinkageModelPolicy.Create(
                ModelId, 9,
                LinkageParameterCatalog.NeutralMissingDecisionEvidenceAlgorithmVersion,
                parameters,
                Snapshot())),
            Throws.InvalidOperationException.With.Message.Contains("guard demográfico"));
    }

    [Test]
    public void P0_RuntimeRanking_ExactVersusHigh_WithCommonFirstNameAndTF()
    {
        // Operational decision path: Rank applies TF after the base FS score.
        // Illustrative fixture, not a certified calibrated model.
        var model = LinkageModelPolicy.Create(ModelId, 9,
            LinkageParameterCatalog.NeutralMissingDecisionEvidenceAlgorithmVersion,
            Parameters(withTf: true), Snapshot());
        var observation = new IdentityObservation(null, "SEM_CPF", "MARIA SILVA", null, null);
        var exactId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var highId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        Assert.That(IdentityComparison.CompareName("MARIA SILVA", "MARIA SILVAA",
            LinkageParameterCatalog.NameComparisonContractForAlgorithm(model.AlgorithmVersion)),
            Is.EqualTo(NameComparisonState.HIGH));
        var ranked = ProbabilisticLinkageDecisions.Rank(model, observation, new[] {
            new LinkageCandidate(exactId, "MARIA SILVA", null, null),
            new LinkageCandidate(highId, "MARIA SILVAA", null, null)
        });
        Assert.That(ranked, Has.Count.EqualTo(2));
        // The fixture sets M_NOME == U_NOME for all states and is not calibrated.
        // It cannot establish a universal EXACT > HIGH decision policy.
        Assert.That(ranked.Select(x => x.PessoaUuid), Is.EquivalentTo(new[] { exactId, highId }));
        Assert.That(ranked[0].LogOdds, Is.GreaterThanOrEqualTo(ranked[1].LogOdds),
            "Rank must order the computed FS/TF log-odds, not a fixed name-state preference.");
        TestContext.WriteLine($"P0 EXACT/HIGH synthetic diagnostic: first={ranked[0].PessoaUuid}; " +
            $"firstLogOdds={ranked[0].LogOdds}; secondLogOdds={ranked[1].LogOdds}; " +
            $"exactFirst={ranked[0].PessoaUuid == exactId}. " +
            "An EXACT/HIGH safety claim requires calibrated parameters and validation evidence.");
    }

    [Test]
    public void P0_NameOnly_RareExact_UsesRealResolveAndReportsDecision()
    {
        // Diagnostic only: canonical policy does not impose an uncalibrated
        // two-attribute gate. This test must not invent a RESOLVIDO requirement.
        var model = LinkageModelPolicy.Create(ModelId, 9,
            LinkageParameterCatalog.NeutralMissingDecisionEvidenceAlgorithmVersion,
            Parameters(withTf: true), Snapshot());
        var candidateId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var observation = new IdentityObservation(null, "SEM_CPF", "ZULEICA KRAUSE", null, null);
        var candidate = new LinkageCandidate(candidateId, "ZULEICA KRAUSE", null, null);
        var ranked = ProbabilisticLinkageDecisions.Rank(model, observation, new[] { candidate });
        var decision = ProbabilisticLinkageDecisions.Resolve(model, observation, new[] { candidate });
        Assert.That(ranked, Has.Count.EqualTo(1));
        Assert.That(decision.MelhorScore, Is.EqualTo(ranked[0].Score),
            "Resolve must use the same TF-adjusted score as Rank.");
        TestContext.WriteLine($"P0 name-only rare exact: status={decision.Status}; score={decision.MelhorScore}; reason={decision.Motivo}");
        if (decision.Status == ResolutionStatus.RESOLVIDO)
            Assert.That(decision.PessoaUuidResolvido, Is.EqualTo(candidateId));
        else
            Assert.That(decision.PessoaUuidResolvido, Is.Null);
    }

    private static NominalTermFrequencySnapshot Snapshot()
        => NominalTermFrequencySnapshot.Create(new[]
        {
            new NominalTermFrequencyEntry(
                NominalTermFrequencySnapshot.PersonFirstNameAttribute,
                "MARIA", 600, 1000, .6m),
            new NominalTermFrequencyEntry(
                NominalTermFrequencySnapshot.PersonFirstNameAttribute,
                "ZULEICA", 10, 1000, .01m),
            new NominalTermFrequencyEntry(
                NominalTermFrequencySnapshot.MotherFirstNameAttribute,
                "ANA", 500, 1000, .5m)
        });

    private static Dictionary<string, decimal> Parameters(bool withTf)
    {
        var p = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .01m,
            [LinkageParameterCatalog.PriorBlockMin] = .000001m,
            [LinkageParameterCatalog.PriorBlockMax] = .25m,
            [LinkageParameterCatalog.Threshold] = .95m,
            [LinkageParameterCatalog.ConflictMargin] = .03m,
            [LinkageParameterCatalog.LogOddsConflictMargin] = .03m,
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.NeutralMissingEvidenceScoring] = 1m,
            [LinkageParameterCatalog.BirthSemanticEvidenceScoring] = 1m,
            [LinkageParameterCatalog.DualThresholdConflictGuard] = 1m,
            [LinkageParameterCatalog.DualThresholdConflictFloorV2] = 1m,
            [LinkageParameterCatalog.DualThresholdConflictFloor] = .9m
        };
        foreach (var state in LinkageParameterCatalog.NameStates)
        {
            p["M_NOME_" + state] = .25m;
            p["U_NOME_" + state] = .25m;
            p["M_NOME_MAE_" + state] = .25m;
            p["U_NOME_MAE_" + state] = .25m;
        }
        foreach (var state in BirthDateSemanticEvidence.States)
        {
            p["M_NASCIMENTO_SEMANTICO_" + state] = .5m;
            p["U_NASCIMENTO_SEMANTICO_" + state] = .5m;
        }

        if (withTf)
        {
            p[LinkageParameterCatalog.TermFrequencyScoring] = 1m;
            p[LinkageParameterCatalog.TermFrequencyWeight] = 1m;
            p[LinkageParameterCatalog.TermFrequencyMinimumU] = .01m;
            p[LinkageParameterCatalog.TermFrequencyFirstTokenContract] = 1m;
        }

        return p;
    }
}

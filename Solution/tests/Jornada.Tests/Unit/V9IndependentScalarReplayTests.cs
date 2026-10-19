using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

/// <summary>
/// Independent conference of the portable fit and its actual Rank -> Resolve results.
/// The oracle multiplies scalar odds, never calls FS/logit/TF/ranking/Resolve helpers.
/// Synthetic fixtures establish arithmetic correctness, not statistical certification.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class V9IndependentScalarReplayTests
{
    private static readonly Guid Model = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid First = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly DateOnly Birth = new(1980, 5, 6);

    private static V9FittedSnapshotPayload Payload(decimal prior = .1m, decimal threshold = .5m,
        decimal margin = .5m, decimal tfWeight = 0m, decimal frequency = .02m)
    {
        // Deliberately not ordinal/monotone: ExactFull's learned BF is smaller
        // than HighFull's. A state name does not constrain its empirical m/u.
        var weights = Enum.GetValues<CompositeNominalLevelV9>()
            .SelectMany(person => Enum.GetValues<CompositeNominalLevelV9>().Select(mother =>
                (Person: person, Mother: mother, M: person switch
                {
                    CompositeNominalLevelV9.ExactFull => 4m,
                    CompositeNominalLevelV9.HighFull => 8m,
                    CompositeNominalLevelV9.ExactFirst => 2m,
                    _ => 1m
                } * (mother == CompositeNominalLevelV9.ExactFull ? 3m : 1m))))
            .ToArray();
        var total = weights.Sum(x => x.M);
        var cells = weights.Select(x => new FellegiSunterJointNominalCellV9(
            x.Person, x.Mother, x.M / total, 1m / 81m)).ToArray();
        var parameters = new Dictionary<string, decimal>
        {
            [LinkageParameterCatalog.PriorMatchProbability] = prior,
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.NeutralMissingEvidenceScoring] = 1m,
            [LinkageParameterCatalog.BirthSingleEvidenceScoring] = 1m,
            ["M_DATA_NASCIMENTO_EXACT"] = .9m, ["M_DATA_NASCIMENTO_DIFF"] = .1m,
            ["U_DATA_NASCIMENTO_EXACT"] = .1m, ["U_DATA_NASCIMENTO_DIFF"] = .9m
        };
        var reference = new NominalFrequencyReferenceContractV9(NominalFrequencyUnitV9.FirstName,
            NominalFrequencySourceV9.IbgePublishedMarginal,
            "IBGE_PUBLISHED:test:" + new string('a', 64) + ":SP_3550308_TODOS",
            new string('c', 64), 0, false,
            new NominalSyntheticIbgeBootstrapV9(CompositeNominalTermFrequencyContractV9.SyntheticIbgeProvenance,
                30000, new string('a', 64), "GENERATOR_V1", 42, new string('b', 64)));
        return new V9FittedSnapshotPayload(V9FittedSnapshot.SchemaVersion,
            CompositeNominalComparisonV9.ContractVersion, "scalar-conference-v1", new string('a', 64),
            new string('b', 64), new string('c', 64), new string('d', 64), new string('e', 64),
            new string('f', 64), "independent-scalar-test", "fixture", threshold, margin, tfWeight,
            [new LinkageBlockingPass("fixture", ["NOME"])], cells, parameters,
            tfWeight == 0m ? null : new V9SnapshotFirstNameTf(CompositeNominalTfSideV9.Person,
                reference, new Dictionary<string, decimal> { ["MARIA"] = frequency },
                new Dictionary<string, decimal> { ["MARIA"] = .2m }, new string('a', 64),
                .000001m, 1, new string('c', 64)), new string('c', 64));
    }

    private static V9FittedSnapshot Reload(V9FittedSnapshotPayload p)
    {
        var fit = V9FittedSnapshot.Create(p);
        return V9FittedSnapshot.Load(fit.SerializeUtf8(), fit.ContentSha256);
    }

    // Inputs are explicit raw states asserted separately below. Missing DOB means
    // no birth factor; Missing mother remains a learned joint-cell state.
    private static (decimal Posterior, decimal LogOdds) Scalar(V9FittedSnapshotPayload p,
        CompositeNominalLevelV9 person, CompositeNominalLevelV9 mother,
        int birthState = 0, bool applyTf = false)
    {
        var cell = p.JointCells.Single(x => x.Person == person && x.Mother == mother);
        var prior = p.Parameters[LinkageParameterCatalog.PriorMatchProbability];
        var odds = (double)(prior / (1m - prior)) * (double)(cell.M / cell.U);
        if (birthState != 0)
            odds *= birthState == 1 ? 9d : 1d / 9d;
        if (applyTf && p.TfWeight != 0m)
        {
            var tf = p.TfCalibration!;
            odds *= Math.Pow((double)(tf.TrainUnitUByValue["MARIA"] /
                Math.Max(tf.Frequencies["MARIA"], tf.MinimumFrequency)), (double)p.TfWeight);
        }
        return (Published(odds / (1d + odds)), Published(Math.Log(odds)));
    }

    private static decimal Published(double value) =>
        Math.Round((decimal)value, 8, MidpointRounding.AwayFromZero);

    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(0, true)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    public void Scalar_formula_replays_missing_mother_and_birth_states(int birthState, bool motherKnown)
    {
        var p = Payload(threshold: .2m);
        var observation = new IdentityObservation(null, "SEM_CPF", "Maria Silva", Birth,
            motherKnown ? "Ana Costa" : null);
        var candidate = new V9SnapshotCandidate(First, "Maria Silva",
            birthState == 0 ? null : birthState == 1 ? Birth : Birth.AddDays(1),
            motherKnown ? "Ana Costa" : null);
        var mother = motherKnown ? CompositeNominalLevelV9.ExactFull : CompositeNominalLevelV9.Missing;
        Assert.That(CompositeNominalComparisonV9.Classify(observation.NomeCompleto, candidate.FullName),
            Is.EqualTo(CompositeNominalLevelV9.ExactFull));
        Assert.That(CompositeNominalComparisonV9.Classify(observation.NomeMae, candidate.MotherName), Is.EqualTo(mother));
        var expected = Scalar(p, CompositeNominalLevelV9.ExactFull, mother, birthState);
        var actual = Reload(p).ResolvePreview(Model, observation, [candidate]);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(actual.MelhorScore, Is.EqualTo(expected.Posterior));
            Assert.That(actual.MelhorCandidatoUuid, Is.EqualTo(First));
            Assert.That(actual.Status, Is.EqualTo(expected.Posterior < p.Threshold
                ? ResolutionStatus.NAO_RESOLVIDO : ResolutionStatus.RESOLVIDO));
        }));
    }

    [TestCase(0)]
    [TestCase(.5)]
    [TestCase(1)]
    public void Tf_weight_zero_fractional_and_one_change_real_decision(double weight)
    {
        var p = Payload(threshold: .1m, tfWeight: (decimal)weight);
        var observation = new IdentityObservation(null, "SEM_CPF", "Maria", null, null);
        var candidate = new V9SnapshotCandidate(First, "Maria Silva", null, null);
        Assert.That(CompositeNominalComparisonV9.Classify(observation.NomeCompleto, candidate.FullName),
            Is.EqualTo(CompositeNominalLevelV9.ExactFirst));
        var expected = Scalar(p, CompositeNominalLevelV9.ExactFirst, CompositeNominalLevelV9.Missing, applyTf: true);
        var actual = Reload(p).ResolvePreview(Model, observation, [candidate]);
        Assert.That(actual.MelhorScore, Is.EqualTo(expected.Posterior));
        Assert.That(actual.Status, Is.EqualTo(weight == 0d ? ResolutionStatus.NAO_RESOLVIDO : ResolutionStatus.RESOLVIDO));
    }

    [Test]
    public void Prior_threshold_and_learned_mu_each_change_the_actual_outcome()
    {
        var observation = new IdentityObservation(null, "SEM_CPF", "Maria Silva", null, null);
        var candidates = new[] { new V9SnapshotCandidate(First, "Maria Silva", null, null) };
        var low = Payload(prior: .01m, threshold: .2m);
        var high = Payload(prior: .5m, threshold: .2m);
        var lowDecision = Reload(low).ResolvePreview(Model, observation, candidates);
        var highDecision = Reload(high).ResolvePreview(Model, observation, candidates);
        Assert.That(lowDecision.MelhorScore, Is.EqualTo(Scalar(low, CompositeNominalLevelV9.ExactFull,
            CompositeNominalLevelV9.Missing).Posterior));
        Assert.That(highDecision.MelhorScore, Is.EqualTo(Scalar(high, CompositeNominalLevelV9.ExactFull,
            CompositeNominalLevelV9.Missing).Posterior));
        Assert.That(lowDecision.Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
        Assert.That(highDecision.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
        Assert.That(Reload(high with { Threshold = .99m }).ResolvePreview(Model, observation, candidates).Status,
            Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
        var neutral = high with { JointCells = high.JointCells.Select(c => c with { M = c.U }).ToArray(), Threshold = .55m };
        Assert.That(Reload(high with { Threshold = .55m }).ResolvePreview(Model, observation, candidates).Status,
            Is.EqualTo(ResolutionStatus.RESOLVIDO));
        Assert.That(Reload(neutral).ResolvePreview(Model, observation, candidates).Status,
            Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
    }

    [Test]
    public void Nonmonotone_learned_cells_rank_by_scalar_evidence_and_margin_controls_resolve()
    {
        var p = Payload(prior: .5m, threshold: .2m, margin: .5m);
        var observation = new IdentityObservation(null, "SEM_CPF", "Maria Silva", null, null);
        var candidates = new[] { new V9SnapshotCandidate(First, "Maria Silva", null, null),
            new V9SnapshotCandidate(Second, "Maria Silvaa", null, null) };
        Assert.That(CompositeNominalComparisonV9.Classify("Maria Silva", "Maria Silvaa"), Is.EqualTo(CompositeNominalLevelV9.HighFull));
        var exact = Scalar(p, CompositeNominalLevelV9.ExactFull, CompositeNominalLevelV9.Missing);
        var high = Scalar(p, CompositeNominalLevelV9.HighFull, CompositeNominalLevelV9.Missing);
        var actual = Reload(p).ResolvePreview(Model, observation, candidates);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(actual.MelhorCandidatoUuid, Is.EqualTo(Second));
            Assert.That(actual.SegundoCandidatoUuid, Is.EqualTo(First));
            Assert.That(actual.MelhorScore, Is.EqualTo(high.Posterior));
            Assert.That(actual.SegundoScore, Is.EqualTo(exact.Posterior));
            Assert.That(actual.Margem, Is.EqualTo(high.LogOdds - exact.LogOdds));
            Assert.That(actual.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
        }));
        Assert.That(Reload(p with { ConflictMargin = 1m }).ResolvePreview(Model, observation, candidates).Status,
            Is.EqualTo(ResolutionStatus.CONFLITO));
    }

    [Test]
    public void Homonyms_have_zero_margin_and_uuid_tiebreak_independent_of_enumeration()
    {
        var p = Payload(prior: .5m, threshold: .2m);
        var observation = new IdentityObservation(null, "SEM_CPF", "Maria Silva", null, null);
        var candidates = new[] { new V9SnapshotCandidate(Second, "Maria Silva", null, null),
            new V9SnapshotCandidate(First, "Maria Silva", null, null) };
        var snapshot = Reload(p);
        var actual = snapshot.ResolvePreview(Model, observation, candidates);
        Assert.That(actual, Is.EqualTo(snapshot.ResolvePreview(Model, observation, candidates.Reverse().ToArray())));
        Assert.That(actual.MelhorCandidatoUuid, Is.EqualTo(First));
        Assert.That(actual.SegundoCandidatoUuid, Is.EqualTo(Second));
        Assert.That(actual.Margem, Is.Zero);
        Assert.That(actual.Status, Is.EqualTo(ResolutionStatus.CONFLITO));
    }
}

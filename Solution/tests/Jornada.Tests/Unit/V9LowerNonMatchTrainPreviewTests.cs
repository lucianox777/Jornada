using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9LowerNonMatchTrainPreviewTests
{
    private static readonly Guid Model = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Candidate = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static string Sha(int i) => i.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);
    private static readonly string Fit = Sha(1);
    private static readonly string Corpus = Sha(2);

    private static V9LowerNonMatchPreview.VerifiedRetrieval Retrieval(
        int candidates = 2, bool materialized = true, bool complete = true, bool eligible = true)
        => new(Fit, Sha(3), candidates, materialized, complete, eligible);

    private static ProbabilisticLinkageDecision Rejected(decimal score, string reason = "ABAIXO_T_LINKAGE")
        => new(ResolutionStatus.NAO_RESOLVIDO, null, Candidate, score, null, null, null, Model, reason);

    private static void Multiple(Action a) => Assert.Multiple(a);
    private static void Reject<T>(Action a) where T : Exception => Assert.Throws<T>(a);

    [Test]
    public void Classifies_lower_band_as_hypothesis_only_without_promoting_new_uuid()
    {
        var below = V9LowerNonMatchPreview.Evaluate(Rejected(.1m), Retrieval(), Fit, .2m, .9m);
        var middle = V9LowerNonMatchPreview.Evaluate(Rejected(.3m), Retrieval(), Fit, .2m, .9m);
        var boundary = V9LowerNonMatchPreview.Evaluate(Rejected(.2m), Retrieval(), Fit, .2m, .9m);
        Multiple(() =>
        {
            Assert.That(below.Decision, Is.EqualTo(V9LowerNonMatchPreview.Disposition.CandidateRejectedNewReferenceHypothesis));
            Assert.That(below.AllowedToPublishNewReference, Is.False);
            Assert.That(below.OperationalActivation, Is.False);
            Assert.That(middle.Decision, Is.EqualTo(V9LowerNonMatchPreview.Disposition.PossibleLink));
            Assert.That(boundary.Decision, Is.EqualTo(V9LowerNonMatchPreview.Disposition.PossibleLink));
        });
    }

    [Test]
    public void Lower_band_cannot_override_incomplete_search_conflict_or_original_acceptance()
    {
        var low = Rejected(.05m);
        var unfinished = V9LowerNonMatchPreview.Evaluate(low, Retrieval(complete: false), Fit, .2m, .9m);
        var unindexed = V9LowerNonMatchPreview.Evaluate(low, Retrieval(materialized: false), Fit, .2m, .9m);
        var noPass = V9LowerNonMatchPreview.Evaluate(low, Retrieval(eligible: false), Fit, .2m, .9m);
        var conflict = V9LowerNonMatchPreview.Evaluate(low with { Status = ResolutionStatus.CONFLITO },
            Retrieval(), Fit, .2m, .9m);
        var accepted = V9LowerNonMatchPreview.Evaluate(low with { Status = ResolutionStatus.RESOLVIDO },
            Retrieval(), Fit, .2m, .9m);
        Multiple(() =>
        {
            Assert.That(unfinished.Decision, Is.EqualTo(V9LowerNonMatchPreview.Disposition.TechnicalAbstention));
            Assert.That(unindexed.Decision, Is.EqualTo(V9LowerNonMatchPreview.Disposition.TechnicalAbstention));
            Assert.That(noPass.Decision, Is.EqualTo(V9LowerNonMatchPreview.Disposition.TechnicalAbstention));
            Assert.That(conflict.Decision, Is.EqualTo(V9LowerNonMatchPreview.Disposition.PossibleLink));
            Assert.That(accepted.Decision, Is.EqualTo(V9LowerNonMatchPreview.Disposition.ExistingLink));
            Assert.That(accepted.AllowedToPublishNewReference, Is.False);
        });
    }

    [Test]
    public void Retains_separate_existing_no_candidate_rule_and_refuses_inconsistent_evidence()
    {
        var old = new ProbabilisticLinkageDecision(ResolutionStatus.NAO_RESOLVIDO,
            null, null, 0m, null, null, null, Model, "SEM_CANDIDATO_NO_RULESET_BLOCKING");
        var result = V9LowerNonMatchPreview.Evaluate(old, Retrieval(candidates: 0), Fit, .2m, .9m);
        Multiple(() =>
        {
            Assert.That(result.Decision, Is.EqualTo(V9LowerNonMatchPreview.Disposition.ExistingNoCandidateRule));
            Assert.That(result.AllowedToPublishNewReference, Is.False);
        });
        Reject<InvalidDataException>(() => V9LowerNonMatchPreview.Evaluate(
            old, Retrieval(), Fit, .2m, .9m));
        Reject<InvalidDataException>(() => V9LowerNonMatchPreview.Evaluate(
            Rejected(.01m), Retrieval() with { SnapshotSha256 = Sha(44) }, Fit, .2m, .9m));
        Reject<ArgumentOutOfRangeException>(() => V9LowerNonMatchPreview.Evaluate(
            Rejected(.01m), Retrieval(), Fit, .9m, .9m));
    }

    private static V9LowerNonMatchTrainCalibrator.Policy Policy(
        double maxWrongAmongCreates = .4, double maxWrongAmongExisting = .2,
        bool manual = true, bool test = false)
        => new(Fit, Corpus, .95m, [.1m, .2m, .5m],
            maxWrongAmongCreates, maxWrongAmongExisting, 6, 5, manual, test);

    private static V9LowerNonMatchTrainCalibrator.TrainingSource Example(
        int i, decimal score, bool alreadyRepresented, string partition = "TRAIN_SELECTION",
        bool materialized = true)
        => new(Sha(100 + i), Sha(200 + i), partition,
            alreadyRepresented, IndependentlyLabeled: true,
            Rejected(score), Retrieval(materialized: materialized));

    private static V9LowerNonMatchTrainCalibrator.TrainingSource[] Sources() =>
    [
        Example(1, .05m, true), Example(2, .22m, true),
        Example(3, .03m, false), Example(4, .12m, false),
        Example(5, .30m, false), Example(6, .60m, true),
        Example(7, .70m, true), Example(8, .90m, true)
    ];

    [Test]
    public void Train_pareto_selects_only_descriptive_threshold_under_both_duplicate_budgets()
    {
        var s = V9AdaptiveCalibrationCoordinator.SearchLowerNonMatchOnTrain(Policy(), Sources());
        var lo = s.Candidates.Single(x => x.LowerThreshold == .1m);
        var mid = s.Candidates.Single(x => x.LowerThreshold == .2m);
        var hi = s.Candidates.Single(x => x.LowerThreshold == .5m);
        Multiple(() =>
        {
            Assert.That(s.Version, Is.EqualTo(V9LowerNonMatchTrainCalibrator.Version));
            Assert.That(lo.FalseNewReferences, Is.EqualTo(1));
            Assert.That(lo.ProposedNewReferences, Is.EqualTo(2));
            Assert.That(lo.MeetsPointBudgets, Is.False);
            Assert.That(mid.FalseNewReferences, Is.EqualTo(1));
            Assert.That(mid.CorrectNewReferences, Is.EqualTo(2));
            Assert.That(mid.PendingAfter, Is.EqualTo(5));
            Assert.That(mid.MeetsPointBudgets, Is.True);
            Assert.That(hi.FalseNewReferences, Is.EqualTo(2));
            Assert.That(hi.PendingAfter, Is.EqualTo(3));
            Assert.That(hi.MeetsPointBudgets, Is.False);
            Assert.That(s.DescriptiveSuggestedLowerThreshold, Is.EqualTo(.2m));
            Assert.That(s.IndependentIdentityGroups, Is.EqualTo(8));
            Assert.That(s.AlreadyRepresentedSources, Is.EqualTo(5));
            Assert.That(s.ExternalValidationUsed, Is.False);
            Assert.That(s.DuplicateConfidenceCertified, Is.False);
            Assert.That(s.LinkageFdrRecalibrated, Is.False);
            Assert.That(s.AutomaticCalibrationOrActivationAllowed, Is.False);
            Assert.That(s.OperationalModelCreated, Is.False);
            Assert.That(s.TestUsed, Is.False);
        });
    }

    private static V9LowerNonMatchTrainCalibrator.TrainingSource[] SourcesWithInvalidPartition()
    {
        var s = Sources();
        s[0] = s[0] with { Partition = "VALIDATION" };
        return s;
    }

    [Test]
    public void Test_holdout_manual_flag_source_collision_and_low_support_fail_closed()
    {
        Reject<InvalidDataException>(() =>
            V9AdaptiveCalibrationCoordinator.SearchLowerNonMatchOnTrain(Policy(), SourcesWithInvalidPartition()));
        Reject<InvalidOperationException>(() =>
            V9AdaptiveCalibrationCoordinator.SearchLowerNonMatchOnTrain(Policy(manual: false), Sources()));
        Reject<InvalidOperationException>(() =>
            V9AdaptiveCalibrationCoordinator.SearchLowerNonMatchOnTrain(Policy(test: true), Sources()));
        var repeated = Sources();
        repeated[1] = repeated[1] with { SourceFingerprintSha256 = repeated[0].SourceFingerprintSha256 };
        Reject<InvalidDataException>(() =>
            V9AdaptiveCalibrationCoordinator.SearchLowerNonMatchOnTrain(Policy(), repeated));
        Reject<InvalidDataException>(() =>
            V9AdaptiveCalibrationCoordinator.SearchLowerNonMatchOnTrain(Policy() with { MinimumIndependentIdentityGroups = 20 }, Sources()));
    }

    [Test]
    public void Technical_incompleteness_never_creates_but_counts_in_baseline_pending()
    {
        var s = Sources();
        s[3] = s[3] with { Retrieval = Retrieval(materialized: false) };
        var p = V9AdaptiveCalibrationCoordinator.SearchLowerNonMatchOnTrain(Policy(), s);
        Multiple(() =>
        {
            Assert.That(p.Candidates.Single(x => x.LowerThreshold == .2m).ProposedNewReferences, Is.EqualTo(2));
            Assert.That(p.Candidates.Select(x => x.BaselinePending).Distinct().Count(), Is.EqualTo(1));
            Assert.That(p.Candidates[0].BaselinePending, Is.EqualTo(8));
        });
    }
}

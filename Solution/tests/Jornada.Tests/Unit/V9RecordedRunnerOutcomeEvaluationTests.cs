using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9RecordedRunnerOutcomeEvaluationTests
{
    private static string Sha(char c) => new(c, 64);
    private static readonly Guid Run = Guid.Parse("aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Model = Guid.Parse("bbbbbbbb-bbbb-4bbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Person1 = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Person2 = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Person3 = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private static IndependentRuleSetEvaluationManifest Manifest()
    {
        var eval = IndependentEvaluationManifestCatalog.Create(
            "eval-v9-validation", "model-v9", Sha('a'), Sha('b'), Sha('c'),
            3, 3, new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero));
        var rules = LinkageDynamicRuleSet.Create(
            "v9-rules", LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
            [BlockingCandidateFeatureCatalog.FirstName],
            [new KeyValuePair<string, decimal>("threshold", .95m)]);
        return IndependentRuleSetEvaluationManifestCatalog.Bind(eval, rules);
    }

    private static IndependentResolutionObservation[] Labeled() =>
    [
        new(Sha('1'), Sha('a'), [new(Sha('a'), .99m)]),
        new(Sha('2'), Sha('b'), [new(Sha('c'), .98m)]),
        new(Sha('3'), null, []),
        new(Sha('4'), Sha('d'), [new(Sha('d'), .40m)])
    ];

    private static V9RecordedRunnerObservation[] Recorded() =>
    [
        new(Sha('1'), Sha('5'), [new(Person1, Sha('a'), .99m)],
            new ProbabilisticLinkageDecision(
                ResolutionStatus.RESOLVIDO, Person1, Person1, .99m,
                null, null, null, Model)),
        new(Sha('2'), Sha('6'), [new(Person2, Sha('c'), .98m)],
            new ProbabilisticLinkageDecision(
                ResolutionStatus.RESOLVIDO, Person2, Person2, .98m,
                null, null, null, Model)),
        new(Sha('3'), Sha('7'), [],
            new ProbabilisticLinkageDecision(
                ResolutionStatus.NAO_RESOLVIDO, null, null, 0m,
                null, null, null, Model,
                "SEM_CANDIDATO_NO_RULESET_BLOCKING")),
        new(Sha('4'), Sha('8'), [new(Person3, Sha('d'), .40m)],
            new ProbabilisticLinkageDecision(
                ResolutionStatus.NAO_RESOLVIDO, null, Person3, .40m,
                null, null, null, Model, "ABAIXO_T_LINKAGE"))
    ];

    private static string[] Cohort() =>
        [Sha('5'), Sha('6'), Sha('7'), Sha('8'), Sha('9')];

    private static V9RecordedRunnerBoundEvaluationReport Evaluate(
        IReadOnlyList<V9RecordedRunnerObservation>? recorded = null,
        IReadOnlyList<IndependentResolutionObservation>? labeled = null,
        IReadOnlyCollection<string>? cohort = null,
        Guid? run = null,
        Guid? model = null,
        string? modelVersion = null,
        bool complete = true) =>
        V9RecordedRunnerOutcomeEvaluation.EvaluateValidation(
            Manifest(), recorded ?? Recorded(), labeled ?? Labeled(),
            cohort ?? Cohort(),
            run ?? Run, model ?? Model, modelVersion ?? "model-v9",
            Manifest().RuleSetFingerprintSha256,
            .95m, 3m, complete, 1);

    [Test]
    public void End_to_end_post_guard_runner_decisions_reach_final_identity_cluster_FDR()
    {
        var result = Evaluate();
        var direct = V9RecordedClusterFdrValidation.Evaluate(
            Manifest(), Labeled(),
            [
                new(Sha('1'), "RESOLVIDO", Sha('a'), Sha('a')),
                new(Sha('2'), "RESOLVIDO", Sha('c'), Sha('c')),
                new(Sha('3'), "NAO_RESOLVIDO", null, null,
                    "SEM_CANDIDATO_NO_RULESET_BLOCKING"),
                new(Sha('4'), "NAO_RESOLVIDO", Sha('d'), null,
                    "ABAIXO_T_LINKAGE")
            ],
            Cohort(),
            [
                new(Sha('1'), Sha('5')), new(Sha('2'), Sha('6')),
                new(Sha('3'), Sha('7')), new(Sha('4'), Sha('8'))
            ],
            Run, "model-v9", Manifest().RuleSetFingerprintSha256,
            .95m, 3m, true, 1);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.Version, Is.EqualTo(V9RecordedRunnerOutcomeEvaluation.Version));
            Assert.That(result.Partition, Is.EqualTo("VALIDATION"));
            Assert.That(result.OperationallyCertified, Is.False);
            Assert.That(result.ClusterFdr.OperationallyCertified, Is.False);
            Assert.That(result.SourceObservations, Is.EqualTo(4));
            Assert.That(result.PreBlockingBaseIdentities, Is.EqualTo(5));
            Assert.That(result.ClusterFdr.IdentitiesWithoutAnyQueryObservation,
                Is.EqualTo(1));
            Assert.That(result.ClusterFdr.DescriptiveFdr.TrueAccepted, Is.EqualTo(1));
            Assert.That(result.ClusterFdr.DescriptiveFdr.FalseAccepted, Is.EqualTo(1));
            Assert.That(result.ClusterFdr.DescriptiveFdr.ObservedFdr, Is.EqualTo(.5d));
            Assert.That(result.ClusterFdr.ConditionalClusterUpper.DenominatorSupported,
                Is.False);
            Assert.That(result.ClusterFdr.RecordedEvaluationFingerprintSha256,
                Is.EqualTo(direct.RecordedEvaluationFingerprintSha256));
            Assert.That(result.ClusterFdr.BoundCohortEvidenceFingerprintSha256,
                Is.EqualTo(direct.BoundCohortEvidenceFingerprintSha256));
            Assert.That(result.RecordedRunnerEvidenceFingerprintSha256, Has.Length.EqualTo(64));
        }));
    }

    [Test]
    public void Bound_run_report_is_order_stable_but_sensitive_to_origin_zero_degree_cohort_and_run()
    {
        var a = Evaluate();
        var reversed = Evaluate(Recorded().Reverse().ToArray(),
            Labeled().Reverse().ToArray(), Cohort().Reverse().ToArray());
        Assert.That(reversed.RecordedRunnerEvidenceFingerprintSha256,
            Is.EqualTo(a.RecordedRunnerEvidenceFingerprintSha256));

        var alteredOrigin = Recorded();
        alteredOrigin[0] = alteredOrigin[0] with
        {
            BaseIdentityFingerprintSha256 = Sha('9')
        };
        var b = Evaluate(recorded: alteredOrigin);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(b.ClusterFdr.DescriptiveFdr, Is.EqualTo(a.ClusterFdr.DescriptiveFdr));
            Assert.That(b.ClusterFdr.BoundCohortEvidenceFingerprintSha256,
                Is.Not.EqualTo(a.ClusterFdr.BoundCohortEvidenceFingerprintSha256));
            Assert.That(b.RecordedRunnerEvidenceFingerprintSha256,
                Is.Not.EqualTo(a.RecordedRunnerEvidenceFingerprintSha256));
        }));
        var changedCensus = Evaluate(cohort:
            [Sha('5'), Sha('6'), Sha('7'), Sha('8'), Sha('f')]);
        Assert.That(changedCensus.RecordedRunnerEvidenceFingerprintSha256,
            Is.Not.EqualTo(a.RecordedRunnerEvidenceFingerprintSha256));
        Assert.That(Evaluate(run: Guid.Parse("aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaab"))
            .RecordedRunnerEvidenceFingerprintSha256,
            Is.Not.EqualTo(a.RecordedRunnerEvidenceFingerprintSha256));
    }

    [Test]
    public void Rejects_missing_frame_forged_decision_wrong_model_and_incomplete_sql_universe()
    {
        Assert.That((Action)(() => Evaluate(complete: false)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => Evaluate(modelVersion: "MODEL_TEST")),
            Throws.TypeOf<InvalidDataException>());
        // Omitting a group actually referenced by an observation MUST fail.
        // Omitting only a zero-degree base cannot be detected from records
        // without an externally authenticated pre-blocking census; its
        // footprint differs instead and is never operationally certified.
        Assert.That((Action)(() => Evaluate(cohort:
            [Sha('5'), Sha('6'), Sha('7'), Sha('9')])),
            Throws.TypeOf<InvalidDataException>());
        var censoredZeroDegree = Evaluate(cohort: Cohort()[..4]);
        Assert.That(censoredZeroDegree.OperationallyCertified, Is.False);
        Assert.That(censoredZeroDegree.PreBlockingBaseIdentities, Is.EqualTo(4));
        Assert.That(censoredZeroDegree.RecordedRunnerEvidenceFingerprintSha256,
            Is.Not.EqualTo(Evaluate().RecordedRunnerEvidenceFingerprintSha256));
        var recorded = Recorded();
        recorded[0] = recorded[0] with
        {
            FinalDecision = recorded[0].FinalDecision with
            {
                PessoaUuidResolvido = Person2
            }
        };
        Assert.That((Action)(() => Evaluate(recorded: recorded)),
            Throws.TypeOf<InvalidDataException>());
        recorded = Recorded();
        recorded[1] = recorded[1] with
        {
            FinalDecision = recorded[1].FinalDecision with
            {
                ModeloId = Guid.Parse("00000000-0000-4000-8000-000000000123")
            }
        };
        Assert.That((Action)(() => Evaluate(recorded: recorded)),
            Throws.TypeOf<InvalidDataException>());
        var mislabeled = Labeled();
        mislabeled[0] = mislabeled[0] with
        {
            Candidates = [new(Sha('a'), .91m)]
        };
        Assert.That((Action)(() => Evaluate(labeled: mislabeled)),
            Throws.TypeOf<InvalidDataException>());
    }
}

using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9RecordedClusterFdrValidationTests
{
    private static string Sha(char value) => new(value, 64);
    private static readonly Guid RunId =
        Guid.Parse("00000000-0000-0000-0000-000000000719");

    private static IndependentRuleSetEvaluationManifest Manifest(
        long candidates = 3, long references = 3)
    {
        var evaluation = IndependentEvaluationManifestCatalog.Create(
            "eval-v9-validation", "model-v9", Sha('a'), Sha('b'), Sha('c'),
            candidates, references,
            new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero));
        var rules = LinkageDynamicRuleSet.Create(
            "v9-rules", LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
            [BlockingCandidateFeatureCatalog.FirstName],
            [new KeyValuePair<string, decimal>("threshold", 0.95m)]);
        return IndependentRuleSetEvaluationManifestCatalog.Bind(evaluation, rules);
    }

    private static IndependentResolutionObservation[] Observations() =>
    [
        new(Sha('1'), Sha('a'), [new(Sha('a'), .99m)]),
        new(Sha('2'), Sha('b'), [new(Sha('c'), .98m)]),
        new(Sha('3'), null, []),
        new(Sha('4'), Sha('d'), [new(Sha('d'), .40m)])
    ];

    private static IndependentRecordedOutcome[] Outcomes() =>
    [
        new(Sha('1'), "RESOLVIDO", Sha('a'), Sha('a')),
        new(Sha('2'), "RESOLVIDO", Sha('c'), Sha('c')),
        new(Sha('3'), "NAO_RESOLVIDO", null, null),
        new(Sha('4'), "NAO_RESOLVIDO", Sha('d'), null)
    ];

    private static string[] Cohort() =>
        [Sha('5'), Sha('6'), Sha('7'), Sha('8'), Sha('9')];

    private static V9RecordedBaseObservation[] Bindings() =>
    [
        new(Sha('1'), Sha('5')),
        new(Sha('2'), Sha('6')),
        new(Sha('3'), Sha('7')),
        new(Sha('4'), Sha('8'))
    ];

    private static V9RecordedClusterFdrValidationReport Evaluate(
        IndependentRecordedOutcome[]? outcomes = null,
        V9RecordedBaseObservation[]? bindings = null,
        string[]? cohort = null,
        bool complete = true,
        int cap = 1,
        IndependentRuleSetEvaluationManifest? manifest = null)
    {
        var m = manifest ?? Manifest();
        return V9RecordedClusterFdrValidation.Evaluate(
            m, Observations(), outcomes ?? Outcomes(),
            cohort ?? Cohort(), bindings ?? Bindings(),
            RunId, m.Evaluation.ModelVersion, m.RuleSetFingerprintSha256,
            .95m, 3m, complete, cap);
    }

    [Test]
    public void Uses_final_rank_resolve_outcomes_not_candidate_thresholds_or_pair_counts()
    {
        var result = Evaluate();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.Version, Is.EqualTo(V9RecordedClusterFdrValidation.Version));
            Assert.That(result.RecordedEvaluationFingerprintSha256, Has.Length.EqualTo(64));
            Assert.That(result.PreBlockingBaseIdentities, Is.EqualTo(5));
            Assert.That(result.IdentitiesWithoutAnyQueryObservation, Is.EqualTo(1));
            Assert.That(result.IdentitiesWithoutAcceptedLinks, Is.EqualTo(3));
            Assert.That(result.DescriptiveFdr.IndependentGroups, Is.EqualTo(5));
            Assert.That(result.DescriptiveFdr.TrueAccepted, Is.EqualTo(1));
            Assert.That(result.DescriptiveFdr.FalseAccepted, Is.EqualTo(1));
            Assert.That(result.DescriptiveFdr.ObservedFdr, Is.EqualTo(.5d));
            Assert.That(result.ConditionalClusterUpper.DenominatorSupported, Is.False);
            Assert.That(result.ConditionalClusterUpper.UpperFdr, Is.Null);
            Assert.That(result.OperationallyCertified, Is.False);
        }));
        var reverse = V9RecordedClusterFdrValidation.Evaluate(
            Manifest(), Observations().Reverse().ToArray(), Outcomes().Reverse().ToArray(),
            Cohort().Reverse().ToArray(), Bindings().Reverse().ToArray(),
            RunId, "model-v9", Manifest().RuleSetFingerprintSha256, .95m, 3m, true, 1);
        Assert.That(reverse.RecordedEvaluationFingerprintSha256,
            Is.EqualTo(result.RecordedEvaluationFingerprintSha256));
    }

    [Test]
    public void Rejects_missing_or_forged_source_binding_and_zero_candidate_identity()
    {
        Assert.That((Action)(() => Evaluate(bindings: Bindings()[..3])),
            Throws.TypeOf<InvalidDataException>());
        var foreign = Bindings();
        foreign[0] = foreign[0] with { BaseIdentityFingerprintSha256 = Sha('f') };
        Assert.That((Action)(() => Evaluate(bindings: foreign)),
            Throws.TypeOf<InvalidDataException>());
        var duplicated = Bindings();
        duplicated[1] = duplicated[1] with { ObservationFingerprintSha256 = Sha('1') };
        Assert.That((Action)(() => Evaluate(bindings: duplicated)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => Evaluate(cohort: [Sha('5'), Sha('6'), Sha('7'), Sha('8'), Sha('8')])),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Rejects_incomplete_unrecorded_or_inconsistent_run_and_cap()
    {
        Assert.That((Action)(() => Evaluate(complete: false)),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That((Action)(() => Evaluate(outcomes:
            [Outcomes()[0], Outcomes()[0], Outcomes()[2], Outcomes()[3]])),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That((Action)(() => Evaluate(outcomes:
            [Outcomes()[0] with { Status = "CONFLITO" }, Outcomes()[1],
             Outcomes()[2], Outcomes()[3]])),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That((Action)(() => Evaluate(cap: 0)),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        // Two accepted outcomes assigned to one identity exceed a cap of 1.
        var sameGroup = Bindings();
        sameGroup[1] = sameGroup[1] with
        {
            BaseIdentityFingerprintSha256 = sameGroup[0].BaseIdentityFingerprintSha256
        };
        Assert.That((Action)(() => Evaluate(bindings: sameGroup)),
            Throws.TypeOf<InvalidDataException>());
    }
}

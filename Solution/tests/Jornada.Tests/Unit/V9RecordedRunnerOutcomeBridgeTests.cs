using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9RecordedRunnerOutcomeBridgeTests
{
    private static readonly Guid Run = Guid.Parse("aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Model = Guid.Parse("bbbbbbbb-bbbb-4bbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PersonA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid PersonB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly string SourceA = new('a', 64);
    private static readonly string SourceB = new('b', 64);
    private static readonly string GroupA = new('c', 64);
    private static readonly string GroupB = new('d', 64);
    private static readonly string CandidateA = new('e', 64);
    private static readonly string CandidateB = new('f', 64);

    private static V9RecordedRunnerObservation Winner() =>
        new(SourceA, GroupA,
        [
            new(PersonA, CandidateA, .95m),
            new(PersonB, CandidateB, .30m)
        ],
        new ProbabilisticLinkageDecision(
            ResolutionStatus.RESOLVIDO, PersonA, PersonA, .95m,
            PersonB, .30m, 2m, Model));

    private static V9RecordedRunnerObservation Empty() =>
        new(SourceB, GroupB, [],
            new ProbabilisticLinkageDecision(
                ResolutionStatus.NAO_RESOLVIDO, null, null, 0m,
                null, null, null, Model, "SEM_CANDIDATO_NO_RULESET_BLOCKING"));

    private static IndependentResolutionObservation LabeledWinner() =>
        new(SourceA, CandidateA,
        [
            new(CandidateB, .30m),
            new(CandidateA, .95m)
        ]);

    private static IndependentResolutionObservation LabeledEmpty() =>
        new(SourceB, null, []);

    private static V9RecordedRunnerOutcomeBinding Bind(
        IReadOnlyList<V9RecordedRunnerObservation>? rows = null,
        IReadOnlyList<IndependentResolutionObservation>? truth = null,
        bool complete = true) =>
        V9RecordedRunnerOutcomeBridge.Bind(
            Run, Model, rows ?? [Winner(), Empty()],
            truth ?? [LabeledWinner(), LabeledEmpty()], complete);

    [Test]
    public void Translates_genuine_post_guard_decisions_losslessly_without_recomputing_resolution()
    {
        var result = Bind();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.OperationallyCertified, Is.False);
            Assert.That(result.RunId, Is.EqualTo(Run));
            Assert.That(result.ModelId, Is.EqualTo(Model));
            Assert.That(result.RecordedOutcomes, Has.Count.EqualTo(2));
            Assert.That(result.BaseIdentityBindings, Has.Count.EqualTo(2));
            Assert.That(result.RecordedOutcomes[0],
                Is.EqualTo(new IndependentRecordedOutcome(
                    SourceA, "RESOLVIDO", CandidateA, CandidateA)));
            Assert.That(result.RecordedOutcomes[1],
                Is.EqualTo(new IndependentRecordedOutcome(
                    SourceB, "NAO_RESOLVIDO", null, null,
                    "SEM_CANDIDATO_NO_RULESET_BLOCKING")));
            Assert.That(result.BaseIdentityBindings[0].BaseIdentityFingerprintSha256,
                Is.EqualTo(GroupA));
            Assert.That(result.BaseIdentityBindings[1].BaseIdentityFingerprintSha256,
                Is.EqualTo(GroupB));
        }));
        // Bound outputs are independent of input ordering, not of the true
        // decisions or of which base identity originates an observation.
        var reorder = Bind([Empty(), Winner()], [LabeledEmpty(), LabeledWinner()]);
        Assert.That(reorder.RecordedOutcomes, Is.EqualTo(result.RecordedOutcomes));
        Assert.That(reorder.BaseIdentityBindings, Is.EqualTo(result.BaseIdentityBindings));
        var differentGroup = Bind([Winner() with
        {
            BaseIdentityFingerprintSha256 = GroupB
        }, Empty()]);
        Assert.That(differentGroup.BaseIdentityBindings, Is.Not.EqualTo(result.BaseIdentityBindings));
    }

    [Test]
    public void Conflict_and_below_threshold_status_are_preserved_not_redecided()
    {
        var conflicting = Winner() with
        {
            FinalDecision = Winner().FinalDecision with
            {
                Status = ResolutionStatus.CONFLITO,
                PessoaUuidResolvido = null,
                Motivo = "MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE"
            }
        };
        var unresolved = Winner() with
        {
            FinalDecision = Winner().FinalDecision with
            {
                Status = ResolutionStatus.NAO_RESOLVIDO,
                PessoaUuidResolvido = null,
                Motivo = "ABAIXO_T_LINKAGE"
            }
        };
        var conflict = Bind([conflicting, Empty()]);
        var below = Bind([unresolved, Empty()]);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(conflict.RecordedOutcomes[0].Status, Is.EqualTo("CONFLITO"));
            Assert.That(conflict.RecordedOutcomes[0].ReasonCode,
                Is.EqualTo("MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE"));
            Assert.That(conflict.RecordedOutcomes[0].ResolvedCandidateFingerprintSha256,
                Is.Null);
            Assert.That(below.RecordedOutcomes[0].Status, Is.EqualTo("NAO_RESOLVIDO"));
            Assert.That(below.RecordedOutcomes[0].BestCandidateFingerprintSha256,
                Is.EqualTo(CandidateA));
            Assert.That(below.RecordedOutcomes[0].ResolvedCandidateFingerprintSha256,
                Is.Null);
        }));
    }

    [Test]
    public void Rejects_unattested_or_inconsistent_sql_candidate_universe_and_missing_decisions()
    {
        void Reject(IReadOnlyList<V9RecordedRunnerObservation>? r = null,
            IReadOnlyList<IndependentResolutionObservation>? t = null,
            bool complete = true) =>
            Assert.That((Action)(() => Bind(r, t, complete)),
                Throws.TypeOf<InvalidDataException>());
        Reject(complete: false);
        Reject([Winner()]);
        Reject([Winner(), Winner()]);
        Reject([Winner(), Empty()],
            [LabeledWinner(), LabeledWinner()]);
        Reject([Winner(), Empty()], [LabeledWinner() with
        {
            Candidates = [new(CandidateA, .95m)]
        }, LabeledEmpty()]);
        Reject([Winner(), Empty()], [LabeledWinner() with
        {
            Candidates = [new(CandidateA, .94m), new(CandidateB, .30m)]
        }, LabeledEmpty()]);
        Reject([Winner() with { ObservationFingerprintSha256 = "MISSING" }, Empty()]);
        Reject([Winner() with { BaseIdentityFingerprintSha256 = "INVALID" }, Empty()]);
        Reject([Winner() with { FinalDecision = Winner().FinalDecision with
        {
            ModeloId = Guid.NewGuid()
        } }, Empty()]);
        Reject([Winner() with { Candidates =
        [
            new(PersonA, CandidateA, .95m),
            new(PersonA, CandidateB, .30m)
        ] }, Empty()]);
        Reject([Winner() with { Candidates =
        [
            new(PersonA, CandidateA, .95m),
            new(PersonB, CandidateA, .30m)
        ] }, Empty()]);
    }

    [Test]
    public void Rejects_forged_runner_winner_second_outside_candidate_union_or_illegal_status()
    {
        void Reject(ProbabilisticLinkageDecision decision) =>
            Assert.That((Action)(() => Bind([Winner() with { FinalDecision = decision }, Empty()])),
                Throws.TypeOf<InvalidDataException>());
        var original = Winner().FinalDecision;
        Reject(original with { PessoaUuidResolvido = PersonB });
        Reject(original with { MelhorCandidatoUuid = Guid.NewGuid() });
        Reject(original with { MelhorCandidatoUuid = PersonB });
        Reject(original with { MelhorScore = .96m });
        Reject(original with { SegundoScore = .29m });
        Reject(original with { SegundoCandidatoUuid = PersonA });
        Reject(original with { SegundoCandidatoUuid = null });
        Reject(original with { Status = ResolutionStatus.CONFLITO });
        Reject(original with { Margem = -1m });
        Reject(Empty().FinalDecision with
        {
            MelhorCandidatoUuid = PersonA
        });
    }

    [Test]
    public void Export_never_claims_run_attestation_or_exposes_raw_uuid_name_or_truth()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Bind());
        Assert.Multiple((Action)(() =>
        {
            Assert.That(json, Does.Not.Contain(PersonA.ToString("D")));
            Assert.That(json, Does.Not.Contain(PersonB.ToString("D")));
            Assert.That(json, Does.Not.Contain("Maria"));
            Assert.That(json, Does.Not.Contain("ReferenceCandidate"));
            Assert.That(json, Does.Contain("OperationallyCertified"));
            Assert.That(json, Does.Contain("false"));
        }));
    }
}

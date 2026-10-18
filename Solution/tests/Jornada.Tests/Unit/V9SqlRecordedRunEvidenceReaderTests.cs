using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9SqlRecordedRunEvidenceReaderTests
{
    private static readonly Guid Run = Guid.Parse("aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Model = Guid.Parse("bbbbbbbb-bbbb-4bbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Person = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private static V9SqlRecordedRunHeader Header() => new(
        Run, Model, 9, "MODEL_VALIDATION", "CONCLUIDO_SEM_PUBLICACAO",
        3, 3, 1, 1, 1, false);

    private static V9SqlRecordedRunOutcome[] Outcomes() =>
    [
        new(100L, new ProbabilisticLinkageDecision(
            ResolutionStatus.RESOLVIDO, Person, Person, .97m,
            Other, .11m, .86m, Model)),
        new(200L, new ProbabilisticLinkageDecision(
            ResolutionStatus.CONFLITO, null, Person, .91m,
            Other, .90m, .01m, Model, "MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE")),
        new(300L, new ProbabilisticLinkageDecision(
            ResolutionStatus.NAO_RESOLVIDO, null, null, 0m,
            null, null, null, Model, "SEM_CANDIDATO_NO_RULESET_BLOCKING"))
    ];

    private static V9SqlRecordedRunEvidence Verify(
        V9SqlRecordedRunHeader? header = null,
        IReadOnlyList<V9SqlRecordedRunOutcome>? outcomes = null) =>
        V9SqlRecordedRunEvidenceReader.ValidateCompletedHml(
            header ?? Header(), outcomes ?? Outcomes());

    [Test]
    public void Reads_final_three_decisions_as_unpublished_HML_evidence_without_certifying_candidate_union()
    {
        var result = Verify();
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.Version,
                Is.EqualTo(V9SqlRecordedRunEvidenceReader.Version));
            Assert.That(result.Header.RunId, Is.EqualTo(Run));
            Assert.That(result.Header.ModelId, Is.EqualTo(Model));
            Assert.That(result.Outcomes, Has.Count.EqualTo(3));
            Assert.That(result.Outcomes[0].FinalDecision.Status,
                Is.EqualTo(ResolutionStatus.RESOLVIDO));
            Assert.That(result.Outcomes[1].FinalDecision.Motivo,
                Is.EqualTo("MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE"));
            Assert.That(result.Outcomes[2].FinalDecision.Status,
                Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
            Assert.That(result.HasCompleteCandidateUnion, Is.False);
            Assert.That(result.OperationallyCertified, Is.False);
            Assert.That(result.CompleteFinalDecisionFingerprintSha256,
                Has.Length.EqualTo(64));
        }));
        var reorder = Verify(outcomes: Outcomes().Reverse().ToArray());
        Assert.That(reorder.CompleteFinalDecisionFingerprintSha256,
            Is.EqualTo(result.CompleteFinalDecisionFingerprintSha256));
        var changed = Outcomes();
        changed[1] = changed[1] with { FinalDecision = changed[1].FinalDecision with
        {
            Motivo = "DOIS_CANDIDATOS_ACIMA_T_LINKAGE"
        }};
        Assert.That(Verify(outcomes: changed).CompleteFinalDecisionFingerprintSha256,
            Is.Not.EqualTo(result.CompleteFinalDecisionFingerprintSha256));
    }

    [Test]
    public void Default_json_export_discloses_only_run_metadata_and_sha_not_observation_uuids()
    {
        var snapshot = Verify();
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(json, Does.Contain("CompleteFinalDecisionFingerprintSha256"));
            Assert.That(json, Does.Contain("HasCompleteCandidateUnion"));
            Assert.That(json, Does.Not.Contain("ObservationId"));
            Assert.That(json, Does.Not.Contain("Outcomes"));
            Assert.That(json, Does.Not.Contain(Person.ToString("D")));
            Assert.That(json, Does.Not.Contain(Other.ToString("D")));
        }));
    }

    [Test]
    public void Fail_closed_for_partial_published_wrong_status_run_or_tampered_counters()
    {
        void Reject(V9SqlRecordedRunHeader header,
            IReadOnlyList<V9SqlRecordedRunOutcome>? outcomes = null) =>
            Assert.That((Action)(() => Verify(header, outcomes)),
                Throws.TypeOf<InvalidDataException>());
        var run = Header();
        Reject(run with { RunId = Guid.Empty });
        Reject(run with { ModelId = Guid.Empty });
        Reject(run with { RunType = "FULL" });
        Reject(run with { RunStatus = "EXECUTANDO" });
        Reject(run with { RunStatus = "PUBLICADO", HasPublicationTimestamp = true });
        Reject(run with { HasPublicationTimestamp = true });
        Reject(run with { EligibleObservations = 4 });
        Reject(run with { EvaluatedObservations = 4, EligibleObservations = 4 });
        Reject(run with { ResolvedObservations = 2 });
        Reject(run with { UnresolvedObservations = 2 });
        Reject(run with { Conflicts = -1 });
        Reject(run with { ModelVersion = 0 });
        Reject(run, Outcomes()[..2]);
    }

    [Test]
    public void Rejects_duplicate_or_mismatched_rows_and_impossible_candidate_state()
    {
        void Reject(V9SqlRecordedRunOutcome[] rows) =>
            Assert.That((Action)(() => Verify(outcomes: rows)),
                Throws.TypeOf<InvalidDataException>());
        var duplicates = Outcomes();
        duplicates[2] = duplicates[2] with { ObservationId = 100 };
        Reject(duplicates);
        var mismatch = Outcomes();
        mismatch[0] = mismatch[0] with
        {
            FinalDecision = mismatch[0].FinalDecision with { ModeloId = Guid.NewGuid() }
        };
        Reject(mismatch);
        var claimed = Outcomes();
        claimed[0] = claimed[0] with
        {
            FinalDecision = claimed[0].FinalDecision with { PessoaUuidResolvido = Other }
        };
        Reject(claimed);
        var impossibleBest = Outcomes();
        impossibleBest[0] = impossibleBest[0] with
        {
            FinalDecision = impossibleBest[0].FinalDecision with
            {
                MelhorCandidatoUuid = null
            }
        };
        Reject(impossibleBest);
        var impossibleSecond = Outcomes();
        impossibleSecond[0] = impossibleSecond[0] with
        {
            FinalDecision = impossibleSecond[0].FinalDecision with
            {
                SegundoScore = .99m
            }
        };
        Reject(impossibleSecond);
        var illegalStatus = Outcomes();
        illegalStatus[1] = illegalStatus[1] with
        {
            FinalDecision = illegalStatus[1].FinalDecision with
            {
                PessoaUuidResolvido = Person
            }
        };
        Reject(illegalStatus);
        var noCandidate = Outcomes();
        noCandidate[2] = noCandidate[2] with
        {
            FinalDecision = noCandidate[2].FinalDecision with
            {
                MelhorScore = .12m
            }
        };
        Reject(noCandidate);
        var negativeMargin = Outcomes();
        negativeMargin[0] = negativeMargin[0] with
        {
            FinalDecision = negativeMargin[0].FinalDecision with
            {
                Margem = -.01m
            }
        };
        Reject(negativeMargin);
    }

    [Test]
    public void Without_real_SQL_connection_cannot_claim_a_completed_authenticated_run()
    {
        Assert.That((AsyncTestDelegate)(async () =>
        {
            _ = await V9SqlRecordedRunEvidenceReader.ReadCompletedHmlAsync(
                null!, Run);
        }), Throws.ArgumentNullException);
        Assert.That((AsyncTestDelegate)(async () =>
        {
            _ = await V9SqlRecordedRunEvidenceReader.ReadCompletedHmlAsync(
                new Microsoft.Data.SqlClient.SqlConnection(), Run);
        }), Throws.TypeOf<InvalidOperationException>());
    }
}

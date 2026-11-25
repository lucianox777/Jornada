using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9ControlledDevSecureEvidenceScoreTests
{
    private static readonly V9FittedSnapshot Winner = V9FittedSnapshotTests.CompleteTf4OperationalWinner();
    private static readonly Guid Model = V9OperationalSqlModelConverter.ModelIdFromWinnerSha(Winner.ContentSha256);
    private static readonly Guid X = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Y = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly IdentityObservation Source = new(null,"SEM_CPF","MARIA SILVA",
        new DateOnly(1980,5,6),"ANA COSTA");
    private static V9SnapshotCandidate Exact(Guid id) =>
        new(id, Source.NomeCompleto, Source.DataNascimento, Source.NomeMae);
    private static V9SnapshotCandidate Weak(Guid id) =>
        new(id,"JOANA PEREIRA",new DateOnly(1970,1,2),"EVA FONSECA");

    private static V9ControlledDevSecureOriginReader.OriginFacts Origin(
        long id, bool founder = false, bool cpf = false, bool correction = false,
        bool feedback = false, bool priorProbabilistic = false, bool conflict = false,
        string state = "REFERENCIA", Guid? target = null) =>
        new(X, id, founder ? X : Guid.Parse("33333333-3333-4333-8333-333333333333"),
            state == "REFERENCIA" ? target ?? X : null, state, 1,
            cpf, correction, feedback, conflict, priorProbabilistic, founder,
            new V9ControlledDevSecureEvidenceScore.Evidence[] {
                new(id * 10 + 1, id, Weak(X)), new(id * 10 + 2, id, Exact(X))
            });

    [Test]
    public void B1_origin_census_bridge_scores_all_versions_of_only_secure_origins()
    {
        var origins = new[] {
            Origin(1, founder: true),
            Origin(2, cpf: true),
            Origin(3, priorProbabilistic: true),
            Origin(4, correction: true, priorProbabilistic: true),
            Origin(5, cpf: true, state: "INDEFINIDA"),
            Origin(6, feedback: true, priorProbabilistic: true),
            Origin(7, feedback: true),
            Origin(8, founder: true, target: Y),
            Origin(9, correction: true, conflict: true)
        };
        var census = new Dictionary<Guid, IReadOnlyList<V9ControlledDevSecureOriginReader.OriginFacts>>
            { [X] = origins };
        var actual = V9ControlledDevSecureEvidenceScore.ScoreFromOrigins(
            Winner, Model, Source, new[] { X }, census);
        var expected = V9ControlledDevSecureEvidenceScore.Score(Winner, Model, Source,
            new[] { X }, origins.Where(o => o.OriginId is 1 or 2 or 4 or 7)
                .SelectMany(o => o.Observations).ToArray());
        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(actual.EvidenceReceipt.SecureRecords, Is.EqualTo(8));
        Assert.That(actual.EvidenceReceipt.ComparedRecords, Is.EqualTo(8));
    }

    [Test]
    public void B1_diagnostic_capture_preserves_decision_and_all_record_scores()
    {
        var evidence = new V9ControlledDevSecureEvidenceScore.Evidence[] {
            new(1,1,Weak(X)), new(2,1,Exact(X)), new(3,2,Weak(Y))
        };
        var baseline=V9ControlledDevSecureEvidenceScore.Score(Winner,Model,Source,new[]{X,Y},evidence);
        IReadOnlyList<V9ControlledDevSecureEvidenceScore.RecordScore>? captured=null;
        var observed=V9ControlledDevSecureEvidenceScore.Score(Winner,Model,Source,new[]{X,Y},evidence,observe:x=>captured=x);
        Assert.That(observed,Is.EqualTo(baseline));
        Assert.That(captured,Is.Not.Null);
        Assert.That(captured!.Select(x=>x.ObservationId),Is.EqualTo(new long[]{1,2,3}));
        Assert.That(captured!.Count(x=>x.Posterior.HasValue),Is.EqualTo(baseline.EvidenceReceipt.ScoreableRecords));
    }

    [Test]
    public void B1_origin_census_without_independent_authority_abstains_without_summary()
    {
        var census = new Dictionary<Guid, IReadOnlyList<V9ControlledDevSecureOriginReader.OriginFacts>>
            { [X] = new[] { Origin(1, priorProbabilistic: true),
                            Origin(2, feedback: true, priorProbabilistic: true) } };
        var decision = V9ControlledDevSecureEvidenceScore.ScoreFromOrigins(
            Winner, Model, Source, new[] { X }, census);
        Assert.That(decision.Decision.Motivo, Is.EqualTo(V9ControlledDevSecureEvidenceScore.NoSecureEvidence));
        Assert.That(decision.Decision.PessoaUuidResolvido, Is.Null);
        Assert.That(decision.EvidenceReceipt.SecureRecords, Is.Zero);
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9ControlledDevSecureEvidenceScore.ScoreFromOrigins(
                Winner, Model, Source, new[] { Y }, census)));
    }

    [Test]
    public void B1_single_record_per_person_preserves_frozen_FS()
    {
        var evidence = new V9ControlledDevSecureEvidenceScore.Evidence[] {
            new(1,1,Exact(X)), new(2,2,Weak(Y))
        };
        var expected = Winner.ResolvePreview(Model,Source,new[] { Exact(X), Weak(Y) });
        var actual = V9ControlledDevSecureEvidenceScore.Score(Winner,Model,Source,new[] {X,Y},evidence);
        Assert.That(actual.Decision, Is.EqualTo(expected));
        Assert.That(actual.EvidenceReceipt.CandidatePeople, Is.EqualTo(2));
        Assert.That(actual.EvidenceReceipt.ComparedRecords, Is.EqualTo(2));
    }

    [Test]
    public void B1_multiple_versions_use_maximum_and_distinct_competitor_not_second_record()
    {
        var evidence = new V9ControlledDevSecureEvidenceScore.Evidence[] {
            new(1,1,Weak(X)), new(2,1,Exact(X)), new(3,1,Exact(X)), new(4,2,Weak(Y))
        };
        // Independent reference: historical #38 observation IDs and full
        // record-population ranking before collapsing the strongest per person.
        Guid RecordId(int id) => new(id,0,0,0,0,0,0,0,0,0,1);
        var peopleByRecord = new Dictionary<Guid,Guid> {
            [RecordId(1)] = X, [RecordId(2)] = X, [RecordId(3)] = X, [RecordId(4)] = Y
        };
        var ranked = Winner.RankPreview(Source, new[] {
            Weak(RecordId(1)), Exact(RecordId(2)), Exact(RecordId(3)), Weak(RecordId(4))
        });
        var expected = Winner.ResolveRankedPreview(Model, ranked
            .GroupBy(x => peopleByRecord[x.PessoaUuid])
            .Select(g => g.First() with { PessoaUuid = g.Key })
            .OrderByDescending(x => x.LogOdds).ThenBy(x => x.PessoaUuid).ToArray());
        var actual = V9ControlledDevSecureEvidenceScore.Score(Winner,Model,Source,new[] {X,Y},evidence);
        Assert.That(actual.Decision, Is.EqualTo(expected));
        Assert.That(actual.Decision.SegundoCandidatoUuid, Is.EqualTo(Y));
        Assert.That(actual.EvidenceReceipt.CandidatePeople, Is.EqualTo(2));
        Assert.That(actual.EvidenceReceipt.SecureRecords, Is.EqualTo(4));
        Assert.That(actual.EvidenceReceipt.ComparedRecords, Is.EqualTo(4));
        var reversed = V9ControlledDevSecureEvidenceScore.Score(Winner,Model,Source,new[] {Y,X},
            evidence.Reverse().ToArray());
        Assert.That(reversed, Is.EqualTo(actual));
    }

    [Test]
    public void B1_distinct_bigint_observations_do_not_alias_on_low_32_bits()
    {
        var evidence = new V9ControlledDevSecureEvidenceScore.Evidence[] {
            new(1,1,Weak(X)), new(4294967297L,1,Exact(X)),
            new(long.MaxValue,2,Weak(Y))
        };
        var actual = V9ControlledDevSecureEvidenceScore.Score(Winner,Model,Source,new[] {X,Y},evidence);
        Assert.That(actual.EvidenceReceipt.ComparedRecords, Is.EqualTo(3));
        Assert.That(actual.EvidenceReceipt.ScoreableRecords, Is.EqualTo(3));
        Assert.That(actual.Decision.MelhorCandidatoUuid, Is.EqualTo(X));
        Assert.That(actual.Decision.SegundoCandidatoUuid, Is.EqualTo(Y));
        Assert.That(actual, Is.EqualTo(V9ControlledDevSecureEvidenceScore.Score(
            Winner,Model,Source,new[] {Y,X},evidence.Reverse().ToArray())));
    }

    [Test]
    public void B1_known_candidate_without_evidence_is_not_no_candidate_or_approval()
    {
        var actual = V9ControlledDevSecureEvidenceScore.Score(Winner,Model,Source,new[] {X,Y},
            new V9ControlledDevSecureEvidenceScore.Evidence[] { new(1,1,Exact(X)) });
        Assert.That(actual.Decision.Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
        Assert.That(actual.Decision.PessoaUuidResolvido, Is.Null);
        Assert.That(actual.Decision.Motivo, Is.EqualTo(V9ControlledDevSecureEvidenceScore.NoSecureEvidence));
        Assert.That(actual.EvidenceReceipt.PeopleWithoutScoreableEvidence, Is.EqualTo(1));
        var noEvidence = V9ControlledDevSecureEvidenceScore.Score(Winner,Model,Source,
            new[] {X}, Array.Empty<V9ControlledDevSecureEvidenceScore.Evidence>());
        Assert.That(noEvidence.Decision.Motivo, Is.EqualTo(V9ControlledDevSecureEvidenceScore.NoSecureEvidence));
        var none = V9ControlledDevSecureEvidenceScore.Score(Winner,Model,Source,
            Array.Empty<Guid>(), Array.Empty<V9ControlledDevSecureEvidenceScore.Evidence>());
        Assert.That(none.Decision.Motivo, Is.EqualTo("SEM_CANDIDATO_NO_RULESET_BLOCKING"));
    }

    [Test]
    public void B1_limit_never_truncates_or_approves()
    {
        var evidence = new V9ControlledDevSecureEvidenceScore.Evidence[] {
            new(1,1,Exact(X)), new(2,1,Weak(X))
        };
        var actual = V9ControlledDevSecureEvidenceScore.Score(Winner,Model,Source,new[] {X},evidence,1);
        Assert.That(actual.Decision.Motivo, Is.EqualTo(V9ControlledDevSecureEvidenceScore.EvidenceLimit));
        Assert.That(actual.Decision.PessoaUuidResolvido, Is.Null);
        Assert.That(actual.EvidenceReceipt.ComparedRecords, Is.Zero);
        Assert.That(actual.EvidenceReceipt.SecureRecords, Is.EqualTo(2));
    }

    [Test]
    public void B1_rejects_evidence_outside_census_or_duplicate_observation()
    {
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9ControlledDevSecureEvidenceScore.Score(Winner,Model,Source,new[] {X},
                new V9ControlledDevSecureEvidenceScore.Evidence[] { new(1,1,Exact(Y)) })));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9ControlledDevSecureEvidenceScore.Score(Winner,Model,Source,new[] {X,Y},
                new V9ControlledDevSecureEvidenceScore.Evidence[] { new(1,1,Exact(X)),new(1,1,Exact(Y)) })));
    }
}

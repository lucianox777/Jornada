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

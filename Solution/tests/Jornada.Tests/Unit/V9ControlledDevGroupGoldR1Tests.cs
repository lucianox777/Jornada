using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9ControlledDevGroupGoldR1Tests
{
    private static readonly Guid Model = Guid.Parse("10101010-1010-4010-8010-101010101010");
    private static readonly Guid X = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Y = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Group = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Group2 = Guid.Parse("44444444-4444-4444-8444-444444444444");

    private static ProbabilisticLinkageDecision Resolved(Guid dest) =>
        new(ResolutionStatus.RESOLVIDO, dest, dest, .99m, null, null, null, Model);
    private static ProbabilisticLinkageDecision Missing() =>
        new(ResolutionStatus.NAO_RESOLVIDO, null, null, 0m, null, null, null,
            Model, "SEM_CANDIDATO_NO_RULESET_BLOCKING");
    private static V9ControlledDevOperationalCandidateSource.GoldCandidate Candidate(Guid id) =>
        new(id, "MARIA SILVA", new DateOnly(1980, 5, 6), "ANA COSTA");

    private static IReadOnlyDictionary<long, ProbabilisticLinkageDecision> Run(
        IReadOnlyDictionary<long, Guid> groups,
        IReadOnlyDictionary<long, ProbabilisticLinkageDecision> decisions,
        IReadOnlyDictionary<long, Guid[]> gold,
        IReadOnlyDictionary<long, Guid>? established = null) =>
        V9ControlledDevGroupGoldR1.Translate(groups, decisions,
            gold.ToDictionary(x => x.Key, x =>
                (IReadOnlyList<V9ControlledDevOperationalCandidateSource.GoldCandidate>)
                    x.Value.Select(Candidate).ToArray()),
            established ?? new Dictionary<long, Guid>());

    [Test]
    public void Singleton_with_independent_frozen_FS_approval_keeps_real_Gold_destination()
    {
        var output = Run(
            new Dictionary<long, Guid> { [1] = Group },
            new Dictionary<long, ProbabilisticLinkageDecision> { [1] = Resolved(X) },
            new Dictionary<long, Guid[]> { [1] = [X] });
        Assert.That(output[1], Is.EqualTo(Resolved(X)));
    }

    [Test]
    public void Group_must_not_pass_accepted_Gold_reference_to_a_nonmatching_neighbor()
    {
        var output = Run(
            new Dictionary<long, Guid> { [1] = Group, [2] = Group },
            new Dictionary<long, ProbabilisticLinkageDecision>
                { [1] = Resolved(X), [2] = Missing() },
            new Dictionary<long, Guid[]> { [1] = [X], [2] = [] });
        Assert.Multiple((Action)(() =>
        {
            Assert.That(output[1].Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
            Assert.That(output[1].PessoaUuidResolvido, Is.EqualTo(X));
            Assert.That(output[2].Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
            Assert.That(output[2].PessoaUuidResolvido, Is.Null);
            Assert.That(output[2].Motivo, Is.EqualTo("V9_R1_GRUPO_SEM_HERANCA"));
            Assert.That(output[2].MelhorScore, Is.Zero);
        }));
    }

    [Test]
    public void Same_group_with_two_distinct_Gold_targets_blocks_all_new_associations()
    {
        var output = Run(
            new Dictionary<long, Guid> { [1] = Group, [2] = Group },
            new Dictionary<long, ProbabilisticLinkageDecision>
                { [1] = Resolved(X), [2] = Resolved(Y) },
            new Dictionary<long, Guid[]> { [1] = [X], [2] = [Y] });
        Assert.That(output.Values.All(x => x.Status == ResolutionStatus.CONFLITO
            && x.PessoaUuidResolvido is null
            && x.Motivo == "V9_R1_GRUPO_GOLD_CONFLITANTE"), Is.True);
        Assert.That(output[1].MelhorCandidatoUuid, Is.EqualTo(X));
        Assert.That(output[2].MelhorCandidatoUuid, Is.EqualTo(Y));
    }

    [Test]
    public void Preexisting_authenticated_Gold_link_is_consistency_vote_not_a_group_inheritance()
    {
        var output = Run(
            new Dictionary<long, Guid> { [1] = Group, [2] = Group },
            new Dictionary<long, ProbabilisticLinkageDecision>
                { [1] = Missing(), [2] = Resolved(Y) },
            new Dictionary<long, Guid[]> { [1] = [], [2] = [Y] },
            new Dictionary<long, Guid> { [1] = X });
        Assert.That(output[2].Status, Is.EqualTo(ResolutionStatus.CONFLITO));
        Assert.That(output[2].PessoaUuidResolvido, Is.Null);
    }

    [Test]
    public void Two_distinct_groups_independently_passing_the_same_Gold_do_not_get_a_new_veto()
    {
        var output = Run(
            new Dictionary<long, Guid> { [1] = Group, [2] = Group2 },
            new Dictionary<long, ProbabilisticLinkageDecision>
                { [1] = Resolved(X), [2] = Resolved(X) },
            new Dictionary<long, Guid[]> { [1] = [X], [2] = [X] });
        Assert.That(output.Values.All(x => x.Status == ResolutionStatus.RESOLVIDO
            && x.PessoaUuidResolvido == X), Is.True);
    }

    [Test]
    public void Single_observation_with_no_Gold_and_complete_search_keeps_no_candidate_reason()
    {
        var output = Run(
            new Dictionary<long, Guid> { [1] = Group },
            new Dictionary<long, ProbabilisticLinkageDecision> { [1] = Missing() },
            new Dictionary<long, Guid[]> { [1] = [] });
        Assert.That(output[1], Is.EqualTo(Missing()));
    }

    [Test]
    public void Two_pending_without_Gold_remain_undefined_not_new_references()
    {
        var output = Run(
            new Dictionary<long, Guid> { [1] = Group, [2] = Group },
            new Dictionary<long, ProbabilisticLinkageDecision>
                { [1] = Missing(), [2] = Missing() },
            new Dictionary<long, Guid[]> { [1] = [], [2] = [] });
        Assert.That(output.Values.All(x => x.Status == ResolutionStatus.NAO_RESOLVIDO
            && x.PessoaUuidResolvido is null
            && x.Motivo == "V9_R1_GRUPO_SEM_HERANCA"), Is.True);
    }

    [Test]
    public void Unverified_Gold_or_false_no_candidate_fails_before_SQL_publication()
    {
        Assert.Throws<InvalidDataException>((Action)(() => Run(
            new Dictionary<long, Guid> { [1] = Group },
            new Dictionary<long, ProbabilisticLinkageDecision> { [1] = Resolved(Y) },
            new Dictionary<long, Guid[]> { [1] = [X] })));
        Assert.Throws<InvalidDataException>((Action)(() => Run(
            new Dictionary<long, Guid> { [1] = Group },
            new Dictionary<long, ProbabilisticLinkageDecision> { [1] = Missing() },
            new Dictionary<long, Guid[]> { [1] = [X] })));
    }

    [Test]
    public void A_missing_member_or_unauthenticated_existing_reference_rejects_entire_translation()
    {
        Assert.Throws<InvalidDataException>((Action)(() => Run(
            new Dictionary<long, Guid> { [1] = Group, [2] = Group },
            new Dictionary<long, ProbabilisticLinkageDecision> { [1] = Missing() },
            new Dictionary<long, Guid[]> { [1] = [], [2] = [] })));
        Assert.Throws<InvalidDataException>((Action)(() => Run(
            new Dictionary<long, Guid> { [1] = Group },
            new Dictionary<long, ProbabilisticLinkageDecision> { [1] = Missing() },
            new Dictionary<long, Guid[]> { [1] = [] },
            new Dictionary<long, Guid> { [2] = X })));
    }
}

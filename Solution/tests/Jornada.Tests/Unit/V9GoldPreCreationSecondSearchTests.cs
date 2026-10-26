using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9GoldPreCreationSecondSearchTests
{
    private static readonly Guid Model = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Candidate = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static string Hash(char c) => new(c, 64);
    private static void Reject<T>(Action action) where T : Exception => Assert.Throws<T>(action);
    private static void Multiple(Action action) => Assert.Multiple(action);

    private static ProbabilisticLinkageDecision NoCandidate() =>
        new(ResolutionStatus.NAO_RESOLVIDO, null, null, 0m, null, null, null,
            Model, "SEM_CANDIDATO_NO_RULESET_BLOCKING");

    private static ProbabilisticLinkageDecision LowerProposal() =>
        new(ResolutionStatus.NAO_RESOLVIDO, null, Candidate, .01m, null, null, null,
            Model, V9GoldLowerNonMatchExperiment.PublicationReason);

    private static ProbabilisticLinkageDecision Reranked(decimal p = .2m) =>
        new(ResolutionStatus.NAO_RESOLVIDO, null, Candidate, p, null, null, null,
            Model, "ABAIXO_T_LINKAGE");

    private static V9FittedSnapshotExecutionRuntime Runtime()
    {
        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        var fit = V9FittedSnapshot.Create(new(V9FittedSnapshot.SchemaVersion,
            CompositeNominalComparisonV9.ContractVersion, V9CalibrationBlockingProjection.SchemaVersion,
            V9CalibrationBlockingProjection.ComputeSchemaFingerprint(4),
            Hash('b'), Hash('c'), Hash('d'), Hash('e'), Hash('f'),
            "adapter-v1", "frozen-fit", .8671875m, .5m, 0m,
            [new("P_FIRST_M_FIRST", ["name_first", "mother_name_first"])],
            levels.SelectMany(p => levels.Select(m =>
                new FellegiSunterJointNominalCellV9(p, m, 1m / 81m, 1m / 81m))).ToArray(),
            new Dictionary<string, decimal>
            {
                [LinkageParameterCatalog.PriorMatchProbability] = .9m,
                [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
                [LinkageParameterCatalog.NeutralMissingEvidenceScoring] = 1m,
                [LinkageParameterCatalog.BirthSingleEvidenceScoring] = 1m,
                ["M_DATA_NASCIMENTO_EXACT"] = .9m, ["M_DATA_NASCIMENTO_DIFF"] = .1m,
                ["U_DATA_NASCIMENTO_EXACT"] = .1m, ["U_DATA_NASCIMENTO_DIFF"] = .9m
            }));
        return V9FittedSnapshotExecutionRuntime.Load(fit.SerializeUtf8(), fit.ContentSha256);
    }

    [Test]
    public void Only_actual_new_identity_proposals_trigger_a_second_search()
    {
        Multiple(() =>
        {
            Assert.That(V9GoldPreCreationSecondSearch.IsProspectiveCreation(NoCandidate()), Is.True);
            Assert.That(V9GoldPreCreationSecondSearch.IsProspectiveCreation(LowerProposal()), Is.True);
            Assert.That(V9GoldPreCreationSecondSearch.IsProspectiveCreation(Reranked()), Is.False);
            Assert.That(V9GoldPreCreationSecondSearch.IsProspectiveCreation(
                Reranked() with { Status = ResolutionStatus.CONFLITO }), Is.False);
            Assert.That(V9GoldPreCreationSecondSearch.IsProspectiveCreation(
                Reranked() with { Status = ResolutionStatus.RESOLVIDO }), Is.False);
        });
    }

    [Test]
    public void Extra_candidate_prevents_a_duplicate_without_manufacturing_a_link()
    {
        var outcome = V9GoldPreCreationSecondSearch.Decide(LowerProposal(), Reranked(), true);
        Multiple(() =>
        {
            Assert.That(outcome.Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
            Assert.That(outcome.MelhorScore, Is.EqualTo(.2m));
            Assert.That(outcome.MelhorCandidatoUuid, Is.EqualTo(Candidate));
            Assert.That(outcome.PessoaUuidResolvido, Is.Null);
            Assert.That(outcome.Motivo, Is.EqualTo(V9GoldPreCreationSecondSearch.AbstentionReason));
            Assert.That(outcome.Motivo, Does.Not.StartWith("SEM_CANDIDATO_"));
            Assert.That(outcome.Motivo, Is.Not.EqualTo(V9GoldLowerNonMatchExperiment.PublicationReason));
        });
        // Also guard the preexisting empty-block creation case.
        Assert.That(V9GoldPreCreationSecondSearch.Decide(NoCandidate(), Reranked(.001m), true).Motivo,
            Is.EqualTo(V9GoldPreCreationSecondSearch.AbstentionReason));
    }

    [Test]
    public void Same_candidates_preserve_original_decision_and_safe_frozen_resolve_may_win()
    {
        var initial = LowerProposal();
        var resolved = Reranked(.94m) with
        {
            Status = ResolutionStatus.RESOLVIDO,
            PessoaUuidResolvido = Candidate
        };
        var conflict = Reranked(.94m) with { Status = ResolutionStatus.CONFLITO };
        Multiple(() =>
        {
            Assert.That(V9GoldPreCreationSecondSearch.Decide(initial, resolved, false),
                Is.SameAs(initial));
            Assert.That(V9GoldPreCreationSecondSearch.Decide(initial, resolved, true),
                Is.SameAs(resolved));
            Assert.That(V9GoldPreCreationSecondSearch.Decide(initial, conflict, true),
                Is.SameAs(conflict));
        });
        Reject<InvalidOperationException>(() =>
            V9GoldPreCreationSecondSearch.Decide(Reranked(), resolved, true));
        Reject<InvalidDataException>(() =>
            V9GoldPreCreationSecondSearch.Decide(initial, NoCandidate(), true));
    }

    [Test]
    public void Nineteen_cell_query_is_a_pinned_superset_of_fitted_passes_and_parameterized()
    {
        var runtime = Runtime();
        var observation = new IdentityObservation(null, "SEM_CPF", "MARIA SILVA",
            new DateOnly(1980, 5, 6), "ANA COSTA");
        using var command = new SqlCommand();
        var plan = V9GoldPreCreationSecondSearch.Plan(runtime, observation);
        var query = V9GoldPreCreationSecondSearch.BuildQuery(command, runtime, observation);
        Multiple(() =>
        {
            Assert.That(V9GoldPreCreationSecondSearch.Catalogue.Count, Is.EqualTo(19));
            Assert.That(V9GoldPreCreationSecondSearch.Catalogue.Select(p => p.PassId).Distinct().Count(),
                Is.EqualTo(19));
            Assert.That(plan.Select(p => p.PassId).Count(id => id == "P_FIRST_M_FIRST"), Is.EqualTo(1));
            Assert.That(plan.Any(p => p.PassId == "P_LAST_M_FIRST"), Is.True);
            Assert.That(plan.Any(p => p.PassId == "P_ANY_DOB_MONTHDAY"), Is.True);
            Assert.That(query, Does.Contain(" UNION ").And.Contain(" INTERSECT "));
            Assert.That(query, Does.Contain("projection_schema_version=@blocking_projection_schema"));
            Assert.That(query, Does.Contain("projection_fingerprint_sha256=@blocking_projection_fingerprint"));
            Assert.That(query, Does.Not.Contain("MARIA").And.Not.Contain("1980"));
            Assert.That(command.Parameters.Count, Is.GreaterThan(5));
            Assert.That(command.Parameters["@blocking_projection_fingerprint"].Value,
                Is.EqualTo(runtime.ProjectionFingerprintSha256));
        });
    }

    [Test]
    public void Missing_attributes_skip_ineligible_catalogue_passes_instead_of_wildcarding()
    {
        var runtime = Runtime();
        var observation = new IdentityObservation(null, "SEM_CPF", "MARIA SILVA", null, null);
        var plan = V9GoldPreCreationSecondSearch.Plan(runtime, observation);
        Assert.That(plan.All(p => p.Clauses.All(c => c.Values.Count > 0)), Is.True);
        Assert.That(plan.Any(p => p.PassId == "P_FIRST_BIRTH_YEAR"), Is.False);
        Assert.That(plan.Any(p => p.PassId == "P_FIRST_M_FIRST"), Is.False);
    }
}

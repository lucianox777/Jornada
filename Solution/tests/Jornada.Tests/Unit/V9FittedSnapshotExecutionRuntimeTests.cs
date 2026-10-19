using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9FittedSnapshotExecutionRuntimeTests
{
    private static string Hash(char value) => new(value, 64);
    private static void Reject<T>(Action action) where T : Exception => Assert.Throws<T>(action);
    private static void Multiple(Action action) => Assert.Multiple(action);
    private static readonly Guid Model = Guid.Parse("12345678-1234-1234-1234-123456789012");
    private static readonly Guid Candidate = Guid.Parse("10000000-0000-0000-0000-000000000001");

    private static V9FittedSnapshot Snapshot(int prefix = 4, decimal threshold = .8m, decimal margin = .5m,
        IReadOnlyList<LinkageBlockingPass>? passes = null)
    {
        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        return V9FittedSnapshot.Create(new(V9FittedSnapshot.SchemaVersion,
            CompositeNominalComparisonV9.ContractVersion, V9CalibrationBlockingProjection.SchemaVersion,
            V9CalibrationBlockingProjection.ComputeSchemaFingerprint(prefix), Hash('b'), Hash('c'), Hash('d'), Hash('e'), Hash('f'),
            "adapter-v1", "fit", threshold, margin, 0m,
            passes ?? [new("prefix", [V9CalibrationBlockingProjection.NamePrefixFeature(3)])],
            levels.SelectMany(p => levels.Select(m => new FellegiSunterJointNominalCellV9(p, m, 1m / 81m, 1m / 81m))).ToArray(),
            new Dictionary<string, decimal>
            {
                [LinkageParameterCatalog.PriorMatchProbability] = .9m,
                [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
                [LinkageParameterCatalog.NeutralMissingEvidenceScoring] = 1m,
                [LinkageParameterCatalog.BirthSingleEvidenceScoring] = 1m,
                ["M_DATA_NASCIMENTO_EXACT"] = .9m, ["M_DATA_NASCIMENTO_DIFF"] = .1m,
                ["U_DATA_NASCIMENTO_EXACT"] = .1m, ["U_DATA_NASCIMENTO_DIFF"] = .9m
            }));
    }

    private static V9FittedSnapshotExecutionRuntime Load(V9FittedSnapshot snapshot) =>
        V9FittedSnapshotExecutionRuntime.Load(snapshot.SerializeUtf8(), snapshot.ContentSha256);
    private static IdentityObservation Observation(DateOnly? birth = null, string? mother = "ANA COSTA") =>
        new(null, "SEM_CPF", "MARIA SILVA", birth, mother);
    private static V9SnapshotCandidate Target(Guid? id = null) => new(id ?? Candidate, "MARIA SILVA", null, null);
    private static ProbabilisticLinkageDecision Resolve(V9FittedSnapshotExecutionRuntime runtime,
        IReadOnlyList<V9SnapshotCandidate> candidates) => runtime.ResolveRetrievedCandidatesPreview(Model, Observation(),
            new(runtime.SnapshotSha256, runtime.ProjectionSchemaVersion, runtime.ProjectionFingerprintSha256, true, candidates));

    [Test]
    public void Exact_projection_digest_resolves_prefix_configuration_and_actual_keys()
    {
        var four = Load(Snapshot(4));
        var seven = Load(Snapshot(7));
        Multiple(() =>
        {
            Assert.That(four.MaximumPrefixLength, Is.EqualTo(4));
            Assert.That(seven.MaximumPrefixLength, Is.EqualTo(7));
            Assert.That(four.ProjectionFingerprintSha256, Is.Not.EqualTo(seven.ProjectionFingerprintSha256));
            Assert.That(four.Project(Observation()), Does.Contain(new BlockingProjectionKey("name_prefix_4", "MARI")));
            Assert.That(four.Project(Observation()).Any(k => k.Feature == "name_prefix_7"), Is.False);
            Assert.That(seven.Project(Observation()), Does.Contain(new BlockingProjectionKey("name_prefix_7", "MARIA S")));
            Assert.That(seven.Project(Observation()), Does.Contain(new BlockingProjectionKey("mother_name_prefix_4", "ANA ")));
        });
    }

    [Test]
    public void Plans_frozen_order_and_omits_passes_with_missing_fields_without_wildcards()
    {
        var runtime = Load(Snapshot(passes:
        [
            new("z-first", ["name_prefix_3", BlockingFeatureNames.BirthYear]),
            new("a-second", ["mother_name_prefix_3"])
        ]));
        Assert.That(runtime.Plan(Observation(new DateOnly(1980, 5, 6))).Select(p => p.PassId),
            Is.EqualTo(new[] { "z-first", "a-second" }));
        var missingBirth = runtime.Plan(Observation());
        Assert.That(missingBirth.Select(p => p.PassId), Is.EqualTo(new[] { "a-second" }));
        Assert.That(missingBirth.Single().Clauses.Single().Values, Is.EqualTo(new[] { "ANA" }));
        Assert.That(runtime.Plan(Observation(mother: null)), Is.Empty);
    }

    [Test]
    public void Candidate_sql_reuses_union_intersect_and_binds_actual_projection_and_prefix_values()
    {
        var runtime = Load(Snapshot(passes:
        [
            new("person-birth", ["name_prefix_3", BlockingFeatureNames.BirthYear]),
            new("mother", ["mother_name_prefix_4"])
        ]));
        using var command = new SqlCommand();
        var sql = runtime.BuildCandidateUuidQuery(command, Observation(new DateOnly(1980, 5, 6)),
            runtime.ProjectionSchemaVersion, runtime.ProjectionFingerprintSha256);
        var parameters = command.Parameters.Cast<SqlParameter>().ToDictionary(p => p.ParameterName, p => p.Value);
        Multiple(() =>
        {
            Assert.That(sql, Does.Contain(" INTERSECT ").And.Contain(" UNION "));
            Assert.That(sql, Does.Contain("projection_schema_version=@blocking_projection_schema"));
            Assert.That(parameters["@blocking_projection_schema"], Is.EqualTo(V9CalibrationBlockingProjection.SchemaVersion));
            Assert.That(parameters["@blocking_projection_fingerprint"], Is.EqualTo(runtime.ProjectionFingerprintSha256));
            Assert.That(parameters["@bp_0_0_feature"], Is.EqualTo("name_prefix_3"));
            Assert.That(parameters["@bp_0_0_v0"], Is.EqualTo("MAR"));
            Assert.That(parameters["@bp_0_1_v0"], Is.EqualTo("1980"));
            Assert.That(parameters["@bp_1_0_v0"], Is.EqualTo("ANA "));
            Assert.That(sql, Does.Not.Contain("MAR").And.Not.Contain("ANA"));
            // Year is stable/current; both prefix aliases retain historical keys.
            Assert.That(sql.Split("vigencia_fim IS NULL", StringSplitOptions.None), Has.Length.EqualTo(2));
        });
    }

    [Test]
    public void Legacy_materialization_cannot_silently_execute_or_resolve_prefix_snapshot()
    {
        var runtime = Load(Snapshot());
        using var command = new SqlCommand();
        Reject<InvalidDataException>(() => runtime.BuildCandidateUuidQuery(command, Observation(),
            PersonResolutionProjectionContract.SchemaVersion, PersonResolutionProjectionContract.FingerprintSha256));
        Assert.That(command.Parameters, Has.Count.EqualTo(0));
        Reject<InvalidDataException>(() => runtime.ResolveRetrievedCandidatesPreview(Model, Observation(), new(runtime.SnapshotSha256,
            PersonResolutionProjectionContract.SchemaVersion, PersonResolutionProjectionContract.FingerprintSha256, true, [])));
        Reject<InvalidOperationException>(() => BlockingProjectionCandidateQueryBuilder.BuildCandidateUuidQuery(
            command, runtime.Plan(Observation()), runtime.ProjectionSchemaVersion, runtime.ProjectionFingerprintSha256));
    }

    [Test]
    public void Pinned_fit_uses_shared_resolve_and_changed_threshold_and_margin_change_real_outcomes()
    {
        var fit = Snapshot();
        var runtime = Load(fit);
        var candidates = new[] { Target() };
        var decision = Resolve(runtime, candidates);
        Assert.That(decision, Is.EqualTo(fit.ResolvePreview(Model, Observation(), candidates)));
        Assert.That(decision.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
        Assert.That(decision.PessoaUuidResolvido, Is.EqualTo(Candidate));
        Assert.That(Resolve(Load(Snapshot(threshold: .95m)), candidates).Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
        var tie = new[] { Target(), Target(Guid.Parse("10000000-0000-0000-0000-000000000002")) };
        Assert.That(Resolve(runtime, tie).Status, Is.EqualTo(ResolutionStatus.CONFLITO));
        Assert.That(Resolve(Load(Snapshot(margin: 0m)), tie).Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
    }

    [Test]
    public void Rejects_unknown_projection_or_features_bad_pin_cpf_and_truncated_candidate_frame()
    {
        var fit = Snapshot();
        var wrongProjection = V9FittedSnapshot.Create(fit.Payload with { ProjectionFingerprintSha256 = Hash('a') });
        Reject<InvalidDataException>(() => Load(wrongProjection));
        Reject<InvalidDataException>(() => Load(Snapshot(passes: [new("invalid", ["name_prefix_7"])])));
        Reject<InvalidDataException>(() => V9FittedSnapshotExecutionRuntime.Load(fit.SerializeUtf8(), Hash('a')));
        var runtime = Load(fit);
        Reject<InvalidOperationException>(() => runtime.Project(Observation() with { Cpf = "12345678909" }));
        Reject<InvalidDataException>(() => runtime.ResolveRetrievedCandidatesPreview(Model, Observation(), new(runtime.SnapshotSha256,
            runtime.ProjectionSchemaVersion, runtime.ProjectionFingerprintSha256, false, [Target()])));
        Reject<InvalidDataException>(() => runtime.ResolveRetrievedCandidatesPreview(Model, Observation(), new(Hash('a'),
            runtime.ProjectionSchemaVersion, runtime.ProjectionFingerprintSha256, true, [Target()])));
        var missing = Load(Snapshot(passes: [new("mother", ["mother_name_prefix_3"])]));
        Reject<InvalidDataException>(() => missing.ResolveRetrievedCandidatesPreview(Model, Observation(mother: null), new(missing.SnapshotSha256,
            missing.ProjectionSchemaVersion, missing.ProjectionFingerprintSha256, true, [Target()])));
    }

    [Test]
    public void Current_legacy_projection_fit_still_projects_through_existing_contract()
    {
        var fit = Snapshot();
        var legacy = V9FittedSnapshot.Create(fit.Payload with
        {
            ProjectionSchemaVersion = PersonResolutionProjectionContract.SchemaVersion,
            ProjectionFingerprintSha256 = PersonResolutionProjectionContract.FingerprintSha256,
            BlockingPasses = [new("first", [BlockingFeatureNames.FirstName])]
        });
        var runtime = Load(legacy);
        Assert.That(runtime.MaximumPrefixLength, Is.Null);
        Assert.That(runtime.Plan(Observation()).Single().Clauses.Single().Values, Is.EqualTo(new[] { "MARIA" }));
        Assert.That(Resolve(runtime, [Target()]), Is.EqualTo(legacy.ResolvePreview(Model, Observation(), [Target()])));
    }
}

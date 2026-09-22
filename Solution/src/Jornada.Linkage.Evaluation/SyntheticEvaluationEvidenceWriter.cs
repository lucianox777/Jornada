using System.Data;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Evaluation;

public sealed record SyntheticEvaluationPersistenceResult(
    Guid EvaluationId,
    Guid RunGroupId);

public sealed class SyntheticEvaluationEvidenceWriter(
    SqlConnection connection,
    int commandTimeoutSeconds)
{
    private const string MetricTypeName = "auditoria.linkage_avaliacao_sintetica_metrica_tvp";

    public async Task<SyntheticEvaluationPersistenceResult> PersistAsync(
        SyntheticEvaluationReport report,
        string reportSha256,
        Guid runGroupId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (runGroupId == Guid.Empty)
            throw new ArgumentException("RunGroupId obrigatório.", nameof(runGroupId));
        if (!IsSha256(reportSha256))
            throw new ArgumentException("reportSha256 deve ser SHA-256 hexadecimal.", nameof(reportSha256));
        if (!string.Equals(
                report.EnvironmentProfile,
                SyntheticEvaluationEngine.RequiredEnvironmentProfile,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Persistência sintética exige EnvironmentProfile={SyntheticEvaluationEngine.RequiredEnvironmentProfile}.");
        }
        if (!string.Equals(report.Model.Status, "RASCUNHO", StringComparison.Ordinal))
            throw new InvalidOperationException("Persistência sintética aceita somente modelo RASCUNHO.");

        var metrics = BuildMetrics(report);

        await using var command = new SqlCommand(
            """
            DECLARE @avaliacao_id UNIQUEIDENTIFIER;
            EXEC auditoria.sp_registrar_avaliacao_sintetica_linkage
                @grupo_execucao_id=@grupo_execucao_id,
                @modelo_id=@modelo_id,
                @modelo_versao=@modelo_versao,
                @modelo_snapshot_sha256=@modelo_snapshot_sha256,
                @corpus_fingerprint_sha256=@corpus_fingerprint_sha256,
                @generation_manifest_sha256=@generation_manifest_sha256,
                @bridge_manifest_sha256=@bridge_manifest_sha256,
                @observations_sha256=@observations_sha256,
                @bridge_truth_sha256=@bridge_truth_sha256,
                @relatorio_schema_versao=@relatorio_schema_versao,
                @natureza=@natureza,
                @finalidade=@finalidade,
                @gerador_versao=@gerador_versao,
                @gerador_seed=@gerador_seed,
                @avaliador_versao=@avaliador_versao,
                @ambiente_perfil=@ambiente_perfil,
                @status=N'CONCLUIDA',
                @ruleset_versao=@ruleset_versao,
                @ruleset_fingerprint_sha256=@ruleset_fingerprint_sha256,
                @u_semantica=@u_semantica,
                @m_truth_universo=@m_truth_universo,
                @u_truth_universo=@u_truth_universo,
                @u_nome_fonte_nominal=@u_nome_fonte_nominal,
                @u_nome_mae_fonte_nominal=@u_nome_mae_fonte_nominal,
                @observacoes_materializadas=@observacoes_materializadas,
                @observacoes_excluidas=@observacoes_excluidas,
                @report_sha256=@report_sha256,
                @metricas=@metricas,
                @avaliacao_id=@avaliacao_id OUTPUT;
            SELECT @avaliacao_id;
            """,
            connection)
        {
            CommandTimeout = commandTimeoutSeconds
        };

        command.Parameters.Add("@grupo_execucao_id", SqlDbType.UniqueIdentifier).Value = runGroupId;
        command.Parameters.Add("@modelo_id", SqlDbType.UniqueIdentifier).Value = report.Model.ModelId;
        command.Parameters.Add("@modelo_versao", SqlDbType.Int).Value = report.Model.Version;
        AddHash(command, "@modelo_snapshot_sha256", report.Model.ModelSnapshotSha256);
        AddHash(command, "@corpus_fingerprint_sha256", report.Input.CorpusInputFingerprintSha256);
        AddHash(command, "@generation_manifest_sha256", report.Input.GenerationManifestSha256);
        AddHash(command, "@bridge_manifest_sha256", report.Input.BridgeManifestSha256);
        AddHash(command, "@observations_sha256", report.Input.ObservationsSha256);
        AddHash(command, "@bridge_truth_sha256", report.Input.BridgeTruthSha256);
        command.Parameters.Add("@relatorio_schema_versao", SqlDbType.NVarChar, 120).Value = report.SchemaVersion;
        command.Parameters.Add("@natureza", SqlDbType.NVarChar, 80).Value = report.Nature;
        command.Parameters.Add("@finalidade", SqlDbType.NVarChar, 120).Value = report.Purpose;
        command.Parameters.Add("@gerador_versao", SqlDbType.NVarChar, 120).Value = report.Input.GeneratorVersion;
        var seed = command.Parameters.Add("@gerador_seed", SqlDbType.Decimal);
        seed.Precision = 20;
        seed.Scale = 0;
        seed.Value = (decimal)report.Input.GeneratorSeed;
        command.Parameters.Add("@avaliador_versao", SqlDbType.NVarChar, 120).Value = report.EvaluatorVersion;
        command.Parameters.Add("@ambiente_perfil", SqlDbType.NVarChar, 32).Value = report.EnvironmentProfile;
        command.Parameters.Add("@ruleset_versao", SqlDbType.NVarChar, 120).Value = report.Model.RuleSetVersion;
        AddHash(command, "@ruleset_fingerprint_sha256", report.Model.RuleSetFingerprintSha256);
        command.Parameters.Add("@u_semantica", SqlDbType.NVarChar, 120).Value = report.Model.UProbabilitySemantics;
        command.Parameters.Add("@m_truth_universo", SqlDbType.NVarChar, 220).Value = report.MRecovery.TruthUniverse;
        command.Parameters.Add("@u_truth_universo", SqlDbType.NVarChar, 220).Value = report.URecovery.TruthUniverse;
        command.Parameters.Add("@u_nome_fonte_nominal", SqlDbType.NVarChar, 80).Value = report.Model.NominalNameUSource;
        command.Parameters.Add("@u_nome_mae_fonte_nominal", SqlDbType.NVarChar, 80).Value = report.Model.NominalMotherNameUSource;
        command.Parameters.Add("@observacoes_materializadas", SqlDbType.BigInt).Value = report.Input.MaterializedObservationCount;
        command.Parameters.Add("@observacoes_excluidas", SqlDbType.BigInt).Value = report.Input.ExcludedObservationCount;
        AddHash(command, "@report_sha256", reportSha256);

        var metricParameter = command.Parameters.AddWithValue("@metricas", metrics);
        metricParameter.SqlDbType = SqlDbType.Structured;
        metricParameter.TypeName = MetricTypeName;

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is not Guid evaluationId || evaluationId == Guid.Empty)
            throw new InvalidDataException("Ledger sintético não retornou avaliacao_id válido.");

        return new SyntheticEvaluationPersistenceResult(evaluationId, runGroupId);
    }

    private static DataTable BuildMetrics(SyntheticEvaluationReport report)
    {
        var table = new DataTable();
        table.Columns.Add("escopo", typeof(string));
        table.Columns.Add("dimensao", typeof(string));
        table.Columns.Add("metrica", typeof(string));
        table.Columns.Add("valor", typeof(decimal));
        table.Columns.Add("unidade", typeof(string));

        Add(table, "OBSERVATION_STRATA", null, "CPF_PRESENT_CNS_PRESENT",
            report.ObservationStrata.CpfPresentCnsPresent, "COUNT");
        Add(table, "OBSERVATION_STRATA", null, "CPF_PRESENT_CNS_ABSENT",
            report.ObservationStrata.CpfPresentCnsAbsent, "COUNT");
        Add(table, "OBSERVATION_STRATA", null, "CPF_ABSENT_CNS_PRESENT",
            report.ObservationStrata.CpfAbsentCnsPresent, "COUNT");
        Add(table, "OBSERVATION_STRATA", null, "CPF_ABSENT_CNS_ABSENT",
            report.ObservationStrata.CpfAbsentCnsAbsent, "COUNT");

        Add(table, "BLOCKING", null, "MATERIALIZED_OBSERVATIONS", report.Blocking.MaterializedObservations, "COUNT");
        Add(table, "BLOCKING", null, "TRUE_INTER_SOURCE_PAIRS", report.Blocking.TrueInterSourcePairs, "COUNT");
        Add(table, "BLOCKING", null, "TRUE_PAIRS_RETAINED_BY_UNION", report.Blocking.TruePairsRetainedByUnion, "COUNT");
        Add(table, "BLOCKING", null, "TRUE_MATCH_RECALL", report.Blocking.TrueMatchRecall, "RATIO");
        Add(table, "BLOCKING", null, "POSSIBLE_NON_MATCH_PAIRS", report.Blocking.PossibleNonMatchPairs, "COUNT");
        Add(table, "BLOCKING", null, "CANDIDATE_UNION_PAIRS", report.Blocking.CandidateUnionPairs, "COUNT");
        Add(table, "BLOCKING", null, "CANDIDATE_UNION_NON_MATCH_PAIRS", report.Blocking.CandidateUnionNonMatchPairs, "COUNT");
        Add(table, "BLOCKING", null, "NON_MATCH_RETENTION", report.Blocking.NonMatchRetention, "RATIO");
        Add(table, "BLOCKING", null, "REDUCTION_RATIO", report.Blocking.ReductionRatio, "RATIO");

        foreach (var pass in report.Blocking.Passes)
        {
            Add(table, "BLOCKING_PASS", pass.PassId, "CANDIDATE_PAIRS", pass.CandidatePairs, "COUNT");
            Add(table, "BLOCKING_PASS", pass.PassId, "CANDIDATE_NON_MATCH_PAIRS", pass.CandidateNonMatchPairs, "COUNT");
            Add(table, "BLOCKING_PASS", pass.PassId, "TRUE_PAIRS_RETAINED", pass.TruePairsRetained, "COUNT");
            Add(table, "BLOCKING_PASS", pass.PassId, "TRUE_MATCH_RECALL", pass.TrueMatchRecall, "RATIO");
        }

        AddRecovery(table, "M", report.MRecovery);
        AddRecovery(table, "U", report.URecovery);

        Add(table, "TRANSPORTABILITY", null, "CPF_LABELED_PAIRS", report.Transportability.CpfLabeledPairs, "COUNT");
        Add(table, "TRANSPORTABILITY", null, "CPF_ABSENT_PAIRS", report.Transportability.CpfAbsentPairs, "COUNT");
        AddDistance(table, "TRANSPORTABILITY_RAW", report.Transportability.RawDistance);
        AddDistance(table, "TRANSPORTABILITY_REWEIGHTED", report.Transportability.ReweightedDistance);

        return table;
    }

    private static void AddRecovery(
        DataTable table,
        string prefix,
        SyntheticDistributionRecovery recovery)
    {
        Add(table, prefix + "_RECOVERY", null, "PAIR_COUNT", recovery.PairCount, "COUNT");
        AddDistribution(table, prefix + "_TRUTH_RAW", recovery.TruthRaw);
        AddDistribution(table, prefix + "_TRUTH_REWEIGHTED", recovery.TruthReweighted);
        AddDistribution(table, prefix + "_MODEL", recovery.Model);
        AddDistance(table, prefix + "_DISTANCE_RAW", recovery.RawDistance);
        AddDistance(table, prefix + "_DISTANCE_REWEIGHTED", recovery.ReweightedDistance);
    }

    private static void AddDistribution(
        DataTable table,
        string prefix,
        SyntheticComparisonDistribution distribution)
    {
        foreach (var (state, value) in distribution.Name.OrderBy(static x => x.Key, StringComparer.Ordinal))
            Add(table, prefix + "_NAME", state, "PROBABILITY", value, "PROBABILITY");
        foreach (var (state, value) in distribution.MotherName.OrderBy(static x => x.Key, StringComparer.Ordinal))
            Add(table, prefix + "_MOTHER_NAME", state, "PROBABILITY", value, "PROBABILITY");
        foreach (var (state, value) in distribution.BirthSemantic.OrderBy(static x => x.Key, StringComparer.Ordinal))
            Add(table, prefix + "_BIRTH_SEMANTIC", state, "PROBABILITY", value, "PROBABILITY");
    }

    private static void AddDistance(
        DataTable table,
        string scope,
        SyntheticDistributionDistance distance)
    {
        Add(table, scope, null, "NAME", distance.NameTotalVariation, "TOTAL_VARIATION");
        Add(table, scope, null, "MOTHER_NAME", distance.MotherNameTotalVariation, "TOTAL_VARIATION");
        Add(table, scope, null, "BIRTH_SEMANTIC", distance.BirthSemanticTotalVariation, "TOTAL_VARIATION");
    }

    private static void Add(
        DataTable table,
        string scope,
        string? dimension,
        string metric,
        long value,
        string unit)
        => Add(table, scope, dimension, metric, (decimal)value, unit);

    private static void Add(
        DataTable table,
        string scope,
        string? dimension,
        string metric,
        decimal value,
        string unit)
    {
        var row = table.NewRow();
        row["escopo"] = scope;
        row["dimensao"] = dimension is null ? DBNull.Value : dimension;
        row["metrica"] = metric;
        row["valor"] = value;
        row["unidade"] = unit;
        table.Rows.Add(row);
    }

    private static void AddHash(SqlCommand command, string name, string value)
    {
        if (!IsSha256(value))
            throw new InvalidDataException($"{name} não contém SHA-256 válido.");
        command.Parameters.Add(name, SqlDbType.Binary, 32).Value = Convert.FromHexString(value);
    }

    private static bool IsSha256(string? value)
    {
        if (value is null || value.Length != 64)
            return false;
        foreach (var character in value)
        {
            if (!(character is >= '0' and <= '9'
                  or >= 'a' and <= 'f'
                  or >= 'A' and <= 'F'))
                return false;
        }
        return true;
    }
}

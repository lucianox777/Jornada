using System.Data;
using Microsoft.Data.SqlClient;
using Jornada.Operational.Sql;

namespace Jornada.Linkage.Runner;

public sealed record Dt05ReplayPreparation(
    long HighWatermark, IReadOnlyList<long> ObservationIds, IReadOnlyList<Dt05BronzePin> Pins,
    long CandidateReferenceCount, string CandidateSetSha256, long GovernanceEventHighWatermark);

public sealed record Dt05HistoricalCandidateStateBinding(
    Guid SourceRunId, string ManifestLogicalPath, string ManifestSha256,
    string PartitionSetSha256, long CandidateReferenceCount, string CandidateSetSha256);
public sealed record Dt05HistoricalBlockingProjectionBinding(
    Guid SourceRunId, string ManifestLogicalPath, string ManifestSha256, string PartitionSetSha256,
    string NormalizationVersion, string ProjectionSchemaVersion, string ProjectionFingerprintSha256);
public sealed record Dt05HistoricalRunIdentity(Guid SourceRunId, Guid ModelId, int ModelVersion, long ObservationHighWatermark);


public sealed class Dt05ReplaySql(IOperationalSqlAdapter sql)
{
    public async Task<Dt05HistoricalRunIdentity> ReadHistoricalRunIdentityAsync(Guid sourceRunId, CancellationToken ct)
    {
        await using var connection=await sql.OpenAsync(ct);
        await using var command=new SqlCommand("""
            SELECT modelo_id,modelo_versao,pessoa_observacao_id_high_watermark,status
              FROM identidade.linkage_run WHERE linkage_run_id=@run_id;
            """,connection);
        command.Parameters.Add("@run_id",SqlDbType.UniqueIdentifier).Value=sourceRunId;
        await using var reader=await command.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct))
            throw new InvalidOperationException("DT-05: source run histórico inexistente; replay recusado.");
        if(!string.Equals(reader.GetString(3),"PUBLICADO",StringComparison.Ordinal))
            throw new InvalidOperationException("DT-05: source run precisa estar PUBLICADO para replay determinístico.");
        return new(sourceRunId,reader.GetGuid(0),reader.GetInt32(1),reader.GetInt64(2));
    }

    public async Task<Dt05HistoricalBlockingProjectionBinding> ReadHistoricalBlockingProjectionBindingAsync(Guid sourceRunId, CancellationToken ct)
    {
        await using var connection=await sql.OpenAsync(ct);
        await using var command=new SqlCommand("""
            SELECT blocking_projection_caminho_logico,blocking_projection_manifesto_sha256,
                   blocking_projection_partition_set_sha256,normalization_version,
                   projection_schema_version,projection_fingerprint_sha256
              FROM identidade.linkage_replay_manifesto
             WHERE linkage_run_id=@run_id AND schema_version=4;
            """,connection);
        command.Parameters.Add("@run_id",SqlDbType.UniqueIdentifier).Value=sourceRunId;
        await using var reader=await command.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct) || Enumerable.Range(0,6).Any(reader.IsDBNull))
            throw new InvalidOperationException("DT-05: run histórico não possui binding blocking-projection v4 completo; replay recusado.");
        var path=reader.GetString(0).Trim(); var manifest=reader.GetString(1).Trim().ToLowerInvariant();
        var partitions=reader.GetString(2).Trim().ToLowerInvariant(); var normalization=reader.GetString(3).Trim();
        var schema=reader.GetString(4).Trim(); var fingerprint=reader.GetString(5).Trim().ToLowerInvariant();
        if(!path.StartsWith("linkage-snapshots/v1/blocking-projection/manifests/",StringComparison.Ordinal)
           || path.Contains("..",StringComparison.Ordinal) || Path.IsPathRooted(path)
           || !IsSha256(manifest)||!IsSha256(partitions)||!IsSha256(fingerprint)
           || string.IsNullOrWhiteSpace(normalization)||string.IsNullOrWhiteSpace(schema))
            throw new InvalidDataException("DT-05: binding blocking-projection histórico inválido; replay recusado.");
        return new(sourceRunId,path,manifest,partitions,normalization,schema,fingerprint);
    }

    public async Task<Dt05HistoricalCandidateStateBinding> ReadHistoricalCandidateStateBindingAsync(Guid sourceRunId, CancellationToken ct)
    {
        await using var connection = await sql.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT m.candidate_state_caminho_logico,m.candidate_state_manifesto_sha256,
                   m.candidate_state_partition_set_sha256,m.candidatos_referencia,m.candidatos_sha256
              FROM identidade.linkage_replay_manifesto m
             WHERE m.linkage_run_id=@run_id AND m.schema_version IN (3,4);
            """, connection);
        command.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = sourceRunId;
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || Enumerable.Range(0, 5).Any(reader.IsDBNull))
            throw new InvalidOperationException("DT-05: run histórico não possui binding candidate-state v3/v4 completo; replay recusado.");
        var path = reader.GetString(0).Trim();
        var manifestSha = reader.GetString(1).Trim().ToLowerInvariant();
        var partitionSha = reader.GetString(2).Trim().ToLowerInvariant();
        var count = reader.GetInt64(3);
        var candidateSha = reader.GetString(4).Trim().ToLowerInvariant();
        if (!path.StartsWith("linkage-snapshots/v1/candidate-state/manifests/", StringComparison.Ordinal)
            || path.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(path)
            || !IsSha256(manifestSha) || !IsSha256(partitionSha) || !IsSha256(candidateSha) || count < 0)
            throw new InvalidDataException("DT-05: binding candidate-state histórico inválido; replay recusado.");
        return new Dt05HistoricalCandidateStateBinding(sourceRunId, path, manifestSha, partitionSha, count, candidateSha);
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public async Task CaptureGovernanceStateAsync(Guid runId, CancellationToken ct)
    {
        await using var connection = await sql.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using var command = new SqlCommand(
                "EXEC identidade.sp_capturar_estado_governanca_linkage @linkage_run_id=@run_id;",
                connection, transaction);
            command.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
            await command.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<Dt05ReplayPreparation> ReadPreparationAsync(Guid runId, CancellationToken ct)
    {
        await using var connection = await sql.OpenAsync(ct);
        long highWatermark;
        var ids = new List<long>();
        var pins = new List<Dt05BronzePin>();
        long candidateReferenceCount;
        string candidateSetSha256;
        long governanceEventHighWatermark;

        await using (var command = new SqlCommand("""
            SELECT pessoa_observacao_id_high_watermark
              FROM identidade.linkage_run
             WHERE linkage_run_id=@run_id AND status=N'EXECUTANDO';
            SELECT pessoa_observacao_id
              FROM identidade.linkage_run_item
             WHERE linkage_run_id=@run_id
             ORDER BY pessoa_observacao_id;
            SELECT objeto_chave,payload_sha256
              FROM identidade.linkage_bronze_pin
             WHERE linkage_run_id=@run_id
             ORDER BY objeto_chave;
            SELECT candidatos_referencia,candidatos_sha256,governanca_evento_high_watermark
              FROM identidade.linkage_replay_estado_governanca
             WHERE linkage_run_id=@run_id;
            """, connection))
        {
            command.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) || reader.IsDBNull(0))
                throw new InvalidOperationException("DT-05: run ativo/high-watermark não encontrado.");
            highWatermark = reader.GetInt64(0);
            if (!await reader.NextResultAsync(ct))
                throw new InvalidOperationException("DT-05: conjunto lógico do run ausente.");
            while (await reader.ReadAsync(ct)) ids.Add(reader.GetInt64(0));
            if (!await reader.NextResultAsync(ct))
                throw new InvalidOperationException("DT-05: pins Bronze do run ausentes.");
            while (await reader.ReadAsync(ct))
                pins.Add(new Dt05BronzePin(reader.GetString(0), reader.GetString(1).Trim().ToLowerInvariant()));
            if (!await reader.NextResultAsync(ct) || !await reader.ReadAsync(ct))
                throw new InvalidOperationException("DT-05: identidade de candidatos/governança do run ausente.");
            candidateReferenceCount = reader.GetInt64(0);
            candidateSetSha256 = reader.GetString(1).Trim().ToLowerInvariant();
            governanceEventHighWatermark = reader.GetInt64(2);
        }
        if (pins.Count == 0) throw new InvalidOperationException("DT-05: nenhum pin Bronze capturado.");
        return new Dt05ReplayPreparation(
            highWatermark, ids, pins, candidateReferenceCount, candidateSetSha256, governanceEventHighWatermark);
    }

    public async Task RegisterAsync(
        Guid runId, string logicalPath, string manifestSha256, string bronzeSetSha256,
        Dt05ReplayManifestIdentity identity, Dt05CandidateStateSnapshot candidateState,
        Dt05BlockingProjectionSnapshot blockingProjection, CancellationToken ct)
    {
        await using var connection = await sql.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await using var command = new SqlCommand("""
                EXEC identidade.sp_registrar_manifesto_replay_linkage
                    @linkage_run_id=@run_id,@schema_version=4,@caminho_logico=@path,
                    @manifesto_sha256=@manifest_sha,@bronze_set_sha256=@bronze_sha,
                    @scorer_version=@scorer,@ruleset_version=@ruleset,
                    @model_version=@model,@input_snapshot_id=@snapshot,
                    @normalization_version=@normalization,@resolution_catalog_version=@catalog,
                    @projection_schema_version=@projection_schema,@projection_fingerprint_sha256=@projection_sha,
                    @candidatos_referencia=@candidate_count,@candidatos_sha256=@candidate_sha,
                    @governanca_evento_high_watermark=@governance_hwm,
                    @candidate_state_caminho_logico=@candidate_state_path,
                    @candidate_state_manifesto_sha256=@candidate_state_manifest_sha,
                    @candidate_state_partition_set_sha256=@candidate_state_partition_sha,
                    @blocking_projection_caminho_logico=@blocking_path,
                    @blocking_projection_manifesto_sha256=@blocking_manifest_sha,
                    @blocking_projection_partition_set_sha256=@blocking_partition_sha;
                """, connection, transaction);
            command.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
            command.Parameters.Add("@path", SqlDbType.NVarChar, 1024).Value = logicalPath;
            command.Parameters.Add("@manifest_sha", SqlDbType.Char, 64).Value = manifestSha256;
            command.Parameters.Add("@bronze_sha", SqlDbType.Char, 64).Value = bronzeSetSha256;
            command.Parameters.Add("@scorer", SqlDbType.NVarChar, 120).Value = identity.ScorerVersion;
            command.Parameters.Add("@ruleset", SqlDbType.NVarChar, 120).Value = identity.RuleSetVersion;
            command.Parameters.Add("@model", SqlDbType.NVarChar, 120).Value = identity.ModelVersion;
            command.Parameters.Add("@snapshot", SqlDbType.NVarChar, 200).Value = identity.InputSnapshotId;
            command.Parameters.Add("@normalization", SqlDbType.NVarChar, 120).Value = identity.NormalizationVersion;
            command.Parameters.Add("@catalog", SqlDbType.NVarChar, 120).Value = identity.ResolutionCatalogVersion;
            command.Parameters.Add("@projection_schema", SqlDbType.NVarChar, 120).Value = identity.ProjectionSchemaVersion;
            command.Parameters.Add("@projection_sha", SqlDbType.Char, 64).Value = identity.ProjectionFingerprintSha256;
            command.Parameters.Add("@candidate_count", SqlDbType.BigInt).Value = identity.CandidateReferenceCount;
            command.Parameters.Add("@candidate_sha", SqlDbType.Char, 64).Value = identity.CandidateSetSha256;
            command.Parameters.Add("@governance_hwm", SqlDbType.BigInt).Value = identity.GovernanceEventHighWatermark;
            command.Parameters.Add("@candidate_state_path", SqlDbType.NVarChar, 1024).Value = candidateState.ManifestLogicalPath;
            command.Parameters.Add("@candidate_state_manifest_sha", SqlDbType.Char, 64).Value = candidateState.ManifestSha256;
            command.Parameters.Add("@candidate_state_partition_sha", SqlDbType.Char, 64).Value = candidateState.PartitionSetSha256;
            command.Parameters.Add("@blocking_path", SqlDbType.NVarChar, 1024).Value = blockingProjection.ManifestLogicalPath;
            command.Parameters.Add("@blocking_manifest_sha", SqlDbType.Char, 64).Value = blockingProjection.ManifestSha256;
            command.Parameters.Add("@blocking_partition_sha", SqlDbType.Char, 64).Value = blockingProjection.PartitionSetSha256;
            await command.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}

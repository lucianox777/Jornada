using System.Data;
using Microsoft.Data.SqlClient;
using Jornada.Operational.Sql;

namespace Jornada.Linkage.Runner;

public sealed record Dt05ReplayPreparation(
    long HighWatermark, IReadOnlyList<long> ObservationIds, IReadOnlyList<Dt05BronzePin> Pins,
    long CandidateReferenceCount, string CandidateSetSha256, long GovernanceEventHighWatermark);

public sealed class Dt05ReplaySql(IOperationalSqlAdapter sql)
{
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
        Dt05ReplayManifestIdentity identity, CancellationToken ct)
    {
        await using var connection = await sql.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await using var command = new SqlCommand("""
                EXEC identidade.sp_registrar_manifesto_replay_linkage
                    @linkage_run_id=@run_id,@schema_version=3,@caminho_logico=@path,
                    @manifesto_sha256=@manifest_sha,@bronze_set_sha256=@bronze_sha,
                    @scorer_version=@scorer,@ruleset_version=@ruleset,
                    @model_version=@model,@input_snapshot_id=@snapshot,
                    @normalization_version=@normalization,@resolution_catalog_version=@catalog,
                    @projection_schema_version=@projection_schema,@projection_fingerprint_sha256=@projection_sha,
                    @candidatos_referencia=@candidate_count,@candidatos_sha256=@candidate_sha,
                    @governanca_evento_high_watermark=@governance_hwm;
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

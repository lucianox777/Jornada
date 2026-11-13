using System.Data;
using System.Data.Common;
using System.Security.Cryptography;

namespace Jornada.Linkage.Runner;

/// <summary>
/// The versioned V9 operational SQL representation consists of ONE immutable
/// model record with the ENTIRE canonical fitted payload (including joint
/// cells, four TF calibration slots and their conditional-U dictionaries).
/// Re-encoding a subset as legacy parametro_linkage would silently change
/// the fitted model. The Runner and independent TEST share this exact loader.
/// No active-model selection, DT-15 promotion or publications here.
/// </summary>
public static class V9OperationalSqlModelConverter
{
    public const string ContractVersion = "JORNADA_V9_OPERATIONAL_SQL_MODEL_V1";
    public const string TableName = "identidade.modelo_linkage_v9_operacional";

    public sealed record Receipt(string ContractVersion, Guid ModelId,
        string SourceSnapshotSha256, string FitFingerprintSha256,
        string ProjectionFingerprintSha256, int JointCellCount,
        int TfSlotCount, int BlockingPassCount,
        bool ByteExactRoundTrip, bool Active, bool Published);

    /// <summary>A deterministic model UUID is derived from the original winner SHA.</summary>
    public static Guid ModelIdFromWinnerSha(string expectedSha256)
    {
        if (string.IsNullOrEmpty(expectedSha256) || expectedSha256.Length != 64
            || expectedSha256.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("Identidade do vencedor SHA-256 inválida.");
        var id = new Guid(Convert.FromHexString(expectedSha256)[..16]);
        if (id == Guid.Empty)
            throw new InvalidDataException("UUID operacional vazio não permitido.");
        return id;
    }

    public static Receipt VerifyConversion(V9FittedSnapshot winner)
    {
        ArgumentNullException.ThrowIfNull(winner);
        var p = winner.Payload;
        if (p.SchemaVersion != V9FittedSnapshot.SupportSchemaVersion
            || p.JointSupport is null
            || p.JointCells.Count != 81
            || p.UnitTfCalibrations is null || p.UnitTfCalibrations.Count != 4
            || p.UnitTfCalibrations.Select(s => (s.Side, s.Unit)).Distinct().Count() != 4
            || p.CandidateGrouping?.CandidateGrouping != "BLIND_FS_COMPLETE_LINK_CANDIDATES"
            || p.BlockingPasses.Count == 0
            || p.GoldParentSnapshotSha256 is not null)
            throw new InvalidDataException(
                "Modelo V9 não tem contrato completo TF4/81 células e agrupamento independente.");
        var bytes = winner.SerializeUtf8();
        var reloaded = V9FittedSnapshot.Load(bytes, winner.ContentSha256);
        if (reloaded.ContentSha256 != winner.ContentSha256
            || !bytes.AsSpan().SequenceEqual(reloaded.SerializeUtf8()))
            throw new InvalidDataException("Conversão V9 alteraria conteúdo científico do vencedor.");
        return new Receipt(ContractVersion, ModelIdFromWinnerSha(winner.ContentSha256),
            winner.ContentSha256, p.FitFingerprintSha256,
            p.ProjectionFingerprintSha256, p.JointCells.Count,
            p.UnitTfCalibrations.Count, p.BlockingPasses.Count,
            ByteExactRoundTrip: true, Active: false, Published: false);
    }

    /// <summary>
    /// Materialize exactly one winner in a *pre-created*, isolated SQL schema.
    /// The table is insert-only: never overwrite an operational model.
    /// </summary>
    public static async Task<Receipt> MaterializeOnceAsync(DbConnection connection,
        V9FittedSnapshot winner, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("SQL operacional V9 deve estar aberto.");
        var receipt = VerifyConversion(winner);
        var bytes = winner.SerializeUtf8();
        await using var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            await using (var probe = Command(connection, transaction, """
                SELECT COUNT_BIG(*) FROM identidade.modelo_linkage_v9_operacional
                WHERE status=N'TEST_ONLY';
                """))
            {
                var count = Convert.ToInt64(await probe.ExecuteScalarAsync(ct),
                    System.Globalization.CultureInfo.InvariantCulture);
                if (count != 0)
                    throw new InvalidDataException("TEST SQL já tem modelo operacional; segundo vencedor proibido.");
            }
            await using (var insert = Command(connection, transaction, """
                INSERT INTO identidade.modelo_linkage_v9_operacional(
                    modelo_id, contrato, snapshot_schema, winner_snapshot_sha256,
                    fit_fingerprint_sha256, projection_fingerprint_sha256,
                    joint_cell_count, tf_slot_count, blocking_pass_count,
                    payload_fitted, status)
                VALUES(@id,@contract,@schema,@winner,@fit,@projection,
                    @cells,@tf,@passes,@payload,N'TEST_ONLY');
                """))
            {
                Add(insert, "@id", DbType.Guid, receipt.ModelId);
                Add(insert, "@contract", DbType.String, ContractVersion);
                Add(insert, "@schema", DbType.String, winner.Payload.SchemaVersion);
                Add(insert, "@winner", DbType.AnsiStringFixedLength, winner.ContentSha256, 64);
                Add(insert, "@fit", DbType.AnsiStringFixedLength, winner.Payload.FitFingerprintSha256, 64);
                Add(insert, "@projection", DbType.AnsiStringFixedLength, winner.Payload.ProjectionFingerprintSha256, 64);
                Add(insert, "@cells", DbType.Int32, receipt.JointCellCount);
                Add(insert, "@tf", DbType.Int32, receipt.TfSlotCount);
                Add(insert, "@passes", DbType.Int32, receipt.BlockingPassCount);
                Add(insert, "@payload", DbType.Binary, bytes, bytes.Length);
                if (await insert.ExecuteNonQueryAsync(ct) != 1)
                    throw new InvalidDataException("Materialização do vencedor operacional não inseriu uma linha.");
            }
            var loaded = await ReadPinnedCoreAsync(connection, transaction,
                receipt.ModelId, receipt.SourceSnapshotSha256, ct);
            if (loaded.ContentSha256 != winner.ContentSha256
                || !loaded.SerializeUtf8().AsSpan().SequenceEqual(bytes))
                throw new InvalidDataException("Round-trip operacional SQL alterou o modelo vencedor.");
            await transaction.CommitAsync(ct);
            return receipt;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// The Runner's production-side V9 operational model loader. Explicit ID+SHA;
    /// there is NO fallback to the active legacy model or the calibration file.
    /// </summary>
    public static async Task<V9FittedSnapshot> ReadPinnedAsync(DbConnection connection,
        Guid modelId, string expectedWinnerSha256, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("SQL operacional V9 deve estar aberto.");
        return await ReadPinnedCoreAsync(connection, null, modelId, expectedWinnerSha256, ct);
    }

    private static async Task<V9FittedSnapshot> ReadPinnedCoreAsync(DbConnection connection,
        DbTransaction? transaction, Guid modelId, string expectedWinnerSha256, CancellationToken ct)
    {
        if (modelId != ModelIdFromWinnerSha(expectedWinnerSha256))
            throw new InvalidDataException("UUID operacional não pertence ao vencedor SHA fixado.");
        await using var command = Command(connection, transaction, """
            SELECT contrato,snapshot_schema,winner_snapshot_sha256,
                   fit_fingerprint_sha256,projection_fingerprint_sha256,
                   joint_cell_count,tf_slot_count,blocking_pass_count,payload_fitted,status
            FROM identidade.modelo_linkage_v9_operacional
            WHERE modelo_id=@id;
            """);
        Add(command, "@id", DbType.Guid, modelId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidDataException("Modelo V9 operacional não encontrado; não usar modelo legado.");
        if (reader.GetString(0) != ContractVersion
            || reader.GetString(1) != V9FittedSnapshot.SupportSchemaVersion
            || reader.GetString(2) != expectedWinnerSha256
            || reader.GetString(9) != "TEST_ONLY")
            throw new InvalidDataException("Modelo SQL V9 não corresponde ao contrato/vencedor/estado de TEST.");
        var fitSha = reader.GetString(3);
        var projectionSha = reader.GetString(4);
        var cells = reader.GetInt32(5);
        var tfSlots = reader.GetInt32(6);
        var passes = reader.GetInt32(7);
        if (cells != 81 || tfSlots != 4 || passes < 1)
            throw new InvalidDataException("Contagens operacionais V9 perderam dimensões do modelo.");
        var buffer = reader.GetFieldValue<byte[]>(8);
        if (buffer.Length <= 0 || buffer.Length > V9FittedSnapshot.MaximumContentBytes)
            throw new InvalidDataException("Payload operacional V9 excede limite.");
        if (await reader.ReadAsync(ct))
            throw new InvalidDataException("Um modelo SHA encontrou múltiplas linhas operacionais.");
        var winner = V9FittedSnapshot.Load(buffer, expectedWinnerSha256);
        var receipt = VerifyConversion(winner);
        if (receipt.FitFingerprintSha256 != fitSha
            || receipt.ProjectionFingerprintSha256 != projectionSha
            || receipt.JointCellCount != cells
            || receipt.TfSlotCount != tfSlots
            || receipt.BlockingPassCount != passes)
            throw new InvalidDataException("Metadados do modelo operacional divergem de seu fit.");
        return winner;
    }

    private static DbCommand Command(DbConnection connection, DbTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.CommandTimeout = 300;
        return command;
    }

    private static void Add(DbCommand command, string name, DbType type, object value, int? size = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        if (size.HasValue) parameter.Size = size.Value;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

using System.Data;
using System.Data.Common;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Processor.Worker;

/// <summary>
/// Reconstrói, dentro da transação do Processor, as chaves de blocking de uma Pessoa.
/// Nascimento vem da Gold corrente; nomes/nomes da mãe são aliases obtidos do histórico Silver.
/// A operação é idempotente para a versão de normalização atual.
/// </summary>
internal static class BlockingProjectionPersistence
{
    internal static async Task RefreshSqlServerAsync(
        SqlConnection connection,
        SqlTransaction tx,
        Guid pessoaUuid,
        CancellationToken ct)
    {
        var snapshot = await LoadSqlServerSnapshotAsync(connection, tx, pessoaUuid, ct);
        await ReplaceSqlServerAsync(connection, tx, pessoaUuid, snapshot, ct);
    }

    internal static async Task RefreshPostgreSqlAsync(
        DbConnection connection,
        DbTransaction tx,
        Guid pessoaUuid,
        CancellationToken ct)
    {
        var snapshot = await LoadPostgreSqlSnapshotAsync(connection, tx, pessoaUuid, ct);
        await ReplacePostgreSqlAsync(connection, tx, pessoaUuid, snapshot, ct);
    }

    private static async Task<BlockingProjectionSnapshot> LoadSqlServerSnapshotAsync(
        SqlConnection connection,
        SqlTransaction tx,
        Guid pessoaUuid,
        CancellationToken ct)
    {
        string? currentName;
        string? currentMother;
        DateOnly currentBirth;
        DateTimeOffset currentAsOf;

        await using (var current = connection.CreateCommand())
        {
            current.Transaction = tx;
            current.CommandText = "SELECT nome_completo,nome_mae,data_nascimento,atualizado_em FROM gold.pessoa WHERE pessoa_uuid=@uuid;";
            current.Parameters.AddWithValue("@uuid", pessoaUuid);
            await using var reader = await current.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return BlockingProjectionSnapshot.Empty;
            currentName = reader.GetString(0);
            currentMother = reader.GetString(1);
            currentBirth = DateOnly.FromDateTime(reader.GetDateTime(2));
            currentAsOf = reader.GetDateTimeOffset(3);
        }

        var observations = new List<BlockingNameObservation>();
        await using (var history = connection.CreateCommand())
        {
            history.Transaction = tx;
            history.CommandText = """
                SELECT po.nome_completo,po.nome_mae,po.source_as_of
                FROM silver.pessoa_observacao po
                JOIN identidade.v_vinculo_corrente vc
                  ON vc.pessoa_observacao_id=po.pessoa_observacao_id
                WHERE vc.pessoa_uuid=@uuid
                  AND vc.status='RESOLVIDO'
                ORDER BY po.source_as_of,po.pessoa_observacao_id;
                """;
            history.Parameters.AddWithValue("@uuid", pessoaUuid);
            await using var reader = await history.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                observations.Add(new BlockingNameObservation(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetDateTimeOffset(2)));
            }
        }

        return BuildSnapshot(currentName, currentMother, currentBirth, currentAsOf, observations);
    }

    private static async Task<BlockingProjectionSnapshot> LoadPostgreSqlSnapshotAsync(
        DbConnection connection,
        DbTransaction tx,
        Guid pessoaUuid,
        CancellationToken ct)
    {
        string? currentName;
        string? currentMother;
        DateOnly currentBirth;
        DateTimeOffset currentAsOf;

        await using (var current = connection.CreateCommand())
        {
            current.Transaction = tx;
            current.CommandText = "SELECT nome_completo,nome_mae,data_nascimento,atualizado_em FROM gold.pessoa WHERE pessoa_uuid=@uuid;";
            Add(current, "@uuid", DbType.Guid, pessoaUuid);
            await using var reader = await current.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return BlockingProjectionSnapshot.Empty;
            currentName = reader.GetString(0);
            currentMother = reader.GetString(1);
            currentBirth = reader.GetFieldValue<DateOnly>(2);
            currentAsOf = reader.GetFieldValue<DateTimeOffset>(3);
        }

        var observations = new List<BlockingNameObservation>();
        await using (var history = connection.CreateCommand())
        {
            history.Transaction = tx;
            history.CommandText = """
                SELECT po.nome_completo,po.nome_mae,po.source_as_of
                FROM silver.pessoa_observacao po
                JOIN identidade.v_vinculo_corrente vc
                  ON vc.pessoa_observacao_id=po.pessoa_observacao_id
                WHERE vc.pessoa_uuid=@uuid
                  AND vc.status='RESOLVIDO'
                ORDER BY po.source_as_of,po.pessoa_observacao_id;
                """;
            Add(history, "@uuid", DbType.Guid, pessoaUuid);
            await using var reader = await history.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                observations.Add(new BlockingNameObservation(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetFieldValue<DateTimeOffset>(2)));
            }
        }

        return BuildSnapshot(currentName, currentMother, currentBirth, currentAsOf, observations);
    }

    private static BlockingProjectionSnapshot BuildSnapshot(
        string? currentName,
        string? currentMother,
        DateOnly currentBirth,
        DateTimeOffset currentAsOf,
        IReadOnlyList<BlockingNameObservation> observations)
    {
        var currentKeys = BlockingProjectionKeyProjector.Project(currentName, currentMother, currentBirth);
        var currentAliases = currentKeys
            .Where(static key => BlockingFeatureTemporalCatalog.Get(key.Feature) == BlockingFeatureTemporalSemantics.VersionedAlias)
            .ToHashSet();

        var stable = currentKeys
            .Where(static key => BlockingFeatureTemporalCatalog.Get(key.Feature) == BlockingFeatureTemporalSemantics.StableIdentityDatum)
            .Select(key => new BlockingProjectionRow(
                key.Feature,
                key.Value,
                "STABLE_IDENTITY_DATUM",
                currentAsOf,
                null))
            .ToArray();

        var aliases = new Dictionary<BlockingProjectionKey, (DateTimeOffset First, DateTimeOffset Last)>();
        foreach (var observation in observations)
        {
            var keys = BlockingProjectionKeyProjector.Project(observation.Name, observation.MotherName, currentBirth);
            foreach (var key in keys.Where(static key =>
                         BlockingFeatureTemporalCatalog.Get(key.Feature) == BlockingFeatureTemporalSemantics.VersionedAlias))
            {
                if (aliases.TryGetValue(key, out var interval))
                    aliases[key] = (Min(interval.First, observation.SourceAsOf), Max(interval.Last, observation.SourceAsOf));
                else
                    aliases.Add(key, (observation.SourceAsOf, observation.SourceAsOf));
            }
        }

        foreach (var key in currentAliases)
        {
            if (!aliases.ContainsKey(key))
                aliases.Add(key, (currentAsOf, currentAsOf));
        }

        var aliasRows = aliases
            .Select(pair => new BlockingProjectionRow(
                pair.Key.Feature,
                pair.Key.Value,
                "VERSIONED_ALIAS",
                pair.Value.First,
                currentAliases.Contains(pair.Key) ? null : pair.Value.Last))
            .ToArray();

        return new BlockingProjectionSnapshot(stable.Concat(aliasRows).ToArray());
    }

    private static async Task ReplaceSqlServerAsync(
        SqlConnection connection,
        SqlTransaction tx,
        Guid pessoaUuid,
        BlockingProjectionSnapshot snapshot,
        CancellationToken ct)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM identidade.blocking_chave WHERE pessoa_uuid=@uuid AND normalizacao_versao=@normalizacao;";
            delete.Parameters.AddWithValue("@uuid", pessoaUuid);
            delete.Parameters.Add(new SqlParameter("@normalizacao", SqlDbType.NVarChar, 80)
                { Value = IdentityComparison.NormalizationVersion });
            await delete.ExecuteNonQueryAsync(ct);
        }

        foreach (var row in snapshot.Rows)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT identidade.blocking_chave(
                    pessoa_uuid,normalizacao_versao,atributo,valor_normalizado,semantica_temporal,vigencia_inicio,vigencia_fim)
                VALUES(@uuid,@normalizacao,@atributo,@valor,@semantica,@inicio,@fim);
                """;
            insert.Parameters.AddWithValue("@uuid", pessoaUuid);
            insert.Parameters.Add(new SqlParameter("@normalizacao", SqlDbType.NVarChar, 80)
                { Value = IdentityComparison.NormalizationVersion });
            insert.Parameters.Add(new SqlParameter("@atributo", SqlDbType.NVarChar, 80) { Value = row.Feature });
            insert.Parameters.Add(new SqlParameter("@valor", SqlDbType.NVarChar, 500) { Value = row.Value });
            insert.Parameters.Add(new SqlParameter("@semantica", SqlDbType.NVarChar, 30) { Value = row.TemporalSemantics });
            insert.Parameters.Add(new SqlParameter("@inicio", SqlDbType.DateTimeOffset) { Value = row.ValidFrom });
            insert.Parameters.Add(new SqlParameter("@fim", SqlDbType.DateTimeOffset) { Value = (object?)row.ValidTo ?? DBNull.Value });
            await insert.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task ReplacePostgreSqlAsync(
        DbConnection connection,
        DbTransaction tx,
        Guid pessoaUuid,
        BlockingProjectionSnapshot snapshot,
        CancellationToken ct)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM identidade.blocking_chave WHERE pessoa_uuid=@uuid AND normalizacao_versao=@normalizacao;";
            Add(delete, "@uuid", DbType.Guid, pessoaUuid);
            Add(delete, "@normalizacao", DbType.String, IdentityComparison.NormalizationVersion, 80);
            await delete.ExecuteNonQueryAsync(ct);
        }

        foreach (var row in snapshot.Rows)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO identidade.blocking_chave(
                    pessoa_uuid,normalizacao_versao,atributo,valor_normalizado,semantica_temporal,vigencia_inicio,vigencia_fim)
                VALUES(@uuid,@normalizacao,@atributo,@valor,@semantica,@inicio,@fim);
                """;
            Add(insert, "@uuid", DbType.Guid, pessoaUuid);
            Add(insert, "@normalizacao", DbType.String, IdentityComparison.NormalizationVersion, 80);
            Add(insert, "@atributo", DbType.String, row.Feature, 80);
            Add(insert, "@valor", DbType.String, row.Value, 500);
            Add(insert, "@semantica", DbType.String, row.TemporalSemantics, 30);
            Add(insert, "@inicio", DbType.DateTimeOffset, row.ValidFrom);
            Add(insert, "@fim", DbType.DateTimeOffset, row.ValidTo);
            await insert.ExecuteNonQueryAsync(ct);
        }
    }

    private static void Add(DbCommand command, string name, DbType type, object? value, int? size = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        if (size.HasValue) parameter.Size = size.Value;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;
    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left >= right ? left : right;

    private sealed record BlockingNameObservation(string? Name, string? MotherName, DateTimeOffset SourceAsOf);
    private sealed record BlockingProjectionRow(
        string Feature,
        string Value,
        string TemporalSemantics,
        DateTimeOffset ValidFrom,
        DateTimeOffset? ValidTo);
    private sealed record BlockingProjectionSnapshot(IReadOnlyList<BlockingProjectionRow> Rows)
    {
        internal static BlockingProjectionSnapshot Empty { get; } = new(Array.Empty<BlockingProjectionRow>());
    }
}

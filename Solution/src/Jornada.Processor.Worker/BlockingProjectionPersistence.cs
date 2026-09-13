using System.Data;
using System.Data.Common;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Processor.Worker;

/// <summary>
/// Reconstrói, dentro da transação do Processor, as chaves de blocking de uma Pessoa.
/// Nascimento vem da Gold corrente; nomes/nomes da mãe são aliases obtidos do histórico Silver;
/// atributos transversais elegíveis usam a Gold versionada mais o vencedor COMPROVADO da Silver
/// ainda não promovido. A operação é idempotente e nunca infere elegibilidade pelo nome do atributo.
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
            currentMother = reader.IsDBNull(1) ? null : reader.GetString(1);
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

        var dynamic = new List<BlockingDynamicAttributeObservation>();
        var eligible = PersonResolutionContractCatalog.EligibleTransversal
            .Select(static field => field.Code)
            .OrderBy(static code => code, StringComparer.Ordinal)
            .ToArray();
        if (eligible.Length > 0)
        {
            var parameterNames = eligible.Select((_, index) => $"@eligible_attr_{index}").ToArray();
            await using (var attributes = connection.CreateCommand())
            {
                attributes.Transaction = tx;
                attributes.CommandText = $"""
                    SELECT atributo_codigo,valor,vigencia_inicio,vigencia_fim
                    FROM gold.pessoa_atributo
                    WHERE pessoa_uuid=@uuid
                      AND atributo_codigo IN ({string.Join(",", parameterNames)})
                    ORDER BY atributo_codigo,atributo_instancia_chave,vigencia_inicio,pessoa_atributo_id;
                    """;
                attributes.Parameters.AddWithValue("@uuid", pessoaUuid);
                for (var index = 0; index < eligible.Length; index++)
                    attributes.Parameters.Add(new SqlParameter(parameterNames[index], SqlDbType.NVarChar, 80) { Value = eligible[index] });

                await using var reader = await attributes.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    dynamic.Add(new BlockingDynamicAttributeObservation(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetDateTimeOffset(2),
                        reader.IsDBNull(3) ? null : reader.GetDateTimeOffset(3)));
                }
            }

            // RefreshGoldPersonAsync ocorre antes de PromoteAttributeAsync no caminho transacional.
            // O vencedor COMPROVADO da Silver é incluído como corrente para que blocking_chave já
            // reflita o mesmo candidato que será promovido à Gold alguns passos depois.
            await using var pending = connection.CreateCommand();
            pending.Transaction = tx;
            pending.CommandText = $"""
                ;WITH candidatos AS(
                    SELECT pa.atributo_codigo,pa.valor,
                           COALESCE(pa.referencia_evidencia,pa.verificado_em) AS precedencia,
                           ROW_NUMBER() OVER(
                               PARTITION BY pa.atributo_codigo,pa.atributo_instancia_chave
                               ORDER BY COALESCE(pa.referencia_evidencia,pa.verificado_em) DESC,
                                        pa.verificado_em DESC,pa.pessoa_atributo_observacao_id DESC) rn
                    FROM silver.pessoa_atributo_observacao pa
                    JOIN identidade.v_vinculo_corrente vc
                      ON vc.pessoa_observacao_id=pa.pessoa_observacao_id
                    WHERE vc.pessoa_uuid=@uuid
                      AND vc.status='RESOLVIDO'
                      AND pa.status_evidencia='COMPROVADO'
                      AND pa.atributo_codigo IN ({string.Join(",", parameterNames)})
                )
                SELECT atributo_codigo,valor,precedencia
                FROM candidatos
                WHERE rn=1
                ORDER BY atributo_codigo,valor;
                """;
            pending.Parameters.AddWithValue("@uuid", pessoaUuid);
            for (var index = 0; index < eligible.Length; index++)
                pending.Parameters.Add(new SqlParameter(parameterNames[index], SqlDbType.NVarChar, 80) { Value = eligible[index] });
            await using (var reader = await pending.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    dynamic.Add(new BlockingDynamicAttributeObservation(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetDateTimeOffset(2),
                        null));
                }
            }
        }

        return BuildSnapshot(currentName, currentMother, currentBirth, currentAsOf, observations, dynamic);
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
            currentMother = reader.IsDBNull(1) ? null : reader.GetString(1);
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

        var dynamic = new List<BlockingDynamicAttributeObservation>();
        var eligible = PersonResolutionContractCatalog.EligibleTransversal
            .Select(static field => field.Code)
            .OrderBy(static code => code, StringComparer.Ordinal)
            .ToArray();
        if (eligible.Length > 0)
        {
            var parameterNames = eligible.Select((_, index) => $"@eligible_attr_{index}").ToArray();
            await using (var attributes = connection.CreateCommand())
            {
                attributes.Transaction = tx;
                attributes.CommandText = $"""
                    SELECT atributo_codigo,valor,vigencia_inicio,vigencia_fim
                    FROM gold.pessoa_atributo
                    WHERE pessoa_uuid=@uuid
                      AND atributo_codigo IN ({string.Join(",", parameterNames)})
                    ORDER BY atributo_codigo,atributo_instancia_chave,vigencia_inicio,pessoa_atributo_id;
                    """;
                Add(attributes, "@uuid", DbType.Guid, pessoaUuid);
                for (var index = 0; index < eligible.Length; index++)
                    Add(attributes, parameterNames[index], DbType.String, eligible[index], 80);

                await using var reader = await attributes.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    dynamic.Add(new BlockingDynamicAttributeObservation(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetFieldValue<DateTimeOffset>(2),
                        reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3)));
                }
            }

            await using var pending = connection.CreateCommand();
            pending.Transaction = tx;
            pending.CommandText = $"""
                WITH candidatos AS(
                    SELECT pa.atributo_codigo,pa.valor,
                           COALESCE(pa.referencia_evidencia,pa.verificado_em) AS precedencia,
                           ROW_NUMBER() OVER(
                               PARTITION BY pa.atributo_codigo,pa.atributo_instancia_chave
                               ORDER BY COALESCE(pa.referencia_evidencia,pa.verificado_em) DESC,
                                        pa.verificado_em DESC,pa.pessoa_atributo_observacao_id DESC) rn
                    FROM silver.pessoa_atributo_observacao pa
                    JOIN identidade.v_vinculo_corrente vc
                      ON vc.pessoa_observacao_id=pa.pessoa_observacao_id
                    WHERE vc.pessoa_uuid=@uuid
                      AND vc.status='RESOLVIDO'
                      AND pa.status_evidencia='COMPROVADO'
                      AND pa.atributo_codigo IN ({string.Join(",", parameterNames)})
                )
                SELECT atributo_codigo,valor,precedencia
                FROM candidatos
                WHERE rn=1
                ORDER BY atributo_codigo,valor;
                """;
            Add(pending, "@uuid", DbType.Guid, pessoaUuid);
            for (var index = 0; index < eligible.Length; index++)
                Add(pending, parameterNames[index], DbType.String, eligible[index], 80);
            await using (var reader = await pending.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    dynamic.Add(new BlockingDynamicAttributeObservation(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetFieldValue<DateTimeOffset>(2),
                        null));
                }
            }
        }

        return BuildSnapshot(currentName, currentMother, currentBirth, currentAsOf, observations, dynamic);
    }

    private static BlockingProjectionSnapshot BuildSnapshot(
        string? currentName,
        string? currentMother,
        DateOnly currentBirth,
        DateTimeOffset currentAsOf,
        IReadOnlyList<BlockingNameObservation> observations,
        IReadOnlyList<BlockingDynamicAttributeObservation> dynamicObservations)
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
            .ToList();

        var aliases = new Dictionary<BlockingProjectionKey, (DateTimeOffset First, DateTimeOffset Last, bool Current)>();
        foreach (var observation in observations)
        {
            var keys = BlockingProjectionKeyProjector.Project(observation.Name, observation.MotherName, currentBirth);
            foreach (var key in keys.Where(static key =>
                         BlockingFeatureTemporalCatalog.Get(key.Feature) == BlockingFeatureTemporalSemantics.VersionedAlias))
            {
                MergeAlias(aliases, key, observation.SourceAsOf, observation.SourceAsOf, Current: false);
            }
        }

        foreach (var key in currentAliases)
            MergeAlias(aliases, key, currentAsOf, currentAsOf, Current: true);

        foreach (var observation in dynamicObservations)
        {
            var keys = PersonResolutionBlockingProjector.Project(
                new[] { new IdentityResolutionAttributeValue(observation.AttributeCode, observation.Value) });
            foreach (var key in keys)
            {
                var semantics = BlockingFeatureTemporalCatalog.Get(key.Feature);
                if (semantics == BlockingFeatureTemporalSemantics.VersionedAlias)
                {
                    MergeAlias(
                        aliases,
                        key,
                        observation.ValidFrom,
                        observation.ValidTo ?? observation.ValidFrom,
                        Current: observation.ValidTo is null);
                }
                else if (observation.ValidTo is null)
                {
                    stable.Add(new BlockingProjectionRow(
                        key.Feature,
                        key.Value,
                        "STABLE_IDENTITY_DATUM",
                        observation.ValidFrom,
                        null));
                }
            }
        }

        var aliasRows = aliases
            .Select(pair => new BlockingProjectionRow(
                pair.Key.Feature,
                pair.Key.Value,
                "VERSIONED_ALIAS",
                pair.Value.First,
                pair.Value.Current ? null : pair.Value.Last))
            .ToArray();

        var rows = stable
            .Concat(aliasRows)
            .Distinct()
            .OrderBy(static row => row.Feature, StringComparer.Ordinal)
            .ThenBy(static row => row.Value, StringComparer.Ordinal)
            .ThenBy(static row => row.ValidFrom)
            .ToArray();
        return new BlockingProjectionSnapshot(rows);
    }

    private static void MergeAlias(
        IDictionary<BlockingProjectionKey, (DateTimeOffset First, DateTimeOffset Last, bool Current)> aliases,
        BlockingProjectionKey key,
        DateTimeOffset first,
        DateTimeOffset last,
        bool Current)
    {
        if (aliases.TryGetValue(key, out var interval))
        {
            aliases[key] = (
                Min(interval.First, first),
                Max(interval.Last, last),
                interval.Current || Current);
        }
        else
        {
            aliases.Add(key, (first, last, Current));
        }
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
    private sealed record BlockingDynamicAttributeObservation(
        string AttributeCode,
        string Value,
        DateTimeOffset ValidFrom,
        DateTimeOffset? ValidTo);
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

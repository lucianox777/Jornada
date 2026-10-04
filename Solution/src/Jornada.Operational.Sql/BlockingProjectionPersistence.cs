using System.Data;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Operational.Sql;

/// <summary>
/// Reconstrói, dentro da transação do Processor, as chaves de blocking de uma Pessoa.
/// Nascimento vem da Gold corrente; nomes/nomes da mãe são aliases obtidos do histórico Silver;
/// atributos transversais elegíveis usam a Gold versionada mais o vencedor COMPROVADO da Silver
/// ainda não promovido. A operação é idempotente e nunca infere elegibilidade pelo nome do atributo.
/// </summary>
public static class BlockingProjectionPersistence
{
    public static async Task RefreshSqlServerAsync(
        SqlConnection connection,
        SqlTransaction tx,
        Guid pessoaUuid,
        CancellationToken ct)
    {
        var snapshot = await LoadSqlServerSnapshotAsync(connection, tx, pessoaUuid, ct);
        await ReplaceSqlServerAsync(connection, tx, pessoaUuid, snapshot, ct);
    }

    /// <summary>
    /// Reconstrói blocking para várias referências com leituras set-based e uma única
    /// substituição em lote. Mantém a mesma projeção de RefreshSqlServerAsync, mas
    /// elimina o padrão N+1 durante a publicação de um linkage.
    /// </summary>
    public static async Task RefreshSqlServerBatchAsync(
        SqlConnection connection,
        SqlTransaction tx,
        IReadOnlyCollection<Guid> pessoaUuids,
        CancellationToken ct)
    {
        var ids = pessoaUuids.Distinct().OrderBy(static x => x).ToArray();
        if (ids.Length == 0)
            return;

        await using (var create = connection.CreateCommand())
        {
            create.Transaction = tx;
            create.CommandText = """
                CREATE TABLE #jornada_blocking_refresh_uuid(
                    pessoa_uuid UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
                """;
            await create.ExecuteNonQueryAsync(ct);
        }

        var uuidTable = new DataTable();
        uuidTable.Columns.Add("pessoa_uuid", typeof(Guid));
        foreach (var id in ids)
            uuidTable.Rows.Add(id);

        using (var bulkIds = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, tx))
        {
            bulkIds.DestinationTableName = "#jornada_blocking_refresh_uuid";
            bulkIds.BatchSize = Math.Min(ids.Length, 5000);
            bulkIds.BulkCopyTimeout = 900;
            bulkIds.ColumnMappings.Add("pessoa_uuid", "pessoa_uuid");
            await bulkIds.WriteToServerAsync(uuidTable, ct);
        }

        var current = new Dictionary<Guid, BlockingCurrentPerson>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = """
                SELECT u.pessoa_uuid,g.nome_completo,g.nome_mae,g.data_nascimento,
                       g.atualizado_em,g.estado_identidade
                FROM #jornada_blocking_refresh_uuid u
                LEFT JOIN gold.pessoa g ON g.pessoa_uuid=u.pessoa_uuid
                ORDER BY u.pessoa_uuid;
                """;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var uuid = reader.GetGuid(0);
                if (reader.IsDBNull(5) || !string.Equals(reader.GetString(5), "REFERENCIA", StringComparison.Ordinal))
                    continue;
                current[uuid] = new BlockingCurrentPerson(
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : DateOnly.FromDateTime(reader.GetDateTime(3)),
                    reader.GetDateTimeOffset(4));
            }
        }

        var observations = ids.ToDictionary(
            static id => id,
            static _ => new List<BlockingNameObservation>());
        await using (var history = connection.CreateCommand())
        {
            history.Transaction = tx;
            history.CommandText = """
                SELECT vc.pessoa_uuid,po.nome_completo,po.nome_mae,po.source_as_of
                FROM #jornada_blocking_refresh_uuid u
                JOIN identidade.v_vinculo_corrente vc
                  ON vc.pessoa_uuid=u.pessoa_uuid AND vc.status='RESOLVIDO'
                JOIN silver.pessoa_observacao po
                  ON po.pessoa_observacao_id=vc.pessoa_observacao_id
                ORDER BY vc.pessoa_uuid,po.source_as_of,po.pessoa_observacao_id;
                """;
            await using var reader = await history.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                observations[reader.GetGuid(0)].Add(new BlockingNameObservation(
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetDateTimeOffset(3)));
            }
        }

        var dynamic = ids.ToDictionary(
            static id => id,
            static _ => new List<BlockingDynamicAttributeObservation>());
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
                    SELECT pa.pessoa_uuid,pa.atributo_codigo,pa.valor,pa.vigencia_inicio,pa.vigencia_fim
                    FROM #jornada_blocking_refresh_uuid u
                    JOIN gold.pessoa_atributo pa ON pa.pessoa_uuid=u.pessoa_uuid
                    WHERE pa.atributo_codigo IN ({string.Join(",", parameterNames)})
                    ORDER BY pa.pessoa_uuid,pa.atributo_codigo,pa.atributo_instancia_chave,
                             pa.vigencia_inicio,pa.pessoa_atributo_id;
                    """;
                for (var index = 0; index < eligible.Length; index++)
                    attributes.Parameters.Add(new SqlParameter(parameterNames[index], SqlDbType.NVarChar, 80) { Value = eligible[index] });

                await using var reader = await attributes.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    dynamic[reader.GetGuid(0)].Add(new BlockingDynamicAttributeObservation(
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetDateTimeOffset(3),
                        reader.IsDBNull(4) ? null : reader.GetDateTimeOffset(4)));
                }
            }

            await using (var pending = connection.CreateCommand())
            {
                pending.Transaction = tx;
                pending.CommandText = $"""
                    ;WITH candidatos AS(
                        SELECT vc.pessoa_uuid,pa.atributo_codigo,pa.valor,
                               COALESCE(pa.referencia_evidencia,pa.verificado_em) AS precedencia,
                               ROW_NUMBER() OVER(
                                   PARTITION BY vc.pessoa_uuid,pa.atributo_codigo,pa.atributo_instancia_chave
                                   ORDER BY COALESCE(pa.referencia_evidencia,pa.verificado_em) DESC,
                                            pa.verificado_em DESC,pa.pessoa_atributo_observacao_id DESC) rn
                        FROM #jornada_blocking_refresh_uuid u
                        JOIN identidade.v_vinculo_corrente vc
                          ON vc.pessoa_uuid=u.pessoa_uuid AND vc.status='RESOLVIDO'
                        JOIN silver.pessoa_atributo_observacao pa
                          ON pa.pessoa_observacao_id=vc.pessoa_observacao_id
                        WHERE pa.status_evidencia='COMPROVADO'
                          AND pa.atributo_codigo IN ({string.Join(",", parameterNames)})
                    )
                    SELECT pessoa_uuid,atributo_codigo,valor,precedencia
                    FROM candidatos
                    WHERE rn=1
                    ORDER BY pessoa_uuid,atributo_codigo,valor;
                    """;
                for (var index = 0; index < eligible.Length; index++)
                    pending.Parameters.Add(new SqlParameter(parameterNames[index], SqlDbType.NVarChar, 80) { Value = eligible[index] });

                await using var reader = await pending.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    dynamic[reader.GetGuid(0)].Add(new BlockingDynamicAttributeObservation(
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetDateTimeOffset(3),
                        null));
                }
            }
        }

        var rows = new DataTable();
        rows.Columns.Add("pessoa_uuid", typeof(Guid));
        rows.Columns.Add("normalizacao_versao", typeof(string));
        rows.Columns.Add("atributo", typeof(string));
        rows.Columns.Add("valor_normalizado", typeof(string));
        rows.Columns.Add("semantica_temporal", typeof(string));
        rows.Columns.Add("vigencia_inicio", typeof(DateTimeOffset));
        rows.Columns.Add("vigencia_fim", typeof(DateTimeOffset));

        foreach (var id in ids)
        {
            if (!current.TryGetValue(id, out var person))
                continue;
            var snapshot = BuildSnapshot(
                person.Name,
                person.MotherName,
                person.BirthDate,
                person.AsOf,
                observations[id],
                dynamic[id]);
            foreach (var row in snapshot.Rows)
            {
                rows.Rows.Add(
                    id,
                    IdentityComparison.NormalizationVersion,
                    row.Feature,
                    row.Value,
                    row.TemporalSemantics,
                    row.ValidFrom,
                    (object?)row.ValidTo ?? DBNull.Value);
            }
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = """
                DELETE bc
                FROM identidade.blocking_chave bc
                JOIN #jornada_blocking_refresh_uuid u ON u.pessoa_uuid=bc.pessoa_uuid
                WHERE bc.normalizacao_versao=@normalizacao;
                """;
            delete.Parameters.Add(new SqlParameter("@normalizacao", SqlDbType.NVarChar, 80)
                { Value = IdentityComparison.NormalizationVersion });
            await delete.ExecuteNonQueryAsync(ct);
        }

        if (rows.Rows.Count > 0)
        {
            using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, tx)
            {
                DestinationTableName = "identidade.blocking_chave",
                BatchSize = Math.Min(rows.Rows.Count, 5000),
                BulkCopyTimeout = 900
            };
            foreach (DataColumn column in rows.Columns)
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            await bulk.WriteToServerAsync(rows, ct);
        }

        await using (var drop = connection.CreateCommand())
        {
            drop.Transaction = tx;
            drop.CommandText = "DROP TABLE #jornada_blocking_refresh_uuid;";
            await drop.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task<BlockingProjectionSnapshot> LoadSqlServerSnapshotAsync(
        SqlConnection connection,
        SqlTransaction tx,
        Guid pessoaUuid,
        CancellationToken ct)
    {
        string? currentName;
        string? currentMother;
        DateOnly? currentBirth;
        DateTimeOffset currentAsOf;

        await using (var current = connection.CreateCommand())
        {
            current.Transaction = tx;
            current.CommandText = "SELECT nome_completo,nome_mae,data_nascimento,atualizado_em,estado_identidade FROM gold.pessoa WHERE pessoa_uuid=@uuid;";
            current.Parameters.AddWithValue("@uuid", pessoaUuid);
            await using var reader = await current.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return BlockingProjectionSnapshot.Empty;
            if (!string.Equals(reader.GetString(4), "REFERENCIA", StringComparison.Ordinal))
                return BlockingProjectionSnapshot.Empty;
            currentName = reader.IsDBNull(0) ? null : reader.GetString(0);
            currentMother = reader.IsDBNull(1) ? null : reader.GetString(1);
            currentBirth = reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2));
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

    private static BlockingProjectionSnapshot BuildSnapshot(
        string? currentName,
        string? currentMother,
        DateOnly? currentBirth,
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

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;
    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left >= right ? left : right;

    private sealed record BlockingCurrentPerson(
        string? Name,
        string? MotherName,
        DateOnly? BirthDate,
        DateTimeOffset AsOf);
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

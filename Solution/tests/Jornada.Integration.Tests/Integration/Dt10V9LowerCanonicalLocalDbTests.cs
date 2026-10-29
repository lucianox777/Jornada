using System.Data;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

/// <summary>
/// Small disposable-only SQL proof. No external Gold, REF active, or historic TEST.
/// Set JORNADA_DT10_V9_HARNESS_CONNECTION only for a newly created LocalDB
/// JornadaHarness bearing the explicit V9 Gold development marker.
/// </summary>
[TestFixture, Category("Integration"), Category("DT10V9"), NonParallelizable]
public sealed class Dt10V9LowerCanonicalLocalDbTests
{
    [Test]
    public async Task Disabled_policy_rejects_lower_and_experimental_reasons_without_ledger_then_legacy_replays()
    {
        var cs = Environment.GetEnvironmentVariable("JORNADA_DT10_V9_HARNESS_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs))
            Assert.Ignore("Requires an explicitly provisioned, disposable LocalDB JornadaHarness.");
        var builder = new SqlConnectionStringBuilder(cs!);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(builder.InitialCatalog, Is.EqualTo("JornadaHarness").IgnoreCase);
            Assert.That(builder.DataSource, Does.StartWith("(localdb)\\").IgnoreCase);
        }));

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();
        await using (var guard = connection.CreateCommand())
        {
            guard.CommandText = """
                SELECT CASE WHEN DB_NAME()=N'JornadaHarness'
                  AND COALESCE(TRY_CONVERT(INT,SERVERPROPERTY(N'IsLocalDB')),0)=1
                  AND EXISTS(SELECT 1 FROM sys.extended_properties
                    WHERE class=0 AND name=N'Jornada.EnvironmentProfile'
                      AND CONVERT(NVARCHAR(200),value)=N'Development')
                  AND EXISTS(SELECT 1 FROM sys.extended_properties
                    WHERE class=0 AND name=N'Jornada.V9.ExperimentalHarness'
                      AND CONVERT(NVARCHAR(200),value)=N'PINNED_FIT_PREVIEW_NO_ACTIVATION')
                  AND OBJECT_ID(N'auditoria.v9_dt10_lower_policy_autorizacao',N'U') IS NOT NULL
                  AND OBJECT_ID(N'auditoria.tr_v9_dt10_lower_policy_autorizacao_append_only',N'TR') IS NOT NULL
                  AND OBJECT_ID(N'identidade.v9_dt10_lower_policy',N'U') IS NOT NULL
                  AND OBJECT_ID(N'identidade.sp_publicar_resolucao_progressiva_linkage_lote',N'P') IS NOT NULL
                  THEN 1 ELSE 0 END;
                """;
            Assert.That(Convert.ToInt32(await guard.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(1),
                "The test must NEVER initialize or modify an unowned database.");
        }

        // The migration must never pre-authorize a policy. A model ACTIVATE
        // record alone cannot authorize lower-band publication.
        await using (var noSeed = connection.CreateCommand())
        {
            noSeed.CommandText = "SELECT COUNT_BIG(*) FROM auditoria.v9_dt10_lower_policy_autorizacao;";
            Assert.That(Convert.ToInt64(await noSeed.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture),
                Is.Zero, "Disposable harness must start with no DT-15 policy authorizations.");
        }

        // Physical negative: a nonexistent policy/approval cannot create a
        // digest attestation. This is NOT the still-missing positive DT-15 test.
        await using (var missingPolicy = connection.CreateCommand())
        {
            missingPolicy.CommandText = """
                EXEC auditoria.sp_autorizar_v9_dt10_lower_policy
                    @politica_versao=N'V9_DT10_LOWER_NONEXISTENT_CI',
                    @aprovacao_id=@approval;
                """;
            missingPolicy.Parameters.AddWithValue("@approval", Guid.NewGuid());
            var denied = Assert.ThrowsAsync<SqlException>((Func<Task>)(async () =>
            {
                await missingPolicy.ExecuteNonQueryAsync();
            }));
            Assert.That(denied!.Number, Is.EqualTo(51836));
        }
        await using (var afterDenied = connection.CreateCommand())
        {
            afterDenied.CommandText =
                "SELECT COUNT_BIG(*) FROM auditoria.v9_dt10_lower_policy_autorizacao;";
            Assert.That(Convert.ToInt64(await afterDenied.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture), Is.Zero);
        }

        // Exercise the real SQL trigger on a disposable harness. A statement
        // affecting zero rows must still be rejected: nobody may rewrite or
        // erase a previously sealed DT-15 authorization.
        foreach (var statement in new[]
        {
            "UPDATE auditoria.v9_dt10_lower_policy_autorizacao SET politica_versao=politica_versao WHERE 1=0;",
            "DELETE FROM auditoria.v9_dt10_lower_policy_autorizacao WHERE 1=0;"
        })
        {
            await using var mutation = connection.CreateCommand();
            mutation.CommandText = statement;
            var exception = Assert.ThrowsAsync<SqlException>((Func<Task>)(async () => { await mutation.ExecuteNonQueryAsync(); }));
            Assert.That(exception!.Number, Is.EqualTo(51834), "DT-15 authorizations must be append-only.");
        }

        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
        await using (var scaleCheck = connection.CreateCommand())
        {
            scaleCheck.CommandText =
                "SELECT COUNT_BIG(*) FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SCALE-%';";
            if (Convert.ToInt64(await scaleCheck.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) == 0)
                await SqlBatchRunner.ExecuteFileWithSqlCmdVariablesAsync(
                    connection, Path.Combine(databaseDir, "Jornada_Dev_SyntheticScale.sql"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["SCALE_PEOPLE"] = "1000", ["SCALE_PAIRED"] = "2",
                        ["SCALE_PENDING"] = "1000", ["SCALE_SEED"] = "991",
                        ["SCALE_COLLISION_MODULO"] = "37",
                        ["SCALE_BIRTH_SHIFT_MODULO"] = "29"
                    });
        }

        long sourceId;
        long observationId;
        await using (var find = connection.CreateCommand())
        {
            find.CommandText = """
                SELECT TOP(1) o.pessoa_origem_id,po.pessoa_observacao_id
                FROM silver.pessoa_origem o
                JOIN silver.pessoa_observacao po ON po.pessoa_origem_id=o.pessoa_origem_id
                LEFT JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=o.pessoa_origem_id
                WHERE o.codigo_pessoa_origem LIKE N'SCALE-%'
                  AND po.cpf IS NULL AND (p.estado=N'PROVISORIA' OR p.pessoa_origem_id IS NULL)
                ORDER BY po.pessoa_observacao_id DESC;
                """;
            await using var reader = await find.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            sourceId = reader.GetInt64(0);
            observationId = reader.GetInt64(1);
        }

        await using (var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await using var ensure = connection.CreateCommand();
            ensure.Transaction = tx;
            ensure.CommandText = "EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@source;";
            ensure.Parameters.AddWithValue("@source", sourceId);
            await ensure.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }

        Guid initial;
        Guid model;
        int version;
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = """
                SELECT TOP(1) p.initial_uuid,m.modelo_id,m.versao
                FROM identidade.pessoa_origem_progressiva p
                CROSS JOIN identidade.modelo_linkage m
                WHERE p.pessoa_origem_id=@source
                ORDER BY CASE WHEN m.status=N'ATIVO' THEN 0 ELSE 1 END,m.versao DESC;
                """;
            read.Parameters.AddWithValue("@source", sourceId);
            await using var reader = await read.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            initial = reader.GetGuid(0);
            model = reader.GetGuid(1);
            version = reader.GetInt32(2);
        }

        // No policy row is seeded by the migration. Even a manually supplied
        // reason cannot promote an initial UUID while the feature is off.
        foreach (var (reason, expected) in new[] {
            ("LIMIAR_INFERIOR_V9_GOVERNADO", 51830),
            ("LIMIAR_INFERIOR_GOLD_TRAIN_EXPERIMENTAL", 51810)
        })
        {
            var run = Guid.NewGuid();
            await SeedRunAsync(connection, run, model, version, sourceId, observationId,
                initial, reason);
            var error = await TryPublishAsync(connection, run);
            Assert.That(error, Is.EqualTo(expected));
            Assert.That(await CountEventsAsync(connection, run), Is.Zero);
            // Rejection must not partially commit a run or promote its identity.
            Assert.That(await ReadProgressiveStateAsync(connection, sourceId),
                Is.EqualTo(("PROVISORIA", (Guid?)null)));
            // The nullable progressive canonical reference is NOT a nullable
            // Gold person identifier. The immutable initial UUID remains the
            // source's identity and every materialized Gold person has a UUID.
            await AssertGoldIdentityInvariantAsync(connection, sourceId, initial);
            Assert.That(await ReadRunStatusAsync(connection, run),
                Is.EqualTo("EXECUTANDO"));
            Assert.That(await TryPublishAsync(connection, run), Is.EqualTo(expected));
            Assert.That(await CountEventsAsync(connection, run), Is.Zero);
        }

        // Regression for forged SEM_CANDIDATO: a persisted best/second
        // candidate must never bypass the guarded lower-band publication.
        // Only the disposable LocalDB created for this test is mutated.
        Guid existingCandidate;
        Guid distinctSecondCandidate;
        await using (var candidateQuery = connection.CreateCommand())
        {
            candidateQuery.CommandText =
                "SELECT TOP(2) pessoa_uuid FROM identidade.pessoa ORDER BY pessoa_uuid;";
            await using var candidates = await candidateQuery.ExecuteReaderAsync();
            Assert.That(await candidates.ReadAsync(), Is.True,
                "Synthetic disposable fixture must have a referenced identity.");
            existingCandidate = candidates.GetGuid(0);
            Assert.That(await candidates.ReadAsync(), Is.True,
                "A second distinct synthetic identity is required by the SQL candidate-order constraint.");
            distinctSecondCandidate = candidates.GetGuid(0);
            Assert.That(distinctSecondCandidate, Is.Not.EqualTo(existingCandidate));
        }
        foreach (var candidateColumn in new[]
        {
            "melhor_candidato_uuid",
            "segundo_candidato_uuid"
        })
        {
            var forged = Guid.NewGuid();
            // Raw linkage evidence is immutable (SQL trigger 51822). Seed the
            // forged candidate as part of the initial INSERT, never UPDATE it.
            await SeedRunAsync(connection, forged, model, version, sourceId,
                observationId, initial, "SEM_CANDIDATO_NO_RULESET_BLOCKING",
                // SQL requires a second-ranked candidate to have a distinct best-ranked candidate.
                // Test best-only and best-plus-second; never fabricate an invalid SQL row.
                bestCandidate: existingCandidate,
                secondCandidate: candidateColumn == "segundo_candidato_uuid" ? distinctSecondCandidate : null);
            Assert.That(await TryPublishAsync(connection, forged), Is.EqualTo(51835),
                "Forged absence with a persisted candidate must abort publication.");
            Assert.That(await CountEventsAsync(connection, forged), Is.Zero,
                "A rejected fake no-candidate result must write no ledger events.");
            Assert.That(await ReadProgressiveStateAsync(connection, sourceId),
                Is.EqualTo(("PROVISORIA", (Guid?)null)));
            Assert.That(await ReadRunStatusAsync(connection, forged),
                Is.EqualTo("EXECUTANDO"));
            Assert.That(await TryPublishAsync(connection, forged), Is.EqualTo(51835),
                "Retry of forged absence must also fail without side effects.");
            Assert.That(await CountEventsAsync(connection, forged), Is.Zero);
        }

        // SQL LIKE treats '_' as a wildcard unless escaped. Both near-match
        // reasons were previously accepted by the generic NOVA_IDENTIDADE gate
        // while escaping the SEM_CANDIDATO candidate-evidence guard.
        foreach (var forgedReason in new[]
        {
            "SEMXCANDIDATO_NO_RULESET_BLOCKING",
            "SEM_CANDIDATOXNO_RULESET_BLOCKING"
        })
        {
            var forged = Guid.NewGuid();
            await SeedRunAsync(connection, forged, model, version, sourceId,
                observationId, initial, forgedReason, bestCandidate: existingCandidate);
            Assert.That(await TryPublishAsync(connection, forged), Is.EqualTo(51810),
                "Only the literal SEM_CANDIDATO_ prefix may enter the legacy no-candidate path.");
            Assert.That(await CountEventsAsync(connection, forged), Is.Zero);
            Assert.That(await ReadProgressiveStateAsync(connection, sourceId),
                Is.EqualTo(("PROVISORIA", (Guid?)null)));
            Assert.That(await ReadRunStatusAsync(connection, forged),
                Is.EqualTo("EXECUTANDO"));
            Assert.That(await TryPublishAsync(connection, forged), Is.EqualTo(51810),
                "Replay must reject a near-match reason without side effects.");
            Assert.That(await CountEventsAsync(connection, forged), Is.Zero);
        }

        // Mixed-batch atomicity: one valid legacy no-candidate observation
        // must NOT be committed if a second source in the SAME run forges
        // absence despite a persisted candidate. A one-row rejection alone
        // cannot prove all-or-nothing ledger semantics.
        long secondSourceId;
        long secondObservationId;
        await using (var second = connection.CreateCommand())
        {
            second.CommandText = """
                SELECT TOP(1) o.pessoa_origem_id, po.pessoa_observacao_id
                FROM silver.pessoa_origem o
                JOIN silver.pessoa_observacao po ON po.pessoa_origem_id=o.pessoa_origem_id
                LEFT JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=o.pessoa_origem_id
                WHERE o.codigo_pessoa_origem LIKE N'SCALE-%'
                  AND o.pessoa_origem_id<>@source AND po.cpf IS NULL
                  AND (p.estado=N'PROVISORIA' OR p.pessoa_origem_id IS NULL)
                ORDER BY po.pessoa_observacao_id DESC;
                """;
            second.Parameters.AddWithValue("@source", sourceId);
            await using var reader = await second.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True, "A second isolated synthetic source is required.");
            secondSourceId = reader.GetInt64(0);
            secondObservationId = reader.GetInt64(1);
        }
        await using (var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await using var ensureSecond = connection.CreateCommand();
            ensureSecond.Transaction = tx;
            ensureSecond.CommandText = "EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@source;";
            ensureSecond.Parameters.AddWithValue("@source", secondSourceId);
            await ensureSecond.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        Guid secondInitial;
        await using (var secondIdentity = connection.CreateCommand())
        {
            secondIdentity.CommandText =
                "SELECT initial_uuid FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=@source;";
            secondIdentity.Parameters.AddWithValue("@source", secondSourceId);
            secondInitial = (Guid)(await secondIdentity.ExecuteScalarAsync())!;
        }
        var mixedRun = Guid.NewGuid();
        await SeedRunAsync(connection, mixedRun, model, version, sourceId,
            observationId, initial, "SEM_CANDIDATO_NO_RULESET_BLOCKING");
        await using (var append = connection.CreateCommand())
        {
            append.CommandText = """
                UPDATE identidade.linkage_run
                   SET limite_solicitado=2,registros_elegiveis=2,avaliados=2,nao_resolvidos=2,
                       pessoa_observacao_id_high_watermark=
                           CASE WHEN pessoa_observacao_id_high_watermark<@secondObs
                                THEN @secondObs ELSE pessoa_observacao_id_high_watermark END
                 WHERE linkage_run_id=@run;
                INSERT identidade.linkage_run_item(linkage_run_id,pessoa_observacao_id)
                  VALUES(@run,@secondObs);
                INSERT identidade.linkage_resultado(
                  linkage_run_id,modelo_id,modelo_versao,pessoa_observacao_id,
                  pessoa_uuid_resolvido,melhor_candidato_uuid,score_melhor,
                  segundo_candidato_uuid,score_segundo,margem,status,motivo,calculado_em,
                  resultado_publicacao,pessoa_uuid_publicado,status_publicacao,motivo_publicacao,
                  pessoa_origem_id_publicado,politica_publicacao_versao,universo_referencia,publicado_em)
                VALUES(@run,@model,@version,@secondObs,NULL,@candidate,0,
                  NULL,NULL,NULL,N'NAO_RESOLVIDO',N'SEM_CANDIDATO_NO_RULESET_BLOCKING',
                  SYSUTCDATETIME(),N'NOVA_IDENTIDADE',@secondInitial,N'RESOLVIDO',
                  N'NOVA_IDENTIDADE_LIMIAR_INFERIOR_V9_GOVERNADO',@secondSource,
                  N'V9_DT10_LOWER_TEST_DISABLED',N'RUN_COMPLETO_DT10',SYSUTCDATETIME());
                """;
            append.Parameters.AddWithValue("@run", mixedRun);
            append.Parameters.AddWithValue("@model", model);
            append.Parameters.AddWithValue("@version", version);
            append.Parameters.AddWithValue("@secondObs", secondObservationId);
            append.Parameters.AddWithValue("@secondSource", secondSourceId);
            append.Parameters.AddWithValue("@secondInitial", secondInitial);
            append.Parameters.AddWithValue("@candidate", existingCandidate);
            await append.ExecuteNonQueryAsync();
        }
        foreach (var attempt in new[] { 1, 2 })
        {
            Assert.That(await TryPublishAsync(connection, mixedRun), Is.EqualTo(51835),
                $"Mixed batch attempt {attempt} must fail on persisted candidate.");
            Assert.That(await CountEventsAsync(connection, mixedRun), Is.Zero,
                "The valid source must not be partially published before the forged source fails.");
            Assert.That(await ReadRunStatusAsync(connection, mixedRun), Is.EqualTo("EXECUTANDO"));
            Assert.That(await ReadProgressiveStateAsync(connection, sourceId),
                Is.EqualTo(("PROVISORIA", (Guid?)null)));
            Assert.That(await ReadProgressiveStateAsync(connection, secondSourceId),
                Is.EqualTo(("PROVISORIA", (Guid?)null)));
        }

        // Unchanged no-candidate path must still publish exactly once,
        // including when the same EXECUTANDO run is replayed.
        var legacy = Guid.NewGuid();
        await SeedRunAsync(connection, legacy, model, version, sourceId, observationId,
            initial, "SEM_CANDIDATO_NO_RULESET_BLOCKING");
        Assert.That(await TryPublishAsync(connection, legacy), Is.Null);
        Assert.That(await CountEventsAsync(connection, legacy), Is.EqualTo(1));
        Assert.That(await TryPublishAsync(connection, legacy), Is.Null);
        Assert.That(await CountEventsAsync(connection, legacy), Is.EqualTo(1));
    }

    private static async Task SeedRunAsync(SqlConnection sql, Guid run, Guid model, int version,
        long source, long observation, Guid initial, string reason,
        Guid? bestCandidate = null, Guid? secondCandidate = null)
    {
        await using var cmd = sql.CreateCommand();
        cmd.CommandText = """
            INSERT identidade.linkage_run(
              linkage_run_id,modelo_id,modelo_versao,tipo_run,status,limite_solicitado,
              escopo_json,batch_size,max_parallelism,pessoa_observacao_id_high_watermark,
              registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,
              sem_candidato_no_bloco,solicitado_por,motivo,correlation_id,iniciado_em)
            VALUES(@run,@model,@version,N'FULL',N'EXECUTANDO',1,N'{"test":"dt10-v9"}',
              1,1,@obs,1,1,0,1,0,0,N'CI',N'DT10 V9 disposable',NEWID(),SYSUTCDATETIME());
            INSERT identidade.linkage_run_item(linkage_run_id,pessoa_observacao_id)
              VALUES(@run,@obs);
            INSERT identidade.linkage_resultado(
              linkage_run_id,modelo_id,modelo_versao,pessoa_observacao_id,
              pessoa_uuid_resolvido,melhor_candidato_uuid,score_melhor,
              segundo_candidato_uuid,score_segundo,margem,status,motivo,calculado_em,
              resultado_publicacao,pessoa_uuid_publicado,status_publicacao,motivo_publicacao,
              pessoa_origem_id_publicado,politica_publicacao_versao,universo_referencia,publicado_em)
            VALUES(@run,@model,@version,@obs,NULL,@best,0,@second,
              CASE WHEN @second IS NULL THEN NULL ELSE 0 END,NULL,N'NAO_RESOLVIDO',
              @reason,SYSUTCDATETIME(),N'NOVA_IDENTIDADE',@initial,N'RESOLVIDO',
              N'NOVA_IDENTIDADE_LIMIAR_INFERIOR_V9_GOVERNADO',@source,
              N'V9_DT10_LOWER_TEST_DISABLED',N'RUN_COMPLETO_DT10',SYSUTCDATETIME());
            """;
        cmd.Parameters.AddWithValue("@run", run);
        cmd.Parameters.AddWithValue("@model", model);
        cmd.Parameters.AddWithValue("@version", version);
        cmd.Parameters.AddWithValue("@source", source);
        cmd.Parameters.AddWithValue("@obs", observation);
        cmd.Parameters.AddWithValue("@initial", initial);
        cmd.Parameters.AddWithValue("@reason", reason);
        cmd.Parameters.Add("@best", SqlDbType.UniqueIdentifier).Value = (object?)bestCandidate ?? DBNull.Value;
        cmd.Parameters.Add("@second", SqlDbType.UniqueIdentifier).Value = (object?)secondCandidate ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int?> TryPublishAsync(SqlConnection sql, Guid run)
    {
        await using var tx = (SqlTransaction)await sql.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await using var cmd = sql.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandTimeout = 90;
            cmd.CommandText =
                "EXEC identidade.sp_publicar_resolucao_progressiva_linkage_lote @linkage_run_id=@run;";
            cmd.Parameters.AddWithValue("@run", run);
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
            return null;
        }
        catch (SqlException ex)
        {
            if (tx.Connection is not null) await tx.RollbackAsync();
            return ex.Number;
        }
    }

    private static async Task AssertGoldIdentityInvariantAsync(
        SqlConnection sql, long source, Guid initial)
    {
        await using (var identity = sql.CreateCommand())
        {
            identity.CommandText = """
                SELECT COUNT_BIG(*)
                FROM identidade.pessoa_origem_progressiva
                WHERE pessoa_origem_id=@source AND initial_uuid=@initial
                  AND initial_uuid IS NOT NULL;
                """;
            identity.Parameters.AddWithValue("@source", source);
            identity.Parameters.AddWithValue("@initial", initial);
            Assert.That(Convert.ToInt64(await identity.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(1),
                "A provisional source must retain its immutable initial UUID.");
        }

        await using (var gold = sql.CreateCommand())
        {
            gold.CommandText = """
                SELECT CASE WHEN OBJECT_ID(N'gold.pessoa', N'U') IS NOT NULL
                  AND EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE object_id=OBJECT_ID(N'gold.pessoa', N'U')
                      AND name=N'pessoa_uuid' AND is_nullable=0
                  )
                THEN 1 ELSE 0 END;
                """;
            Assert.That(Convert.ToInt32(await gold.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(1),
                "gold.pessoa must exist and expose a NOT NULL pessoa_uuid column.");
        }

        await using (var goldIdentity = sql.CreateCommand())
        {
            goldIdentity.CommandText = """
                SELECT COUNT_BIG(*) FROM gold.pessoa WHERE pessoa_uuid=@initial;
                """;
            goldIdentity.Parameters.AddWithValue("@initial", initial);
            // A Gold shell may be materialized asynchronously; when present
            // it must use the immutable initial UUID, never a NULL key.
            Assert.That(Convert.ToInt64(await goldIdentity.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture), Is.LessThanOrEqualTo(1L),
                "A materialized Gold identity cannot duplicate its initial UUID.");
        }
    }

    private static async Task<(string State, Guid? Canonical)> ReadProgressiveStateAsync(
        SqlConnection sql, long source)
    {
        await using var cmd = sql.CreateCommand();
        cmd.CommandText = "SELECT estado, canonical_uuid FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=@source;";
        cmd.Parameters.AddWithValue("@source", source);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1));
    }

    private static async Task<string> ReadRunStatusAsync(SqlConnection sql, Guid run)
    {
        await using var cmd = sql.CreateCommand();
        cmd.CommandText = "SELECT status FROM identidade.linkage_run WHERE linkage_run_id=@run;";
        cmd.Parameters.AddWithValue("@run", run);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<long> CountEventsAsync(SqlConnection sql, Guid run)
    {
        await using var cmd = sql.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT_BIG(*) FROM identidade.pessoa_origem_progressiva_evento WHERE linkage_run_id=@run;";
        cmd.Parameters.AddWithValue("@run", run);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}

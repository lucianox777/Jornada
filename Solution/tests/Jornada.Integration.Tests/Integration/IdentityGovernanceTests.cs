using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class IdentityGovernanceTests
{
    [Test]
    public async Task Governed_case_suspends_then_restores_assignment_without_reopening_lot()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var observations = new List<(long Obs, Guid Uuid, Guid Lote)>();
            await using (var pick = connection.CreateCommand())
            {
                pick.Transaction = tx;
                pick.CommandText = """
                    SELECT TOP(2) po.pessoa_observacao_id,vc.pessoa_uuid,po.lote_id
                    FROM silver.pessoa_observacao po
                    JOIN ref.gestor g ON g.gestor_id=po.gestor_id AND g.codigo='SMADS'
                    JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
                    WHERE vc.status='RESOLVIDO' AND vc.pessoa_uuid IS NOT NULL
                      AND EXISTS(SELECT 1 FROM silver.registro_observacao ro WHERE ro.pessoa_observacao_id=po.pessoa_observacao_id)
                    ORDER BY po.pessoa_observacao_id;
                    """;
                await using var reader = await pick.ExecuteReaderAsync();
                while (await reader.ReadAsync()) observations.Add((reader.GetInt64(0), reader.GetGuid(1), reader.GetGuid(2)));
            }
            Assert.That(observations.Count, Is.GreaterThanOrEqualTo(2), "Fixture DEV deve conter duas observações SMADS resolvidas com fatos.");

            var obsJson = JsonSerializer.Serialize(observations.Select(x => x.Obs));
            var caseId = Guid.Empty;
            await using (var open = connection.CreateCommand())
            {
                open.Transaction = tx;
                open.CommandType = CommandType.StoredProcedure;
                open.CommandText = "identidade.sp_abrir_caso_conflito_identidade";
                open.Parameters.AddWithValue("@gestor_codigo", "SMADS");
                open.Parameters.AddWithValue("@motivo", "LINKAGE_INCORRETO");
                open.Parameters.AddWithValue("@observacoes_json", obsJson);
                open.Parameters.AddWithValue("@ato_referencia", "TESTE-INTEGRACAO-V346");
                open.Parameters.AddWithValue("@justificativa", "Exercita suspensão e correção governada sem reprocessar o fato.");
                open.Parameters.AddWithValue("@correlation_id", Guid.NewGuid());
                var output = open.Parameters.Add("@caso_id", SqlDbType.UniqueIdentifier); output.Direction = ParameterDirection.Output;
                await open.ExecuteNonQueryAsync();
                caseId = (Guid)output.Value;
            }

            await using (var suspended = connection.CreateCommand())
            {
                suspended.Transaction = tx;
                suspended.CommandText = """
                    SELECT
                      (SELECT COUNT(*) FROM identidade.v_vinculo_corrente WHERE pessoa_observacao_id IN(@o1,@o2) AND status='CONFLITO' AND metodo_resolucao='CONFLITO_GOVERNADO'),
                      (SELECT COUNT(*) FROM serving.registro_integrado ri JOIN silver.registro_observacao ro ON ro.registro_observacao_id=ri.registro_observacao_id WHERE ro.pessoa_observacao_id IN(@o1,@o2) AND ri.estado_atribuicao_identidade='CONFLITO_IDENTIDADE' AND ri.pessoa_uuid IS NULL),
                      (SELECT COUNT(*) FROM ingestao.lote WHERE lote_id IN(@l1,@l2) AND status='PROCESSADO');
                    """;
                suspended.Parameters.AddWithValue("@o1", observations[0].Obs); suspended.Parameters.AddWithValue("@o2", observations[1].Obs);
                suspended.Parameters.AddWithValue("@l1", observations[0].Lote); suspended.Parameters.AddWithValue("@l2", observations[1].Lote);
                await using var reader = await suspended.ExecuteReaderAsync(); Assert.That(await reader.ReadAsync(), Is.True);
                Assert.That(reader.GetInt32(0), Is.EqualTo(2));
                Assert.That(reader.GetInt32(1), Is.GreaterThanOrEqualTo(2), "Os fatos permanecem materializados, mas sem atribuição canônica.");
                Assert.That(reader.GetInt32(2), Is.GreaterThanOrEqualTo(1), "Abrir o caso não reabre lotes.");
            }

            var groups = JsonSerializer.Serialize(new[]
            {
                new { grupoCodigo = "A", pessoaUuidDestino = observations[0].Uuid, pessoaObservacaoIds = new[] { observations[0].Obs } },
                new { grupoCodigo = "B", pessoaUuidDestino = observations[1].Uuid, pessoaObservacaoIds = new[] { observations[1].Obs } }
            });
            await using (var apply = connection.CreateCommand())
            {
                apply.Transaction = tx;
                apply.CommandType = CommandType.StoredProcedure;
                apply.CommandText = "identidade.sp_aplicar_caso_conflito_identidade";
                apply.Parameters.AddWithValue("@gestor_codigo", "SMADS");
                apply.Parameters.AddWithValue("@caso_id", caseId);
                apply.Parameters.AddWithValue("@grupos_json", groups);
                apply.Parameters.AddWithValue("@correlation_id", Guid.NewGuid());
                await apply.ExecuteNonQueryAsync();
            }

            await using (var verify = connection.CreateCommand())
            {
                verify.Transaction = tx;
                verify.CommandText = """
                    SELECT
                      (SELECT COUNT(*) FROM identidade.v_vinculo_corrente WHERE pessoa_observacao_id IN(@o1,@o2) AND status='RESOLVIDO' AND metodo_resolucao='CORRECAO_GOVERNADA'),
                      (SELECT COUNT(*) FROM serving.registro_integrado ri JOIN silver.registro_observacao ro ON ro.registro_observacao_id=ri.registro_observacao_id WHERE ro.pessoa_observacao_id IN(@o1,@o2) AND ri.estado_atribuicao_identidade='ATRIBUIDA' AND ri.pessoa_uuid IS NOT NULL),
                      (SELECT COUNT(*) FROM identidade.caso_conflito_identidade WHERE caso_id=@case AND status='APLICADO'),
                      (SELECT COUNT(*) FROM ingestao.lote WHERE lote_id IN(@l1,@l2) AND status='PROCESSADO'),
                      (SELECT COUNT(*) FROM gold.pessoa WHERE pessoa_uuid IN(@u1,@u2));
                    """;
                verify.Parameters.AddWithValue("@o1", observations[0].Obs); verify.Parameters.AddWithValue("@o2", observations[1].Obs);
                verify.Parameters.AddWithValue("@l1", observations[0].Lote); verify.Parameters.AddWithValue("@l2", observations[1].Lote);
                verify.Parameters.AddWithValue("@u1", observations[0].Uuid); verify.Parameters.AddWithValue("@u2", observations[1].Uuid);
                verify.Parameters.AddWithValue("@case", caseId);
                await using var reader = await verify.ExecuteReaderAsync(); Assert.That(await reader.ReadAsync(), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(reader.GetInt32(0), Is.EqualTo(2));
                    Assert.That(reader.GetInt32(1), Is.GreaterThanOrEqualTo(2));
                    Assert.That(reader.GetInt32(2), Is.EqualTo(1));
                    Assert.That(reader.GetInt32(3), Is.GreaterThanOrEqualTo(1), "A correção muda atribuição e não reabre lote.");
                    Assert.That(reader.GetInt32(4), Is.EqualTo(2), "Os núcleos Gold de destino são recompostos na mesma correção.");
                });
            }
        }
        finally { await tx.RollbackAsync(); }
    }

    [Test]
    public async Task Historical_merge_preserves_canonical_uuid_and_moves_active_identifiers()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var canonical = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1");
            var absorbed = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa5");
            await using (var addIdentifier = connection.CreateCommand())
            {
                addIdentifier.Transaction = tx;
                addIdentifier.CommandText = """
                    IF NOT EXISTS(SELECT 1 FROM identidade.identity_map WHERE tipo='NIS' AND identificador='12345678901' AND vigencia_fim IS NULL)
                    INSERT identidade.identity_map(pessoa_uuid,tipo,identificador,vigencia_inicio,metodo_resolucao,estado,estado_motivo,estado_em)
                    VALUES(@absorbed,'NIS','12345678901',SYSDATETIMEOFFSET(),'CORRECAO_GOVERNADA','ATIVO','TESTE_FUSAO',SYSDATETIMEOFFSET());
                    """;
                addIdentifier.Parameters.AddWithValue("@absorbed", absorbed);
                await addIdentifier.ExecuteNonQueryAsync();
            }

            var observations = new List<long>();
            await using (var pick = connection.CreateCommand())
            {
                pick.Transaction = tx;
                pick.CommandText = """
                    SELECT vc.pessoa_observacao_id
                    FROM identidade.v_vinculo_corrente vc
                    WHERE vc.status='RESOLVIDO' AND vc.pessoa_uuid IN(@canonical,@absorbed)
                    ORDER BY vc.pessoa_observacao_id;
                    """;
                pick.Parameters.AddWithValue("@canonical", canonical);
                pick.Parameters.AddWithValue("@absorbed", absorbed);
                await using var reader = await pick.ExecuteReaderAsync();
                while (await reader.ReadAsync()) observations.Add(reader.GetInt64(0));
            }
            Assert.That(observations.Count, Is.GreaterThanOrEqualTo(2), "Fixture DEV deve conter observações suficientes para fusão histórica 2→1.");

            Guid caseId;
            await using (var open = connection.CreateCommand())
            {
                open.Transaction = tx; open.CommandType = CommandType.StoredProcedure; open.CommandText = "identidade.sp_abrir_caso_conflito_identidade";
                open.Parameters.AddWithValue("@gestor_codigo", "SMADS");
                open.Parameters.AddWithValue("@motivo", "FUSAO_HISTORICA");
                open.Parameters.AddWithValue("@observacoes_json", JsonSerializer.Serialize(observations));
                open.Parameters.AddWithValue("@ato_referencia", "TESTE-FUSAO-2-1-V362");
                open.Parameters.AddWithValue("@justificativa", "Comprova sucessão canônica e migração de identificadores no fluxo governado.");
                open.Parameters.AddWithValue("@correlation_id", Guid.NewGuid());
                var output = open.Parameters.Add("@caso_id", SqlDbType.UniqueIdentifier); output.Direction = ParameterDirection.Output;
                await open.ExecuteNonQueryAsync(); caseId = (Guid)output.Value;
            }

            var groups = JsonSerializer.Serialize(new[] { new { grupoCodigo = "CANONICO", pessoaUuidDestino = canonical, pessoaObservacaoIds = observations.ToArray() } });
            await using (var apply = connection.CreateCommand())
            {
                apply.Transaction = tx; apply.CommandType = CommandType.StoredProcedure; apply.CommandText = "identidade.sp_aplicar_caso_conflito_identidade";
                apply.Parameters.AddWithValue("@gestor_codigo", "SMADS"); apply.Parameters.AddWithValue("@caso_id", caseId);
                apply.Parameters.AddWithValue("@grupos_json", groups); apply.Parameters.AddWithValue("@correlation_id", Guid.NewGuid());
                await apply.ExecuteNonQueryAsync();
            }

            await using var verify = connection.CreateCommand(); verify.Transaction = tx;
            verify.CommandText = """
                SELECT
                  (SELECT COUNT(*) FROM identidade.pessoa WHERE pessoa_uuid=@absorbed AND status='FUNDIDO' AND pessoa_uuid_sucessor=@canonical),
                  CASE WHEN identidade.fn_pessoa_uuid_canonico(@absorbed)=@canonical THEN 1 ELSE 0 END,
                  (SELECT COUNT(*) FROM identidade.v_vinculo_corrente WHERE pessoa_observacao_id IN (SELECT value FROM OPENJSON(@obs)) AND status='RESOLVIDO' AND pessoa_uuid=@canonical),
                  (SELECT COUNT(*) FROM identidade.identity_map WHERE tipo='NIS' AND identificador='12345678901' AND pessoa_uuid=@canonical AND vigencia_fim IS NULL AND estado='ATIVO'),
                  (SELECT COUNT(*) FROM identidade.identity_map WHERE pessoa_uuid=@absorbed AND vigencia_fim IS NULL),
                  (SELECT COUNT(*) FROM gold.pessoa WHERE pessoa_uuid=@absorbed),
                  (SELECT COUNT(*) FROM identidade.caso_conflito_identidade WHERE caso_id=@case AND status='APLICADO'),
                  (SELECT COUNT(*) FROM identidade.identity_map_estado_evento WHERE referencia_operacao=@case AND motivo='CASO_GOVERNADO_APLICADO' AND estado_anterior='EM_CONFLITO' AND estado_novo='ATIVO'),
                  (SELECT COUNT(*) FROM identidade.identity_map_estado_evento WHERE referencia_operacao=@case AND motivo='FUSAO_HISTORICA' AND estado_novo='ENCERRADO');
                """;
            verify.Parameters.AddWithValue("@absorbed", absorbed); verify.Parameters.AddWithValue("@canonical", canonical);
            verify.Parameters.AddWithValue("@obs", JsonSerializer.Serialize(observations)); verify.Parameters.AddWithValue("@case", caseId);
            await using var result = await verify.ExecuteReaderAsync(); Assert.That(await result.ReadAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(result.GetInt32(0), Is.EqualTo(1));
                Assert.That(result.GetInt32(1), Is.EqualTo(1));
                Assert.That(result.GetInt32(2), Is.EqualTo(observations.Count));
                Assert.That(result.GetInt32(3), Is.EqualTo(1));
                Assert.That(result.GetInt32(4), Is.Zero);
                Assert.That(result.GetInt32(5), Is.Zero);
                Assert.That(result.GetInt32(6), Is.EqualTo(1));
                Assert.That(result.GetInt32(7), Is.GreaterThanOrEqualTo(1));
                Assert.That(result.GetInt32(8), Is.GreaterThanOrEqualTo(1));
            });
        }
        finally { await tx.RollbackAsync(); }
    }

    [Test]
    public async Task Historical_merge_rejects_partial_absorption_with_diagnostic_error()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var absorbed = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1");
            var canonical = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa2");
            var observations = new List<long>();
            await using (var pick = connection.CreateCommand())
            {
                pick.Transaction = tx;
                pick.CommandText = """
                    SELECT pessoa_observacao_id
                    FROM identidade.v_vinculo_corrente
                    WHERE pessoa_uuid=@absorbed AND status='RESOLVIDO'
                    ORDER BY pessoa_observacao_id;
                    """;
                pick.Parameters.AddWithValue("@absorbed", absorbed);
                await using var reader = await pick.ExecuteReaderAsync();
                while (await reader.ReadAsync()) observations.Add(reader.GetInt64(0));
            }
            Assert.That(observations.Count, Is.GreaterThanOrEqualTo(2), "Fixture DEV deve conter duas observações correntes para provar fusão histórica parcial.");

            Guid caseId;
            await using (var open = connection.CreateCommand())
            {
                open.Transaction = tx; open.CommandType = CommandType.StoredProcedure; open.CommandText = "identidade.sp_abrir_caso_conflito_identidade";
                open.Parameters.AddWithValue("@gestor_codigo", "SMADS");
                open.Parameters.AddWithValue("@motivo", "FUSAO_HISTORICA");
                open.Parameters.AddWithValue("@observacoes_json", JsonSerializer.Serialize(new[] { observations[0] }));
                open.Parameters.AddWithValue("@ato_referencia", "TESTE-FUSAO-PARCIAL-V363");
                open.Parameters.AddWithValue("@justificativa", "A fusão deve falhar se o UUID absorvido conservar observações RESOLVIDAS fora do caso.");
                open.Parameters.AddWithValue("@correlation_id", Guid.NewGuid());
                var output = open.Parameters.Add("@caso_id", SqlDbType.UniqueIdentifier); output.Direction = ParameterDirection.Output;
                await open.ExecuteNonQueryAsync(); caseId = (Guid)output.Value;
            }

            var groups = JsonSerializer.Serialize(new[]
            {
                new { grupoCodigo = "CANONICO", pessoaUuidDestino = canonical, pessoaObservacaoIds = new[] { observations[0] } }
            });
            await using var apply = connection.CreateCommand();
            apply.Transaction = tx; apply.CommandType = CommandType.StoredProcedure; apply.CommandText = "identidade.sp_aplicar_caso_conflito_identidade";
            apply.Parameters.AddWithValue("@gestor_codigo", "SMADS"); apply.Parameters.AddWithValue("@caso_id", caseId);
            apply.Parameters.AddWithValue("@grupos_json", groups); apply.Parameters.AddWithValue("@correlation_id", Guid.NewGuid());
            var ex = Assert.ThrowsAsync<SqlException>(async () => await apply.ExecuteNonQueryAsync());
            Assert.That(ex!.Number, Is.EqualTo(51119));
        }
        finally { await tx.RollbackAsync(); }
    }

    [Test]
    public async Task Historical_merge_rejects_one_origin_split_across_multiple_destinations()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var absorbed = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1");
            var canonical1 = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa2");
            var canonical2 = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa3");
            var observations = new List<long>();
            await using (var pick = connection.CreateCommand())
            {
                pick.Transaction = tx;
                pick.CommandText = """
                    SELECT pessoa_observacao_id FROM identidade.v_vinculo_corrente
                    WHERE pessoa_uuid=@absorbed AND status='RESOLVIDO'
                    ORDER BY pessoa_observacao_id;
                    """;
                pick.Parameters.AddWithValue("@absorbed", absorbed);
                await using var reader = await pick.ExecuteReaderAsync();
                while (await reader.ReadAsync()) observations.Add(reader.GetInt64(0));
            }
            Assert.That(observations.Count, Is.GreaterThanOrEqualTo(2), "Fixture DEV deve conter duas observações correntes para provar dispersão 1→N.");

            Guid caseId;
            await using (var open = connection.CreateCommand())
            {
                open.Transaction = tx; open.CommandType = CommandType.StoredProcedure; open.CommandText = "identidade.sp_abrir_caso_conflito_identidade";
                open.Parameters.AddWithValue("@gestor_codigo", "SMADS");
                open.Parameters.AddWithValue("@motivo", "FUSAO_HISTORICA");
                open.Parameters.AddWithValue("@observacoes_json", JsonSerializer.Serialize(observations));
                open.Parameters.AddWithValue("@ato_referencia", "TESTE-FUSAO-DISPERSA-V364");
                open.Parameters.AddWithValue("@justificativa", "Uma origem de FUSAO_HISTORICA não pode ser repartida entre dois destinos.");
                open.Parameters.AddWithValue("@correlation_id", Guid.NewGuid());
                var output = open.Parameters.Add("@caso_id", SqlDbType.UniqueIdentifier); output.Direction = ParameterDirection.Output;
                await open.ExecuteNonQueryAsync(); caseId = (Guid)output.Value;
            }

            var midpoint = observations.Count / 2;
            var groups = JsonSerializer.Serialize(new[]
            {
                new { grupoCodigo = "DESTINO_A", pessoaUuidDestino = canonical1, pessoaObservacaoIds = observations.Take(midpoint).ToArray() },
                new { grupoCodigo = "DESTINO_B", pessoaUuidDestino = canonical2, pessoaObservacaoIds = observations.Skip(midpoint).ToArray() }
            });
            await using var apply = connection.CreateCommand();
            apply.Transaction = tx; apply.CommandType = CommandType.StoredProcedure; apply.CommandText = "identidade.sp_aplicar_caso_conflito_identidade";
            apply.Parameters.AddWithValue("@gestor_codigo", "SMADS"); apply.Parameters.AddWithValue("@caso_id", caseId);
            apply.Parameters.AddWithValue("@grupos_json", groups); apply.Parameters.AddWithValue("@correlation_id", Guid.NewGuid());
            var ex = Assert.ThrowsAsync<SqlException>(async () => await apply.ExecuteNonQueryAsync());
            Assert.That(ex!.Number, Is.EqualTo(51119));
        }
        finally { await tx.RollbackAsync(); }
    }

    [Test]
    public async Task Open_governed_case_is_atomic_when_called_directly_without_outer_transaction()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        long observation;
        Guid originalUuid;
        await using (var pick = connection.CreateCommand())
        {
            pick.CommandText = """
                SELECT TOP(1) vc.pessoa_observacao_id,vc.pessoa_uuid
                FROM identidade.v_vinculo_corrente vc
                WHERE vc.status='RESOLVIDO' AND vc.pessoa_uuid IS NOT NULL
                ORDER BY vc.pessoa_observacao_id;
                """;
            await using var reader = await pick.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            observation = reader.GetInt64(0); originalUuid = reader.GetGuid(1);
        }

        const string trigger = "identidade.tr_test_atomic_open_case";
        const string ato = "TESTE-ATOMICIDADE-OPEN-V365";
        try
        {
            await ExecuteNonQueryAsync(connection, $"""
                CREATE OR ALTER TRIGGER {trigger} ON identidade.vinculo_fonte AFTER INSERT AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted WHERE metodo_resolucao='CONFLITO_GOVERNADO' AND motivo='OUTRO')
                    THROW 51980,'Falha injetada para provar rollback de abertura governada.',1;
                END;
                """);

            await using var open = connection.CreateCommand();
            open.CommandType = CommandType.StoredProcedure;
            open.CommandText = "identidade.sp_abrir_caso_conflito_identidade";
            open.Parameters.AddWithValue("@gestor_codigo", "SMADS");
            open.Parameters.AddWithValue("@motivo", "OUTRO");
            open.Parameters.AddWithValue("@observacoes_json", JsonSerializer.Serialize(new[] { observation }));
            open.Parameters.AddWithValue("@ato_referencia", ato);
            open.Parameters.AddWithValue("@justificativa", "Prova de atomicidade sem transação externa.");
            open.Parameters.AddWithValue("@correlation_id", Guid.NewGuid());
            var output = open.Parameters.Add("@caso_id", SqlDbType.UniqueIdentifier); output.Direction = ParameterDirection.Output;
            var ex = Assert.ThrowsAsync<SqlException>(async () => await open.ExecuteNonQueryAsync());
            Assert.That(ex!.Number, Is.EqualTo(51980));

            await using var verify = connection.CreateCommand();
            verify.CommandText = """
                SELECT
                  (SELECT COUNT(*) FROM identidade.caso_conflito_identidade WHERE ato_referencia=@ato),
                  (SELECT COUNT(*) FROM identidade.v_vinculo_corrente WHERE pessoa_observacao_id=@obs AND pessoa_uuid=@uuid AND status='RESOLVIDO'),
                  (SELECT COUNT(*) FROM identidade.pessoa WHERE pessoa_uuid=@uuid AND status='ATIVO');
                """;
            verify.Parameters.AddWithValue("@ato", ato); verify.Parameters.AddWithValue("@obs", observation); verify.Parameters.AddWithValue("@uuid", originalUuid);
            await using var reader = await verify.ExecuteReaderAsync(); Assert.That(await reader.ReadAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reader.GetInt32(0), Is.EqualTo(0), "Cabeçalho do caso deve ser revertido.");
                Assert.That(reader.GetInt32(1), Is.EqualTo(1), "Vínculo corrente original deve ser preservado.");
                Assert.That(reader.GetInt32(2), Is.EqualTo(1), "Status da Pessoa deve ser preservado.");
            });
        }
        finally { await DropTriggerAsync(connection, trigger); }
    }

    [Test]
    public async Task Apply_governed_case_is_atomic_when_called_directly_without_outer_transaction()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        long observation;
        Guid originalUuid;
        await using (var pick = connection.CreateCommand())
        {
            pick.CommandText = "SELECT TOP(1) pessoa_observacao_id,pessoa_uuid FROM identidade.v_vinculo_corrente WHERE status='RESOLVIDO' AND pessoa_uuid IS NOT NULL ORDER BY pessoa_observacao_id;";
            await using var reader = await pick.ExecuteReaderAsync(); Assert.That(await reader.ReadAsync(), Is.True);
            observation = reader.GetInt64(0); originalUuid = reader.GetGuid(1);
        }

        Guid caseId = Guid.Empty;
        var groups = JsonSerializer.Serialize(new[] { new { grupoCodigo = "ORIGINAL", pessoaUuidDestino = originalUuid, pessoaObservacaoIds = new[] { observation } } });
        const string trigger = "identidade.tr_test_atomic_apply_case";
        try
        {
            await using (var open = connection.CreateCommand())
            {
                open.CommandType = CommandType.StoredProcedure; open.CommandText = "identidade.sp_abrir_caso_conflito_identidade";
                open.Parameters.AddWithValue("@gestor_codigo", "SMADS"); open.Parameters.AddWithValue("@motivo", "OUTRO");
                open.Parameters.AddWithValue("@observacoes_json", JsonSerializer.Serialize(new[] { observation }));
                open.Parameters.AddWithValue("@ato_referencia", "TESTE-ATOMICIDADE-APPLY-V365");
                open.Parameters.AddWithValue("@justificativa", "Prepara caso para prova de rollback de aplicação direta.");
                open.Parameters.AddWithValue("@correlation_id", Guid.NewGuid());
                var output = open.Parameters.Add("@caso_id", SqlDbType.UniqueIdentifier); output.Direction = ParameterDirection.Output;
                await open.ExecuteNonQueryAsync(); caseId = (Guid)output.Value;
            }

            await ExecuteNonQueryAsync(connection, $"""
                CREATE OR ALTER TRIGGER {trigger} ON identidade.vinculo_fonte AFTER INSERT AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted WHERE metodo_resolucao='CORRECAO_GOVERNADA')
                    THROW 51981,'Falha injetada para provar rollback de aplicação governada.',1;
                END;
                """);

            await using var apply = connection.CreateCommand();
            apply.CommandType = CommandType.StoredProcedure; apply.CommandText = "identidade.sp_aplicar_caso_conflito_identidade";
            apply.Parameters.AddWithValue("@gestor_codigo", "SMADS"); apply.Parameters.AddWithValue("@caso_id", caseId);
            apply.Parameters.AddWithValue("@grupos_json", groups); apply.Parameters.AddWithValue("@correlation_id", Guid.NewGuid());
            var ex = Assert.ThrowsAsync<SqlException>(async () => await apply.ExecuteNonQueryAsync());
            Assert.That(ex!.Number, Is.EqualTo(51981));

            await using var verify = connection.CreateCommand();
            verify.CommandText = """
                SELECT
                  (SELECT COUNT(*) FROM identidade.caso_conflito_identidade WHERE caso_id=@case AND status='ABERTO'),
                  (SELECT COUNT(*) FROM identidade.v_vinculo_corrente WHERE pessoa_observacao_id=@obs AND status='CONFLITO' AND metodo_resolucao='CONFLITO_GOVERNADO');
                """;
            verify.Parameters.AddWithValue("@case", caseId); verify.Parameters.AddWithValue("@obs", observation);
            await using var reader = await verify.ExecuteReaderAsync(); Assert.That(await reader.ReadAsync(), Is.True);
            Assert.That(reader.GetInt32(0), Is.EqualTo(1));
            Assert.That(reader.GetInt32(1), Is.EqualTo(1), "O vínculo de conflito aberto deve sobreviver à falha injetada da aplicação.");
        }
        finally
        {
            await DropTriggerAsync(connection, trigger);
            if (caseId != Guid.Empty)
            {
                try
                {
                    await using var cleanup = connection.CreateCommand();
                    cleanup.CommandType = CommandType.StoredProcedure; cleanup.CommandText = "identidade.sp_aplicar_caso_conflito_identidade";
                    cleanup.Parameters.AddWithValue("@gestor_codigo", "SMADS"); cleanup.Parameters.AddWithValue("@caso_id", caseId);
                    cleanup.Parameters.AddWithValue("@grupos_json", groups); cleanup.Parameters.AddWithValue("@correlation_id", Guid.NewGuid());
                    await cleanup.ExecuteNonQueryAsync();
                }
                catch { /* best-effort cleanup; the failing assertion remains authoritative */ }
            }
        }
    }

    [Test]
    public async Task Cpf_identity_correction_is_atomic_when_called_directly_without_outer_transaction()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        const string cpf = "11144477735";
        long mapId;
        string originalState;
        string? originalReason;
        DateTimeOffset? originalStateAt;
        await using (var map = connection.CreateCommand())
        {
            map.CommandText = "SELECT TOP(1) identity_map_id,estado,estado_motivo,estado_em FROM identidade.identity_map WHERE tipo='CPF' AND identificador=@cpf AND vigencia_fim IS NULL;";
            map.Parameters.AddWithValue("@cpf", cpf);
            await using var reader = await map.ExecuteReaderAsync(); Assert.That(await reader.ReadAsync(), Is.True);
            mapId = reader.GetInt64(0); originalState = reader.GetString(1);
            originalReason = reader.IsDBNull(2) ? null : reader.GetString(2);
            originalStateAt = reader.IsDBNull(3) ? null : reader.GetDateTimeOffset(3);
        }
        await using (var conflict = connection.CreateCommand())
        {
            conflict.CommandText = "UPDATE identidade.identity_map SET estado='EM_CONFLITO',estado_motivo='TESTE_ATOMICIDADE',estado_em=SYSDATETIMEOFFSET() WHERE identity_map_id=@id;";
            conflict.Parameters.AddWithValue("@id", mapId); await conflict.ExecuteNonQueryAsync();
        }

        var observations = new List<long>();
        await using (var obs = connection.CreateCommand())
        {
            obs.CommandText = "SELECT pessoa_observacao_id FROM silver.pessoa_observacao WHERE cpf=@cpf ORDER BY pessoa_observacao_id;";
            obs.Parameters.AddWithValue("@cpf", cpf);
            await using var reader = await obs.ExecuteReaderAsync(); while (await reader.ReadAsync()) observations.Add(reader.GetInt64(0));
        }
        Assert.That(observations, Is.Not.Empty);
        var peopleBefore = Convert.ToInt32(await ExecuteScalarAsync(connection, "SELECT COUNT(*) FROM identidade.pessoa;"), System.Globalization.CultureInfo.InvariantCulture);
        var groups = JsonSerializer.Serialize(new[] { new { grupoCodigo = "NOVO", pessoaUuidDestino = (Guid?)null, pessoaObservacaoIds = observations.ToArray() } });
        const string trigger = "identidade.tr_test_atomic_cpf_correction";
        const string ato = "TESTE-ATOMICIDADE-CPF-V365";
        try
        {
            await ExecuteNonQueryAsync(connection, $"""
                CREATE OR ALTER TRIGGER {trigger} ON identidade.correcao_identidade_item AFTER INSERT AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted i JOIN identidade.correcao_identidade c ON c.correcao_id=i.correcao_id WHERE c.ato_referencia=N'{ato}')
                    THROW 51982,'Falha injetada para provar rollback de correção CPF.',1;
                END;
                """);

            await using var apply = connection.CreateCommand();
            apply.CommandType = CommandType.StoredProcedure; apply.CommandText = "identidade.sp_aplicar_correcao_identidade";
            apply.Parameters.AddWithValue("@gestor_codigo", "SMADS"); apply.Parameters.AddWithValue("@cpf", cpf);
            apply.Parameters.AddWithValue("@grupo_titular", "NOVO"); apply.Parameters.AddWithValue("@grupos_json", groups);
            apply.Parameters.AddWithValue("@ato_referencia", ato); apply.Parameters.AddWithValue("@justificativa", "Prova de atomicidade sem transação externa.");
            apply.Parameters.AddWithValue("@correlation_id", Guid.NewGuid());
            var correction = apply.Parameters.Add("@correcao_id", SqlDbType.UniqueIdentifier); correction.Direction = ParameterDirection.Output;
            var titular = apply.Parameters.Add("@pessoa_uuid_titular", SqlDbType.UniqueIdentifier); titular.Direction = ParameterDirection.Output;
            var ex = Assert.ThrowsAsync<SqlException>(async () => await apply.ExecuteNonQueryAsync());
            Assert.That(ex!.Number, Is.EqualTo(51982));

            var peopleAfter = Convert.ToInt32(await ExecuteScalarAsync(connection, "SELECT COUNT(*) FROM identidade.pessoa;"), System.Globalization.CultureInfo.InvariantCulture);
            await using var verify = connection.CreateCommand();
            verify.CommandText = "SELECT (SELECT COUNT(*) FROM identidade.correcao_identidade WHERE ato_referencia=@ato),(SELECT COUNT(*) FROM identidade.identity_map WHERE identity_map_id=@map AND estado='EM_CONFLITO');";
            verify.Parameters.AddWithValue("@ato", ato); verify.Parameters.AddWithValue("@map", mapId);
            await using var reader = await verify.ExecuteReaderAsync(); Assert.That(await reader.ReadAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(peopleAfter, Is.EqualTo(peopleBefore), "UUID novo criado antes da falha deve ser revertido.");
                Assert.That(reader.GetInt32(0), Is.EqualTo(0), "Cabeçalho da correção deve ser revertido.");
                Assert.That(reader.GetInt32(1), Is.EqualTo(1), "Mapa CPF original deve permanecer no estado de entrada da procedure.");
            });
        }
        finally
        {
            await DropTriggerAsync(connection, trigger);
            await using var restore = connection.CreateCommand();
            restore.CommandText = "UPDATE identidade.identity_map SET estado=@estado,estado_motivo=@motivo,estado_em=@em WHERE identity_map_id=@id;";
            restore.Parameters.AddWithValue("@estado", originalState);
            restore.Parameters.AddWithValue("@motivo", (object?)originalReason ?? DBNull.Value);
            restore.Parameters.AddWithValue("@em", (object?)originalStateAt ?? DBNull.Value);
            restore.Parameters.AddWithValue("@id", mapId);
            await restore.ExecuteNonQueryAsync();
        }
    }

    [Test]
    public async Task Recompose_gold_person_executes_source_and_no_source_paths()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var existing = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1");
            await using (var recompute = connection.CreateCommand())
            {
                recompute.Transaction = tx;
                recompute.CommandType = CommandType.StoredProcedure;
                recompute.CommandText = "identidade.sp_recompor_gold_pessoa";
                recompute.Parameters.AddWithValue("@pessoa_uuid", existing);
                await recompute.ExecuteNonQueryAsync();
            }

            await using (var verifyExisting = connection.CreateCommand())
            {
                verifyExisting.Transaction = tx;
                verifyExisting.CommandText = "SELECT COUNT(*) FROM gold.pessoa WHERE pessoa_uuid=@uuid;";
                verifyExisting.Parameters.AddWithValue("@uuid", existing);
                Assert.That(Convert.ToInt32(await verifyExisting.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(1));
            }

            var noSource = Guid.Parse("f3670000-0000-4000-8000-000000000002");
            await using (var fixture = connection.CreateCommand())
            {
                fixture.Transaction = tx;
                fixture.CommandText = """
                    IF NOT EXISTS(SELECT 1 FROM identidade.pessoa WHERE pessoa_uuid=@uuid)
                      INSERT identidade.pessoa(pessoa_uuid,status) VALUES(@uuid,'ATIVO');
                    DELETE FROM gold.pessoa WHERE pessoa_uuid=@uuid;
                    INSERT gold.pessoa(pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia,atualizado_em)
                    VALUES(@uuid,NULL,'SEM_CPF',N'Fixture obsoleta','2000-01-01',N'Fixture',1,'BASELINE_FONTE_UNICA',SYSDATETIMEOFFSET());
                    """;
                fixture.Parameters.AddWithValue("@uuid", noSource);
                await fixture.ExecuteNonQueryAsync();
            }

            await using (var recompute = connection.CreateCommand())
            {
                recompute.Transaction = tx;
                recompute.CommandType = CommandType.StoredProcedure;
                recompute.CommandText = "identidade.sp_recompor_gold_pessoa";
                recompute.Parameters.AddWithValue("@pessoa_uuid", noSource);
                await recompute.ExecuteNonQueryAsync();
            }

            await using var verifyNoSource = connection.CreateCommand();
            verifyNoSource.Transaction = tx;
            verifyNoSource.CommandText = "SELECT COUNT(*) FROM gold.pessoa WHERE pessoa_uuid=@uuid;";
            verifyNoSource.Parameters.AddWithValue("@uuid", noSource);
            Assert.That(Convert.ToInt32(await verifyNoSource.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(0),
                "Pessoa sem vínculo corrente não pode conservar Gold obsoleta e o caminho pós-MERGE deve executar sem referência a CTE fora de escopo.");
        }
        finally { await tx.RollbackAsync(); }
    }

    [Test]
    public async Task Divergence_disposition_is_scoped_to_the_responsible_gestor()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            long id;
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = """
                    DECLARE @g BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo='SMADS');
                    INSERT qualidade.divergencia_gestor(gestor_id,tipo,motivo,codigo_pessoa_origem,status)
                    VALUES(@g,'DIVERGENCIA_IDENTIDADE','TESTE_V346','TESTE-ORIGEM','ABERTA');
                    SELECT CONVERT(bigint,SCOPE_IDENTITY());
                    """;
                id = Convert.ToInt64(await insert.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
            }

            await using (var wrong = connection.CreateCommand())
            {
                wrong.Transaction = tx; wrong.CommandType = CommandType.StoredProcedure; wrong.CommandText = "qualidade.sp_registrar_desfecho_divergencia";
                wrong.Parameters.AddWithValue("@gestor_codigo", "SEHAB"); wrong.Parameters.AddWithValue("@divergencia_id", id);
                wrong.Parameters.AddWithValue("@status", "RESOLVIDA"); wrong.Parameters.AddWithValue("@desfecho", "NAO_DEVERIA");
                wrong.Parameters.AddWithValue("@observacao", DBNull.Value); wrong.Parameters.AddWithValue("@correlation_id", DBNull.Value);
                var ex = Assert.ThrowsAsync<SqlException>(async () => await wrong.ExecuteNonQueryAsync());
                Assert.That(ex!.Number, Is.EqualTo(51121));
            }

            await using (var right = connection.CreateCommand())
            {
                right.Transaction = tx; right.CommandType = CommandType.StoredProcedure; right.CommandText = "qualidade.sp_registrar_desfecho_divergencia";
                right.Parameters.AddWithValue("@gestor_codigo", "SMADS"); right.Parameters.AddWithValue("@divergencia_id", id);
                right.Parameters.AddWithValue("@status", "RESOLVIDA"); right.Parameters.AddWithValue("@desfecho", "CADASTRO_CORRIGIDO");
                right.Parameters.AddWithValue("@observacao", "Teste de escopo institucional."); right.Parameters.AddWithValue("@correlation_id", Guid.NewGuid());
                await right.ExecuteNonQueryAsync();
            }

            await using var verify = connection.CreateCommand(); verify.Transaction = tx;
            verify.CommandText = "SELECT status,desfecho,encerrada_em FROM qualidade.divergencia_gestor WHERE divergencia_id=@id;";
            verify.Parameters.AddWithValue("@id", id);
            await using var reader = await verify.ExecuteReaderAsync(); Assert.That(await reader.ReadAsync(), Is.True);
            Assert.That(reader.GetString(0), Is.EqualTo("RESOLVIDA"));
            Assert.That(reader.GetString(1), Is.EqualTo("CADASTRO_CORRIGIDO"));
            Assert.That(reader.IsDBNull(2), Is.False);
        }
        finally { await tx.RollbackAsync(); }
    }

    private static async Task ExecuteNonQueryAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ExecuteScalarAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static async Task DropTriggerAsync(SqlConnection connection, string twoPartName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"IF OBJECT_ID(N'{twoPartName}',N'TR') IS NOT NULL DROP TRIGGER {twoPartName};";
        await command.ExecuteNonQueryAsync();
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION.");
        var csb = new SqlConnectionStringBuilder(connectionString!);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase) && !db.Contains("dev", StringComparison.OrdinalIgnoreCase) && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Banco de integração deve conter Test, Dev ou Local.");
        return connectionString!;
    }
}

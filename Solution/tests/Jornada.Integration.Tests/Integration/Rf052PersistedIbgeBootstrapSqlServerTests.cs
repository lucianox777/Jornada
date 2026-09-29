using Jornada.Linkage.Parameters.Worker;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration"), NonParallelizable]
public sealed class Rf052PersistedIbgeBootstrapSqlServerTests
{
    [Test]
    public async Task Persisted_bootstrap_remains_usable_after_source_is_no_longer_active()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            var sourceId = await InsertCanonicalSourceAsync(connection, transaction);
            await InsertReadyDerivedAsync(connection, transaction, sourceId, "TODOS", 20260917);
            await InsertReadyDerivedAsync(connection, transaction, sourceId, "FEMININO", 20260918);

            await using (var obsolete = new SqlCommand(
                "UPDATE ref.frequencia_nome_versao SET status=N'OBSOLETA' WHERE frequencia_nome_versao_id=@id;",
                connection, transaction))
            {
                obsolete.Parameters.AddWithValue("@id", sourceId);
                Assert.That(await obsolete.ExecuteNonQueryAsync(), Is.EqualTo(1));
            }

            var persisted = await PersistedIbgeBootstrapReferenceQuery.RequireAsync(
                connection, 20260917, 10_000, CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(persisted.Id, Is.EqualTo(sourceId));
                Assert.That(persisted.Code, Is.EqualTo(PersistedIbgeBootstrapReferenceQuery.CanonicalReferenceCode));
            });

            var modelId = Guid.NewGuid();
            await using (var insertModel = new SqlCommand(
                """
                DECLARE @versao INT=(SELECT ISNULL(MAX(versao),0)+1 FROM identidade.modelo_linkage);
                INSERT identidade.modelo_linkage(
                    modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
                    deduplicacao_metodo,base_referencia,snapshot_referencia,
                    registros_lidos,pessoas_unicas,gerado_em,ativado_em,
                    snapshot_capturado_em,amostra_metodo,amostra_pool_tamanho,
                    amostra_m_tamanho,amostra_u_tamanho,falha_resumo,
                    frequencia_nome_versao_id)
                VALUES(
                    @id,@versao,N'GERANDO',N'FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8',
                    N'IDENTITY_NORMALIZATION_V1',N'GOLD_PESSOA_UUID_PK',N'gold.pessoa',
                    NULL,NULL,NULL,SYSDATETIMEOFFSET(),NULL,NULL,
                    N'TEST_RF052_BOOTSTRAP_PERSISTIDO',1000,NULL,NULL,NULL,@ref);
                """, connection, transaction))
            {
                insertModel.Parameters.AddWithValue("@id", modelId);
                insertModel.Parameters.AddWithValue("@ref", sourceId);
                Assert.That(await insertModel.ExecuteNonQueryAsync(), Is.EqualTo(1));
            }

            await using (var read = new SqlCommand(
                "SELECT frequencia_nome_versao_id FROM identidade.modelo_linkage WHERE modelo_id=@id;",
                connection, transaction))
            {
                read.Parameters.AddWithValue("@id", modelId);
                Assert.That(Convert.ToInt64(await read.ExecuteScalarAsync()), Is.EqualTo(sourceId));
            }

            var missingReference = Assert.ThrowsAsync<SqlException>(async () =>
            {
                await using var invalid = new SqlCommand(
                    """
                    DECLARE @versao INT=(SELECT ISNULL(MAX(versao),0)+1 FROM identidade.modelo_linkage);
                    INSERT identidade.modelo_linkage(
                        modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
                        deduplicacao_metodo,base_referencia,snapshot_referencia,
                        registros_lidos,pessoas_unicas,gerado_em,ativado_em,
                        snapshot_capturado_em,amostra_metodo,amostra_pool_tamanho,
                        amostra_m_tamanho,amostra_u_tamanho,falha_resumo,
                        frequencia_nome_versao_id)
                    VALUES(
                        NEWID(),@versao,N'GERANDO',N'FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8',
                        N'IDENTITY_NORMALIZATION_V1',N'GOLD_PESSOA_UUID_PK',N'gold.pessoa',
                        NULL,NULL,NULL,SYSDATETIMEOFFSET(),NULL,NULL,
                        N'TEST_RF052_SEM_BOOTSTRAP',1000,NULL,NULL,NULL,NULL);
                    """, connection, transaction);
                await invalid.ExecuteNonQueryAsync();
            });
            Assert.That(missingReference!.Number, Is.EqualTo(51639));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Test]
    public async Task Persisted_bootstrap_query_fails_when_first_bootstrap_does_not_exist()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using var cleanup = new SqlCommand(
                """
                DELETE e FROM ref.ibge_u_referencia_estado e
                JOIN ref.ibge_u_referencia u ON u.ibge_u_referencia_id=e.ibge_u_referencia_id
                JOIN ref.frequencia_nome_versao v ON v.frequencia_nome_versao_id=u.frequencia_nome_versao_id
                WHERE v.codigo=@codigo AND u.status<>N'PRONTA';
                """, connection, transaction);
            cleanup.Parameters.AddWithValue("@codigo", PersistedIbgeBootstrapReferenceQuery.CanonicalReferenceCode);
            await cleanup.ExecuteNonQueryAsync();

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await PersistedIbgeBootstrapReferenceQuery.RequireAsync(
                    connection, 20269999, 10_000, CancellationToken.None));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task<long> InsertCanonicalSourceAsync(
        SqlConnection connection, SqlTransaction transaction)
    {
        await using var command = new SqlCommand(
            """
            INSERT ref.frequencia_nome_versao(
                codigo,fonte,edicao,data_referencia,publicado_em,status)
            VALUES(
                @codigo,N'IBGE - TESTE RF052',N'TESTE','2022-08-01','2025-11-04',N'CARREGANDO');
            DECLARE @id BIGINT=CONVERT(BIGINT,SCOPE_IDENTITY());

            INSERT ref.frequencia_nome(
                frequencia_nome_versao_id,tipo,valor,valor_normalizado,sexo,
                periodo_nascimento,escopo_geografico,uf_codigo,municipio_codigo,frequencia)
            VALUES
                (@id,N'NOME',N'MARIA',N'MARIA',N'TODOS',N'TODOS',N'BRASIL','00','0000000',1000),
                (@id,N'NOME',N'MARIA',N'MARIA',N'FEMININO',N'TODOS',N'BRASIL','00','0000000',900),
                (@id,N'SOBRENOME',N'SILVA',N'SILVA',N'TODOS',N'TODOS',N'BRASIL','00','0000000',1000);

            EXEC ref.sp_publicar_frequencia_nome_versao
                @frequencia_nome_versao_id=@id,
                @conteudo_sha256=0x1111111111111111111111111111111111111111111111111111111111111111;
            SELECT @id;
            """, connection, transaction);
        command.Parameters.AddWithValue(
            "@codigo", PersistedIbgeBootstrapReferenceQuery.CanonicalReferenceCode);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task InsertReadyDerivedAsync(
        SqlConnection connection, SqlTransaction transaction,
        long sourceId, string slice, int seed)
    {
        await using var command = new SqlCommand(
            """
            INSERT ref.ibge_u_referencia(
                frequencia_nome_versao_id,conteudo_origem_sha256,metodo_versao,
                construcao_versao,canal_versao,comparador_versao,recorte_prenome,
                seed,pares,vocabulario_prenomes,vocabulario_sobrenomes,
                ocorrencias_prenomes,ocorrencias_sobrenomes,
                colisoes_prenome,colisoes_sobrenome,colisoes_nome_completo,status)
            VALUES(
                @ref,0x1111111111111111111111111111111111111111111111111111111111111111,
                N'IBGE_NOMINAL_U_BOOTSTRAP_V1',
                N'INDEPENDENT_FIRST_NAME_SURNAME_MARGINALS_V1',
                N'CLEAN_PUBLISHED_REFERENCE_NO_ERROR_CHANNEL_V1',
                N'WholeNameJaroWinklerV1',@slice,@seed,10000,1,1,1000,1000,
                0.1,0.1,0.01,N'CARREGANDO');
            DECLARE @id BIGINT=CONVERT(BIGINT,SCOPE_IDENTITY());
            INSERT ref.ibge_u_referencia_estado(
                ibge_u_referencia_id,estado,suporte,probabilidade,erro_padrao)
            VALUES
                (@id,N'EXACT',2500,0.25,0.01),
                (@id,N'HIGH',2500,0.25,0.01),
                (@id,N'MEDIUM',2500,0.25,0.01),
                (@id,N'LOW',2500,0.25,0.01);
            UPDATE ref.ibge_u_referencia
               SET status=N'PRONTA',
                   resultado_sha256=0x2222222222222222222222222222222222222222222222222222222222222222,
                   publicado_em=SYSDATETIMEOFFSET()
             WHERE ibge_u_referencia_id=@id;
            """, connection, transaction);
        command.Parameters.AddWithValue("@ref", sourceId);
        command.Parameters.AddWithValue("@slice", slice);
        command.Parameters.AddWithValue("@seed", seed);
        await command.ExecuteNonQueryAsync();
    }

    private static string RequireIntegrationConnection() =>
        Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION")
        ?? throw new InvalidOperationException("JORNADA_TEST_SQL_CONNECTION não configurada.");
}

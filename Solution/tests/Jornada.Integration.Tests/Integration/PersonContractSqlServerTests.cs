using Jornada.Tests.Integration;
using Microsoft.Data.SqlClient;
using NUnit.Framework;

namespace Jornada.Integration.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class PersonContractSqlServerTests
{
    [Test]
    public async Task Current_person_contract_seed_keeps_only_v1_and_current_semantics()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using var setup = connection.CreateCommand();
            setup.Transaction = tx;
            setup.CommandText = """
                DECLARE @lote UNIQUEIDENTIFIER=(SELECT TOP(1) lote_id FROM ingestao.lote ORDER BY criado_em,lote_id);
                DECLARE @gestor BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo=N'SEHAB');
                IF @lote IS NULL OR @gestor IS NULL THROW 52020,'Fixture DEV insuficiente para contrato Pessoa corrente.',1;

                INSERT silver.pessoa_observacao(
                    pessoa_origem_id,lote_id,id_pessoa_entrega,gestor_id,codigo_pessoa_origem,
                    versao_interna,conteudo_hash,cpf,cpf_ausente_motivo,nome_completo,nome_cmp,
                    data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
                VALUES(
                    NULL,@lote,N'CURRENT-CONTRACT-SQL',@gestor,NULL,
                    1,REPLICATE('a',64),NULL,N'SEM_DOCUMENTACAO_BASE_DECLARADA',
                    N'Pessoa Sintetica',N'PESSOA SINTETICA','1990-01-01',
                    N'Mae Sintetica',N'MAE SINTETICA','2026-10-06T00:00:00-03:00');

                DECLARE @obs BIGINT=SCOPE_IDENTITY();

                INSERT silver.pessoa_identificador_observacao(
                    pessoa_observacao_id,tipo_identificador_codigo,namespace_codigo,
                    valor_original,valor_normalizado,emissor_codigo,uf_emissor,
                    status_validacao,status_evidencia)
                VALUES
                    (@obs,N'RG',N'BR-SP',N'00123456X',N'00123456X',NULL,NULL,N'NAO_VALIDADO',N'DECLARADO'),
                    (@obs,N'CNH',N'BR',N'001.234.567-89',N'00123456789',NULL,NULL,N'NAO_VALIDADO',N'DECLARADO');

                INSERT silver.pessoa_atributo_observacao(
                    source_record_id,pessoa_observacao_id,fonte_gestor_id,atributo_codigo,
                    atributo_instancia_chave,valor,status_evidencia,ingested_at)
                VALUES
                    (N'CURRENT-SEM-FIXO',@obs,@gestor,N'REFERENCIA_TERRITORIAL',N'CURRENT-SEM-FIXO',
                     N'ESTADO=SEM_ENDERECO_FIXO_DECLARADO',N'DECLARADO',SYSDATETIMEOFFSET()),
                    (N'CURRENT-PRISIONAL',@obs,@gestor,N'REFERENCIA_TERRITORIAL',N'CURRENT-PRISIONAL',
                     N'UNIDADE_PRISIONAL_SINTETICA',N'DECLARADO',SYSDATETIMEOFFSET());

                DECLARE @sem BIGINT=(
                    SELECT pessoa_atributo_observacao_id
                    FROM silver.pessoa_atributo_observacao
                    WHERE source_record_id=N'CURRENT-SEM-FIXO');
                DECLARE @pris BIGINT=(
                    SELECT pessoa_atributo_observacao_id
                    FROM silver.pessoa_atributo_observacao
                    WHERE source_record_id=N'CURRENT-PRISIONAL');

                INSERT silver.referencia_territorial_observacao(
                    pessoa_atributo_observacao_id,estado_referencia,natureza_referencia,fonte_semantica,
                    subprefeitura_id,distrito_id,situacao_geografia,origem_geografia,referencia_malha,resolvido_em)
                VALUES
                    (@sem,N'SEM_ENDERECO_FIXO_DECLARADO',NULL,N'REFERENCIA_TERRITORIAL',
                     NULL,NULL,NULL,NULL,NULL,NULL),
                    (@pris,N'INFORMADA',N'INSTITUCIONAL_PRISIONAL',N'REFERENCIA_TERRITORIAL',
                     NULL,NULL,N'NAO_RESOLVIDA_ORIGEM',NULL,NULL,NULL);
                """;
            await setup.ExecuteNonQueryAsync();

            await using var query = connection.CreateCommand();
            query.Transaction = tx;
            query.CommandText = """
                DECLARE @obs BIGINT=(SELECT pessoa_observacao_id FROM silver.pessoa_observacao WHERE id_pessoa_entrega=N'CURRENT-CONTRACT-SQL');

                SELECT
                    (SELECT COUNT(*) FROM ref.gestor_pessoa_versao WHERE versao=1 AND status=N'ATIVA'),
                    (SELECT COUNT(*) FROM ref.gestor_pessoa_versao WHERE versao<>1),
                    (SELECT COUNT(*) FROM silver.pessoa_identificador_observacao
                      WHERE pessoa_observacao_id=@obs AND tipo_identificador_codigo=N'RG'
                        AND emissor_codigo IS NULL AND uf_emissor IS NULL),
                    (SELECT COUNT(*) FROM silver.pessoa_identificador_observacao
                      WHERE pessoa_observacao_id=@obs AND tipo_identificador_codigo=N'CNH'
                        AND valor_normalizado=N'00123456789'),
                    (SELECT COUNT(*) FROM identidade.identity_map
                      WHERE tipo IN(N'RG',N'CNH') AND identificador IN(N'00123456X',N'00123456789')),
                    (SELECT COUNT(*) FROM silver.referencia_territorial_observacao rt
                      JOIN silver.pessoa_atributo_observacao pa
                        ON pa.pessoa_atributo_observacao_id=rt.pessoa_atributo_observacao_id
                      WHERE pa.pessoa_observacao_id=@obs
                        AND rt.estado_referencia=N'SEM_ENDERECO_FIXO_DECLARADO'
                        AND rt.natureza_referencia IS NULL
                        AND rt.situacao_geografia IS NULL),
                    (SELECT COUNT(*) FROM silver.v_pessoa_referencia_territorial
                      WHERE pessoa_observacao_id=@obs
                        AND referencia_territorial_observacao_id IS NOT NULL),
                    (SELECT COUNT(*) FROM serving.v_bi_referencia_territorial_v5
                      WHERE gestor=N'SEHAB' AND natureza_publicavel=N'INSTITUCIONAL_PRISIONAL');
                """;
            await using var reader = await query.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.Multiple((Action)(() =>
            {
                Assert.That(reader.GetInt32(0), Is.EqualTo(4), "Os quatro Gestores DEV devem usar somente Pessoa v1.");
                Assert.That(reader.GetInt32(1), Is.Zero, "O catálogo DEV não deve manter versões Pessoa anteriores.");
                Assert.That(reader.GetInt32(2), Is.EqualTo(1), "RG parcial deve persistir sem emissor/UF.");
                Assert.That(reader.GetInt32(3), Is.EqualTo(1), "CNH deve persistir como identificador secundário.");
                Assert.That(reader.GetInt32(4), Is.Zero, "RG/CNH não podem criar identity_map automaticamente.");
                Assert.That(reader.GetInt32(5), Is.EqualTo(1), "Sem endereço fixo é estado próprio, sem geografia fabricada.");
                Assert.That(reader.GetInt32(6), Is.Zero, "Referência prisional não pode aparecer na visão territorial compartilhada.");
                Assert.That(reader.GetInt32(7), Is.Zero, "A projeção BI compartilhada não pode revelar natureza prisional.");
            }));
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    [Test]
    public async Task Dev_console_zip_catalog_returns_only_current_person_contract()
    {
        var connectionString=RequireIntegrationConnection();
        await using var connection=new SqlConnection(connectionString);
        await connection.OpenAsync();

        var databaseDir=Path.Combine(AppContext.BaseDirectory,"database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection,databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(connection,Path.Combine(databaseDir,"Jornada_Seed_Dev.sql"));

        await using var command=connection.CreateCommand();
        command.CommandText="""
            SELECT COUNT_BIG(*),MIN(gpv.versao),MAX(gpv.versao)
            FROM ref.gestor g
            JOIN ref.sistema_origem so ON so.gestor_id=g.gestor_id AND so.ativo=1
            JOIN ref.gestor_pessoa_versao gpv
              ON gpv.gestor_id=g.gestor_id
             AND gpv.versao=1
             AND gpv.status=N'ATIVA'
            JOIN ref.tipo_registro tr ON tr.gestor_id=g.gestor_id AND tr.ativo=1
            JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_id=tr.tipo_registro_id AND trv.status IN(N'ATIVA',N'ENCERRADA')
            WHERE g.ativo=1;
            """;

        await using var reader=await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(),Is.True);
        Assert.Multiple((Action)(()=>
        {
            Assert.That(reader.GetInt64(0),Is.GreaterThan(0),"A Console deve encontrar contratos correntes no catálogo ref.*.");
            Assert.That(reader.GetInt32(1),Is.EqualTo(1));
            Assert.That(reader.GetInt32(2),Is.EqualTo(1));
        }));
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");

        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog ?? string.Empty;
        // Explicit CI-only exception: disposable SQL on loopback, never external or Fabric.
        var isEphemeralGithubE2E =
            string.Equals(database, "JornadaE2E", StringComparison.Ordinal)
            && string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_TARGET") ?? "SQL_SERVER_2022", "SQL_SERVER_2022", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(builder.DataSource, "localhost,1433", StringComparison.OrdinalIgnoreCase)
                || string.Equals(builder.DataSource, "127.0.0.1,1433", StringComparison.Ordinal)
                || string.Equals(builder.DataSource, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(builder.DataSource, "127.0.0.1", StringComparison.Ordinal));
        if (!isEphemeralGithubE2E
            && !database.Contains("test", StringComparison.OrdinalIgnoreCase)
            && !database.Contains("dev", StringComparison.OrdinalIgnoreCase)
            && !database.Contains("local", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail("Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.");
        }

        return connectionString!;
    }
}

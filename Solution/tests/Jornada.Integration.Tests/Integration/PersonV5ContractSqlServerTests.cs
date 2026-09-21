using Microsoft.Data.SqlClient;
using NUnit.Framework;

namespace Jornada.Integration.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class PersonV5ContractSqlServerTests
{
    [Test]
    public async Task Pessoa_v5_structural_contract_is_additive_secondary_and_fail_closed()
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
                IF @lote IS NULL OR @gestor IS NULL THROW 52020,'Fixture DEV insuficiente para Pessoa v5.',1;

                INSERT silver.pessoa_observacao(
                    pessoa_origem_id,lote_id,id_pessoa_entrega,gestor_id,codigo_pessoa_origem,
                    versao_interna,conteudo_hash,cpf,cpf_ausente_motivo,nome_completo,nome_cmp,
                    data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
                VALUES(
                    NULL,@lote,N'V5-CONTRACT-SQL',@gestor,NULL,
                    1,REPLICATE('a',64),NULL,N'SEM_DOCUMENTACAO_BASE_DECLARADA',
                    N'Pessoa Sintetica V5',N'PESSOA SINTETICA V5','1990-01-01',
                    N'Mae Sintetica',N'MAE SINTETICA','2026-09-21T00:00:00-03:00');

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
                    (N'V5-SEM-FIXO',@obs,@gestor,N'REFERENCIA_TERRITORIAL',N'V5-SEM-FIXO',
                     N'ESTADO=SEM_ENDERECO_FIXO_DECLARADO',N'DECLARADO',SYSDATETIMEOFFSET()),
                    (N'V5-PRISIONAL',@obs,@gestor,N'REFERENCIA_TERRITORIAL',N'V5-PRISIONAL',
                     N'UNIDADE_PRISIONAL_SINTETICA',N'DECLARADO',SYSDATETIMEOFFSET());

                DECLARE @sem BIGINT=(
                    SELECT pessoa_atributo_observacao_id
                    FROM silver.pessoa_atributo_observacao
                    WHERE source_record_id=N'V5-SEM-FIXO');
                DECLARE @pris BIGINT=(
                    SELECT pessoa_atributo_observacao_id
                    FROM silver.pessoa_atributo_observacao
                    WHERE source_record_id=N'V5-PRISIONAL');

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
                DECLARE @obs BIGINT=(SELECT pessoa_observacao_id FROM silver.pessoa_observacao WHERE id_pessoa_entrega=N'V5-CONTRACT-SQL');

                SELECT
                    (SELECT COUNT(*) FROM sys.columns c
                      WHERE (c.object_id=OBJECT_ID(N'silver.pessoa_observacao') AND c.name=N'cpf_ausente_motivo' AND c.max_length=100)
                         OR (c.object_id=OBJECT_ID(N'gold.beneficio_concedido') AND c.name=N'cpf_ausente_motivo' AND c.max_length=100)
                         OR (c.object_id=OBJECT_ID(N'gold.servico_prestado') AND c.name=N'cpf_ausente_motivo' AND c.max_length=100)
                         OR (c.object_id=OBJECT_ID(N'serving.registro_integrado') AND c.name=N'cpf_ausente_motivo' AND c.max_length=100)
                         OR (c.object_id=OBJECT_ID(N'gold.pessoa') AND c.name=N'status_cpf' AND c.max_length=100)),
                    (SELECT COUNT(*) FROM ref.gestor_pessoa_versao WHERE versao=5 AND status=N'RASCUNHO'),
                    (SELECT COUNT(*) FROM ref.gestor_pessoa_versao WHERE versao=4 AND status=N'ATIVA'),
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
                      WHERE gestor=N'SEHAB' AND natureza_publicavel=N'INSTITUCIONAL_PRISIONAL'),
                    (SELECT COUNT(*) FROM serving.v_bi_cpf_ausencia_taxonomia
                      WHERE gestor=N'SEHAB' AND pessoa_schema_versao=4
                        AND cpf_estado=N'LEGADO_SEM_CPF_NAO_DECOMPOSTO');
                """;
            await using var reader = await query.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reader.GetInt32(0), Is.EqualTo(5), "Todos os campos que propagam a taxonomia CPF devem suportar NVARCHAR(50).");
                Assert.That(reader.GetInt32(1), Is.EqualTo(4), "Os quatro contratos Pessoa v5 devem existir somente como RASCUNHO.");
                Assert.That(reader.GetInt32(2), Is.EqualTo(4), "Pessoa v4 continua ativa até a ativação coordenada do trem 3.71.");
                Assert.That(reader.GetInt32(3), Is.EqualTo(1), "RG parcial deve persistir sem emissor/UF.");
                Assert.That(reader.GetInt32(4), Is.EqualTo(1), "CNH deve persistir como identificador secundário.");
                Assert.That(reader.GetInt32(5), Is.Zero, "RG/CNH não podem criar identity_map automaticamente.");
                Assert.That(reader.GetInt32(6), Is.EqualTo(1), "Sem endereço fixo é estado próprio, sem natureza/geografia fabricada.");
                Assert.That(reader.GetInt32(7), Is.Zero, "Sem endereço fixo e prisão não aparecem como referência territorial compartilhada.");
                Assert.That(reader.GetInt32(8), Is.Zero, "A projeção BI compartilhada não pode revelar nem contar a natureza prisional.");
                Assert.That(reader.GetInt32(9), Is.GreaterThanOrEqualTo(1), "SEM_CPF v4 deve permanecer classificado como legado, sem decomposição inferida.");
            });
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");

        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog ?? string.Empty;
        if (!database.Contains("test", StringComparison.OrdinalIgnoreCase)
            && !database.Contains("dev", StringComparison.OrdinalIgnoreCase)
            && !database.Contains("local", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail("Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.");
        }

        return connectionString!;
    }
}

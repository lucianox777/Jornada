using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class NameFrequencyCoverageSqlServerTests
{
    [Test]
    public async Task Coverage_metadata_preserves_suppression_as_absence_not_zero()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "migrations", "20260912_Frequencia_Nomes_Referencia.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "migrations", "20260912_Frequencia_Nomes_Cobertura.sql"));

        var code = $"TEST-COVERAGE-{Guid.NewGuid():N}";
        long versionId;
        await using (var create = connection.CreateCommand())
        {
            create.CommandText = """
                INSERT ref.frequencia_nome_versao(codigo,fonte,edicao,data_referencia,status)
                VALUES(@codigo,N'IBGE - Censo Demográfico 2022 - Nomes no Brasil',N'Teste','2022-08-01','CARREGANDO');
                DECLARE @id BIGINT=SCOPE_IDENTITY();

                INSERT ref.frequencia_nome(
                    frequencia_nome_versao_id,tipo,valor,valor_normalizado,sexo,periodo_nascimento,
                    escopo_geografico,uf_codigo,municipio_codigo,frequencia)
                VALUES
                    (@id,'NOME',N'Maria',N'MARIA','TODOS','TODOS','BRASIL','00','0000000',1000),
                    (@id,'SOBRENOME',N'Silva',N'SILVA','TODOS','TODOS','BRASIL','00','0000000',400);

                INSERT ref.frequencia_nome_cobertura(
                    frequencia_nome_versao_id,tipo,escopo_geografico,inclui_sexo,
                    inclui_periodo_nascimento,cobertura,ausencia_semantica,origem_endpoint,observacao)
                VALUES
                    (@id,'NOME','BRASIL',0,0,'COMPLETA','NAO_PUBLICADA_OU_SUPRIMIDA',N'/localidade/0/ranking/nome',N'Total Brasil'),
                    (@id,'SOBRENOME','BRASIL',0,0,'COMPLETA','NAO_PUBLICADA_OU_SUPRIMIDA',N'/localidade/0/ranking/sobrenome',N'Total Brasil'),
                    (@id,'NOME','MUNICIPIO',0,0,'PARCIAL','NAO_PUBLICADA_OU_SUPRIMIDA',N'contrato-detalhado',N'Células omitidas não significam zero');

                SELECT @id;
                """;
            create.Parameters.AddWithValue("@codigo", code);
            versionId = Convert.ToInt64(await create.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }

        await using (var verify = connection.CreateCommand())
        {
            verify.CommandText = """
                SELECT cobertura,ausencia_semantica
                FROM ref.frequencia_nome_cobertura
                WHERE frequencia_nome_versao_id=@id AND tipo='NOME' AND escopo_geografico='MUNICIPIO';
                """;
            verify.Parameters.AddWithValue("@id", versionId);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reader.GetString(0), Is.EqualTo("PARCIAL"));
                Assert.That(reader.GetString(1), Is.EqualTo("NAO_PUBLICADA_OU_SUPRIMIDA"));
            });
        }

        await using (var publish = connection.CreateCommand())
        {
            publish.CommandText = "EXEC ref.sp_publicar_frequencia_nome_versao @id,@sha;";
            publish.Parameters.AddWithValue("@id", versionId);
            publish.Parameters.Add("@sha", System.Data.SqlDbType.Binary, 32).Value = Enumerable.Repeat((byte)0x31, 32).ToArray();
            await publish.ExecuteNonQueryAsync();
        }

        await using (var mutate = connection.CreateCommand())
        {
            mutate.CommandText = "UPDATE ref.frequencia_nome_cobertura SET cobertura='COMPLETA' WHERE frequencia_nome_versao_id=@id AND escopo_geografico='MUNICIPIO';";
            mutate.Parameters.AddWithValue("@id", versionId);
            var ex = Assert.ThrowsAsync<SqlException>(async () => await mutate.ExecuteNonQueryAsync());
            Assert.That(ex!.Number, Is.EqualTo(51650));
        }
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");

        var csb = new SqlConnectionStringBuilder(connectionString);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase) &&
            !db.Contains("dev", StringComparison.OrdinalIgnoreCase) &&
            !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.");

        return connectionString!;
    }
}

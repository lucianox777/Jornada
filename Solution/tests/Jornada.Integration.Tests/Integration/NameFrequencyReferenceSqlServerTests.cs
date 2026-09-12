using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class NameFrequencyReferenceSqlServerTests
{
    [Test]
    public async Task Published_reference_is_versioned_immutable_and_replay_addressable()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(
            connection,
            Path.Combine(databaseDir, "migrations", "20260912_Frequencia_Nomes_Referencia.sql"));

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var firstId = await CreateVersionWithMinimumReferenceAsync(connection, $"TEST-NOMES-{suffix}-1", 1000, 400);
        await PublishAsync(connection, firstId, 0x11);

        await using (var verify = connection.CreateCommand())
        {
            verify.CommandText = """
                SELECT
                    (SELECT status FROM ref.frequencia_nome_versao WHERE frequencia_nome_versao_id=@id),
                    (SELECT COUNT(*) FROM ref.v_frequencia_nome_ativa WHERE frequencia_nome_versao_id=@id),
                    CASE WHEN COL_LENGTH('identidade.modelo_linkage','frequencia_nome_versao_id') IS NULL THEN 0 ELSE 1 END,
                    CASE WHEN EXISTS(
                        SELECT 1 FROM sys.foreign_keys
                        WHERE parent_object_id=OBJECT_ID('identidade.modelo_linkage')
                          AND name='fk_modelo_linkage_frequencia_nome_versao') THEN 1 ELSE 0 END;
                """;
            verify.Parameters.AddWithValue("@id", firstId);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reader.GetString(0), Is.EqualTo("ATIVA"));
                Assert.That(reader.GetInt32(1), Is.EqualTo(2));
                Assert.That(reader.GetInt32(2), Is.EqualTo(1));
                Assert.That(reader.GetInt32(3), Is.EqualTo(1));
            });
        }

        await using (var forbiddenMutation = connection.CreateCommand())
        {
            forbiddenMutation.CommandText = """
                UPDATE ref.frequencia_nome
                   SET frequencia=frequencia+1
                 WHERE frequencia_nome_versao_id=@id;
                """;
            forbiddenMutation.Parameters.AddWithValue("@id", firstId);
            var ex = Assert.ThrowsAsync<SqlException>(async () => await forbiddenMutation.ExecuteNonQueryAsync());
            Assert.That(ex!.Number, Is.EqualTo(51630));
        }

        var secondId = await CreateVersionWithMinimumReferenceAsync(connection, $"TEST-NOMES-{suffix}-2", 1100, 450);
        await PublishAsync(connection, secondId, 0x22);

        await using var status = connection.CreateCommand();
        status.CommandText = """
            SELECT
                (SELECT status FROM ref.frequencia_nome_versao WHERE frequencia_nome_versao_id=@first),
                (SELECT status FROM ref.frequencia_nome_versao WHERE frequencia_nome_versao_id=@second),
                (SELECT COUNT(*) FROM ref.frequencia_nome_versao WHERE status='ATIVA');
            """;
        status.Parameters.AddWithValue("@first", firstId);
        status.Parameters.AddWithValue("@second", secondId);
        await using var statusReader = await status.ExecuteReaderAsync();
        Assert.That(await statusReader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(statusReader.GetString(0), Is.EqualTo("OBSOLETA"));
            Assert.That(statusReader.GetString(1), Is.EqualTo("ATIVA"));
            Assert.That(statusReader.GetInt32(2), Is.EqualTo(1));
        });
    }

    private static async Task<long> CreateVersionWithMinimumReferenceAsync(
        SqlConnection connection,
        string code,
        long nameFrequency,
        long surnameFrequency)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT ref.frequencia_nome_versao(
                codigo,fonte,edicao,data_referencia,publicado_em,status)
            VALUES(
                @codigo,N'IBGE - Censo Demográfico 2022 - Nomes no Brasil',N'Nota técnica 01/2025',
                '2022-08-01','2025-11-04','CARREGANDO');
            DECLARE @id BIGINT=SCOPE_IDENTITY();

            INSERT ref.frequencia_nome(
                frequencia_nome_versao_id,tipo,valor,valor_normalizado,sexo,periodo_nascimento,
                escopo_geografico,uf_codigo,municipio_codigo,frequencia)
            VALUES
                (@id,'NOME',N'Maria',N'MARIA','TODOS','TODOS','BRASIL','00','0000000',@freq_nome),
                (@id,'SOBRENOME',N'Silva',N'SILVA','TODOS','TODOS','BRASIL','00','0000000',@freq_sobrenome);

            SELECT @id;
            """;
        command.Parameters.AddWithValue("@codigo", code);
        command.Parameters.AddWithValue("@freq_nome", nameFrequency);
        command.Parameters.AddWithValue("@freq_sobrenome", surnameFrequency);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task PublishAsync(SqlConnection connection, long versionId, byte hashByte)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC ref.sp_publicar_frequencia_nome_versao @id,@sha;";
        command.Parameters.AddWithValue("@id", versionId);
        command.Parameters.Add("@sha", System.Data.SqlDbType.Binary, 32).Value = Enumerable.Repeat(hashByte, 32).ToArray();
        await command.ExecuteNonQueryAsync();
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

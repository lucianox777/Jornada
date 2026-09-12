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
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "migrations", "20260912_Frequencia_Nomes_Referencia.sql"));

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
                    CASE WHEN EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('identidade.modelo_linkage') AND name='fk_modelo_linkage_frequencia_nome_versao') THEN 1 ELSE 0 END;
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
            forbiddenMutation.CommandText = "UPDATE ref.frequencia_nome SET frequencia=frequencia+1 WHERE frequencia_nome_versao_id=@id;";
            forbiddenMutation.Parameters.AddWithValue("@id", firstId);
            var ex = Assert.ThrowsAsync<SqlException>(async () => await forbiddenMutation.ExecuteNonQueryAsync());
            Assert.That(ex!.Number, Is.EqualTo(51630));
        }

        var secondId = await CreateVersionWithMinimumReferenceAsync(connection, $"TEST-NOMES-{suffix}-2", 1100, 450);
        await PublishAsync(connection, secondId, 0x22);

        var modelId = Guid.NewGuid();
        await InsertGeneratingModelAsync(connection, modelId, suffix);
        Assert.That(await ReadModelReferenceAsync(connection, modelId), Is.EqualTo(secondId),
            "Novo modelo GERANDO deve capturar atomicamente a referência ATIVA.");

        var thirdId = await CreateVersionWithMinimumReferenceAsync(connection, $"TEST-NOMES-{suffix}-3", 1200, 500);
        await PublishAsync(connection, thirdId, 0x33);

        Assert.That(await ReadModelReferenceAsync(connection, modelId), Is.EqualTo(secondId),
            "Ativar referência nova não pode alterar a referência já fixada no modelo histórico.");

        var staleReferenceModel = Guid.NewGuid();
        var staleReference = Assert.ThrowsAsync<SqlException>(async () =>
            await InsertGeneratingModelAsync(connection, staleReferenceModel, suffix + "-STALE", secondId));
        Assert.That(staleReference!.Number, Is.EqualTo(51641),
            "Novo modelo GERANDO não pode contornar a captura da referência ATIVA informando versão obsoleta.");

        await using (var forbiddenModelMutation = connection.CreateCommand())
        {
            forbiddenModelMutation.CommandText = "UPDATE identidade.modelo_linkage SET frequencia_nome_versao_id=@third WHERE modelo_id=@model;";
            forbiddenModelMutation.Parameters.AddWithValue("@third", thirdId);
            forbiddenModelMutation.Parameters.AddWithValue("@model", modelId);
            var ex = Assert.ThrowsAsync<SqlException>(async () => await forbiddenModelMutation.ExecuteNonQueryAsync());
            Assert.That(ex!.Number, Is.EqualTo(51640));
        }

        await using (var status = connection.CreateCommand())
        {
            status.CommandText = """
                SELECT
                    (SELECT status FROM ref.frequencia_nome_versao WHERE frequencia_nome_versao_id=@first),
                    (SELECT status FROM ref.frequencia_nome_versao WHERE frequencia_nome_versao_id=@second),
                    (SELECT status FROM ref.frequencia_nome_versao WHERE frequencia_nome_versao_id=@third),
                    (SELECT COUNT(*) FROM ref.frequencia_nome_versao WHERE status='ATIVA');
                """;
            status.Parameters.AddWithValue("@first", firstId);
            status.Parameters.AddWithValue("@second", secondId);
            status.Parameters.AddWithValue("@third", thirdId);
            await using var reader = await status.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reader.GetString(0), Is.EqualTo("OBSOLETA"));
                Assert.That(reader.GetString(1), Is.EqualTo("OBSOLETA"));
                Assert.That(reader.GetString(2), Is.EqualTo("ATIVA"));
                Assert.That(reader.GetInt32(3), Is.EqualTo(1));
            });
        }

        await using (var removeActive = connection.CreateCommand())
        {
            removeActive.CommandText = "UPDATE ref.frequencia_nome_versao SET status='OBSOLETA' WHERE status='ATIVA';";
            await removeActive.ExecuteNonQueryAsync();
        }

        var noReferenceModel = Guid.NewGuid();
        var failClosed = Assert.ThrowsAsync<SqlException>(async () =>
            await InsertGeneratingModelAsync(connection, noReferenceModel, suffix + "-NOREF"));
        Assert.That(failClosed!.Number, Is.EqualTo(51639),
            "Calibrador deve falhar fechado quando não existe referência ATIVA.");
    }

    private static async Task InsertGeneratingModelAsync(SqlConnection connection, Guid modelId, string suffix, long? explicitReferenceId = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @versao INT=(SELECT ISNULL(MAX(versao),0)+1 FROM identidade.modelo_linkage);
            INSERT identidade.modelo_linkage(
                modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
                deduplicacao_metodo,base_referencia,snapshot_referencia,
                registros_lidos,pessoas_unicas,gerado_em,ativado_em,
                snapshot_capturado_em,amostra_metodo,amostra_pool_tamanho,
                amostra_m_tamanho,amostra_u_tamanho,falha_resumo,
                frequencia_nome_versao_id)
            VALUES(
                @modelo,@versao,'GERANDO','TEST_ALGORITHM','TEST_NORMALIZATION',
                'GOLD_PESSOA_UUID_PK','gold.pessoa',NULL,NULL,NULL,SYSDATETIMEOFFSET(),NULL,
                NULL,@amostra,1000,NULL,NULL,NULL,@referencia);
            """;
        command.Parameters.AddWithValue("@modelo", modelId);
        command.Parameters.AddWithValue("@amostra", "TEST_REFERENCE_" + suffix);
        command.Parameters.AddWithValue("@referencia", explicitReferenceId is null ? DBNull.Value : explicitReferenceId.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long?> ReadModelReferenceAsync(SqlConnection connection, Guid modelId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT frequencia_nome_versao_id FROM identidade.modelo_linkage WHERE modelo_id=@modelo;";
        command.Parameters.AddWithValue("@modelo", modelId);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<long> CreateVersionWithMinimumReferenceAsync(SqlConnection connection,string code,long nameFrequency,long surnameFrequency)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT ref.frequencia_nome_versao(codigo,fonte,edicao,data_referencia,publicado_em,status)
            VALUES(@codigo,N'IBGE - Censo Demográfico 2022 - Nomes no Brasil',N'Nota técnica 01/2025','2022-08-01','2025-11-04','CARREGANDO');
            DECLARE @id BIGINT=SCOPE_IDENTITY();
            INSERT ref.frequencia_nome(frequencia_nome_versao_id,tipo,valor,valor_normalizado,sexo,periodo_nascimento,escopo_geografico,uf_codigo,municipio_codigo,frequencia)
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

    private static async Task PublishAsync(SqlConnection connection,long versionId,byte hashByte)
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
        if (string.IsNullOrWhiteSpace(connectionString)) Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");
        var csb = new SqlConnectionStringBuilder(connectionString);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase) && !db.Contains("dev", StringComparison.OrdinalIgnoreCase) && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.");
        return connectionString!;
    }
}

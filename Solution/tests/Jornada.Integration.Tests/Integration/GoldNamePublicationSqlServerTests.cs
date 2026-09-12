using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class GoldNamePublicationSqlServerTests
{
    [Test]
    public async Task Gold_key_uses_canonical_silver_normalization_and_survives_recomposition()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "migrations", "20260912_Gold_Nome_Publicacao.sql"));

        var sample = await ReadSampleAsync(connection);
        Assert.That(sample, Is.Not.Null, "Seed deve conter ao menos uma Pessoa Gold sustentada por observação Silver resolvida.");

        var projected = IbgeNamePublicationSemantics.ProjectFirstName(sample!.NomeCompleto);
        Assert.That(projected, Is.Not.Null);
        var silverFirstToken = sample.NomeCmp.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        Assert.Multiple(() =>
        {
            Assert.That(sample.NomePublicacaoNormalizado, Is.EqualTo(projected!.FirstNameNormalized));
            Assert.That(sample.NomePublicacaoNormalizado, Is.EqualTo(silverFirstToken),
                "Gold deve reutilizar nome_cmp canônico, sem reimplementar normalização em T-SQL.");
            Assert.That(sample.MetodoVersao, Is.EqualTo(IbgeNamePublicationSemantics.MethodVersion));
            Assert.That(sample.NormalizacaoVersao, Is.EqualTo(IdentityComparison.NormalizationVersion));
        });

        await using (var drift = connection.CreateCommand())
        {
            drift.CommandText = """
                UPDATE gold.pessoa
                   SET nome_publicacao_normalizado=N'DRIFT_INDEVIDO',
                       nome_publicacao_metodo_versao=N'IBGE_CENSO_2022_NOMES_PUBLICACAO_V1',
                       nome_publicacao_normalizacao_versao=N'IDENTITY_NORMALIZATION_V1'
                 WHERE pessoa_uuid=@uuid;
                """;
            drift.Parameters.AddWithValue("@uuid", sample.PessoaUuid);
            await drift.ExecuteNonQueryAsync();
        }
        Assert.That((await ReadGoldKeyAsync(connection, sample.PessoaUuid)).Key, Is.EqualTo(projected!.FirstNameNormalized),
            "Trigger deve recompor a chave a partir da evidência Silver, impedindo drift manual.");

        await using (var recompose = connection.CreateCommand())
        {
            recompose.CommandText = "EXEC identidade.sp_recompor_gold_pessoa @pessoa_uuid=@uuid;";
            recompose.Parameters.AddWithValue("@uuid", sample.PessoaUuid);
            await recompose.ExecuteNonQueryAsync();
        }

        var afterRecomposition = await ReadGoldKeyAsync(connection, sample.PessoaUuid);
        Assert.Multiple(() =>
        {
            Assert.That(afterRecomposition.Key, Is.EqualTo(projected.FirstNameNormalized));
            Assert.That(afterRecomposition.MethodVersion, Is.EqualTo(IbgeNamePublicationSemantics.MethodVersion));
            Assert.That(afterRecomposition.NormalizationVersion, Is.EqualTo(IdentityComparison.NormalizationVersion));
        });
    }

    private static async Task<Sample?> ReadSampleAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP(1)
                   g.pessoa_uuid,g.nome_completo,g.nome_publicacao_normalizado,
                   g.nome_publicacao_metodo_versao,g.nome_publicacao_normalizacao_versao,src.nome_cmp
            FROM gold.pessoa g
            CROSS APPLY(
                SELECT TOP(1) po.nome_cmp
                FROM silver.pessoa_observacao po
                JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
                WHERE vc.pessoa_uuid=g.pessoa_uuid
                  AND vc.status='RESOLVIDO'
                  AND po.nome_completo=g.nome_completo
                ORDER BY po.source_as_of DESC,po.pessoa_observacao_id DESC
            ) src
            WHERE g.nome_publicacao_normalizado IS NOT NULL
            ORDER BY g.pessoa_uuid;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new Sample(
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5));
    }

    private static async Task<(string? Key, string? MethodVersion, string? NormalizationVersion)> ReadGoldKeyAsync(
        SqlConnection connection, Guid pessoaUuid)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT nome_publicacao_normalizado,nome_publicacao_metodo_versao,nome_publicacao_normalizacao_versao
            FROM gold.pessoa WHERE pessoa_uuid=@uuid;
            """;
        command.Parameters.AddWithValue("@uuid", pessoaUuid);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        return (
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");
        var csb = new SqlConnectionStringBuilder(connectionString);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("dev", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.");
        return connectionString!;
    }

    private sealed record Sample(
        Guid PessoaUuid,
        string NomeCompleto,
        string NomePublicacaoNormalizado,
        string MetodoVersao,
        string NormalizacaoVersao,
        string NomeCmp);
}

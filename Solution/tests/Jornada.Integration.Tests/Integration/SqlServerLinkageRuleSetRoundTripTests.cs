using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class SqlServerLinkageRuleSetRoundTripTests
{
    [Test]
    public async Task Model_and_dynamic_ruleset_round_trip_with_same_fingerprint()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
        await EnsureActiveFrequencyReferenceAsync(connection);

        var modelId = Guid.NewGuid();
        const string algorithm = "TEST_SQLSERVER_DYNAMIC_BLOCKING_V1";
        var parameters = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["CONFLICT_MARGIN"] = 0.03m,
            ["T_LINKAGE"] = 0.95m
        };
        var expected = LinkageDynamicRuleSet.CreateWithPasses(
            "TEST_SQLSERVER_RULESET_V1",
            algorithm,
            new[] { LinkageBlockingPass.Create("P001", new[] { "birth_year" }) },
            parameters);

        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
        {
            await using (var model = new SqlCommand(
                """
                DECLARE @versao INT=(SELECT ISNULL(MAX(versao),0)+1 FROM identidade.modelo_linkage WITH (UPDLOCK,HOLDLOCK));
                INSERT identidade.modelo_linkage(
                    modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
                    deduplicacao_metodo,base_referencia,snapshot_referencia,
                    registros_lidos,pessoas_unicas,gerado_em,ativado_em,
                    snapshot_capturado_em,amostra_metodo,amostra_pool_tamanho,
                    amostra_m_tamanho,amostra_u_tamanho,falha_resumo)
                VALUES(
                    @modelo,@versao,'GERANDO',@algoritmo,@normalizacao,
                    'GOLD_PESSOA_UUID_PK','gold.pessoa','ci-roundtrip',
                    2,2,SYSDATETIMEOFFSET(),NULL,SYSUTCDATETIME(),
                    'CI_RULESET_ROUNDTRIP_CURRENT',2,1,1,NULL);
                """, connection, transaction))
            {
                model.Parameters.AddWithValue("@modelo", modelId);
                model.Parameters.AddWithValue("@algoritmo", algorithm);
                model.Parameters.AddWithValue("@normalizacao", IdentityComparison.NormalizationVersion);
                await model.ExecuteNonQueryAsync();
            }

            foreach (var (name, value) in parameters)
            {
                await using var parameter = new SqlCommand(
                    "INSERT identidade.parametro_linkage(modelo_id,nome,valor) VALUES(@modelo,@nome,@valor);",
                    connection,
                    transaction);
                parameter.Parameters.AddWithValue("@modelo", modelId);
                parameter.Parameters.AddWithValue("@nome", name);
                var p = parameter.Parameters.Add("@valor", System.Data.SqlDbType.Decimal);
                p.Precision = 30;
                p.Scale = 12;
                p.Value = value;
                await parameter.ExecuteNonQueryAsync();
            }

            await LinkageRuleSetWriter.WriteAsync(
                connection,
                transaction,
                modelId,
                expected,
                CancellationToken.None);

            await using var publish = new SqlCommand(
                "UPDATE identidade.modelo_linkage SET status='VALIDADO' WHERE modelo_id=@modelo AND status='GERANDO';",
                connection,
                transaction);
            publish.Parameters.AddWithValue("@modelo", modelId);
            Assert.That(await publish.ExecuteNonQueryAsync(), Is.EqualTo(1));

            await transaction.CommitAsync();
        }

        var actual = await LinkageRuleSetReader.TryLoadAsync(connection, modelId, CancellationToken.None);
        Assert.That(actual, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(actual!.RuleSetVersion, Is.EqualTo(expected.RuleSetVersion));
            Assert.That(actual.AlgorithmVersion, Is.EqualTo(algorithm));
            Assert.That(actual.FingerprintSha256, Is.EqualTo(expected.FingerprintSha256));
            Assert.That(actual.BlockingPasses, Has.Count.EqualTo(1));
            Assert.That(actual.BlockingPasses[0].Fields, Is.EqualTo(new[] { "birth_year" }));
            Assert.That(actual.ProjectionSchemaVersion, Is.EqualTo(PersonResolutionProjectionContract.SchemaVersion));
            Assert.That(actual.ProjectionFingerprintSha256, Is.EqualTo(PersonResolutionProjectionContract.FingerprintSha256));
        });

        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM identidade.linkage_ruleset WHERE modelo_id=@modelo;";
        count.Parameters.AddWithValue("@modelo", modelId);
        Assert.That(
            Convert.ToInt32(await count.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture),
            Is.EqualTo(1));
    }

    private static async Task EnsureActiveFrequencyReferenceAsync(SqlConnection connection)
    {
        await using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT COUNT(*) FROM ref.frequencia_nome_versao WHERE status='ATIVA';";
            if (Convert.ToInt32(await exists.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) > 0)
                return;
        }

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            long versionId;
            await using (var version = connection.CreateCommand())
            {
                version.Transaction = transaction;
                version.CommandText = """
                    INSERT ref.frequencia_nome_versao(codigo,fonte,edicao,data_referencia,status)
                    OUTPUT INSERTED.frequencia_nome_versao_id
                    VALUES(@codigo,N'CI',N'roundtrip','2022-01-01','CARREGANDO');
                    """;
                version.Parameters.AddWithValue("@codigo", $"CI-RULESET-{Guid.NewGuid():N}");
                versionId = Convert.ToInt64(await version.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
            }

            await using (var rows = connection.CreateCommand())
            {
                rows.Transaction = transaction;
                rows.CommandText = """
                    INSERT ref.frequencia_nome(
                      frequencia_nome_versao_id,tipo,valor,valor_normalizado,sexo,periodo_nascimento,
                      escopo_geografico,uf_codigo,municipio_codigo,frequencia)
                    VALUES
                      (@id,'NOME',N'ANA',N'ANA','TODOS','TODOS','BRASIL','00','0000000',1),
                      (@id,'SOBRENOME',N'SILVA',N'SILVA','TODOS','TODOS','BRASIL','00','0000000',1);
                    """;
                rows.Parameters.AddWithValue("@id", versionId);
                await rows.ExecuteNonQueryAsync();
            }

            await using (var publish = connection.CreateCommand())
            {
                publish.Transaction = transaction;
                publish.CommandType = System.Data.CommandType.StoredProcedure;
                publish.CommandText = "ref.sp_publicar_frequencia_nome_versao";
                publish.Parameters.AddWithValue("@frequencia_nome_versao_id", versionId);
                publish.Parameters.Add("@conteudo_sha256", System.Data.SqlDbType.Binary, 32).Value =
                    Enumerable.Repeat((byte)0x5A, 32).ToArray();
                await publish.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync();
            throw;
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

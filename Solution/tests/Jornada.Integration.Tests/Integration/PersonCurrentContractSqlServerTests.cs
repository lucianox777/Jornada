using Jornada.Tests.Integration;
using Microsoft.Data.SqlClient;

namespace Jornada.Integration.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class PersonCurrentContractSqlServerTests
{
    [Test]
    public async Task Fresh_dev_seed_keeps_only_current_pessoa_v6()
    {
        var connectionString=RequireIntegrationConnection();
        await using var connection=new SqlConnection(connectionString);
        await connection.OpenAsync();

        var databaseDir=Path.Combine(AppContext.BaseDirectory,"database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection,databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(connection,Path.Combine(databaseDir,"Jornada_Seed_Dev.sql"));

        await using var command=connection.CreateCommand();
        command.CommandText="""
            SELECT
              (SELECT COUNT_BIG(*)
                 FROM ref.gestor_pessoa_versao v
                 JOIN ref.gestor g ON g.gestor_id=v.gestor_id
                WHERE g.codigo IN(N'SEHAB',N'SMADS',N'SMDET',N'SMS')),
              (SELECT COUNT_BIG(*)
                 FROM ref.gestor_pessoa_versao v
                 JOIN ref.gestor g ON g.gestor_id=v.gestor_id
                WHERE g.codigo IN(N'SEHAB',N'SMADS',N'SMDET',N'SMS')
                  AND v.versao=6 AND v.status=N'ATIVA'
                  AND v.pessoa_schema_ref LIKE N'%/pessoa/v6/pessoa.schema.json'),
              (SELECT COUNT_BIG(*)
                 FROM ref.gestor_pessoa_versao v
                 JOIN ref.gestor g ON g.gestor_id=v.gestor_id
                WHERE g.codigo IN(N'SEHAB',N'SMADS',N'SMDET',N'SMS')
                  AND v.versao<>6);
            """;

        await using var reader=await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(),Is.True);
        Assert.Multiple(()=>
        {
            Assert.That(reader.GetInt64(0),Is.EqualTo(4),"O seed limpo deve publicar um único contrato Pessoa por Gestor.");
            Assert.That(reader.GetInt64(1),Is.EqualTo(4),"Os quatro Gestores DEV devem usar Pessoa v6 ATIVA.");
            Assert.That(reader.GetInt64(2),Is.Zero,"O seed corrente não deve recriar versões Pessoa anteriores.");
        });
    }

    [Test]
    public async Task Dev_console_catalog_query_resolves_current_v6_only()
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
            JOIN ref.gestor_pessoa_versao gpv ON gpv.gestor_id=g.gestor_id AND gpv.status=N'ATIVA'
            JOIN ref.tipo_registro tr ON tr.gestor_id=g.gestor_id AND tr.ativo=1
            JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_id=tr.tipo_registro_id
                                             AND trv.status IN(N'ATIVA',N'ENCERRADA')
            WHERE g.ativo=1;
            """;

        await using var reader=await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(),Is.True);
        Assert.Multiple(()=>
        {
            Assert.That(reader.GetInt64(0),Is.GreaterThan(0));
            Assert.That(reader.GetInt32(1),Is.EqualTo(6));
            Assert.That(reader.GetInt32(2),Is.EqualTo(6));
        });
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString=Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if(string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");

        var builder=new SqlConnectionStringBuilder(connectionString);
        var database=builder.InitialCatalog??string.Empty;
        if(!database.Contains("test",StringComparison.OrdinalIgnoreCase)
           &&!database.Contains("dev",StringComparison.OrdinalIgnoreCase)
           &&!database.Contains("local",StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.");

        return connectionString!;
    }
}

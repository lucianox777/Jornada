using Jornada.Api;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class ProgressiveOriginSqlTests
{
    [Test]
    public async Task Query_reads_existing_origin_without_mutation_and_cannot_cross_owner_or_namespace()
    {
        var cs = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs)) Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION.");
        var db = new SqlConnectionStringBuilder(cs!).InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase) &&
            !db.Contains("dev", StringComparison.OrdinalIgnoreCase) &&
            !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Banco de integração deve conter Test, Dev ou Local.");

        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();
        var dir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(dir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(dir, "Jornada_Seed_Dev.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(dir, "Jornada_Identidade_Progressiva.sql"));
        var serving = Path.Combine(dir, "migrations", "20260908_Identidade_Progressiva_Serving.sql");
        await SqlBatchRunner.ExecuteFileAsync(connection, serving);
        await SqlBatchRunner.ExecuteFileAsync(connection, serving);

        long sourceId;
        await using (var source = connection.CreateCommand())
        {
            source.CommandText = "SELECT TOP(1) pessoa_origem_id FROM silver.pessoa_origem ORDER BY pessoa_origem_id;";
            sourceId = Convert.ToInt64(await source.ExecuteScalarAsync() ?? throw new InvalidOperationException("Fixture sem origem."), System.Globalization.CultureInfo.InvariantCulture);
        }
        // A única escrita do teste prepara uma referência já existente na Silver.
        // A consulta HTTP/SQL abaixo não cria nem publica identidades.
        await using (var ensure = connection.CreateCommand())
        {
            ensure.CommandText = "BEGIN TRAN; EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@id; COMMIT;";
            ensure.Parameters.AddWithValue("@id", sourceId);
            await ensure.ExecuteNonQueryAsync();
        }

        string owner, system, code, state;
        Guid initial;
        Guid? canonical;
        long version;
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT gestor_codigo,sistema_origem_codigo,codigo_pessoa_origem,initial_uuid,canonical_uuid,estado,versao FROM serving.v_identidade_origem_progressiva WHERE pessoa_origem_id=@id;";
            read.Parameters.AddWithValue("@id", sourceId);
            await using var reader = await read.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            owner = reader.GetString(0);
            system = reader.GetString(1);
            code = reader.GetString(2);
            initial = reader.GetGuid(3);
            canonical = reader.IsDBNull(4) ? null : reader.GetGuid(4);
            state = reader.GetString(5);
            version = reader.GetInt64(6);
        }

        var context = new AccessContext(Guid.NewGuid(), AccessCredentialType.GESTOR, owner, owner, null,
            [ProgressiveOriginApi.Permission], []);
        var query = new ProgressiveOriginQueryRequest(system, code);
        var service = new SqlProgressiveOriginQueryService(new OperationalSqlAdapter(cs!));
        var first = await service.GetAsync(context, query, CancellationToken.None);
        var repeat = await service.GetAsync(context, query, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(first!.InitialUuid, Is.EqualTo(initial));
            Assert.That(first.CanonicalUuid, Is.EqualTo(canonical));
            Assert.That(first.Estado.ToString(), Is.EqualTo(state));
            Assert.That(first.Versao, Is.EqualTo(version));
            Assert.That(repeat, Is.EqualTo(first));
        });

        var other = context with { GestorCodigo = "CI_GESTOR_SEM_PROPRIEDADE", PublicCode = "CI_GESTOR_SEM_PROPRIEDADE" };
        Assert.That(await service.GetAsync(other, query, CancellationToken.None), Is.Null);
        Assert.That(await service.GetAsync(context, query with { CodigoSistemaOrigem = "CI_SISTEMA_INEXISTENTE" }, CancellationToken.None), Is.Null);
        Assert.That(await service.GetAsync(context, query with { CodigoPessoaOrigem = "CI_ORIGEM_INEXISTENTE_" + Guid.NewGuid().ToString("N") }, CancellationToken.None), Is.Null);
        var changedCase = string.Equals(code, code.ToUpperInvariant(), StringComparison.Ordinal) ? code.ToLowerInvariant() : code.ToUpperInvariant();
        if (!string.Equals(changedCase, code, StringComparison.Ordinal))
            Assert.That(await service.GetAsync(context, query with { CodigoPessoaOrigem = changedCase }, CancellationToken.None), Is.Null);
        Assert.That(await service.GetAsync(context, query with { CodigoPessoaOrigem = code + " " }, CancellationToken.None), Is.Null,
            "Espaços finais não podem ampliar a igualdade de chaves do SQL Server.");
        Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await service.GetAsync(context with { Scopes = [] }, query, CancellationToken.None));
        Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await service.GetAsync(context with { CredentialType = AccessCredentialType.SERVICO }, query, CancellationToken.None));

        await using var verify = connection.CreateCommand();
        verify.CommandText = "SELECT initial_uuid,versao FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=@id;";
        verify.Parameters.AddWithValue("@id", sourceId);
        await using var final = await verify.ExecuteReaderAsync();
        Assert.That(await final.ReadAsync(), Is.True);
        Assert.That(final.GetGuid(0), Is.EqualTo(initial));
        Assert.That(final.GetInt64(1), Is.EqualTo(version));
    }
}

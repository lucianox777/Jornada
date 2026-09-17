using System.Data;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class PersonV4IdentitySqlServerTests
{
    [Test]
    public async Task Observation_without_origin_accepts_zero_or_many_typed_identifiers()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await EnsureV4IdentitySchemaAsync(connection);

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var zeroHash = UniqueHash('a');
            var zeroObservationId = await InsertObservationWithoutOriginAsync(connection, tx, zeroHash);

            await using (var zero = connection.CreateCommand())
            {
                zero.Transaction = tx;
                zero.CommandText = "SELECT COUNT(*) FROM silver.pessoa_identificador_observacao WHERE pessoa_observacao_id=@obs;";
                zero.Parameters.AddWithValue("@obs", zeroObservationId);
                Assert.That(Convert.ToInt32(await zero.ExecuteScalarAsync()), Is.Zero,
                    "Pessoa v4 deve aceitar observação sem origem e sem identificadores.");
            }

            var manyHash = UniqueHash('b');
            var manyObservationId = await InsertObservationWithoutOriginAsync(connection, tx, manyHash);

            await InsertIdentifierAsync(connection, tx, manyObservationId,
                "CNS", "BR", "898001234567890", "898001234567890", null, null);
            await InsertIdentifierAsync(connection, tx, manyObservationId,
                "RG", "SSP_SP", "12.345.678-9", "123456789", "SSP", "SP");
            await InsertIdentifierAsync(connection, tx, manyObservationId,
                "OUTRO", "TESTE_INTEGRACAO", "LEGADO-42", "LEGADO-42", null, null);

            await using var verify = connection.CreateCommand();
            verify.Transaction = tx;
            verify.CommandText = """
                SELECT
                    COUNT(*) AS total,
                    SUM(CASE WHEN tipo_identificador_codigo='CNS' AND namespace_codigo='BR' THEN 1 ELSE 0 END) AS cns,
                    SUM(CASE WHEN tipo_identificador_codigo='RG' AND emissor_codigo='SSP' AND uf_emissor='SP' THEN 1 ELSE 0 END) AS rg,
                    SUM(CASE WHEN tipo_identificador_codigo='OUTRO' AND namespace_codigo='TESTE_INTEGRACAO' THEN 1 ELSE 0 END) AS outro
                FROM silver.pessoa_identificador_observacao
                WHERE pessoa_observacao_id=@obs;
                """;
            verify.Parameters.AddWithValue("@obs", manyObservationId);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reader.GetInt32(0), Is.EqualTo(3));
                Assert.That(reader.GetInt32(1), Is.EqualTo(1));
                Assert.That(reader.GetInt32(2), Is.EqualTo(1));
                Assert.That(reader.GetInt32(3), Is.EqualTo(1));
            });
        }
        finally
        {
            if (tx.Connection is not null) await tx.RollbackAsync();
        }
    }

    [Test]
    public async Task Shared_person_base_has_one_identity_namespace_and_multiple_observing_systems()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await EnsureV4IdentitySchemaAsync(connection);

        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var systems = new List<(long SystemId, long GestorId)>();
            await using (var pick = connection.CreateCommand())
            {
                pick.Transaction = tx;
                pick.CommandText = "SELECT TOP(2) sistema_origem_id,gestor_id FROM ref.sistema_origem WHERE ativo=1 ORDER BY sistema_origem_id;";
                await using var reader = await pick.ExecuteReaderAsync();
                while (await reader.ReadAsync()) systems.Add((reader.GetInt64(0), reader.GetInt64(1)));
            }
            Assert.That(systems.Count, Is.EqualTo(2), "Fixture deve possuir ao menos dois sistemas de origem ativos.");

            var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            var baseCode = $"TEST_SHARED_{suffix}";
            var personCode = $"P-{suffix}";
            long baseId;
            await using (var insertBase = connection.CreateCommand())
            {
                insertBase.Transaction = tx;
                insertBase.CommandText = """
                    INSERT ref.base_pessoa_origem(codigo,nome,gestor_custodiante_id,escopo,confianca_identidade)
                    OUTPUT INSERTED.base_pessoa_origem_id
                    VALUES(@codigo,@nome,@gestor,'COMPARTILHADA','REFERENCIAL');
                    """;
                insertBase.Parameters.AddWithValue("@codigo", baseCode);
                insertBase.Parameters.AddWithValue("@nome", "Base compartilhada - teste de integração");
                insertBase.Parameters.AddWithValue("@gestor", systems[0].GestorId);
                baseId = Convert.ToInt64(await insertBase.ExecuteScalarAsync());
            }

            foreach (var system in systems)
            {
                await using var authorize = connection.CreateCommand();
                authorize.Transaction = tx;
                authorize.CommandText = """
                    INSERT ref.sistema_origem_base_pessoa(sistema_origem_id,base_pessoa_origem_id,padrao,ativo)
                    VALUES(@sistema,@base,0,1);
                    """;
                authorize.Parameters.AddWithValue("@sistema", system.SystemId);
                authorize.Parameters.AddWithValue("@base", baseId);
                await authorize.ExecuteNonQueryAsync();
            }

            long personOriginId;
            await using (var origin = connection.CreateCommand())
            {
                origin.Transaction = tx;
                origin.CommandText = """
                    INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem,base_pessoa_origem_id)
                    OUTPUT INSERTED.pessoa_origem_id
                    VALUES(@sistema,@codigo,@base);
                    """;
                origin.Parameters.AddWithValue("@sistema", systems[0].SystemId);
                origin.Parameters.AddWithValue("@codigo", personCode);
                origin.Parameters.AddWithValue("@base", baseId);
                personOriginId = Convert.ToInt64(await origin.ExecuteScalarAsync());
            }

            foreach (var system in systems)
            {
                await using var observe = connection.CreateCommand();
                observe.Transaction = tx;
                observe.CommandText = """
                    INSERT silver.pessoa_origem_sistema(pessoa_origem_id,sistema_origem_id)
                    VALUES(@origem,@sistema);
                    """;
                observe.Parameters.AddWithValue("@origem", personOriginId);
                observe.Parameters.AddWithValue("@sistema", system.SystemId);
                await observe.ExecuteNonQueryAsync();
            }

            await using var verify = connection.CreateCommand();
            verify.Transaction = tx;
            verify.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM silver.pessoa_origem WHERE base_pessoa_origem_id=@base AND codigo_pessoa_origem=@codigo),
                    (SELECT COUNT(*) FROM silver.pessoa_origem_sistema WHERE pessoa_origem_id=@origem),
                    (SELECT COUNT(*) FROM ref.sistema_origem_base_pessoa WHERE base_pessoa_origem_id=@base AND ativo=1);
                """;
            verify.Parameters.AddWithValue("@base", baseId);
            verify.Parameters.AddWithValue("@codigo", personCode);
            verify.Parameters.AddWithValue("@origem", personOriginId);
            await using var result = await verify.ExecuteReaderAsync();
            Assert.That(await result.ReadAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(result.GetInt32(0), Is.EqualTo(1), "A identidade de origem é única no namespace da base.");
                Assert.That(result.GetInt32(1), Is.EqualTo(2), "Dois sistemas podem observar a mesma identidade da base compartilhada.");
                Assert.That(result.GetInt32(2), Is.EqualTo(2), "A base compartilhada está explicitamente autorizada para os dois sistemas.");
            });
        }
        finally
        {
            if (tx.Connection is not null) await tx.RollbackAsync();
        }
    }

    [Test]
    public async Task Observation_and_identifiers_roll_back_as_one_transaction()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await EnsureV4IdentitySchemaAsync(connection);

        var hash = UniqueHash('c');
        long observationId;
        await using (var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            observationId = await InsertObservationWithoutOriginAsync(connection, tx, hash);
            await InsertIdentifierAsync(connection, tx, observationId,
                "OUTRO", "TESTE_ROLLBACK", "TX-ROLLBACK", "TX-ROLLBACK", null, null);
            await tx.RollbackAsync();
        }

        await using var verify = connection.CreateCommand();
        verify.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM silver.pessoa_observacao WHERE conteudo_hash=@hash),
                (SELECT COUNT(*) FROM silver.pessoa_identificador_observacao WHERE pessoa_observacao_id=@obs);
            """;
        verify.Parameters.AddWithValue("@hash", hash);
        verify.Parameters.AddWithValue("@obs", observationId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetInt32(0), Is.Zero, "Rollback não pode deixar observação parcial.");
            Assert.That(reader.GetInt32(1), Is.Zero, "Rollback não pode deixar identificador órfão.");
        });
    }

    private static async Task EnsureV4IdentitySchemaAsync(SqlConnection connection)
    {
        var repositoryRoot = FindRepositoryRoot();
        var database = Path.Combine(repositoryRoot, "Solution", "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(database, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(database, "Jornada_Seed_Dev.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(database, "migrations", "20260913_Base_Pessoa_Origem.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(database, "migrations", "20260913_Pessoa_Identificadores_Multiplos.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(database, "migrations", "20260913_Pessoa_Observacao_Sem_Identificador.sql"));
    }

    private static async Task<long> InsertObservationWithoutOriginAsync(
        SqlConnection connection,
        SqlTransaction tx,
        string hash)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT silver.pessoa_observacao(
                pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
                cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
            OUTPUT INSERTED.pessoa_observacao_id
            SELECT TOP(1)
                NULL,lote_id,gestor_id,NULL,
                (SELECT ISNULL(MAX(x.versao_interna),0)+1 FROM silver.pessoa_observacao x WHERE x.pessoa_origem_id IS NULL),
                @hash,NULL,'NAO_INFORMADO_ORIGEM',nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of
            FROM silver.pessoa_observacao
            ORDER BY pessoa_observacao_id;
            """;
        insert.Parameters.Add(new SqlParameter("@hash", SqlDbType.Char, 64) { Value = hash });
        var value = await insert.ExecuteScalarAsync();
        Assert.That(value, Is.Not.Null, "Fixture DEV deve possuir ao menos uma observação para clonar o núcleo demográfico.");
        return Convert.ToInt64(value);
    }

    private static async Task InsertIdentifierAsync(
        SqlConnection connection,
        SqlTransaction tx,
        long observationId,
        string type,
        string ns,
        string original,
        string normalized,
        string? issuer,
        string? issuerState)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT silver.pessoa_identificador_observacao(
                pessoa_observacao_id,tipo_identificador_codigo,namespace_codigo,
                valor_original,valor_normalizado,emissor_codigo,uf_emissor,
                status_validacao,status_evidencia)
            VALUES(@obs,@tipo,@namespace,@original,@normalizado,@emissor,@uf,'NAO_VALIDADO','DECLARADO');
            """;
        insert.Parameters.AddWithValue("@obs", observationId);
        insert.Parameters.AddWithValue("@tipo", type);
        insert.Parameters.AddWithValue("@namespace", ns);
        insert.Parameters.AddWithValue("@original", original);
        insert.Parameters.AddWithValue("@normalizado", normalized);
        insert.Parameters.Add(new SqlParameter("@emissor", SqlDbType.NVarChar, 120) { Value = (object?)issuer ?? DBNull.Value });
        insert.Parameters.Add(new SqlParameter("@uf", SqlDbType.Char, 2) { Value = (object?)issuerState ?? DBNull.Value });
        await insert.ExecuteNonQueryAsync();
    }

    private static string UniqueHash(char prefix)
    {
        var suffix = Guid.NewGuid().ToString("N").ToLowerInvariant();
        return new string(prefix, 32) + suffix;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Solution", "database", "Jornada_Fase1.sql")))
                return current.FullName;
            current = current.Parent;
        }

        Assert.Fail("Raiz do repositório Jornada não encontrada a partir do diretório de testes.");
        return string.Empty;
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar integração SQL Server.");
        return connectionString!;
    }
}

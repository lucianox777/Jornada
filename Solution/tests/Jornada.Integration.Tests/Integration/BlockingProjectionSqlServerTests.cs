using Jornada.Contracts;
using Jornada.Operational.Sql;
using Jornada.Processor.Worker;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class BlockingProjectionSqlServerTests
{
    [Test]
    public async Task PersistValidated_publishes_blocking_projection_in_same_processor_path()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareDatabaseAsync(connectionString);
        await SetOneCadastralBatchPendingAsync(connectionString);

        var repository = new SqlProcessorRepository(
            new OperationalSqlAdapter(connectionString),
            new RegistryQualityEngine(Array.Empty<IRegistryQualityEvaluator>()));

        var batch = await repository.ReserveNextAsync(CancellationToken.None);
        Assert.That(batch, Is.Not.Null);
        var reserved = batch!;

        // CPF válido e exclusivo desta prova: o repositório persiste a identidade fora do escopo do teste.
        const string cpf = "73124896564";
        var verifiedAt = DateTimeOffset.Parse(
            "2026-09-11T10:00:00-03:00",
            System.Globalization.CultureInfo.InvariantCulture);
        var person = new ParsedPerson(
            "BLOCKING-SQLSERVER-001",
            new string('a', 64),
            "TX-BLOCKING-SQLSERVER-001",
            cpf,
            null,
            "María Silva Teste",
            new DateOnly(1991, 4, 13),
            "Ana Souza Teste",
            [
                new ParsedTransversalAttribute(
                    "BLOCKING-PHONE-1", PersonResolutionContractCatalog.ContactPhone,
                    "+55 (11) 99999-0001", "COMPROVADO", "DOCUMENTO", null, verifiedAt, null,
                    null, null, null),
                new ParsedTransversalAttribute(
                    "BLOCKING-EMAIL-1", PersonResolutionContractCatalog.ContactEmail,
                    "Pessoa@EXAMPLE.Test", "COMPROVADO", "DOCUMENTO", null, verifiedAt, null,
                    null, null, null),
                new ParsedTransversalAttribute(
                    "BLOCKING-SOCIAL-1", PersonResolutionContractCatalog.SocialName,
                    "Maria Social Teste", "COMPROVADO", "DOCUMENTO", null, verifiedAt, null,
                    null, null, null)
            ],
            []);
        var manifest = new IngestionPackageManifest(
            2,
            reserved.PessoaSchemaVersao,
            reserved.CodigoSistemaOrigem,
            null,
            null,
            null,
            reserved.DataReferencia);

        await repository.PersistValidatedAsync(
            reserved,
            new ParsedPackage(manifest, [person], []),
            CancellationToken.None);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @uuid UNIQUEIDENTIFIER=(SELECT pessoa_uuid FROM gold.pessoa WHERE cpf=@cpf);
            SELECT COUNT(*)
              FROM identidade.blocking_chave
             WHERE pessoa_uuid=@uuid
               AND normalizacao_versao=@normalizacao
               AND vigencia_fim IS NULL
               AND (
                    (atributo='name_full' AND valor_normalizado='MARIA SILVA TESTE' AND semantica_temporal='VERSIONED_ALIAS') OR
                    (atributo='name_first' AND valor_normalizado='MARIA' AND semantica_temporal='VERSIONED_ALIAS') OR
                    (atributo='name_last' AND valor_normalizado='TESTE' AND semantica_temporal='VERSIONED_ALIAS') OR
                    (atributo='mother_name_full' AND valor_normalizado='ANA SOUZA TESTE' AND semantica_temporal='VERSIONED_ALIAS') OR
                    (atributo='mother_name_first' AND valor_normalizado='ANA' AND semantica_temporal='VERSIONED_ALIAS') OR
                    (atributo='mother_name_last' AND valor_normalizado='TESTE' AND semantica_temporal='VERSIONED_ALIAS') OR
                    (atributo='birth_day' AND valor_normalizado='13' AND semantica_temporal='STABLE_IDENTITY_DATUM') OR
                    (atributo='birth_month' AND valor_normalizado='04' AND semantica_temporal='STABLE_IDENTITY_DATUM') OR
                    (atributo='birth_year' AND valor_normalizado='1991' AND semantica_temporal='STABLE_IDENTITY_DATUM') OR
                    (atributo='telefone_contato__canonical' AND valor_normalizado='5511999990001' AND semantica_temporal='VERSIONED_ALIAS') OR
                    (atributo='email_contato__canonical' AND valor_normalizado='pessoa@example.test' AND semantica_temporal='VERSIONED_ALIAS') OR
                    (atributo='nome_social__normalized' AND valor_normalizado='MARIA SOCIAL TESTE' AND semantica_temporal='VERSIONED_ALIAS') OR
                    (atributo='nome_social__first' AND valor_normalizado='MARIA' AND semantica_temporal='VERSIONED_ALIAS') OR
                    (atributo='nome_social__last' AND valor_normalizado='TESTE' AND semantica_temporal='VERSIONED_ALIAS')
               );
            """;
        command.Parameters.AddWithValue("@cpf", cpf);
        command.Parameters.AddWithValue("@normalizacao", IdentityComparison.NormalizationVersion);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(count, Is.EqualTo(14));
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

    private static async Task PrepareDatabaseAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection,
            Path.Combine(databaseDir, "migrations", "20260910_Linkage_Blocking_Chave.sql"));

        await using var reset = connection.CreateCommand();
        reset.CommandText = """
            DELETE FROM identidade.blocking_chave;
            UPDATE ingestao.lote
               SET status='PROCESSADO',erro_codigo=NULL,lease_id=NULL,lease_owner=NULL,
                   lease_adquirido_em=NULL,heartbeat_em=NULL,lease_expira_em=NULL,
                   proxima_tentativa_em=NULL,poison_em=NULL,atualizado_em=SYSUTCDATETIME();
            UPDATE ingestao.entrega SET status='PROCESSADA',ultima_atualizacao=SYSDATETIMEOFFSET();
            """;
        await reset.ExecuteNonQueryAsync();
    }

    private static async Task SetOneCadastralBatchPendingAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @lote UNIQUEIDENTIFIER=(
                SELECT TOP(1) l.lote_id
                  FROM ingestao.lote l
                  JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id
                 WHERE e.natureza IS NULL AND e.tipo_registro_id IS NULL
                 ORDER BY l.criado_em,l.lote_id);
            IF @lote IS NULL THROW 51000,'Seed precisa conter Entrega cadastral.',1;
            UPDATE ingestao.lote
               SET status='PENDENTE',erro_codigo=NULL,tentativa_count=0,recuperacao_count=0,
                   lease_id=NULL,lease_owner=NULL,lease_adquirido_em=NULL,heartbeat_em=NULL,
                   lease_expira_em=NULL,ultima_tentativa_em=NULL,proxima_tentativa_em=NULL,
                   poison_em=NULL,atualizado_em=SYSUTCDATETIME()
             WHERE lote_id=@lote;
            UPDATE e SET status='RECEBIDA',ultima_atualizacao=SYSDATETIMEOFFSET()
              FROM ingestao.entrega e JOIN ingestao.lote l ON l.entrega_id=e.entrega_id
             WHERE l.lote_id=@lote;
            """;
        await command.ExecuteNonQueryAsync();
    }
}

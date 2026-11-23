using Jornada.Contracts;
using Jornada.Operational.Sql;
using Jornada.Linkage.Runner;
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

        // Segunda identidade inteiramente sintética: mesmo prenome materno e ano,
        // nome civil e CPF distintos. Exige recuperar duas pessoas distintas por
        // mother-year e recusar fan-out com orçamento de apenas uma pessoa.
        // CPF 73124896645 possui dígitos verificadores válidos.
        var secondPerson = person with
        {
            IdPessoaEntrega = "BLOCKING-SQLSERVER-002",
            CodigoPessoaOrigem = "BLOCKING-SQLSERVER-002",
            ConteudoHash = new string('b', 64),
            SourceTransactionId = "TX-BLOCKING-SQLSERVER-002",
            Cpf = "73124896645",
            NomeCompleto = "Beatriz Soares Teste",
            Atributos = [],
            ConferenciasDocumentais = []
        };
        await repository.PersistValidatedAsync(
            reserved,
            new ParsedPackage(manifest, [person, secondPerson], []),
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
               AND projection_schema_version=@projection_schema
               AND projection_fingerprint_sha256=@projection_fingerprint
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
        command.Parameters.AddWithValue("@projection_schema", PersonResolutionProjectionContract.SchemaVersion);
        command.Parameters.AddWithValue("@projection_fingerprint", PersonResolutionProjectionContract.FingerprintSha256);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(count, Is.EqualTo(14));

        // #1851: prove the exact UTF-16LE + LF + ordinal source digest used
        // by the 1k IBGE HTTP-to-Silver verification on real disposable SQL.
        // This does not prove the HTTP step, Runner V9 or institutional policy.
        await using (var sourceDigest = connection.CreateCommand())
        {
            sourceDigest.CommandText = """
                SELECT UPPER(CONVERT(VARCHAR(64),
                    HASHBYTES('SHA2_256',
                        STRING_AGG(CONVERT(NVARCHAR(MAX),
                            po.codigo_pessoa_origem + NCHAR(10)), N'')
                        WITHIN GROUP (ORDER BY po.codigo_pessoa_origem COLLATE Latin1_General_100_BIN2)
                    ), 2))
                FROM silver.pessoa_observacao po
                WHERE po.codigo_pessoa_origem IN (
                    N'BLOCKING-SQLSERVER-001', N'BLOCKING-SQLSERVER-002');
                """;
            var observedDigest = Convert.ToString(
                await sourceDigest.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture);
            var expectedBytes = System.Text.Encoding.Unicode.GetBytes(
                "BLOCKING-SQLSERVER-001\nBLOCKING-SQLSERVER-002\n");
            var expectedDigest = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(expectedBytes));
            Assert.That(observedDigest, Is.EqualTo(expectedDigest),
                "Hash SQL ordenado deve igualar SHA-256 UTF-16LE/LF do preflight E2E.");
        }

        // SQL real: sem nascimento, CombinedIdentityCandidatePlanner não pode atuar.
        // Um passe dinâmico nominal elegível ainda deve devolver a referência da Gold.
        var ruleset = LinkageDynamicRuleSet.CreateWithPasses(
            "INTEGRATION-SEMIBLIND-NO-BIRTH", "INTEGRATION-TEST",
            [LinkageBlockingPass.Create("full-name", [BlockingFeatureNames.FullName])],
            Array.Empty<KeyValuePair<string, decimal>>()) with
        {
            ProjectionSchemaVersion = PersonResolutionProjectionContract.SchemaVersion,
            ProjectionFingerprintSha256 = PersonResolutionProjectionContract.FingerprintSha256
        };
        var observation = new IdentityObservation(
            null, "NAO_INFORMADO", "María Silva Teste", null, "Ana Souza Teste");
        var passes = SemiblindCandidatePassPlanner.Plan(ruleset, observation);
        Assert.That(passes.Select(p => p.PassId), Is.EqualTo(new[] { "full-name" }));

        var found = await BlockingProjectionCandidateLoader.LoadAsync(
            connection, ruleset, observation, maxCandidates: 20, commandTimeoutSeconds: 60,
            ct: CancellationToken.None, searchPasses: passes);
        Assert.That(found.Select(candidate => candidate.NomeCompleto),
            Does.Contain("María Silva Teste"));

        // #612: a ausência do nome da própria pessoa não é insuficiência universal.
        // Este passe usa somente prenome materno + ano de nascimento e é executado na projeção SQL real.
        var motherBirthRuleset = LinkageDynamicRuleSet.CreateWithPasses(
            "INTEGRATION-SEMIBLIND-MOTHER-BIRTH", "INTEGRATION-TEST",
            [LinkageBlockingPass.Create("mother-year",
                [BlockingFeatureNames.MotherFirstName, BlockingFeatureNames.BirthYear])],
            Array.Empty<KeyValuePair<string, decimal>>()) with
        {
            ProjectionSchemaVersion = PersonResolutionProjectionContract.SchemaVersion,
            ProjectionFingerprintSha256 = PersonResolutionProjectionContract.FingerprintSha256
        };
        var noPersonName = new IdentityObservation(
            null, "NAO_INFORMADO", null, new DateOnly(1991, 4, 13), "Ana Souza Teste");
        var motherBirthPasses = SemiblindCandidatePassPlanner.Plan(motherBirthRuleset, noPersonName);
        Assert.That(motherBirthPasses.Select(p => p.PassId), Is.EqualTo(new[] { "mother-year" }));
        var foundWithoutPersonName = await BlockingProjectionCandidateLoader.LoadAsync(
            connection, motherBirthRuleset, noPersonName, maxCandidates: 20, commandTimeoutSeconds: 60,
            ct: CancellationToken.None, searchPasses: motherBirthPasses);
        var motherYearNames = foundWithoutPersonName
            .Select(candidate => candidate.NomeCompleto).ToArray();
        Assert.That(motherYearNames, Does.Contain("María Silva Teste"));
        Assert.That(motherYearNames, Does.Contain("Beatriz Soares Teste"));
        Assert.That(foundWithoutPersonName.Select(x => x.PessoaUuid).Distinct().Count(),
            Is.EqualTo(foundWithoutPersonName.Count),
            "O UNION deve retornar cada UUID uma vez, sem truncamento silencioso.");

        // #612: contrato residual explícito da issue: PRENOME ISOLADO
        // + NOME COMPLETO DA MÃE + NASCIMENTO (dia, mês, ano).
        // O passe é executado no índice SQL real, não inferido da existência
        // das features. As duas identidades compartilham mãe e nascimento;
        // somente o prenome deve separar Maria de Beatriz.
        var firstMotherBirthRuleset = LinkageDynamicRuleSet.CreateWithPasses(
            "INTEGRATION-SEMIBLIND-FIRST-MOTHER-DOB", "INTEGRATION-TEST",
            [LinkageBlockingPass.Create("first-mother-dob",
                [BlockingFeatureNames.FirstName,
                 BlockingFeatureNames.MotherFullName,
                 BlockingFeatureNames.BirthDay,
                 BlockingFeatureNames.BirthMonth,
                 BlockingFeatureNames.BirthYear])],
            Array.Empty<KeyValuePair<string, decimal>>()) with
        {
            ProjectionSchemaVersion = PersonResolutionProjectionContract.SchemaVersion,
            ProjectionFingerprintSha256 = PersonResolutionProjectionContract.FingerprintSha256
        };
        var firstOnlyMaria = new IdentityObservation(
            null, "NAO_INFORMADO", "María",
            new DateOnly(1991, 4, 13), "Ana Souza Teste");
        var preciseMariaPasses = BlockingRuleSetCandidatePlanner.Plan(
            firstMotherBirthRuleset, firstOnlyMaria);
        Assert.That(preciseMariaPasses.Select(p => p.PassId),
            Is.EqualTo(new[] { "first-mother-dob" }));
        var preciseMaria = await BlockingProjectionCandidateLoader.LoadAsync(
            connection, firstMotherBirthRuleset, firstOnlyMaria,
            maxCandidates: 20, commandTimeoutSeconds: 60,
            ct: CancellationToken.None, searchPasses: preciseMariaPasses);
        Assert.That(preciseMaria.Select(p => p.NomeCompleto),
            Does.Contain("María Silva Teste"));
        Assert.That(preciseMaria.Select(p => p.NomeCompleto),
            Does.Not.Contain("Beatriz Soares Teste"));

        var firstOnlyBeatriz = firstOnlyMaria with { NomeCompleto = "Beatriz" };
        var preciseBeatrizPasses = BlockingRuleSetCandidatePlanner.Plan(
            firstMotherBirthRuleset, firstOnlyBeatriz);
        Assert.That(preciseBeatrizPasses.Select(p => p.PassId),
            Is.EqualTo(new[] { "first-mother-dob" }));
        var preciseBeatriz = await BlockingProjectionCandidateLoader.LoadAsync(
            connection, firstMotherBirthRuleset, firstOnlyBeatriz,
            maxCandidates: 20, commandTimeoutSeconds: 60,
            ct: CancellationToken.None, searchPasses: preciseBeatrizPasses);
        Assert.That(preciseBeatriz.Select(p => p.NomeCompleto),
            Does.Contain("Beatriz Soares Teste"));
        Assert.That(preciseBeatriz.Select(p => p.NomeCompleto),
            Does.Not.Contain("María Silva Teste"));
        Assert.That(preciseMaria.Select(p => p.PessoaUuid)
            .Intersect(preciseBeatriz.Select(p => p.PessoaUuid)), Is.Empty,
            "O passe SQL seletivo não pode confundir pessoas com a mesma mãe e data.");

        // O loader SQL real lê limite+1 e recusa antes de projetar Top 5.
        // O serviço HTTP traduz esse caso em busca_completa=false, não em
        // uma resposta vazia interpretada como 'nenhuma pessoa encontrada'.
        var overflow = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
        {
            await BlockingProjectionCandidateLoader.LoadAsync(
                connection, motherBirthRuleset, noPersonName, maxCandidates: 1,
                commandTimeoutSeconds: 60, ct: CancellationToken.None,
                searchPasses: motherBirthPasses);
        }));
        Assert.That(overflow!.Message, Does.Contain("MaxCandidatesPerBlock"));

        // Ano informado incorreto em +1: a união deve manter o passe
        // combined-neighbor-year no SQL, sem suprimir mother-year.
        var adjacentYear = new IdentityObservation(
            null, "NAO_INFORMADO", "María Silva Teste",
            new DateOnly(1992, 4, 13), "Ana Souza Teste");
        var adjacentPasses = SemiblindCandidatePassPlanner.Plan(
            motherBirthRuleset, adjacentYear);
        Assert.That(adjacentPasses.Select(p => p.PassId),
            Does.Contain("combined-neighbor-year"));
        Assert.That(adjacentPasses.Select(p => p.PassId), Does.Contain("mother-year"));
        var adjacentCandidates = await BlockingProjectionCandidateLoader.LoadAsync(
            connection, motherBirthRuleset, adjacentYear, maxCandidates: 20,
            commandTimeoutSeconds: 60, ct: CancellationToken.None,
            searchPasses: adjacentPasses);
        Assert.That(adjacentCandidates.Select(candidate => candidate.NomeCompleto),
            Does.Contain("María Silva Teste"));

        // Nenhum passe elegível é INCOMPLETUDE antes do SQL, diferente da
        // consulta SQL concluída com zero candidatos abaixo.
        var noEvidence = new IdentityObservation(
            null, "NAO_INFORMADO", null, null, null);
        Assert.That(SemiblindCandidatePassPlanner.Plan(ruleset, noEvidence), Is.Empty);

        var missing = new IdentityObservation(
            null, "NAO_INFORMADO", "Pessoa Inexistente No Corpus", null, null);
        var noMatches = await BlockingProjectionCandidateLoader.LoadAsync(
            connection, ruleset, missing, maxCandidates: 20, commandTimeoutSeconds: 60,
            ct: CancellationToken.None, searchPasses: SemiblindCandidatePassPlanner.Plan(ruleset, missing));
        Assert.That(noMatches, Is.Empty);
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
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection,
            Path.Combine(databaseDir, "migrations", "20260913_Base_Pessoa_Origem.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection,
            Path.Combine(databaseDir, "migrations", "20260913_Pessoa_Identificadores_Multiplos.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection,
            Path.Combine(databaseDir, "migrations", "20260913_Pessoa_Observacao_Sem_Identificador.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection,
            Path.Combine(databaseDir, "migrations", "20260919_Pessoa_Origem_Runtime_V4_Cutover.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection,
            Path.Combine(databaseDir, "migrations", "20260910_Linkage_Blocking_Chave.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection,
            Path.Combine(databaseDir, "migrations", "20260910_Linkage_RuleSet_Passes.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection,
            Path.Combine(databaseDir, "migrations", "20260911_Linkage_Blocking_Projection_Contract.sql"));

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

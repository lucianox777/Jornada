using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using NUnit.Framework;

namespace Jornada.Tests.Integration;

/// <summary>
/// Executes the exact tagged operational read-only audit SQL against canonical SQL Server.
/// Uses unmatchable synthetic tokens and never writes to Gold or identity projections.
/// </summary>
[TestFixture, Category("Integration"), NonParallelizable]
public sealed class BlockingParallelSqlAuditQueryTests
{
    [OneTimeSetUp]
    public async Task InstallRequiredProjectionSchemaAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION")
            ?? throw new InvalidOperationException("JORNADA_TEST_SQL_CONNECTION não configurada.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        // The CI integration database starts without optional linkage migrations.
        // Set up ONLY this isolated test fixture; the audit implementation stays read-only.
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(connection,
            Path.Combine(databaseDir, "migrations", "20260910_Linkage_Blocking_Chave.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection,
            Path.Combine(databaseDir, "migrations", "20260910_Linkage_RuleSet_Passes.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection,
            Path.Combine(databaseDir, "migrations", "20260911_Linkage_Blocking_Projection_Contract.sql"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TaggedQuery_ExecutesWithEmptyAndCombinedPassesWithoutExposingCandidateIds(
        bool includeCombined)
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION")
            ?? throw new InvalidOperationException("JORNADA_TEST_SQL_CONNECTION não configurada.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var impossibleName = "JORNADA_AUDIT_NEVER_MATCH_" + Guid.NewGuid().ToString("N");
        var dynamicPass = new BlockingCandidatePassLookup("dynamic-audit-test",
        [
            new BlockingCandidateClause(BlockingFeatureNames.FullName, [impossibleName])
        ]);
        var combinedPass = new BlockingCandidatePassLookup("combined-audit-test",
        [
            new BlockingCandidateClause(BlockingFeatureNames.FullName, [impossibleName]),
            new BlockingCandidateClause(BlockingFeatureNames.MotherFullName,
                ["JORNADA_AUDIT_NEVER_MATCH_" + Guid.NewGuid().ToString("N")]),
            new BlockingCandidateClause(BlockingFeatureNames.BirthYear, ["1954"])
        ]);

        await using var command = connection.CreateCommand();
        var candidateSql = BlockingProjectionCandidateQueryBuilder.BuildTaggedCandidateUuidQuery(
            command, [dynamicPass], includeCombined ? [combinedPass] : []);
        command.CommandText = BlockingPassAuditCommand.BuildTaggedAuditSql(candidateSql);
        command.Parameters.Add("@truth_uuid", System.Data.SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();

        Assert.That(command.CommandText, Does.Not.Contain(impossibleName),
            "A consulta precisa continuar 100% parametrizada.");
        await using var reader = await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple((TestDelegate)(() =>
        {
            Assert.That(reader.GetInt64(0), Is.Zero, "Nenhum candidato sintético deve existir em Gold.");
            Assert.That(reader.GetInt64(1), Is.Zero);
            Assert.That(reader.GetInt64(2), Is.Zero);
            Assert.That(reader.GetInt64(3), Is.Zero);
            Assert.That(reader.GetInt32(4), Is.Zero);
            Assert.That(reader.GetInt32(5), Is.Zero);
        }));
    }
    [Test]
    public async Task MariaTrio_TaggedOperationalSqlCountsDynamicCombinedSharedAndMissingMother()
    {
        // DT-17: exercise the production SQL builder against four synthetic Gold
        // references with real projection rows. All writes roll back; no model
        // or operational data is promoted. A UUID filter makes the test isolated
        // from unrelated references already seeded in this integration database.
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION")
            ?? throw new InvalidOperationException("JORNADA_TEST_SQL_CONNECTION não configurada.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var mariaSouza = Guid.NewGuid();
        var mariaSouzaLima = Guid.NewGuid();
        var mariaLimaNoMother = Guid.NewGuid();
        var homonym = Guid.NewGuid();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);
        try
        {
            await using (var seed = connection.CreateCommand())
            {
                seed.Transaction = tx;
                seed.CommandText = """
                    INSERT identidade.pessoa(pessoa_uuid,status) VALUES
                        (@p1,N'ATIVO'),(@p2,N'ATIVO'),(@p3,N'ATIVO'),(@p4,N'ATIVO');
                    INSERT gold.pessoa(
                        pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,
                        fontes_distintas,estado_concordancia,atualizado_em,
                        estado_identidade,completude_nucleo)
                    VALUES
                        (@p1,NULL,N'SEM_CPF',N'MARIA SOUZA','1982-04-10',N'ANA LIMA',
                         1,N'BASELINE_FONTE_UNICA',SYSUTCDATETIME(),N'REFERENCIA',N'COMPLETO'),
                        (@p2,NULL,N'SEM_CPF',N'MARIA SOUZA LIMA','1982-04-10',N'ANA LIMA',
                         1,N'BASELINE_FONTE_UNICA',SYSUTCDATETIME(),N'REFERENCIA',N'COMPLETO'),
                        (@p3,NULL,N'SEM_CPF',N'MARIA LIMA','1982-04-10',NULL,
                         1,N'BASELINE_FONTE_UNICA',SYSUTCDATETIME(),N'REFERENCIA',N'PARCIAL'),
                        (@p4,NULL,N'SEM_CPF',N'MARIA SOUZA','1982-04-10',N'BIA LIMA',
                         1,N'BASELINE_FONTE_UNICA',SYSUTCDATETIME(),N'REFERENCIA',N'COMPLETO');
                    INSERT identidade.blocking_chave(
                        pessoa_uuid,normalizacao_versao,atributo,valor_normalizado,semantica_temporal)
                    VALUES
                        (@p1,@norm,@name,N'MARIA SOUZA',N'VERSIONED_ALIAS'),
                        (@p1,@norm,@mother,N'ANA LIMA',N'VERSIONED_ALIAS'),
                        (@p1,@norm,@year,N'1982',N'STABLE_IDENTITY_DATUM'),
                        (@p2,@norm,@name,N'MARIA SOUZA LIMA',N'VERSIONED_ALIAS'),
                        (@p2,@norm,@mother,N'ANA LIMA',N'VERSIONED_ALIAS'),
                        (@p2,@norm,@year,N'1982',N'STABLE_IDENTITY_DATUM'),
                        (@p3,@norm,@name,N'MARIA LIMA',N'VERSIONED_ALIAS'),
                        (@p3,@norm,@year,N'1982',N'STABLE_IDENTITY_DATUM'),
                        (@p4,@norm,@name,N'MARIA SOUZA',N'VERSIONED_ALIAS'),
                        (@p4,@norm,@mother,N'BIA LIMA',N'VERSIONED_ALIAS'),
                        (@p4,@norm,@year,N'1982',N'STABLE_IDENTITY_DATUM');
                    """;
                seed.Parameters.Add("@p1", System.Data.SqlDbType.UniqueIdentifier).Value = mariaSouza;
                seed.Parameters.Add("@p2", System.Data.SqlDbType.UniqueIdentifier).Value = mariaSouzaLima;
                seed.Parameters.Add("@p3", System.Data.SqlDbType.UniqueIdentifier).Value = mariaLimaNoMother;
                seed.Parameters.Add("@p4", System.Data.SqlDbType.UniqueIdentifier).Value = homonym;
                seed.Parameters.Add("@norm", System.Data.SqlDbType.NVarChar, 80).Value =
                    IdentityComparison.NormalizationVersion;
                seed.Parameters.Add("@name", System.Data.SqlDbType.NVarChar, 80).Value =
                    BlockingFeatureNames.FullName;
                seed.Parameters.Add("@mother", System.Data.SqlDbType.NVarChar, 80).Value =
                    BlockingFeatureNames.MotherFullName;
                seed.Parameters.Add("@year", System.Data.SqlDbType.NVarChar, 80).Value =
                    BlockingFeatureNames.BirthYear;
                await seed.ExecuteNonQueryAsync();
            }

            var dynamicPass = new BlockingCandidatePassLookup("dt17-maria-d",
            [
                new BlockingCandidateClause(BlockingFeatureNames.FullName,
                    ["MARIA SOUZA", "MARIA LIMA"])
            ]);
            var combinedPass = new BlockingCandidatePassLookup("dt17-maria-c",
            [
                new BlockingCandidateClause(BlockingFeatureNames.FullName,
                    ["MARIA SOUZA", "MARIA SOUZA LIMA", "MARIA LIMA"]),
                new BlockingCandidateClause(BlockingFeatureNames.MotherFullName, ["ANA LIMA"]),
                new BlockingCandidateClause(BlockingFeatureNames.BirthYear, ["1982"])
            ]);
            await using var audit = connection.CreateCommand();
            audit.Transaction = tx;
            var taggedSql = BlockingProjectionCandidateQueryBuilder.BuildTaggedCandidateUuidQuery(
                audit, [dynamicPass], [combinedPass],
                PersonResolutionProjectionContract.SchemaVersion,
                PersonResolutionProjectionContract.FingerprintSha256);
            Assert.That(taggedSql, Does.Not.Contain("MARIA SOUZA"),
                "Valores da consulta efetiva devem permanecer parametrizados.");
            // The production query and its D/C provenance are unchanged. Only
            // the fixture scope is limited to the UUIDs inserted by this test.
            audit.CommandText = BlockingPassAuditCommand.BuildTaggedAuditSql(
                "SELECT t.pessoa_uuid,t.in_dynamic,t.in_combined FROM (" + taggedSql +
                ") AS t WHERE t.pessoa_uuid IN (@p1,@p2,@p3,@p4)");
            audit.Parameters.Add("@p1", System.Data.SqlDbType.UniqueIdentifier).Value = mariaSouza;
            audit.Parameters.Add("@p2", System.Data.SqlDbType.UniqueIdentifier).Value = mariaSouzaLima;
            audit.Parameters.Add("@p3", System.Data.SqlDbType.UniqueIdentifier).Value = mariaLimaNoMother;
            audit.Parameters.Add("@p4", System.Data.SqlDbType.UniqueIdentifier).Value = homonym;
            var truth = audit.Parameters.Add("@truth_uuid", System.Data.SqlDbType.UniqueIdentifier);
            truth.Value = mariaSouzaLima;

            await using (var reader = await audit.ExecuteReaderAsync())
            {
                Assert.That(await reader.ReadAsync(), Is.True);
                Assert.Multiple((TestDelegate)(() =>
                {
                    Assert.That(reader.GetInt64(0), Is.EqualTo(4), "D∪C deduplica quatro UUIDs.");
                    Assert.That(reader.GetInt64(1), Is.EqualTo(3), "D inclui homônimo distinto.");
                    Assert.That(reader.GetInt64(2), Is.EqualTo(2), "C exige mãe e ano.");
                    Assert.That(reader.GetInt64(3), Is.EqualTo(1), "Interseção D∩C explícita.");
                    Assert.That(reader.GetInt32(4), Is.Zero, "MARIA SOUZA LIMA é C-only.");
                    Assert.That(reader.GetInt32(5), Is.EqualTo(1));
                }));
            }

            truth.Value = mariaLimaNoMother;
            await using (var reader = await audit.ExecuteReaderAsync())
            {
                Assert.That(await reader.ReadAsync(), Is.True);
                Assert.That(reader.GetInt32(4), Is.EqualTo(1), "Sem mãe, D ainda encontra.");
                Assert.That(reader.GetInt32(5), Is.Zero, "Sem mãe não pode entrar em C.");
            }
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

}

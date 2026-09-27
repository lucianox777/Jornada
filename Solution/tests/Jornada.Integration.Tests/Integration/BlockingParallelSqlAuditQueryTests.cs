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
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetInt64(0), Is.Zero, "Nenhum candidato sintético deve existir em Gold.");
            Assert.That(reader.GetInt64(1), Is.Zero);
            Assert.That(reader.GetInt64(2), Is.Zero);
            Assert.That(reader.GetInt64(3), Is.Zero);
            Assert.That(reader.GetInt32(4), Is.Zero);
            Assert.That(reader.GetInt32(5), Is.Zero);
        });
    }
}

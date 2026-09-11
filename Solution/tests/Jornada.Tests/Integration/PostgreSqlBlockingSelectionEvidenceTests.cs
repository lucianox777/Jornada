using Jornada.Linkage.Parameters.Worker;
using Jornada.Operational.Sql;
using Npgsql;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration"), Category("PostgreSqlCalibration"), NonParallelizable]
public sealed class PostgreSqlBlockingSelectionEvidenceTests
{
    private static readonly PostgreSqlCalibrationOptions Options = new(8,10,2,0.5m,0.95m,0.03m,60,true);

    [Test]
    public async Task Draft_PersistsBlockingSelectionEvidenceOutsideFellegiSunterParameters()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_CONNECTION")
            ?? throw new InvalidOperationException("Conexão de calibração não configurada.");
        if (Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_CALIBRATION_TESTS") != "1" ||
            new NpgsqlConnectionStringBuilder(connectionString).Database != "JornadaPgCalibrationTest")
            throw new InvalidOperationException("Exige opt-in e banco descartável JornadaPgCalibrationTest.");

        var calibrator = new PostgreSqlLinkageCalibrator(new PostgreSqlOperationalAdapter(connectionString));
        var draft = await calibrator.GenerateDraftAsync(Options, CancellationToken.None);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT nome, valor, metodo
                  FROM identidade.estatistica_linkage
                 WHERE modelo_id = @id
                   AND nome LIKE 'BLOCKING_%'
                 ORDER BY nome;
                """;
            command.Parameters.AddWithValue("id", draft.ModelId);

            var evidence = new Dictionary<string,(decimal Value,string Method)>(StringComparer.Ordinal);
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    evidence.Add(reader.GetString(0), (reader.GetDecimal(1), reader.GetString(2)));
            }

            var expectedNames = new[] {
                "BLOCKING_COMPLETE_MATCH_COVERAGE",
                "BLOCKING_COMPLETE_NON_MATCH_COVERAGE",
                "BLOCKING_DISTINCT_FIELD_COUNT",
                "BLOCKING_EFFECTIVE_OBSERVED_WEIGHT",
                "BLOCKING_FIELD_CLAUSE_COUNT",
                "BLOCKING_NON_MATCH_RETENTION",
                "BLOCKING_PASS_COUNT",
                "BLOCKING_REDUCTION_RATIO",
                "BLOCKING_TRUE_MATCH_RECALL"
            };
            Assert.That(evidence.Keys.OrderBy(static x => x, StringComparer.Ordinal), Is.EqualTo(expectedNames));
            Assert.Multiple(() => {
                Assert.That(evidence["BLOCKING_TRUE_MATCH_RECALL"].Value, Is.InRange(0m,1m));
                Assert.That(evidence["BLOCKING_NON_MATCH_RETENTION"].Value, Is.InRange(0m,1m));
                Assert.That(evidence["BLOCKING_REDUCTION_RATIO"].Value, Is.InRange(0m,1m));
                Assert.That(evidence["BLOCKING_COMPLETE_MATCH_COVERAGE"].Value, Is.InRange(0m,1m));
                Assert.That(evidence["BLOCKING_COMPLETE_NON_MATCH_COVERAGE"].Value, Is.InRange(0m,1m));
                Assert.That(evidence["BLOCKING_EFFECTIVE_OBSERVED_WEIGHT"].Value, Is.GreaterThan(0m));
                Assert.That(evidence["BLOCKING_PASS_COUNT"].Value, Is.GreaterThanOrEqualTo(1m));
                Assert.That(evidence["BLOCKING_FIELD_CLAUSE_COUNT"].Value, Is.GreaterThanOrEqualTo(evidence["BLOCKING_PASS_COUNT"].Value));
                Assert.That(evidence["BLOCKING_DISTINCT_FIELD_COUNT"].Value, Is.GreaterThanOrEqualTo(1m));
            });
            Assert.That(evidence.Where(static x => x.Key is not "BLOCKING_PASS_COUNT" and not "BLOCKING_FIELD_CLAUSE_COUNT" and not "BLOCKING_DISTINCT_FIELD_COUNT")
                .All(static x => x.Value.Method == BlockingRuleSetDiagnostic.MethodVersion), Is.True);
            Assert.That(evidence.Where(static x => x.Key is "BLOCKING_PASS_COUNT" or "BLOCKING_FIELD_CLAUSE_COUNT" or "BLOCKING_DISTINCT_FIELD_COUNT")
                .All(static x => x.Value.Method == "BLOCKING_RULESET_COMPLEXITY_V1"), Is.True);

            await using var parameterCheck = connection.CreateCommand();
            parameterCheck.CommandText = """
                SELECT COUNT(*)
                  FROM identidade.parametro_linkage
                 WHERE modelo_id = @id
                   AND nome LIKE 'BLOCKING_%';
                """;
            parameterCheck.Parameters.AddWithValue("id", draft.ModelId);
            Assert.That(Convert.ToInt64(await parameterCheck.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture), Is.Zero);
        }
        finally
        {
            // Este teste compartilha o banco descartável com as regressões de ciclo de vida.
            // Remova o rascunho mesmo quando uma asserção falhar para que uma falha não
            // contamine os fixtures seguintes e esconda a causa raiz.
            await CleanupDraftAsync(connection, draft.ModelId);
        }
    }

    private static async Task CleanupDraftAsync(NpgsqlConnection connection, Guid modelId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM identidade.linkage_ruleset_passe_campo
             WHERE ruleset_id IN (SELECT ruleset_id FROM identidade.linkage_ruleset WHERE modelo_id=@id);
            DELETE FROM identidade.linkage_ruleset_passe
             WHERE ruleset_id IN (SELECT ruleset_id FROM identidade.linkage_ruleset WHERE modelo_id=@id);
            DELETE FROM identidade.linkage_ruleset WHERE modelo_id=@id;
            DELETE FROM identidade.frequencia_linkage WHERE modelo_id=@id;
            DELETE FROM identidade.estatistica_linkage WHERE modelo_id=@id;
            DELETE FROM identidade.parametro_linkage WHERE modelo_id=@id;
            DELETE FROM identidade.calibracao_linkage WHERE modelo_id=@id;
            DELETE FROM identidade.modelo_linkage WHERE modelo_id=@id;
            """;
        command.Parameters.AddWithValue("id", modelId);
        await command.ExecuteNonQueryAsync();
    }
}

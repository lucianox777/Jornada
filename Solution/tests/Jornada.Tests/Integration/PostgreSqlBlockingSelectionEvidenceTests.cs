using System.Security.Cryptography;
using System.Text;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Operational.Sql;
using Npgsql;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration"), Category("PostgreSqlBlockingSelectionEvidence"), NonParallelizable]
public sealed class PostgreSqlBlockingSelectionEvidenceTests
{
    private static readonly PostgreSqlCalibrationOptions Options = new(8,10,2,0.5m,0.95m,0.03m,60,true);
    private const string ProjectionFingerprint = "838b108f654d9c49f02a6a293ed13ca8add2fe20dcf3d5f312769d3b576177ce";

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

        await AssertReplayManifestAsync(connection, draft.ModelId);
    }

    private static async Task AssertReplayManifestAsync(NpgsqlConnection connection, Guid modelId)
    {
        Assert.That(
            BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan.Fingerprint,
            Is.EqualTo(ProjectionFingerprint),
            "O fingerprint congelado no DDL deve mudar somente junto com uma nova versão do schema de projeção.");

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rm.manifest_version,rm.calibrator_version,rm.projection_schema_version,rm.projection_fingerprint,
                   rm.algorithm_catalog_version,rm.comparator_catalog_version,
                   rm.blocking_plan_version,rm.blocking_plan_fingerprint,
                   rm.person_source_id,rm.person_source_version,rm.person_content_fingerprint,
                   rm.training_source_id,rm.training_source_version,rm.training_content_fingerprint,
                   rm.external_snapshots_json::text,rm.manifest_fingerprint,
                   c.amostra_m_sha256,c.amostra_u_sha256
              FROM identidade.calibracao_replay_manifest rm
              JOIN identidade.calibracao_linkage c ON c.modelo_id=rm.modelo_id
             WHERE rm.modelo_id=@id;
            """;
        command.Parameters.AddWithValue("id", modelId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True, "Todo RASCUNHO deve possuir manifesto de replay.");

        var manifestVersion = reader.GetString(0);
        var calibratorVersion = reader.GetString(1);
        var projectionSchemaVersion = reader.GetString(2);
        var projectionFingerprint = reader.GetString(3);
        var algorithmCatalogVersion = reader.GetString(4);
        var comparatorCatalogVersion = reader.GetString(5);
        var blockingPlanVersion = reader.GetString(6);
        var blockingPlanFingerprint = reader.GetString(7);
        var personSourceId = reader.GetString(8);
        var personSourceVersion = reader.GetString(9);
        var personFingerprint = reader.GetString(10);
        var trainingSourceId = reader.GetString(11);
        var trainingSourceVersion = reader.GetString(12);
        var trainingFingerprint = reader.GetString(13);
        var externalJson = reader.GetString(14);
        var databaseManifestFingerprint = reader.GetString(15);
        var mHash = reader.GetString(16);
        var uHash = reader.GetString(17);
        Assert.That(await reader.ReadAsync(), Is.False, "Deve existir exatamente um manifesto por modelo.");

        var expectedTrainingFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"M={mHash}\nU={uHash}\n"))).ToLowerInvariant();
        var expected = CalibrationReplayManifest.Create(
            calibratorVersion,
            BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan,
            blockingPlanVersion,
            blockingPlanFingerprint,
            new CalibrationSourceSnapshot(CalibrationSourceKind.PersonData, personSourceId, personSourceVersion, personFingerprint),
            new CalibrationSourceSnapshot(CalibrationSourceKind.TrainingCorpus, trainingSourceId, trainingSourceVersion, trainingFingerprint));

        Assert.Multiple(() =>
        {
            Assert.That(manifestVersion, Is.EqualTo(CalibrationReplayManifest.CurrentManifestVersion));
            Assert.That(calibratorVersion, Is.EqualTo("POSTGRESQL_LINKAGE_CALIBRATOR_V3"));
            Assert.That(projectionSchemaVersion, Is.EqualTo(BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan.SchemaVersion));
            Assert.That(projectionFingerprint, Is.EqualTo(ProjectionFingerprint));
            Assert.That(algorithmCatalogVersion, Is.EqualTo(HomologatedResolutionAlgorithmCatalog.CatalogVersion));
            Assert.That(comparatorCatalogVersion, Is.EqualTo(HomologatedResolutionComparatorCatalog.CatalogVersion));
            Assert.That(personSourceId, Is.EqualTo("gold.pessoa"));
            Assert.That(trainingSourceId, Is.EqualTo("linkage_training_corpus"));
            Assert.That(trainingFingerprint, Is.EqualTo(expectedTrainingFingerprint));
            Assert.That(externalJson, Is.EqualTo("[]"));
            Assert.That(databaseManifestFingerprint, Is.EqualTo(expected.Fingerprint),
                "PostgreSQL e CalibrationReplayManifest.Create devem produzir o mesmo contrato canônico.");
        });
    }
}

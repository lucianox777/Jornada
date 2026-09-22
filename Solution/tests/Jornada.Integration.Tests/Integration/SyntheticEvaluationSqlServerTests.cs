using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Evaluation;
using Microsoft.Data.SqlClient;
using NUnit.Framework;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration"), NonParallelizable]
public sealed class SyntheticEvaluationSqlServerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    [Test]
    public async Task Post_draft_evaluator_recovers_exact_truth_without_emitting_row_level_identifiers()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareAsync(connectionString);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var modelId = Guid.NewGuid();
        var version = await NextModelVersionAsync(connection);
        await InsertDraftModelAsync(connection, modelId, version);

        var root = Path.Combine(
            Path.GetTempPath(),
            "jornada-synthetic-evaluation-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteSyntheticFixture(root);

            var evaluator = new SyntheticEvaluationEngine(connection, 60);
            var report = await evaluator.EvaluateAsync(
                new SyntheticEvaluationOptions(
                    modelId,
                    root,
                    MaxCandidatePairs: 1_000,
                    CommandTimeoutSeconds: 60));

            var json = JsonSerializer.Serialize(report, JsonOptions);

            Assert.Multiple(() =>
            {
                Assert.That(report.Model.Status, Is.EqualTo("RASCUNHO"));
                Assert.That(report.Model.ModelId, Is.EqualTo(modelId));
                Assert.That(report.Blocking.TrueInterSourcePairs, Is.EqualTo(2));
                Assert.That(report.Blocking.TruePairsRetainedByUnion, Is.EqualTo(2));
                Assert.That(report.Blocking.TrueMatchRecall, Is.EqualTo(1m));
                Assert.That(report.Blocking.PossibleNonMatchPairs, Is.EqualTo(4));
                Assert.That(report.Blocking.CandidateUnionPairs, Is.EqualTo(6));
                Assert.That(report.Blocking.CandidateUnionNonMatchPairs, Is.EqualTo(4));
                Assert.That(report.Blocking.NonMatchRetention, Is.EqualTo(1m));
                Assert.That(report.MRecovery.PairCount, Is.EqualTo(1));
                Assert.That(report.MRecovery.TruthRaw.Name["EXACT"], Is.EqualTo(1m));
                Assert.That(report.URecovery.PairCount, Is.EqualTo(4));
                Assert.That(report.URecovery.TruthRaw.Name["LOW"], Is.EqualTo(1m));
                Assert.That(report.Transportability.CpfLabeledPairs, Is.EqualTo(1));
                Assert.That(report.Transportability.CpfAbsentPairs, Is.EqualTo(1));
                Assert.That(report.ObservationStrata.CpfPresentCnsPresent, Is.EqualTo(1));
                Assert.That(report.ObservationStrata.CpfPresentCnsAbsent, Is.EqualTo(1));
                Assert.That(report.ObservationStrata.CpfAbsentCnsPresent, Is.EqualTo(1));
                Assert.That(report.ObservationStrata.CpfAbsentCnsAbsent, Is.EqualTo(1));
                Assert.That(json, Does.Not.Contain("P-TRUE-1"));
                Assert.That(json, Does.Not.Contain("P-TRUE-2"));
                Assert.That(json, Does.Not.Contain("OBS-1"));
                Assert.That(report.EvaluatorVersion, Is.EqualTo(SyntheticEvaluationEngine.EvaluatorVersion));
                Assert.That(report.EnvironmentProfile, Is.EqualTo("Development"));
                Assert.That(report.Input.GeneratorSeed, Is.EqualTo(42UL));
                Assert.That(report.Input.GenerationManifestSha256, Has.Length.EqualTo(64));
                Assert.That(report.Model.ModelSnapshotSha256, Has.Length.EqualTo(64));
                Assert.That(json, Does.Not.Contain("OBS-2"));
            });

            var reportJson = JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine;
            var reportSha = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(reportJson))).ToLowerInvariant();
            var runGroupId = Guid.NewGuid();
            var writer = new SyntheticEvaluationEvidenceWriter(connection, 60);
            var persisted = await writer.PersistAsync(report, reportSha, runGroupId);
            var repeated = await writer.PersistAsync(report, reportSha, runGroupId);

            Assert.Multiple(() =>
            {
                Assert.That(persisted.EvaluationId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(persisted.RunGroupId, Is.EqualTo(runGroupId));
                Assert.That(repeated.EvaluationId, Is.EqualTo(persisted.EvaluationId));
            });

            await using (var persistedCheck = connection.CreateCommand())
            {
                persistedCheck.CommandText = """
                    SELECT e.ambiente_perfil,e.status,e.gerador_seed,e.validacao_estatistica,
                           e.promocao_autorizada,COUNT(m.linkage_avaliacao_sintetica_metrica_id)
                    FROM auditoria.linkage_avaliacao_sintetica e
                    JOIN auditoria.linkage_avaliacao_sintetica_metrica m
                      ON m.avaliacao_id=e.avaliacao_id
                    WHERE e.avaliacao_id=@evaluation_id
                    GROUP BY e.ambiente_perfil,e.status,e.gerador_seed,
                             e.validacao_estatistica,e.promocao_autorizada;
                    """;
                persistedCheck.Parameters.AddWithValue("@evaluation_id", persisted.EvaluationId);
                await using var reader = await persistedCheck.ExecuteReaderAsync();
                Assert.That(await reader.ReadAsync(), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(reader.GetString(0), Is.EqualTo("Development"));
                    Assert.That(reader.GetString(1), Is.EqualTo("CONCLUIDA"));
                    Assert.That(reader.GetDecimal(2), Is.EqualTo(42m));
                    Assert.That(reader.GetString(3), Is.EqualTo("NOT_ASSESSED_ISSUE_31"));
                    Assert.That(reader.GetBoolean(4), Is.False);
                    Assert.That(reader.GetInt32(5), Is.GreaterThan(40));
                });
            }

            await using (var immutable = connection.CreateCommand())
            {
                immutable.CommandText = """
                    UPDATE auditoria.linkage_avaliacao_sintetica
                    SET status=N'CONCLUIDA'
                    WHERE avaliacao_id=@evaluation_id;
                    """;
                immutable.Parameters.AddWithValue("@evaluation_id", persisted.EvaluationId);
                var error = Assert.ThrowsAsync<SqlException>(async () => await immutable.ExecuteNonQueryAsync());
                Assert.That(error!.Number, Is.EqualTo(51911));
            }

            await using (var immutableMetric = connection.CreateCommand())
            {
                immutableMetric.CommandText = """
                    DELETE FROM auditoria.linkage_avaliacao_sintetica_metrica
                    WHERE avaliacao_id=@evaluation_id;
                    """;
                immutableMetric.Parameters.AddWithValue("@evaluation_id", persisted.EvaluationId);
                var error = Assert.ThrowsAsync<SqlException>(async () => await immutableMetric.ExecuteNonQueryAsync());
                Assert.That(error!.Number, Is.EqualTo(51912));
            }

            await using (var schema = connection.CreateCommand())
            {
                schema.CommandText = """
                    SELECT LOWER(c.name)
                    FROM sys.columns c
                    WHERE c.object_id IN(
                        OBJECT_ID(N'auditoria.linkage_avaliacao_sintetica'),
                        OBJECT_ID(N'auditoria.linkage_avaliacao_sintetica_metrica'));
                    """;
                var columns = new List<string>();
                await using var reader = await schema.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    columns.Add(reader.GetString(0));

                Assert.Multiple(() =>
                {
                    Assert.That(columns, Does.Contain("modelo_id"));
                    Assert.That(columns, Does.Contain("report_sha256"));
                    Assert.That(columns, Does.Contain("valor"));
                    Assert.That(columns, Does.Not.Contain("base_person_id"));
                    Assert.That(columns, Does.Not.Contain("observation_id"));
                    Assert.That(columns, Does.Not.Contain("cpf"));
                    Assert.That(columns, Does.Not.Contain("cns"));
                    Assert.That(columns, Does.Not.Contain("nome"));
                    Assert.That(columns, Does.Not.Contain("data_nascimento"));
                    Assert.That(columns, Does.Not.Contain("score_par"));
                });
            }

            await using (var environment = connection.CreateCommand())
            {
                environment.CommandText = """
                    EXEC sys.sp_updateextendedproperty
                        @name=N'Jornada.EnvironmentProfile',
                        @value=N'HML';
                    """;
                await environment.ExecuteNonQueryAsync();
            }

            try
            {
                var secondReportSha = new string('f', 64);
                var environmentError = Assert.ThrowsAsync<SqlException>(async () =>
                    await writer.PersistAsync(report, secondReportSha, Guid.NewGuid()));
                Assert.That(environmentError!.Number, Is.EqualTo(51914));
            }
            finally
            {
                await using var restoreEnvironment = connection.CreateCommand();
                restoreEnvironment.CommandText = """
                    EXEC sys.sp_updateextendedproperty
                        @name=N'Jornada.EnvironmentProfile',
                        @value=N'Development';
                    """;
                await restoreEnvironment.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Evaluator_refuses_non_draft_model_before_reading_truth()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareAsync(connectionString);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var activeModelId = await ScalarGuidAsync(
            connection,
            "SELECT TOP(1) modelo_id FROM identidade.modelo_linkage WHERE status=N'ATIVO' ORDER BY versao DESC;");

        var root = Path.Combine(
            Path.GetTempPath(),
            "jornada-synthetic-evaluation-status-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteSyntheticFixture(root);
            var evaluator = new SyntheticEvaluationEngine(connection, 60);
            var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await evaluator.EvaluateAsync(
                    new SyntheticEvaluationOptions(
                        activeModelId,
                        root,
                        MaxCandidatePairs: 1_000,
                        CommandTimeoutSeconds: 60)));

            Assert.That(error!.Message, Does.Contain("somente modelo RASCUNHO"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task PrepareAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
        await using var environment = connection.CreateCommand();
        environment.CommandText = """
            IF EXISTS(
                SELECT 1 FROM sys.extended_properties
                WHERE class=0 AND name=N'Jornada.EnvironmentProfile')
                EXEC sys.sp_updateextendedproperty
                    @name=N'Jornada.EnvironmentProfile',
                    @value=N'Development';
            ELSE
                EXEC sys.sp_addextendedproperty
                    @name=N'Jornada.EnvironmentProfile',
                    @value=N'Development';
            """;
        await environment.ExecuteNonQueryAsync();
    }

    private static async Task<int> NextModelVersionAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand(
            "SELECT ISNULL(MAX(versao),0)+1 FROM identidade.modelo_linkage;",
            connection);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task InsertDraftModelAsync(
        SqlConnection connection,
        Guid modelId,
        int version)
    {
        await using (var command = new SqlCommand(
                         """
                         INSERT identidade.modelo_linkage(
                           modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
                           deduplicacao_metodo,base_referencia,registros_lidos,pessoas_unicas,
                           gerado_em,amostra_metodo,amostra_m_tamanho,amostra_u_tamanho)
                         VALUES(
                           @model_id,@version,N'RASCUNHO',@algorithm,@normalization,
                           N'TEST_SYNTHETIC_DEDUP',N'SYNTHETIC_TEST',4,2,
                           SYSDATETIMEOFFSET(),N'TEST_SYNTHETIC_SAMPLE',1,4);

                         INSERT identidade.linkage_ruleset(
                           ruleset_id,modelo_id,ruleset_versao,algoritmo_versao,fingerprint_sha256)
                         VALUES(
                           @ruleset_id,@model_id,N'SYNTHETIC_TEST_RULESET_V1',@algorithm,REPLICATE('a',64));

                         INSERT identidade.linkage_ruleset_passe(
                           ruleset_id,passe_ordem,passe_id)
                         VALUES(@ruleset_id,0,N'P_BIRTH_YEAR');

                         INSERT identidade.linkage_ruleset_passe_campo(
                           ruleset_id,passe_ordem,campo_ordem,atributo)
                         VALUES(@ruleset_id,0,0,N'birth_year');
                         """,
                         connection))
        {
            command.Parameters.Add("@model_id", SqlDbType.UniqueIdentifier).Value = modelId;
            command.Parameters.Add("@ruleset_id", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
            command.Parameters.Add("@version", SqlDbType.Int).Value = version;
            command.Parameters.Add("@algorithm", SqlDbType.NVarChar, 80).Value =
                LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion;
            command.Parameters.Add("@normalization", SqlDbType.NVarChar, 80).Value =
                IdentityComparison.NormalizationVersion;
            await command.ExecuteNonQueryAsync();
        }

        var parameters = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["M_NOME_EXACT"] = .70m,
            ["M_NOME_HIGH"] = .10m,
            ["M_NOME_MEDIUM"] = .10m,
            ["M_NOME_LOW"] = .10m,
            ["U_NOME_EXACT"] = .10m,
            ["U_NOME_HIGH"] = .10m,
            ["U_NOME_MEDIUM"] = .10m,
            ["U_NOME_LOW"] = .70m,

            ["M_NOME_MAE_EXACT"] = .70m,
            ["M_NOME_MAE_HIGH"] = .10m,
            ["M_NOME_MAE_MEDIUM"] = .05m,
            ["M_NOME_MAE_LOW"] = .05m,
            ["M_NOME_MAE_MISSING"] = .10m,
            ["U_NOME_MAE_EXACT"] = .10m,
            ["U_NOME_MAE_HIGH"] = .10m,
            ["U_NOME_MAE_MEDIUM"] = .10m,
            ["U_NOME_MAE_LOW"] = .60m,
            ["U_NOME_MAE_MISSING"] = .10m
        };

        foreach (var state in BirthDateSemanticEvidence.States)
        {
            parameters[$"M_NASCIMENTO_SEMANTICO_{state}"] =
                state == BirthDateSemanticEvidence.Exact ? .70m : .05m;
            parameters[$"U_NASCIMENTO_SEMANTICO_{state}"] =
                state == BirthDateSemanticEvidence.Exact ? .10m : .15m;
        }

        foreach (var (name, value) in parameters)
        {
            await using var command = new SqlCommand(
                "INSERT identidade.parametro_linkage(modelo_id,nome,valor) VALUES(@model_id,@name,@value);",
                connection);
            command.Parameters.Add("@model_id", SqlDbType.UniqueIdentifier).Value = modelId;
            command.Parameters.Add("@name", SqlDbType.NVarChar, 100).Value = name;
            var parameter = command.Parameters.Add("@value", SqlDbType.Decimal);
            parameter.Precision = 30;
            parameter.Scale = 12;
            parameter.Value = value;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static void WriteSyntheticFixture(string root)
    {
        var corpus = Path.Combine(root, "corpus");
        var ingestion = Path.Combine(root, "ingestion");
        Directory.CreateDirectory(corpus);
        Directory.CreateDirectory(ingestion);

        var generationManifest = new
        {
            schema_version = 1,
            generator_version = "JORNADA_SYNTH_CORPUS_CSHARP_V1",
            ruleset_version = "JORNADA_SYNTH_CORPUS_V2_RULES_CSHARP_V1",
            rng_version = "XOSHIRO256SS_SPLITMIX64_V1",
            seed = 42UL,
            input_fingerprint_sha256 = new string('b', 64)
        };
        File.WriteAllText(
            Path.Combine(corpus, "generation-manifest.json"),
            JsonSerializer.Serialize(generationManifest, JsonOptions));

        File.WriteAllText(
            Path.Combine(corpus, "observacoes.csv"),
            """
            observacao_id,base_person_id,particao,gestor,nome,nome_mae,data_nascimento,sexo,cpf,cns,corrupcoes,evaluation_weight
            OBS-1,P-TRUE-1,TRAIN,G0,MARIA SILVA,ANA SILVA,1980-01-02,F,11144477735,700000000000007,,1
            OBS-2,P-TRUE-1,TRAIN,G1,MARIA SILVA,ANA SILVA,1980-01-02,F,11144477735,,,1
            OBS-3,P-TRUE-2,TRAIN,G2,CARLOS SOUZA,BIA SOUZA,1980-05-06,M,,700000000000018,,1
            OBS-4,P-TRUE-2,TRAIN,G3,CARLOS SOUZA,BIA SOUZA,1980-05-06,M,,,,1
            """);

        var truthRows = new[]
        {
            new { Status = "MATERIALIZADA", ObservationId = "OBS-1", BasePersonId = "P-TRUE-1" },
            new { Status = "MATERIALIZADA", ObservationId = "OBS-2", BasePersonId = "P-TRUE-1" },
            new { Status = "MATERIALIZADA", ObservationId = "OBS-3", BasePersonId = "P-TRUE-2" },
            new { Status = "MATERIALIZADA", ObservationId = "OBS-4", BasePersonId = "P-TRUE-2" }
        };
        File.WriteAllText(
            Path.Combine(ingestion, "bridge-truth.jsonl"),
            string.Join(
                Environment.NewLine,
                truthRows.Select(row => JsonSerializer.Serialize(row))) + Environment.NewLine);

        var manifest = new
        {
            BridgeVersion = "SYNTHETIC_INGESTION_BRIDGE_V1",
            GeneratorVersion = "JORNADA_SYNTH_CORPUS_CSHARP_V1",
            RulesetVersion = "JORNADA_SYNTH_CORPUS_V2_RULES_CSHARP_V1",
            RngVersion = "XOSHIRO256SS_SPLITMIX64_V1",
            SourceObservationCount = 4,
            MaterializedObservationCount = 4,
            ExcludedObservationCount = 0,
            CorpusInputFingerprintSha256 = new string('b', 64)
        };
        File.WriteAllText(
            Path.Combine(ingestion, "bridge-manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static async Task<Guid> ScalarGuidAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        return (Guid)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Consulta não retornou UUID."));
    }

    private static string RequireIntegrationConnection() =>
        Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION")
        ?? throw new InvalidOperationException("JORNADA_TEST_SQL_CONNECTION não configurada.");
}

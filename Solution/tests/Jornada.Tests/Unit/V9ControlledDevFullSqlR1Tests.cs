using System.Data;
using System.Text.Json;
using Jornada.Bronze.Storage;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Linkage.Runner;
using Jornada.Operational.Sql;
using Jornada.Pipeline.Coordination;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jornada.Tests.Unit;

/// <summary>
/// Actual FULL -> frozen Silver graph -> independent FS -> raw linkage_resultado
/// -> DT-10 Gold publication in a DISTINCT per-attempt SQL database. This is a
/// synthetic DEV smoke, NOT scientific TEST/10k/production activation.
/// Never skip: only invoked by the existing disposable SQL CI job after setup.
/// </summary>
[TestFixture, Category("SQLSmoke")]
public sealed class V9ControlledDevFullSqlR1Tests
{
    [Test]
    public async Task FULL_200_real_SQL_publishes_Gold_with_frozen_r1_and_no_implicit_identity_inheritance()
    {
        var source = Environment.GetEnvironmentVariable("JORNADA_V9_FULL_SCHEMA_NONPUBLISH_SQL");
        var dbName = Environment.GetEnvironmentVariable("JORNADA_V9_DEV_ATTEMPT_DB");
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(dbName))
            throw new InvalidOperationException(
                "RUNNER_V9_DEV_FULL_PHYSICAL_SQL_CI_NOT_PROVISIONED: no Assert.Ignore or fake PASS.");
        var prefix = "JornadaV9Dev_";
        if (!dbName.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(dbName[prefix.Length..], "N", out var attempt)
            || attempt == Guid.Empty
            || new SqlConnectionStringBuilder(source).InitialCatalog != "JornadaHarness")
            throw new InvalidOperationException("RUNNER_V9_DEV_SQL_MUST_BE_DISPOSABLE");

        var builder = new SqlConnectionStringBuilder(source) { InitialCatalog = dbName };
        var sql = new OperationalSqlAdapter(builder.ConnectionString);
        await using var connection = await sql.OpenAsync(CancellationToken.None);
        await V9ControlledDevSelection.VerifyDisposableAttemptDatabaseAsync(
            connection, attempt, CancellationToken.None);
        await using (var check = new SqlCommand("""
            IF OBJECT_ID(N'identidade.linkage_run',N'U') IS NULL
              OR OBJECT_ID(N'identidade.linkage_resultado',N'U') IS NULL
              OR OBJECT_ID(N'gold.pessoa',N'U') IS NULL
              OR OBJECT_ID(N'identidade.modelo_linkage_v9_operacional',N'U') IS NULL
              OR OBJECT_ID(N'identidade.sp_publicar_resolucao_progressiva_linkage_lote',N'P') IS NULL
              THROW 52901,'Esquema SQL DEV integral ou DT10 ausente.',1;
            IF EXISTS (SELECT 1 FROM silver.pessoa_observacao)
              OR EXISTS (SELECT 1 FROM identidade.linkage_run)
              OR EXISTS (SELECT 1 FROM gold.pessoa)
              THROW 52902,'Tentativa SQL DEV já possui dados; não reutilizar.',1;
            """, connection))
            await check.ExecuteNonQueryAsync();

        var winner = V9FittedSnapshotTests.CompleteTf4OperationalWinner();
        var stored = await V9OperationalSqlModelConverter.MaterializeOnceAsync(connection, winner);
        if (stored.ModelId != V9OperationalSqlModelConverter.ModelIdFromWinnerSha(winner.ContentSha256))
            throw new InvalidDataException("RUNNER_V9_DEV_MODEL_SHA_MISMATCH");
        await using (var anchor = new SqlCommand("""
            INSERT identidade.modelo_linkage(
              modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
              deduplicacao_metodo,base_referencia,snapshot_referencia,gerado_em)
            VALUES(@id,9,N'RASCUNHO',@algorithm,@normalization,
              N'V9_DEV_R1_DISPOSABLE',N'SYNTHETIC_FULL_ONLY',
              @sha,SYSDATETIMEOFFSET());
            """, connection))
        {
            anchor.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = stored.ModelId;
            anchor.Parameters.Add("@algorithm", SqlDbType.NVarChar, 120).Value =
                LinkageParameterCatalog.CompositeNominalDecisionEvidenceAlgorithmVersion;
            anchor.Parameters.Add("@normalization", SqlDbType.NVarChar, 80).Value =
                IdentityComparison.NormalizationVersion;
            anchor.Parameters.Add("@sha", SqlDbType.Char, 64).Value = winner.ContentSha256;
            Assert.That(await anchor.ExecuteNonQueryAsync(), Is.EqualTo(1));
        }

        // Reuses the established 200-observation SQL FKs/Processor-style origin
        // fixture; no Gold reference or synthetic TEST truth is populated.
        await V9FullSchemaNonPublishingRunnerTests.SeedControlledDevFullFixtureAsync(connection, 200);

        var temp = Path.Combine(Path.GetTempPath(),
            "jornada-v9-dev-full-r1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var receipt = await V9ValidatedOperationalModelFile.ExportAsync(winner, temp);
            var selected = Path.Combine(temp, "selection.json");
            await File.WriteAllTextAsync(selected, JsonSerializer.Serialize(
                new V9ConfiguredArtifactLoader.Selection(receipt.FileName, winner.ContentSha256)));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Linkage:SelectedModelVersion"] = "9",
                    ["Linkage:V9SelectionFile"] = selected,
                    ["LinkageReplay:CaptureBronzeSources"] = "false"
                }).Build();
            var scorer = await V9ControlledDevCohortScorer.CreateAsync(
                configuration, sql, attempt, CancellationToken.None);
            var runner = new ProbabilisticLinkageBatchRunner(
                configuration, sql, scorer,
                new SqlPipelineCoordinator(sql), new Dt05ReplaySql(sql),
                new Dt05ReplayManifestPublisher(new FileSystemBronzeObjectStore(temp), temp),
                new Dt05CandidateStateSnapshotPublisher(sql, temp),
                new Dt05BlockingProjectionSnapshotPublisher(sql, temp),
                new Dt05HistoricalCandidateStateVerifier(temp),
                new Dt05HistoricalBlockingProjectionVerifier(temp),
                NullLogger<ProbabilisticLinkageBatchRunner>.Instance);
            var request = new ProbabilisticLinkageRunRequest(
                LinkageRunType.FULL, 9, null, null, null, 100, 1, null,
                "V9_DEV_DISPOSABLE_CI", "R1_FULL_REAL_SQL_GOLD_ONLY",
                Guid.NewGuid(), true);
            var result = await runner.RunV9ControlledDevAsync(
                request, attempt, CancellationToken.None);
            Assert.Multiple((Action)(() =>
            {
                Assert.That(result.Status, Is.EqualTo(LinkageRunStatus.PUBLICADO));
                Assert.That(result.Elegiveis, Is.EqualTo(200));
                Assert.That(result.Avaliados, Is.EqualTo(200));
                Assert.That(result.Resolvidos + result.NaoResolvidos + result.Conflitos,
                    Is.EqualTo(result.Elegiveis));
            }));
            await using var verify = new SqlCommand("""
                SELECT
                  (SELECT COUNT_BIG(*) FROM identidade.linkage_resultado
                   WHERE linkage_run_id=@run),
                  (SELECT COUNT_BIG(*) FROM identidade.linkage_resultado
                   WHERE linkage_run_id=@run AND publicado_em IS NOT NULL
                     AND resultado_publicacao IS NOT NULL
                     AND status_publicacao IS NOT NULL
                     AND motivo_publicacao IS NOT NULL),
                  (SELECT COUNT_BIG(*) FROM identidade.pessoa_origem_progressiva
                   WHERE estado=N'REFERENCIA' AND canonical_uuid IS NOT NULL),
                  (SELECT COUNT_BIG(*) FROM gold.pessoa
                   WHERE estado_identidade=N'REFERENCIA');
                """, connection);
            verify.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = result.RunId;
            await using var rows = await verify.ExecuteReaderAsync();
            Assert.That(await rows.ReadAsync(), Is.True);
            Assert.Multiple((Action)(() =>
            {
                Assert.That(rows.GetInt64(0), Is.EqualTo(200),
                    "Every frozen Silver observation must have one raw SQL result.");
                Assert.That(rows.GetInt64(1), Is.EqualTo(200),
                    "Actual DT-10 publication must complete every result.");
                Assert.That(rows.GetInt64(2), Is.GreaterThan(0),
                    "At least one origin must be published to a reference.");
                Assert.That(rows.GetInt64(3), Is.GreaterThan(0),
                    "Gold must reflect physically published reference identities.");
            }));
            Assert.That(await rows.ReadAsync(), Is.False);
            TestContext.WriteLine(
                "V9_DEV_FULL_R1_PHYSICAL_SQL_GOLD_PASS: real FULL run, " +
                "200 Silver origins, frozen graph/FS, raw results, DT-10, Gold; " +
                "TEST/10k/production not executed");
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}

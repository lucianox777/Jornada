using System.Data;
using System.Globalization;
using Jornada.Bronze.Storage;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Operational.Sql;
using Jornada.Pipeline.Coordination;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Stage 7: explicit one-use FULL in one isolated DEV 1k attempt.
/// Not a productive Runner activation or a TEST/training command.
/// Program.cs default V9 fail-closed branch remains unchanged.
/// </summary>
public static class V9DevE2EFullCommand
{
    public const string Flag = "--v9-dev-full-1k";

    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 1 || args[0] != Flag
            || Environment.GetEnvironmentVariable("JORNADA_RUNTIME_MODE") != "DEV"
            || Environment.GetEnvironmentVariable("JORNADA_E2E_V9_DEV_FULL") != "true"
            || Environment.GetEnvironmentVariable("JORNADA_E2E_HTTP_VOLUME_COUNT") != "1000"
            || Environment.GetEnvironmentVariable("JORNADA_E2E_IBGE_1K") != "true"
            || Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") != "Development"
            || !Guid.TryParseExact(
                Environment.GetEnvironmentVariable("JORNADA_V9_DEV_ATTEMPT"), "D",
                out var attempt) || attempt == Guid.Empty)
            throw new InvalidOperationException("RUNNER_V9_DEV_E2E_FULL_1K_EXPLICIT_ADMISSION_REQUIRED");

        var host = Host.CreateApplicationBuilder(args);
        var config = host.Configuration;
        var connectionString = config.GetConnectionString("Jornada")
            ?? throw new InvalidOperationException("RUNNER_V9_DEV_E2E_SQL_CONNECTION_MISSING");
        var sql = new OperationalSqlAdapter(connectionString);

        // This independently checks the exact database name, resident
        // Development property, attempt UUID, pinned V9 selection and SHA
        // BEFORE staging any SQL model or writing a linkage_run.
        var boundary = await V9ControlledDevSelection.VerifyAsync(
            config, sql, attempt, CancellationToken.None);
        var loaded = await V9ConfiguredArtifactLoader.LoadAsync(
            config["Linkage:V9SelectionFile"]!, CancellationToken.None);
        if (loaded.Snapshot.ContentSha256 != boundary.Runtime.WinnerSha256)
            throw new InvalidDataException("RUNNER_V9_DEV_E2E_WINNER_SHA_DRIFT");

        // Integration only after the real Processor has persisted the received
        // 1k IBGE bridge cohort and an independent private preflight accepted it.
        var preflightPath = Environment.GetEnvironmentVariable("JORNADA_V9_DEV_1K_PREFLIGHT");
        if (string.IsNullOrWhiteSpace(preflightPath)
            || !File.Exists(preflightPath)
            || new FileInfo(preflightPath).LinkTarget is not null)
            throw new InvalidDataException("RUNNER_V9_DEV_E2E_1K_PREFLIGHT_MISSING");
        using (var json = System.Text.Json.JsonDocument.Parse(
            await File.ReadAllBytesAsync(preflightPath)))
        {
            var root = json.RootElement;
            if (!root.TryGetProperty("baseIdentities", out var baseIdentities)
                || baseIdentities.GetInt32() != 1000
                || !root.TryGetProperty("materializedObservations", out var sourceObservations)
                || sourceObservations.GetInt32() <= 0
                || root.GetProperty("seed").GetInt32() != 355
                || root.GetProperty("status").GetString() != "PASS")
                throw new InvalidDataException("RUNNER_V9_DEV_E2E_1K_PREFLIGHT_INVALID");
        }

        await using (var connection = await sql.OpenAsync(CancellationToken.None))
        {
            await V9ControlledDevSelection.VerifyDisposableAttemptDatabaseAsync(
                connection, attempt, CancellationToken.None);
            // This attempt must not import an operational winner already chosen
            // by another caller. RASCUNHO is never activated.
            await using (var admission = new SqlCommand("""
                SELECT CASE WHEN
                    NOT EXISTS(SELECT 1 FROM identidade.modelo_linkage_v9_operacional)
                    AND NOT EXISTS(SELECT 1 FROM identidade.modelo_linkage WHERE versao=9)
                    AND NOT EXISTS(SELECT 1 FROM identidade.linkage_run)
                    AND EXISTS(SELECT 1 FROM silver.pessoa_observacao
                               WHERE cpf IS NULL AND pessoa_origem_id IS NOT NULL)
                    AND NOT EXISTS(SELECT 1 FROM ingestao.lote
                                   WHERE status NOT IN(N'PROCESSADO',N'CONCLUIDO'))
                  THEN 1 ELSE 0 END;
                """, connection))
            {
                if (Convert.ToInt32(await admission.ExecuteScalarAsync(),
                    CultureInfo.InvariantCulture) != 1)
                    throw new InvalidDataException("RUNNER_V9_DEV_E2E_PROCESSOR_OR_UNIVERSE_INCOMPLETE");
            }
            var receipt = await V9OperationalSqlModelConverter.MaterializeOnceAsync(
                connection, loaded.Snapshot, CancellationToken.None);
            if (receipt.ModelId != boundary.Runtime.ModelId || receipt.Active || receipt.Published)
                throw new InvalidDataException("RUNNER_V9_DEV_E2E_MODEL_NOT_TEST_ONLY");
            await using var anchor = new SqlCommand("""
                INSERT identidade.modelo_linkage(
                    modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
                    deduplicacao_metodo,base_referencia,snapshot_referencia,gerado_em)
                VALUES(@id,9,N'RASCUNHO',@algo,@normalization,
                    N'V9_DEV_E2E_DISPOSABLE',N'IBGE_1K_DEV_AFTER_PROCESSOR',
                    @sha,SYSDATETIMEOFFSET());
                """, connection);
            anchor.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = receipt.ModelId;
            anchor.Parameters.Add("@algo", SqlDbType.NVarChar, 120).Value =
                LinkageParameterCatalog.CompositeNominalDecisionEvidenceAlgorithmVersion;
            anchor.Parameters.Add("@normalization", SqlDbType.NVarChar, 80).Value =
                IdentityComparison.NormalizationVersion;
            anchor.Parameters.Add("@sha", SqlDbType.Char, 64).Value = loaded.WinnerSha256;
            if (await anchor.ExecuteNonQueryAsync() != 1)
                throw new InvalidDataException("RUNNER_V9_DEV_E2E_MODEL_LEDGER_NOT_INSERTED");

            // No silent fallback from existing Gold indexes of a different
            // projection. Stage 7 cannot equate "zero candidates" with an
            // unmaterialized or stale Gold V9 reference.
            await using var physicalGold = new SqlCommand("""
                SELECT COUNT_BIG(*)
                  FROM gold.pessoa g
                 WHERE g.estado_identidade=N'REFERENCIA'
                   AND g.cpf IS NULL
                   AND NOT EXISTS(
                       SELECT 1 FROM identidade.blocking_chave k
                        WHERE k.pessoa_uuid=g.pessoa_uuid
                          AND k.projection_schema_version=@schema
                          AND k.projection_fingerprint_sha256=@fingerprint);
                """, connection);
            physicalGold.Parameters.Add("@schema", SqlDbType.NVarChar, 120).Value =
                loaded.Snapshot.Payload.ProjectionSchemaVersion;
            physicalGold.Parameters.Add("@fingerprint", SqlDbType.Char, 64).Value =
                loaded.Snapshot.Payload.ProjectionFingerprintSha256;
            if (Convert.ToInt64(await physicalGold.ExecuteScalarAsync(),
                CultureInfo.InvariantCulture) != 0)
                throw new InvalidDataException("RUNNER_V9_DEV_E2E_GOLD_V9_BLOCKING_INCOMPLETE");
        }

        var scopedRoot = Path.Combine(Path.GetTempPath(),
            "jornada-v9-e2e-full-" + attempt.ToString("N"));
        if (Directory.Exists(scopedRoot))
            throw new InvalidOperationException("RUNNER_V9_DEV_E2E_TEMP_ALREADY_EXISTS");
        Directory.CreateDirectory(scopedRoot);
        var scorer = await V9ControlledDevCohortScorer.CreateAsync(
            config, sql, attempt, CancellationToken.None);
        var store = new FileSystemBronzeObjectStore(scopedRoot);
        var runner = new ProbabilisticLinkageBatchRunner(
            config, sql, scorer, new SqlPipelineCoordinator(sql),
            new Dt05ReplaySql(sql),
            new Dt05ReplayManifestPublisher(store, scopedRoot),
            new Dt05CandidateStateSnapshotPublisher(sql, scopedRoot),
            new Dt05BlockingProjectionSnapshotPublisher(sql, scopedRoot),
            new Dt05HistoricalCandidateStateVerifier(scopedRoot),
            new Dt05HistoricalBlockingProjectionVerifier(scopedRoot),
            NullLogger<ProbabilisticLinkageBatchRunner>.Instance);
        var summary = await runner.RunV9ControlledDevAsync(
            new ProbabilisticLinkageRunRequest(
                LinkageRunType.FULL, 9, null, null, null, 100, 1, null,
                "V9_DEV_E2E_FULL_1K", "AFTER_PROCESSOR_IBGE_SYNTHETIC",
                Guid.NewGuid(), true),
            attempt, CancellationToken.None);
        if (summary.Status != LinkageRunStatus.PUBLICADO
            || summary.Elegiveis <= 0 || summary.Avaliados != summary.Elegiveis)
            throw new InvalidDataException("RUNNER_V9_DEV_E2E_FULL_PUBLICATION_INCOMPLETE");

        // No PII, per-person truth, model contents, connection string or
        // SQL credentials in public process logs.
        Console.WriteLine(FormattableString.Invariant(
            $"V9_DEV_E2E_FULL_1K_PASS run_id={summary.RunId:D} " +
            $"evaluated={summary.Avaliados} winner_sha256={loaded.WinnerSha256}"));
    }
}

using System.Data;
using System.Text.Json;
using Jornada.Bronze.Storage;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Jornada.Pipeline.Coordination;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Run-once DEV-only HTTP/Processor -> frozen V9 FULL r1 -> DT-10 on the
/// SAME disposable JornadaE2E database. This is a separate opt-in entrypoint;
/// productive Program.cs V9 remains fail-closed and INCREMENTAL is unsupported.
/// No fitting, scientific TRAIN/TEST, or implicit selection can occur.
/// </summary>
public static class V9HttpE2e1kFullCommand
{
    public const string Flag = "--v9-dev-e2e-1k-full";

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value : throw new InvalidOperationException(
                "RUNNER_V9_E2E_1K_REQUIRED_" + name);

    private static void Exact(string name, string expected)
    {
        if (Required(name) != expected)
            throw new InvalidOperationException("RUNNER_V9_E2E_1K_SCOPE_REJECTED_" + name);
    }

    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 1 || args[0] != Flag)
            throw new InvalidOperationException("RUNNER_V9_E2E_1K_ARGUMENTS_FORBIDDEN");
        Exact("DOTNET_ENVIRONMENT", "Development");
        Exact("JORNADA_E2E_V9_DEV_FULL", "true");
        Exact("JORNADA_E2E_HTTP_VOLUME_COUNT", "1000");
        Exact("JORNADA_E2E_IBGE_1K", "true");
        Exact("JORNADA_E2E_V9_RUN_MODE", "FULL");
        var attempt = Guid.ParseExact(Required("JORNADA_V9_DEV_ATTEMPT_ID"), "D");
        if (attempt == Guid.Empty)
            throw new InvalidOperationException("RUNNER_V9_E2E_1K_ATTEMPT_INVALID");

        var selection = Path.GetFullPath(Required("JORNADA_E2E_V9_SELECTION_FILE"));
        if (!Path.IsPathFullyQualified(selection) || !File.Exists(selection))
            throw new InvalidOperationException("RUNNER_V9_E2E_1K_WINNER_FILE_REQUIRED");
        var rawConnection = Required("ConnectionStrings__Jornada");
        var connection = new SqlConnectionStringBuilder(rawConnection);
        if (connection.InitialCatalog != "JornadaE2E"
            || !(connection.DataSource.StartsWith("localhost,", StringComparison.OrdinalIgnoreCase)
                 || connection.DataSource.StartsWith("127.0.0.1,", StringComparison.Ordinal)))
            throw new InvalidOperationException("RUNNER_V9_E2E_1K_LOCAL_DB_REQUIRED");
        var evidenceRoot = Path.GetFullPath(Required("JORNADA_E2E_V9_EVIDENCE_ROOT"));
        if (!Directory.Exists(evidenceRoot))
            throw new InvalidOperationException("RUNNER_V9_E2E_1K_EVIDENCE_ROOT_REQUIRED");
        var receiptPath = Path.Combine(evidenceRoot, "v9-e2e-dev-1k-summary.json");
        if (File.Exists(receiptPath))
            throw new InvalidOperationException("RUNNER_V9_E2E_1K_RECEIPT_ALREADY_EXISTS");

        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Linkage:SelectedModelVersion"] = "9",
                ["Linkage:V9SelectionFile"] = selection,
                ["LinkageReplay:CaptureBronzeSources"] = "false"
            }).Build();
        var sql = new OperationalSqlAdapter(rawConnection);
        var verified = await V9ControlledDevSelection.VerifyHttpE2e1kAsync(
            config, sql, attempt, CancellationToken.None);

        // Exactly one synthetic IBGE 1k source population after the Processor
        // completed all four HTTP deliveries; no other pending CPF-less source
        // is admitted. The absolute source count may reach 4k for 1k people.
        await using (var db = await sql.OpenAsync(CancellationToken.None))
        {
            await using (var preflight = new SqlCommand("""
                IF OBJECT_ID(N'identidade.modelo_linkage_v9_operacional',N'U') IS NULL
                  OR OBJECT_ID(N'identidade.sp_publicar_resolucao_progressiva_linkage_lote',N'P') IS NULL
                  THROW 52941,'V9 E2E SQL schema incomplete; refuse run.',1;
                SELECT
                  (SELECT COUNT_BIG(*) FROM silver.pessoa_observacao po
                    WHERE po.cpf IS NULL),
                  (SELECT COUNT_BIG(*) FROM silver.pessoa_observacao po
                    JOIN ingestao.lote l ON l.lote_id=po.lote_id
                    WHERE po.cpf IS NULL AND po.codigo_pessoa_origem LIKE N'SYNTH-%'
                      AND l.status=N'PROCESSADO'),
                  (SELECT COUNT_BIG(*) FROM identidade.linkage_run),
                  (SELECT COUNT_BIG(*) FROM identidade.modelo_linkage_v9_operacional);
                """, db))
            {
                await using var rows = await preflight.ExecuteReaderAsync();
                if (!await rows.ReadAsync())
                    throw new InvalidDataException("RUNNER_V9_E2E_1K_MISSING_SQL_COUNTS");
                var all = rows.GetInt64(0);
                var ibge = rows.GetInt64(1);
                if (all < 1000 || all > 4000 || all != ibge
                    || rows.GetInt64(2) != 0 || rows.GetInt64(3) != 0)
                    throw new InvalidDataException(
                        "RUNNER_V9_E2E_1K_PROCESSOR_OR_SELECTION_NOT_COMPLETE");
            }

            // V9 winner must be a previously pinned unmodified file.
            // The converter writes one TEST_ONLY metadata record, never
            // activates a productive model or refits anything.
            var pin = await V9OperationalSqlModelConverter.MaterializeOnceAsync(
                db, verified.Runtime.FrozenWinner, CancellationToken.None);
            await using var anchor = new SqlCommand("""
                INSERT identidade.modelo_linkage(
                    modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
                    deduplicacao_metodo,base_referencia,snapshot_referencia,gerado_em)
                VALUES(@id,9,N'RASCUNHO',@algorithm,@normalization,
                    N'V9_E2E_DEV_1K_ONLY',N'HTTP_IBGE_1K_DISPOSABLE',
                    @sha,SYSDATETIMEOFFSET());
                """, db);
            anchor.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = pin.ModelId;
            anchor.Parameters.Add("@algorithm", SqlDbType.NVarChar, 120).Value =
                LinkageParameterCatalog.CompositeNominalDecisionEvidenceAlgorithmVersion;
            anchor.Parameters.Add("@normalization", SqlDbType.NVarChar, 80).Value =
                IdentityComparison.NormalizationVersion;
            anchor.Parameters.Add("@sha", SqlDbType.Char, 64).Value = pin.SourceSnapshotSha256;
            if (await anchor.ExecuteNonQueryAsync() != 1)
                throw new InvalidDataException("RUNNER_V9_E2E_1K_MODEL_ANCHOR_FAILED");
        }

        var scorer = await V9ControlledDevCohortScorer.CreateHttpE2e1kAsync(
            config, sql, attempt, CancellationToken.None);
        var pipeline = new ProbabilisticLinkageBatchRunner(
            config, sql, scorer, new SqlPipelineCoordinator(sql), new Dt05ReplaySql(sql),
            new Dt05ReplayManifestPublisher(
                new FileSystemBronzeObjectStore(evidenceRoot), evidenceRoot),
            new Dt05CandidateStateSnapshotPublisher(sql, evidenceRoot),
            new Dt05BlockingProjectionSnapshotPublisher(sql, evidenceRoot),
            new Dt05HistoricalCandidateStateVerifier(evidenceRoot),
            new Dt05HistoricalBlockingProjectionVerifier(evidenceRoot),
            NullLogger<ProbabilisticLinkageBatchRunner>.Instance);

        var result = await pipeline.RunV9HttpE2e1kAsync(
            new ProbabilisticLinkageRunRequest(
                LinkageRunType.FULL, 9, null, null, null, 100, 1, null,
                "V9_E2E_DEV_1K", "HTTP_PROCESSOR_SILVER_FULL_R1",
                Guid.NewGuid(), true),
            attempt, CancellationToken.None);
        if (result.Status != LinkageRunStatus.PUBLICADO
            || result.Elegiveis < 1000 || result.Elegiveis > 4000
            || result.Avaliados != result.Elegiveis)
            throw new InvalidDataException("RUNNER_V9_E2E_1K_RUN_NOT_FULLY_PUBLISHED");

        await using (var db = await sql.OpenAsync(CancellationToken.None))
        await using (var counts = new SqlCommand("""
            SELECT COUNT_BIG(*),
                   SUM(CASE WHEN publicado_em IS NOT NULL
                             AND resultado_publicacao IS NOT NULL
                             AND status_publicacao IS NOT NULL THEN 1 ELSE 0 END)
              FROM identidade.linkage_resultado
             WHERE linkage_run_id=@run;
            """, db))
        {
            counts.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = result.RunId;
            await using var rows = await counts.ExecuteReaderAsync();
            if (!await rows.ReadAsync()
                || rows.IsDBNull(0) || rows.IsDBNull(1)
                || rows.GetInt64(0) != result.Elegiveis
                || rows.GetInt32(1) != result.Elegiveis)
                throw new InvalidDataException("RUNNER_V9_E2E_1K_SQL_PUBLICATION_INCOMPLETE");
        }

        // Sanitized aggregate receipt only. All synthetic source identifiers,
        // raw XML/JSON and truth sidecar remain under the disposable E2E root.
        var receipt = new
        {
            schema = "V9_E2E_DEV_1K_FULL_R1_V1",
            status = "PASS",
            scope = "HTTP_1K_PROCESSOR_TO_V9_FULL_DT10_DEV_ONLY",
            mode = "FULL", runId = result.RunId,
            winnerSha256 = verified.Runtime.WinnerSha256,
            eligible = result.Elegiveis,
            evaluated = result.Avaliados,
            resolved = result.Resolvidos,
            unresolved = result.NaoResolvidos,
            conflicts = result.Conflitos,
            publish = true, scientificTest = false,
            activation = false, productionCertified = false,
            sha = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? ""
        };
        await File.WriteAllTextAsync(receiptPath,
            JsonSerializer.Serialize(receipt) + "\n");
        Console.WriteLine("V9_E2E_DEV_1K_FULL_R1_SQL_PASS: published count and run verified");
    }
}

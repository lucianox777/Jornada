using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Jornada.Bronze.Storage;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Operational.Sql;
using Jornada.Pipeline.Coordination;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jornada.Linkage.Runner;

/// <summary>Only the explicitly admitted, fresh DEV 1k attempt. No productive dispatch.</summary>
public static class V9DevE2EFullCommand
{
    public const string Flag = "--v9-dev-full-1k";
    public const string ScaleFlag = "--v9-dev-full-10k";
    internal static bool ScaleAdmitted =>
        Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" &&
        Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") == "lucianox777/Jornada" &&
        Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME") == "pull_request" &&
        Environment.GetEnvironmentVariable("JORNADA_V9_PHASE4_MATRIX") == "true" &&
        Environment.GetEnvironmentVariable("JORNADA_V9_PHASE4_SCALE") is "comparable" or "challenge" &&
        Environment.GetEnvironmentVariable("JORNADA_E2E_HTTP_VOLUME_COUNT") == "10000";
    private static string stage = "admission";
    private const string Winner = "6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee";

    public static async Task RunAsync(string[] args)
    {
        var scale = args.Length == 2 && args[0] == ScaleFlag && ScaleAdmitted;
        if (args.Length != 2 || (!scale && args[0] != Flag) || args[1] is not ("catalog" or "full" or "matrix")
            || Environment.GetEnvironmentVariable("JORNADA_RUNTIME_MODE") != "DEV"
            || Environment.GetEnvironmentVariable("JORNADA_E2E_V9_DEV_FULL") != "true"
            || (!scale && Environment.GetEnvironmentVariable("JORNADA_E2E_HTTP_VOLUME_COUNT") != "1000")
            || Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") != "Development"
            || !Guid.TryParseExact(Environment.GetEnvironmentVariable("JORNADA_V9_DEV_ATTEMPT"),
                "D", out var attempt) || attempt == Guid.Empty)
            throw new InvalidOperationException("RUNNER_V9_DEV_E2E_FULL_1K_EXPLICIT_ADMISSION_REQUIRED");

        stage = "configuration";
        var config = Host.CreateApplicationBuilder(Array.Empty<string>()).Configuration;
        var sql = new OperationalSqlAdapter(config.GetConnectionString("Jornada")
            ?? throw new InvalidOperationException("RUNNER_V9_DEV_E2E_SQL_CONNECTION_MISSING"));
        if (!config.GetValue<bool>(V9BlockingMaintenanceGate.EnabledSetting)
            || config[V9BlockingMaintenanceGate.AttemptSetting] != attempt.ToString("D")
            || config.GetValue<bool>(V9ControlledDevCohortScorer.DiagnosticFixtureSetting)
            || config.GetValue("LinkageReplay:CaptureBronzeSources", false))
            throw new InvalidOperationException(V9BlockingMaintenanceGate.ProductiveNotCertified);
        stage = "selection";
        var boundary = await V9ControlledDevSelection.VerifyAsync(config, sql, attempt, CancellationToken.None);
        if (boundary.Runtime.WinnerSha256 != Winner)
            throw new InvalidDataException("RUNNER_V9_DEV_E2E_WINNER_MISMATCH");
        var root = Environment.GetEnvironmentVariable("JORNADA_V9_E2E_PRIVATE_ROOT");
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)
            || !Directory.Exists(root) || new DirectoryInfo(root).LinkTarget is not null)
            throw new InvalidDataException("RUNNER_V9_DEV_E2E_PRIVATE_ROOT_REQUIRED");

        stage = "catalog";
        if (args[1] == "catalog")
        {
            // Catalog batch was derived by the fixed source-pin script. The
            // batch repeats the exact attempt and empty-state verification.
            await using var connection = await sql.OpenAsync(CancellationToken.None);
            await V9ControlledDevSelection.VerifyDisposableAttemptDatabaseAsync(connection, attempt, CancellationToken.None);
            var catalog = await ReadPrivateAsync(root, "catalog.sql");
            if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(catalog))) != "a976dfda5ef42e15120d1318f80fd3274f54c85310567e5081fab6f73126f728")
                throw new InvalidDataException("RUNNER_V9_DEV_E2E_CATALOG_PIN_MISMATCH");
            await using var command = new SqlCommand(catalog, connection);
            command.Parameters.Add("@attempt", SqlDbType.UniqueIdentifier).Value = attempt;
            await command.ExecuteNonQueryAsync();
            Console.WriteLine("V9_E2E_CATALOG_EXECUTED");
            return;
        }

        stage = "lineage";
        using var pre = JsonDocument.Parse(await ReadPrivateAsync(root, "wave-preflight-receipt.json"));
        using var lineage = JsonDocument.Parse(await ReadPrivateAsync(root, "http-lineage.json"));
        var p = pre.RootElement;
        var l = lineage.RootElement;
        var total = p.GetProperty("sourceVersions").GetInt32();
        var eligible = p.GetProperty("nonCpfVersions").GetInt32();
        if (p.GetProperty("schema").GetString() != "V9_E2E_1K_WAVE_PREFLIGHT_V1"
            || p.GetProperty("status").GetString() != "PASS"
            || p.GetProperty("baseIdentities").GetInt32() != (scale ? 10000 : 1000)
            || total <= eligible || eligible <= 0
            || l.GetProperty("schema").GetString() != "V9_E2E_1K_HTTP_LINEAGE_V1"
            || l.GetProperty("status").GetString() != "PASS"
            || l.GetProperty("sourceVersions").GetInt32() != total
            || l.GetProperty("attempt").GetString() != attempt.ToString("D")
            || l.GetProperty("preflightSha256").GetString() !=
               Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                   await File.ReadAllBytesAsync(Path.Combine(root, "wave-preflight-receipt.json")))))
            throw new InvalidDataException("RUNNER_V9_DEV_E2E_LINEAGE_INVALID");

        stage = "scorer";
        var loaded = await V9ConfiguredArtifactLoader.LoadAsync(config["Linkage:V9SelectionFile"]!, CancellationToken.None);
        if (args[1] == "matrix")
        {
            stage = "matrix";
            await V9Phase4Matrix.ExecuteAsync(root, sql, loaded.Snapshot, attempt, CancellationToken.None);
            return;
        }
        var scorer = await V9ControlledDevCohortScorer.CreateAsync(config, sql, attempt,
            CancellationToken.None, effectiveEnvironment: "Development");
        stage = "pre-full-sql";
        await using (var connection = await sql.OpenAsync(CancellationToken.None))
        {
            await V9ControlledDevSelection.VerifyDisposableAttemptDatabaseAsync(connection, attempt, CancellationToken.None);
            await using var admission = new SqlCommand("""
                SELECT CASE WHEN
                    NOT EXISTS(SELECT 1 FROM identidade.modelo_linkage_v9_operacional)
                    AND NOT EXISTS(SELECT 1 FROM identidade.modelo_linkage WHERE versao=9)
                    AND NOT EXISTS(SELECT 1 FROM identidade.linkage_run)
                    AND NOT EXISTS(SELECT 1 FROM identidade.blocking_chave_v9)
                    AND NOT EXISTS(SELECT 1 FROM identidade.blocking_chave_v9_cobertura)
                    AND (SELECT COUNT_BIG(*) FROM silver.pessoa_observacao)=@total
                    AND (SELECT COUNT_BIG(*) FROM silver.pessoa_observacao WHERE cpf IS NULL)=@eligible
                    AND NOT EXISTS(SELECT 1 FROM silver.pessoa_observacao
                        WHERE pessoa_origem_id IS NULL OR codigo_pessoa_origem NOT LIKE N'SYNTH-%')
                    AND NOT EXISTS(SELECT 1 FROM ingestao.lote WHERE status<>N'PROCESSADO')
                    AND NOT EXISTS(SELECT 1 FROM ingestao.entrega WHERE status<>N'PROCESSADA')
                  THEN 1 ELSE 0 END;
                """, connection);
            admission.Parameters.Add("@total", SqlDbType.BigInt).Value = total;
            admission.Parameters.Add("@eligible", SqlDbType.BigInt).Value = eligible;
            if (Convert.ToInt32(await admission.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 1)
                throw new InvalidDataException("RUNNER_V9_DEV_E2E_PROCESSOR_CENSUS_INVALID");

            await using (var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                await V9BlockingMaintenanceGate.RequireAsync("Development", true, attempt,
                    connection, tx, CancellationToken.None);
                await tx.CommitAsync();
            }
            stage = "model-staging";
        var model = await V9OperationalSqlModelConverter.MaterializeOnceAsync(connection, loaded.Snapshot, CancellationToken.None);
            if (model.ModelId != boundary.Runtime.ModelId || model.Active || model.Published)
                throw new InvalidDataException("RUNNER_V9_DEV_E2E_MODEL_NOT_RASCUNHO");
            await using var anchor = new SqlCommand("""
                INSERT identidade.modelo_linkage(
                    modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
                    deduplicacao_metodo,base_referencia,snapshot_referencia,gerado_em)
                VALUES(@id,9,N'RASCUNHO',@algo,@normalization,
                    N'V9_DEV_E2E_DISPOSABLE',N'IBGE_1K_DEV_AFTER_PROCESSOR',
                    @sha,SYSDATETIMEOFFSET());
                SELECT @@ROWCOUNT;
                """, connection);
            anchor.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = model.ModelId;
            anchor.Parameters.Add("@algo", SqlDbType.NVarChar, 120).Value =
                LinkageParameterCatalog.CompositeNominalDecisionEvidenceAlgorithmVersion;
            anchor.Parameters.Add("@normalization", SqlDbType.NVarChar, 80).Value = IdentityComparison.NormalizationVersion;
            anchor.Parameters.Add("@sha", SqlDbType.Char, 64).Value = Winner;
            if (Convert.ToInt32(await anchor.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 1)
                throw new InvalidDataException("RUNNER_V9_DEV_E2E_MODEL_LEDGER_INVALID");
        }

        if (Environment.GetEnvironmentVariable("JORNADA_V9_PHASE4_MATRIX") == "true")
        {
            stage = "matrix-frame";
            await V9Phase4Matrix.CaptureAsync(root, sql, attempt, CancellationToken.None);
        }
        stage = "pre-full-membership";
        var membership = await V9DevE2EFirstFullMembershipOracle.ReadAsync(
            sql, loaded.Snapshot, attempt, total, eligible, CancellationToken.None);
        await WriteNewAsync(root, "membership-expected.json", JsonSerializer.Serialize(membership));

        stage = "runner-composition";
        object? privateDiagnostic = null;
        Guid observedRun = Guid.Empty;
        scorer.DiagnosticObserver = (runId, ids, edgesSha, groups, directions, goldScores, candidates) =>
        {
            if (privateDiagnostic is not null || !ids.SequenceEqual(membership.ExpectedObservationIds)
                || edgesSha != membership.SilverEdgesSha256
                || directions.Count != 2 * membership.SilverEdges
                || directions.Select(x => (x.Source,x.Target)).Distinct().Count() != directions.Count
                || V9DevE2EGroupOracle.DirectionDigest(directions) != membership.SilverDirectionalReplay!.DirectionSha256)
                throw new InvalidDataException("RUNNER_V9_E2E_ACTUAL_GRAPH_SCORE_DIVERGENCE");
            if (goldScores.Count != membership.GoldRecordScoreCount || goldScores.Count != membership.SecureRecords
                || goldScores.Select(x=>(x.Source,x.Person,x.Record)).Distinct().Count()!=goldScores.Count
                || V9DevE2EFirstFullMembershipOracle.GoldScoreDigest(goldScores)!=membership.GoldRecordScoreSha256)
                throw new InvalidDataException("RUNNER_V9_E2E_ACTUAL_GOLD_RECORD_SCORE_DIVERGENCE");
            var roots = groups.GroupBy(x => x.Value).SelectMany(g =>
                g.Select(x => (Index:x.Key-1,Root:g.Min(y => y.Key)-1)))
                .OrderBy(x => x.Index).Select(x => x.Root).ToArray();
            if (!roots.SequenceEqual(membership.ExpectedGroupRoots))
                throw new InvalidDataException("RUNNER_V9_E2E_ACTUAL_PARTITION_DIVERGENCE");
            var sizes = roots.GroupBy(x => x).ToDictionary(x => x.Key,x => x.Count());
            var neighbors = directions.GroupBy(x=>x.Source).ToDictionary(x=>x.Key,
                x=>x.Select(y=>ids[y.Target-1]).OrderBy(y=>y).ToArray());
            // Private diagnostic only: the weakest *actual* directional
            // posterior inside each complete-link group, not a group probability.
            var internalScores = directions.Where(x=>roots[x.Source-1]==roots[x.Target-1])
                .GroupBy(x=>roots[x.Source-1]).ToDictionary(x=>x.Key,x=>x.ToArray());
            var groupMinimum = new Dictionary<int,decimal?>();
            foreach (var (groupRoot,size) in sizes)
            {
                var scores = internalScores.GetValueOrDefault(groupRoot) ?? Array.Empty<V9DevDirectionalScore>();
                if (scores.Length != size*(size-1) || scores.Any(x=>!x.Posterior.HasValue))
                    throw new InvalidDataException("RUNNER_V9_E2E_GROUP_INTERNAL_SCORE_DIVERGENCE");
                groupMinimum.Add(groupRoot, size==1 ? null : scores.Min(x=>x.Posterior));
            }
            observedRun = runId;
            privateDiagnostic = ids.Select((id,i) => new {
                observationId = id, groupSize = sizes[roots[i]], groupRoot = roots[i],
                candidates = candidates[id].Select(x => x.PessoaUuid).OrderBy(x => x).ToArray(),
                silverNeighbors = neighbors.GetValueOrDefault(i+1) ?? Array.Empty<long>(),
                groupMinPosterior = groupMinimum[roots[i]],
                rankReason = membership.ExpectedRanks[i].Reason
            }).ToArray();
        };
        var storeRoot = Path.Combine(root, "runner-storage");
        if (Directory.Exists(storeRoot))
            throw new InvalidDataException("RUNNER_V9_DEV_E2E_ATTEMPT_REUSE");
        Directory.CreateDirectory(storeRoot);
        var runner = new ProbabilisticLinkageBatchRunner(config, sql, scorer,
            new SqlPipelineCoordinator(sql), new Dt05ReplaySql(sql),
            new Dt05ReplayManifestPublisher(new FileSystemBronzeObjectStore(storeRoot), storeRoot),
            new Dt05CandidateStateSnapshotPublisher(sql, storeRoot),
            new Dt05BlockingProjectionSnapshotPublisher(sql, storeRoot),
            new Dt05HistoricalCandidateStateVerifier(storeRoot),
            new Dt05HistoricalBlockingProjectionVerifier(storeRoot),
            NullLogger<ProbabilisticLinkageBatchRunner>.Instance);
        stage = "full-run";
        var request = new ProbabilisticLinkageRunRequest(
            LinkageRunType.FULL, 9, null, null, null, 100, 1, null,
            scale ? "V9_DEV_E2E_FULL_10K" : "V9_DEV_E2E_FULL_1K", "AFTER_PROCESSOR_IBGE_SYNTHETIC", Guid.NewGuid(), true);
        var matrixEnabled = Environment.GetEnvironmentVariable("JORNADA_V9_PHASE4_MATRIX") == "true";
        double[]? physicalTiming = null;
        if (matrixEnabled)
            scorer.DiagnosticTimingObserver = (read,snapshot,group,score,translate) =>
                physicalTiming = [read,snapshot,group,score,translate];
        var summary = matrixEnabled
            ? await V9Phase4Matrix.MeasurePhysicalFullAsync(root,sql,attempt,
                () => runner.RunV9ControlledDevAsync(request,attempt,CancellationToken.None),
                () => physicalTiming,CancellationToken.None)
            : await runner.RunV9ControlledDevAsync(request,attempt,CancellationToken.None);
        if (summary.Status != LinkageRunStatus.PUBLICADO || summary.Elegiveis != eligible
            || summary.Avaliados != eligible)
            throw new InvalidDataException("RUNNER_V9_DEV_E2E_PUBLICATION_INCOMPLETE");

        if (privateDiagnostic is null || observedRun != summary.RunId)
            throw new InvalidDataException("RUNNER_V9_E2E_ACTUAL_GRAPH_MISSING");
        await WriteNewAsync(root, "fn-private.json", JsonSerializer.Serialize(privateDiagnostic));
        await WriteNewAsync(root, "actual-graph-receipt.json", JsonSerializer.Serialize(new {
            runId = observedRun, edges = membership.SilverEdges, edgesSha256 = membership.SilverEdgesSha256,
            directions = membership.SilverDirectionalReplay!.DirectionsCompared,
            directionSha256 = membership.SilverDirectionalReplay.DirectionSha256,
            groups = membership.IndependentGroupCount, groupSha256 = membership.IndependentGroupSha256,
            actualConsumedSilverEdgesVerified = true, actualConsumedScoresVerified = true,
            actualConsumedGroupsVerified = true,
            goldRecordScores = membership.GoldRecordScoreCount, goldRecordScoreSha256 = membership.GoldRecordScoreSha256,
            actualGoldRecordScoresVerified = true
        }));
        stage = "post-full-census";
        // Only the independent oracle sees the private census; the scorer never reads truth.
        await using (var connection = await sql.OpenAsync(CancellationToken.None))
        {
            await V9ControlledDevSelection.VerifyDisposableAttemptDatabaseAsync(connection, attempt, CancellationToken.None);
            await using var count = new SqlCommand("""
                SELECT CASE WHEN EXISTS(SELECT 1 FROM identidade.blocking_chave_v9)
                  AND (SELECT COUNT_BIG(*) FROM identidade.blocking_chave_v9_cobertura)=1
                  THEN 1 ELSE 0 END;
                """, connection);
            if (Convert.ToInt32(await count.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 1)
                throw new InvalidDataException("RUNNER_V9_DEV_E2E_AUTOMATIC_INDEX_MISSING");
            stage = "scalar-ledger";
            var scalar = await V9DevE2EScalarOracle.VerifyLedgerAsync(connection, summary.RunId,
                membership.ExpectedRanks, CancellationToken.None);
            await WriteNewAsync(root, "scalar-receipt.json", JsonSerializer.Serialize(scalar));
            stage = "post-full-census";
            var query = await ReadPrivateAsync(root, "census.sql");
            if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(query))) != "0b9a080ed9908384341d988e953bb03f4905f68152acaa09a97d2846e0d47ece")
                throw new InvalidDataException("RUNNER_V9_DEV_E2E_CENSUS_PIN_MISMATCH");
            await using var census = new SqlCommand(query, connection)
                { CommandTimeout = 120 };
            census.Parameters.Add("@attempt", SqlDbType.UniqueIdentifier).Value = attempt;
            census.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = summary.RunId;
            var json = new StringBuilder();
            if (scale && Environment.GetEnvironmentVariable("JORNADA_V9_CENSUS_DIAGNOSTIC") == "true")
            {
                await V9DevCensusDiagnostics.ReadAsync(sql, connection, census, json, attempt);
                // Diagnostic-only attempt: no matrix or quality PASS on this SHA.
                throw new InvalidDataException("RUNNER_V9_DEV_E2E_CENSUS_DIAGNOSTIC_ONLY");
            }
            await using (var reader = await census.ExecuteReaderAsync())
                while (await reader.ReadAsync()) json.Append(reader.GetString(0));
            await WriteNewAsync(root, "sql-census.json", json.ToString());
        }
        if (!scale)
        {
            stage = "retry-preview";
            var preview = await V9DevE2ERetryPreview.ReadAsync(sql,loaded.Snapshot,attempt,summary.RunId,CancellationToken.None);
            await WriteNewAsync(root, "retry-private.json", JsonSerializer.Serialize(preview));
        }
        stage = "receipt";
        await WriteNewAsync(root, "runner-receipt.json", JsonSerializer.Serialize(new {
            mode = "FULL", status = "PUBLICADO", winnerSha256 = Winner, scientificTest = false,
            activated = false, eligible = summary.Elegiveis, evaluated = summary.Avaliados,
            runId = summary.RunId, automaticV9Index = true
        }));
        Console.WriteLine("V9_DEV_E2E_FULL_PUBLICATION_PASS_NOT_INTEGRAL");
    }

    public static void ReportFailure(Exception error)
    {
        // Only fixed stage/type and numeric SQL diagnostic. The exception's
        // arbitrary message/stack may contain synthetic attributes or connection data.
        var code = error is SqlException sql
            ? FormattableString.Invariant($"SQL_{sql.Number}_{sql.State}")
            : error.GetType().Name;
        var prefix = System.Text.RegularExpressions.Regex.Match(error.Message,
            @"^(?:RUNNER_V9_[A-Z0-9_]{1,120}|V9_E2E_[A-Z0-9_]{1,120})(?=[:. ]|$)",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
        Console.Error.WriteLine($"V9_DEV_E2E_FAILURE stage={stage} type={code} code={(prefix.Success ? prefix.Value : "UNCLASSIFIED")}");
    }

    private static async Task<string> ReadPrivateAsync(string root, string name)
    {
        var file = new FileInfo(Path.Combine(root, name));
        if (!file.Exists || file.LinkTarget is not null || file.Length is <= 0 or > 1048576)
            throw new InvalidDataException("RUNNER_V9_DEV_E2E_PRIVATE_RECEIPT_INVALID");
        return await File.ReadAllTextAsync(file.FullName);
    }

    private static async Task WriteNewAsync(string root, string name, string json)
    {
        await using var stream = new FileStream(Path.Combine(root, name), FileMode.CreateNew, FileAccess.Write);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(json);
    }
}

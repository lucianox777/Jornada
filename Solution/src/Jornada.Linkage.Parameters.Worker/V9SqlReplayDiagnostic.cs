// Opt-in, isolated performance investigation. Never called by --v9-calibrate or scientific TEST.
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record V9SqlReplayPhaseSample(
    double SignaturesMilliseconds, double CreateTablesMilliseconds,
    double BulkObservationsMilliseconds, double BulkSignaturesMilliseconds,
    double SqlDistinctJoinAndIndexMilliseconds, double ReadEdgesMilliseconds,
    double FrozenScoringGroupingMilliseconds, double TotalMilliseconds,
    long CandidatePairs, string FrozenReplaySha256);

public sealed record V9SqlPlanDiagnostic(
    string ActualPlanSha256, IReadOnlyList<string> PhysicalOperators,
    IReadOnlyList<string> ReferencedIndexes, double SeparateIndexBuildMilliseconds,
    long ReconciledCandidatePairs, long? LogicalReads, long? PhysicalReads,
    long? CpuMilliseconds, long? SqlElapsedMilliseconds);

public sealed record V9SqlReplayBatchDiagnostic(
    int Workers, int ReplaysPerWorker, int ReplaysMeasured, double WallSeconds,
    double CompleteReplaysPerSecond, double TotalP95Milliseconds,
    double SqlMaterializeP95Milliseconds, double FrozenScoringGroupingP95Milliseconds,
    double CreateTablesAverageMilliseconds, double BulkObservationsAverageMilliseconds,
    double BulkSignaturesAverageMilliseconds, double ReadEdgesAverageMilliseconds,
    double SignaturesAverageMilliseconds, string FrozenReplaySha256,
    long CandidatePairs, bool AllReplaysByteStable);

public static partial class V9PhysicalSqlAdaptiveCalibrationProbe
{
    /// <summary>
    /// Reuses the exact private SQL and C# operations from the frozen replay, with timers
    /// ONLY in this diagnostic entrypoint. Unlike the official scientific P95, these
    /// phase samples exclude summary metrics and are never admitted for model selection.
    /// </summary>
    public static async Task<V9SqlReplayPhaseSample> DiagnoseFrozenReplayPhasesAsync(
        SqlConnection sql, IReadOnlyCollection<V9PhysicalCalibrationObservation> validation,
        V9FittedSnapshot snapshot, V9PhysicalAdaptiveExecution execution, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(execution);
        if (validation.Count < 4 || validation.Any(x => x.Partition != "VALIDATION")
            || validation.Select(x => x.ObservationId).Distinct().Count() != validation.Count)
            throw new InvalidDataException("Diagnóstico só admite VALIDATION sintética íntegra.");
        execution.Validate(true, true);
        var fingerprint = execution.MaximumPrefixLength == 0
            ? Jornada.Contracts.PersonResolutionProjectionContract.FingerprintSha256
            : V9CalibrationBlockingProjection.ComputeSchemaFingerprint(execution.MaximumPrefixLength);
        if (snapshot.Payload.ProjectionFingerprintSha256 != fingerprint)
            throw new InvalidDataException("Fingerprint de projeção do fit divergente.");
        await RequireHarnessAsync(sql, ct);
        var total = Stopwatch.StartNew();
        var phase = Stopwatch.StartNew();

        var signatures = BuildSignatures(validation, snapshot.Payload.BlockingPasses,
            execution.MaximumPrefixLength, execution.MaximumProjectedSignatures);
        var incidences = CountPairIncidences(signatures, validation,
            execution.MaximumCandidatePairIncidences);
        if (incidences > execution.MaximumCandidatePairIncidences)
            throw new InvalidDataException("Censo físico excede limite; nenhuma truncagem.");
        var signatureMs = phase.Elapsed.TotalMilliseconds;
        phase.Restart();
        await CreateTablesAsync(sql, ct);
        var createMs = phase.Elapsed.TotalMilliseconds;
        phase.Restart();
        await BulkObservationsAsync(sql, validation, ct);
        var bulkObsMs = phase.Elapsed.TotalMilliseconds;
        phase.Restart();
        await BulkSignaturesAsync(sql, signatures, ct);
        var bulkSigMs = phase.Elapsed.TotalMilliseconds;
        phase.Restart();
        var pairs = await MaterializePairsAsync(sql, ct);
        var materializeMs = phase.Elapsed.TotalMilliseconds;
        phase.Restart();
        var edges = await ReadValidationPairsAsync(sql, ct);
        var readMs = phase.Elapsed.TotalMilliseconds;
        if (pairs != edges.Count)
            throw new InvalidDataException("Censo SQL versus arestas não reconciliou.");
        phase.Restart();
        var replay = V9FrozenSourceResolveReplay.Replay(snapshot, validation, edges);
        var digest = Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(replay))).ToLowerInvariant();
        var csharpMs = phase.Elapsed.TotalMilliseconds;
        return new(signatureMs, createMs, bulkObsMs, bulkSigMs, materializeMs,
            readMs, csharpMs, total.Elapsed.TotalMilliseconds, pairs, digest);
    }

    /// <summary>
    /// One EXTRA, excluded, instrumented materialization. The physical SELECT
    /// is identical to MaterializePairsAsync, but separated from CREATE INDEX to
    /// capture its actual SQL execution plan; it is not used in the P95 samples.
    /// </summary>
    public static async Task<V9SqlPlanDiagnostic> DiagnoseActualSqlPlanAsync(
        SqlConnection sql, long expectedPairs, CancellationToken ct)
    {
        await RequireHarnessAsync(sql, ct);
        await using (var drop = new SqlCommand(
            "IF OBJECT_ID('tempdb..#v9_cal_pairs') IS NOT NULL DROP TABLE #v9_cal_pairs;", sql))
            await drop.ExecuteNonQueryAsync(ct);
        var messages = new List<string>();
        SqlInfoMessageEventHandler handler = (_, args) => messages.Add(args.Message);
        sql.InfoMessage += handler;
        string? planXml = null;
        try
        {
            await using (var turnOn = new SqlCommand(
                "SET STATISTICS XML ON; SET STATISTICS IO ON; SET STATISTICS TIME ON;", sql))
                await turnOn.ExecuteNonQueryAsync(ct);
            await using (var select = new SqlCommand(
                """
                SELECT DISTINCT s1.obs_id AS a,s2.obs_id AS b,s1.part
                INTO #v9_cal_pairs
                FROM #v9_cal_sig s1
                JOIN #v9_cal_sig s2
                  ON s2.part=s1.part
                 AND s2.pass_id=s1.pass_id
                 AND s2.assinatura=s1.assinatura
                 AND s2.obs_id>s1.obs_id
                JOIN #v9_cal_obs a ON a.obs_id=s1.obs_id
                JOIN #v9_cal_obs b ON b.obs_id=s2.obs_id
                WHERE a.gestor<>b.gestor;
                """, sql) { CommandTimeout = 300 })
            await using (var reader = await select.ExecuteReaderAsync(ct))
            {
                do
                {
                    while (await reader.ReadAsync(ct))
                    {
                        if (reader.FieldCount != 1 || reader.IsDBNull(0)) continue;
                        var item = reader.GetValue(0);
                        var value = item is System.Data.SqlTypes.SqlXml xml
                            ? xml.Value : Convert.ToString(item, CultureInfo.InvariantCulture);
                        if (value?.Contains("ShowPlanXML", StringComparison.Ordinal) == true)
                            planXml = value;
                    }
                } while (await reader.NextResultAsync(ct));
            }
        }
        finally
        {
            await using var off = new SqlCommand(
                "SET STATISTICS XML OFF; SET STATISTICS IO OFF; SET STATISTICS TIME OFF;", sql);
            await off.ExecuteNonQueryAsync(ct);
            sql.InfoMessage -= handler;
        }
        if (string.IsNullOrWhiteSpace(planXml))
            throw new InvalidDataException("SQL Server não devolveu plano real do SELECT DISTINCT.");
        var document = XDocument.Parse(planXml);
        var operators = document.Descendants()
            .Where(e => e.Name.LocalName == "RelOp")
            .Select(e => (string?)e.Attribute("PhysicalOp") ?? "")
            .Where(x => x.Length != 0).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var indexes = document.Descendants().Where(e => e.Name.LocalName == "Object")
            .Select(e => ((string?)e.Attribute("Index") ?? "").Trim('[', ']'))
            .Where(x => x.Length > 0).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var indexTimer = Stopwatch.StartNew();
        await using (var create = new SqlCommand(
            "CREATE UNIQUE CLUSTERED INDEX IX_v9_cal_pairs ON #v9_cal_pairs(part,a,b);", sql)
            { CommandTimeout = 300 })
            await create.ExecuteNonQueryAsync(ct);
        var indexMs = indexTimer.Elapsed.TotalMilliseconds;
        await using var count = new SqlCommand("SELECT COUNT_BIG(*) FROM #v9_cal_pairs;", sql);
        var actualPairs = Convert.ToInt64(await count.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        if (actualPairs != expectedPairs)
            throw new InvalidDataException("Plano diagnóstico alterou cardinalidade dos pares.");
        var stats = string.Join("\n", messages);
        static long? SumMatches(string input, string pattern)
        {
            var list = Regex.Matches(input, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (list.Count == 0) return null;
            return list.Cast<Match>().Sum(x => long.Parse(x.Groups[1].Value, CultureInfo.InvariantCulture));
        }
        return new(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(planXml))).ToLowerInvariant(),
            operators, indexes, indexMs, actualPairs,
            SumMatches(stats, @"logical reads (\d+)"), SumMatches(stats, @"physical reads (\d+)"),
            SumMatches(stats, @"CPU time = (\d+) ms"),
            SumMatches(stats, @"elapsed time = (\d+) ms"));
    }
}

public static class V9SqlReplayDiagnosticCli
{
    public const string Flag = "--v9-diagnose-sql-replay";
    private const string HistoricalCorpusSha =
        "6a2cb2ffd48d14b705faeb14ec4dd517bef94ced730e20a062e0dd5403c832b1";
    private const int Repetitions = 20;

    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 5 || args[0] != Flag)
            throw new ArgumentException("Uso: --v9-diagnose-sql-replay <plan.json> <observacoes.csv> <SHA.fitted.json> <output.json>");
        var destination = Path.GetFullPath(args[4]);
        if (File.Exists(destination)) throw new InvalidOperationException("Recibo diagnóstico deve ser novo.");
        var sourceSha = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(args[2]))).ToLowerInvariant();
        if (sourceSha != HistoricalCorpusSha)
            throw new InvalidDataException("Corpus não corresponde ao SHA físico 15/11.");
        var plan = JsonSerializer.Deserialize<V9NestedCalibrationPlan>(
            await File.ReadAllBytesAsync(args[1]),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Plano não disponível.");
        V9NestedCalibrationCoordinator.ValidatePlan(plan);
        var snapshotSha = Path.GetFileName(args[3]).Split('.', 2)[0];
        if (snapshotSha.Length != 64 || !snapshotSha.All(Uri.IsHexDigit))
            throw new InvalidDataException("Nome do fit não é SHA256.");
        var snapshot = await V9FittedSnapshotArtifact.ReadPinnedAsync(args[3], snapshotSha);
        V9NestedCalibrationCli.RequirePinnedFinalistReference(plan, snapshot);
        var frame = V9PhysicalCalibrationEvidenceCli.ReadNoCpfTrainValidation(args[2], 4,
            plan.Calibration.MaximumAcceptedPerBaseIdentity, onlyPartition: "VALIDATION");
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
            ?? throw new InvalidOperationException("JornadaHarness temporário ausente.");
        if (!string.Equals(new SqlConnectionStringBuilder(connectionString).InitialCatalog,
            "JornadaHarness", StringComparison.Ordinal))
            throw new InvalidOperationException("Somente banco JornadaHarness é permitido.");
        var execution = plan.Calibration.Execution with { EndToEndRepetitions = Repetitions };

        async Task<V9SqlReplayBatchDiagnostic> Batch(int workers)
        {
            var watch = Stopwatch.StartNew();
            var rows = await Task.WhenAll(Enumerable.Range(0, workers).Select(async _ =>
            {
                await using var sql = new SqlConnection(connectionString);
                await sql.OpenAsync();
                var samples = new List<V9SqlReplayPhaseSample>();
                for (var i = 0; i < Repetitions; i++)
                    samples.Add(await V9PhysicalSqlAdaptiveCalibrationProbe.DiagnoseFrozenReplayPhasesAsync(
                        sql, frame, snapshot, execution, CancellationToken.None));
                return samples;
            }));
            var all = rows.SelectMany(x => x).ToArray();
            var digest = all[0].FrozenReplaySha256;
            if (all.Any(x => x.FrozenReplaySha256 != digest ||
                x.CandidatePairs != all[0].CandidatePairs))
                throw new InvalidDataException("Replay físico ou universo de candidatos mudou entre amostras.");
            static double P95(IEnumerable<double> values)
                => V9PhysicalSqlCostQuantiles.FromMeasuredMilliseconds(values).P95Milliseconds;
            static double Avg(IEnumerable<double> values) => values.Average();
            return new(workers, Repetitions, all.Length, watch.Elapsed.TotalSeconds,
                all.Length / watch.Elapsed.TotalSeconds, P95(all.Select(x => x.TotalMilliseconds)),
                P95(all.Select(x => x.SqlDistinctJoinAndIndexMilliseconds)),
                P95(all.Select(x => x.FrozenScoringGroupingMilliseconds)),
                Avg(all.Select(x => x.CreateTablesMilliseconds)),
                Avg(all.Select(x => x.BulkObservationsMilliseconds)),
                Avg(all.Select(x => x.BulkSignaturesMilliseconds)),
                Avg(all.Select(x => x.ReadEdgesMilliseconds)),
                Avg(all.Select(x => x.SignaturesMilliseconds)), digest, all[0].CandidatePairs, true);
        }

        var isolated = await Batch(1);
        V9SqlPlanDiagnostic planEvidence;
        await using (var sql = new SqlConnection(connectionString))
        {
            await sql.OpenAsync();
            var warmup = await V9PhysicalSqlAdaptiveCalibrationProbe.DiagnoseFrozenReplayPhasesAsync(
                sql, frame, snapshot, execution, CancellationToken.None);
            planEvidence = await V9PhysicalSqlAdaptiveCalibrationProbe.DiagnoseActualSqlPlanAsync(
                sql, warmup.CandidatePairs, CancellationToken.None);
        }
        var concurrent = await Batch(3);
        if (isolated.FrozenReplaySha256 != concurrent.FrozenReplaySha256
            || isolated.CandidatePairs != concurrent.CandidatePairs)
            throw new InvalidDataException("Conexões concorrentes não preservaram o resultado.");
        var output = new
        {
            schema = "V9_SQL_REPLAY_PHASE_DIAGNOSTIC_V1",
            evidence_only = true, scientific_p95_certified = false,
            publication = false, test_opened = false,
            corpus_sha256 = sourceSha, snapshot_sha256 = snapshot.ContentSha256,
            plan_sha256 = Convert.ToHexString(SHA256.HashData(
                await File.ReadAllBytesAsync(args[1]))).ToLowerInvariant(),
            sql_image = "mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89",
            observations = frame.Length, isolated, planEvidence, concurrent
        };
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(destination, JsonSerializer.Serialize(
            output, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"V9_SQL_REPLAY_DIAGNOSTIC_PASS: isolated={isolated.TotalP95Milliseconds:F0}ms concurrent={concurrent.TotalP95Milliseconds:F0}ms; no TRAIN/TEST.");
    }
}

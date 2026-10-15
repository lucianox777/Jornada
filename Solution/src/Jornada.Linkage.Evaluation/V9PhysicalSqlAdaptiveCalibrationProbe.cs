using System.Globalization;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Evaluation;

public sealed record V9PhysicalCalibrationObservation(
    int ObservationId,
    string BasePersonId,
    string Partition,
    string Gestor,
    string? PersonName,
    string? MotherName,
    DateOnly? BirthDate);

public sealed record V9PhysicalCalibrationRunResult(
    string Version,
    string BlockingFingerprintSha256,
    long PreflightPairIncidences,
    long PhysicalDistinctCandidatePairs,
    long TrainCandidatePairs,
    long ValidationCandidatePairs,
    int TrainDistinctBasePeople,
    int ValidationDistinctBasePeople,
    AdaptiveCalibrationStudyReport CalibrationReport);

/// <summary>
/// Calibration-only physical SQL bridge. It accepts TRAIN/VALIDATION observations only,
/// projects the frozen blocking passes with the canonical C# projector, materializes the
/// complete deduplicated candidate union in JornadaHarness SQL Server (no TOP N), then
/// feeds exactly those physical pairs to the V9 Fellegi-Sunter adaptive adapter.
/// TEST input is rejected before any SQL command.
/// </summary>
public static class V9PhysicalSqlAdaptiveCalibrationProbe
{
    public const string Version = "V9_PHYSICAL_SQL_ADAPTIVE_FS_TRAIN_VALIDATION_V1";
    public const int HardPairIncidenceCap = 1_000_000;

    private sealed record SignatureRow(
        int ObservationId, string Partition, string PassId, string Signature);

    public static async Task<V9PhysicalCalibrationRunResult> RunAsync(
        SqlConnection connection,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> observations,
        IReadOnlyCollection<LinkageBlockingPass> passes,
        IReadOnlyCollection<V9FsAdaptiveOptionConfiguration> options,
        AdaptiveCalibrationPreviewBudget budget,
        string studyFingerprintSha256,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(passes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(budget);
        RequireSha(studyFingerprintSha256, nameof(studyFingerprintSha256));

        if (observations.Count < 4)
            throw new ArgumentException("Frame físico exige observações TRAIN/VALIDATION suficientes.");
        if (observations.Any(x => x is null
                || x.ObservationId <= 0
                || string.IsNullOrWhiteSpace(x.BasePersonId)
                || string.IsNullOrWhiteSpace(x.Gestor)
                || x.Partition is not ("TRAIN" or "VALIDATION")))
            throw new InvalidDataException(
                "Probe físico aceita exclusivamente TRAIN/VALIDATION; TEST ou linha inválida é proibido.");
        if (observations.Select(x => x.ObservationId).Distinct().Count() != observations.Count)
            throw new InvalidDataException("ObservationId duplicado no frame físico.");
        if (!observations.Any(x => x.Partition == "TRAIN")
            || !observations.Any(x => x.Partition == "VALIDATION"))
            throw new InvalidDataException("TRAIN e VALIDATION são obrigatórios.");

        var trainBases = observations.Where(x => x.Partition == "TRAIN")
            .Select(x => x.BasePersonId).ToHashSet(StringComparer.Ordinal);
        var validationBases = observations.Where(x => x.Partition == "VALIDATION")
            .Select(x => x.BasePersonId).ToHashSet(StringComparer.Ordinal);
        if (trainBases.Overlaps(validationBases))
            throw new InvalidDataException("Vazamento de identidade entre TRAIN e VALIDATION.");

        var canonicalPasses = passes
            .Select(p => LinkageBlockingPass.Create(p.PassId, p.Fields))
            .OrderBy(p => p.PassId, StringComparer.Ordinal)
            .ToArray();
        if (canonicalPasses.Length is < 1 or > 64)
            throw new ArgumentException("Ruleset físico precisa de 1..64 passes.");
        var allowed = BlockingCandidateFeatureCatalog.RequiredCalibratorCandidates
            .ToHashSet(StringComparer.Ordinal);
        if (canonicalPasses.SelectMany(x => x.Fields).Any(x => !allowed.Contains(x)))
            throw new InvalidDataException("Ruleset contém feature fora do catálogo do Calibrador.");

        var signatures = BuildSignatures(observations, canonicalPasses);
        var preflight = CountPairIncidences(signatures);
        if (preflight > HardPairIncidenceCap)
            throw new InvalidDataException(
                $"Censo físico recusado antes do join: {preflight} incidências excedem {HardPairIncidenceCap}.");

        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Conexão SQL precisa estar aberta.");

        await RequireHarnessAsync(connection, cancellationToken);

        await CreateTablesAsync(connection, cancellationToken);
        await BulkObservationsAsync(connection, observations, cancellationToken);
        await BulkSignaturesAsync(connection, signatures, cancellationToken);

        var physicalPairs = await MaterializePairsAsync(connection, cancellationToken);
        if (physicalPairs > HardPairIncidenceCap)
            throw new InvalidDataException("União SQL distinta excedeu o hard cap após materialização.");

        var rows = await ReadCandidatePairsAsync(connection, cancellationToken);
        if (rows.Count != physicalPairs)
            throw new InvalidDataException("Contagem da união SQL diverge da leitura do frame.");

        var trainRows = rows.Where(x => x.Partition == "TRAIN").ToArray();
        var validationRows = rows.Where(x => x.Partition == "VALIDATION").ToArray();
        if (trainRows.Length == 0 || validationRows.Length == 0
            || trainRows.All(x => x.IsTruePair) || trainRows.All(x => !x.IsTruePair)
            || validationRows.All(x => x.IsTruePair) || validationRows.All(x => !x.IsTruePair))
            throw new InvalidDataException("TRAIN/VALIDATION físicos precisam conter M e U.");

        var blockingFingerprint = BlockingFingerprint(canonicalPasses);
        var trainCorpusSha = CorpusFingerprint("TRAIN", observations, blockingFingerprint);
        var validationCorpusSha = CorpusFingerprint("VALIDATION", observations, blockingFingerprint);
        var trainIdentitySha = IdentityFingerprint("TRAIN", trainBases);
        var validationIdentitySha = IdentityFingerprint("VALIDATION", validationBases);

        var frame = new V9FsAdaptiveCandidateFrame(
            trainCorpusSha,
            validationCorpusSha,
            trainIdentitySha,
            validationIdentitySha,
            rows);

        var optionList = options.ToArray();
        var adapter = new V9FellegiSunterAdaptiveCalibrationAdapter(frame, optionList);
        var modelOptions = optionList.Select(x => new AdaptiveCalibrationModelOption(
            V9FellegiSunterAdaptiveCalibrationAdapter.Family,
            V9FellegiSunterAdaptiveCalibrationAdapter.Version,
            x.OptionId,
            x.FingerprintSha256)).ToArray();

        var trainPairBases = PairBaseSet(trainRows);
        var validationPairBases = PairBaseSet(validationRows);
        if (trainPairBases.Overlaps(validationPairBases))
            throw new InvalidDataException("União física contaminou identidades entre splits.");

        var request = new AdaptiveCalibrationStudyRequest(
            studyFingerprintSha256,
            new AdaptiveCalibrationSlice(
                "TRAIN", trainCorpusSha, trainIdentitySha,
                trainPairBases.Count, true, false),
            new AdaptiveCalibrationSlice(
                "VALIDATION", validationCorpusSha, validationIdentitySha,
                validationPairBases.Count, true, false),
            modelOptions,
            ManuallyRequestedCalibration: true,
            budget);

        var report = AdaptiveCalibrationModelSearch.Evaluate(request, [adapter]);

        return new V9PhysicalCalibrationRunResult(
            Version,
            blockingFingerprint,
            preflight,
            physicalPairs,
            trainRows.LongLength,
            validationRows.LongLength,
            trainPairBases.Count,
            validationPairBases.Count,
            report);
    }

    private static List<SignatureRow> BuildSignatures(
        IReadOnlyCollection<V9PhysicalCalibrationObservation> observations,
        IReadOnlyCollection<LinkageBlockingPass> passes)
    {
        var result = new List<SignatureRow>();
        foreach (var observation in observations)
        {
            var byFeature = BlockingProjectionKeyProjector
                .Project(observation.PersonName, observation.MotherName, observation.BirthDate)
                .GroupBy(x => x.Feature, StringComparer.Ordinal)
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(v => v.Value).Distinct(StringComparer.Ordinal)
                        .OrderBy(v => v, StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal);

            foreach (var pass in passes)
            {
                if (pass.Fields.Any(f => !byFeature.TryGetValue(f, out var values) || values.Length == 0))
                    continue;

                foreach (var values in Cartesian(pass.Fields, byFeature))
                {
                    var canonical = pass.PassId + "\u001e" + string.Join("\u001f", values);
                    if (result.Count >= HardPairIncidenceCap)
                        throw new InvalidDataException(
                            "Projeção de assinaturas excedeu o limite físico antes da materialização SQL.");
                    result.Add(new SignatureRow(
                        observation.ObservationId,
                        observation.Partition,
                        pass.PassId,
                        Sha(canonical)));
                }
            }
        }

        return result
            .Distinct()
            .OrderBy(x => x.Partition, StringComparer.Ordinal)
            .ThenBy(x => x.PassId, StringComparer.Ordinal)
            .ThenBy(x => x.Signature, StringComparer.Ordinal)
            .ThenBy(x => x.ObservationId)
            .ToList();
    }

    private static IEnumerable<string[]> Cartesian(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, string[]> values)
    {
        var current = new string[fields.Count];

        IEnumerable<string[]> Walk(int index)
        {
            if (index == fields.Count)
            {
                yield return current.ToArray();
                yield break;
            }

            foreach (var value in values[fields[index]])
            {
                current[index] = value;
                foreach (var row in Walk(index + 1))
                    yield return row;
            }
        }

        return Walk(0);
    }

    private static long CountPairIncidences(IReadOnlyCollection<SignatureRow> signatures)
    {
        decimal total = 0m;
        foreach (var group in signatures.GroupBy(
            x => (x.Partition, x.PassId, x.Signature)))
        {
            var n = group.Select(x => x.ObservationId).Distinct().LongCount();
            checked { total += (decimal)n * (n - 1L) / 2m; }
            if (total > HardPairIncidenceCap)
                return HardPairIncidenceCap + 1L;
        }

        return checked((long)total);
    }

    private static async Task RequireHarnessAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("SELECT DB_NAME();", connection);
        var database = (string?)await command.ExecuteScalarAsync(cancellationToken);
        if (!string.Equals(database, "JornadaHarness", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Probe de calibração V9 proibido fora do SQL isolado JornadaHarness.");
    }

    private static async Task CreateTablesAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            IF OBJECT_ID('tempdb..#v9_cal_pairs') IS NOT NULL DROP TABLE #v9_cal_pairs;
            IF OBJECT_ID('tempdb..#v9_cal_sig') IS NOT NULL DROP TABLE #v9_cal_sig;
            IF OBJECT_ID('tempdb..#v9_cal_obs') IS NOT NULL DROP TABLE #v9_cal_obs;

            CREATE TABLE #v9_cal_obs(
                obs_id int NOT NULL PRIMARY KEY,
                base_id nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL,
                part varchar(12) COLLATE Latin1_General_100_BIN2 NOT NULL,
                gestor nvarchar(80) COLLATE Latin1_General_100_BIN2 NOT NULL,
                nome nvarchar(500) NULL,
                mae nvarchar(500) NULL,
                nascimento date NULL
            );

            CREATE TABLE #v9_cal_sig(
                obs_id int NOT NULL,
                part varchar(12) COLLATE Latin1_General_100_BIN2 NOT NULL,
                pass_id nvarchar(160) COLLATE Latin1_General_100_BIN2 NOT NULL,
                assinatura char(64) COLLATE Latin1_General_100_BIN2 NOT NULL
            );
            CREATE INDEX IX_v9_cal_sig
              ON #v9_cal_sig(part,pass_id,assinatura,obs_id);
            """,
            connection)
        {
            CommandTimeout = 300
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task BulkObservationsAsync(
        SqlConnection connection,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> observations,
        CancellationToken cancellationToken)
    {
        var table = new DataTable();
        table.Columns.Add("obs_id", typeof(int));
        table.Columns.Add("base_id", typeof(string));
        table.Columns.Add("part", typeof(string));
        table.Columns.Add("gestor", typeof(string));
        table.Columns.Add("nome", typeof(string));
        table.Columns.Add("mae", typeof(string));
        table.Columns.Add("nascimento", typeof(DateTime));
        foreach (var x in observations)
            table.Rows.Add(
                x.ObservationId,
                x.BasePersonId,
                x.Partition,
                x.Gestor,
                (object?)x.PersonName ?? DBNull.Value,
                (object?)x.MotherName ?? DBNull.Value,
                x.BirthDate is { } d ? d.ToDateTime(TimeOnly.MinValue) : DBNull.Value);

        using var bulk = new SqlBulkCopy(connection)
        {
            DestinationTableName = "#v9_cal_obs",
            BatchSize = 15_000,
            BulkCopyTimeout = 300
        };
        await bulk.WriteToServerAsync(table, cancellationToken);
    }

    private static async Task BulkSignaturesAsync(
        SqlConnection connection,
        IReadOnlyCollection<SignatureRow> signatures,
        CancellationToken cancellationToken)
    {
        var table = new DataTable();
        table.Columns.Add("obs_id", typeof(int));
        table.Columns.Add("part", typeof(string));
        table.Columns.Add("pass_id", typeof(string));
        table.Columns.Add("assinatura", typeof(string));
        foreach (var x in signatures)
            table.Rows.Add(x.ObservationId, x.Partition, x.PassId, x.Signature);

        using var bulk = new SqlBulkCopy(connection)
        {
            DestinationTableName = "#v9_cal_sig",
            BatchSize = 25_000,
            BulkCopyTimeout = 300
        };
        await bulk.WriteToServerAsync(table, cancellationToken);
    }

    private static async Task<long> MaterializePairsAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
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

            CREATE UNIQUE CLUSTERED INDEX IX_v9_cal_pairs
              ON #v9_cal_pairs(part,a,b);

            SELECT COUNT_BIG(*) FROM #v9_cal_pairs;
            """,
            connection)
        {
            CommandTimeout = 300
        };
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<V9FsAdaptiveCandidatePair>> ReadCandidatePairsAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            WITH endpoints AS (
                SELECT a AS obs_id FROM #v9_cal_pairs
                UNION ALL
                SELECT b AS obs_id FROM #v9_cal_pairs
            ), degree AS (
                SELECT obs_id,COUNT_BIG(*) AS degree
                FROM endpoints GROUP BY obs_id
            )
            SELECT p.part,
                   a.base_id,b.base_id,
                   CASE WHEN a.base_id=b.base_id THEN 1 ELSE 0 END AS is_true,
                   a.nome,b.nome,a.mae,b.mae,a.nascimento,b.nascimento,
                   CASE WHEN da.degree>=db.degree THEN da.degree ELSE db.degree END AS degree
            FROM #v9_cal_pairs p
            JOIN #v9_cal_obs a ON a.obs_id=p.a
            JOIN #v9_cal_obs b ON b.obs_id=p.b
            JOIN degree da ON da.obs_id=p.a
            JOIN degree db ON db.obs_id=p.b
            ORDER BY p.part,p.a,p.b;
            """,
            connection)
        {
            CommandTimeout = 300
        };

        var rows = new List<V9FsAdaptiveCandidatePair>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var degree = reader.GetInt64(10);
            if (degree <= 0 || degree > int.MaxValue)
                throw new InvalidDataException("Fanout físico fora do domínio do adaptador.");

            rows.Add(new V9FsAdaptiveCandidatePair(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3) == 1,
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : DateOnly.FromDateTime(reader.GetDateTime(8)),
                reader.IsDBNull(9) ? null : DateOnly.FromDateTime(reader.GetDateTime(9)),
                (int)degree));
        }

        return rows;
    }

    private static HashSet<string> PairBaseSet(
        IEnumerable<V9FsAdaptiveCandidatePair> rows)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            result.Add(row.LeftIdentityGroupId);
            result.Add(row.RightIdentityGroupId);
        }
        return result;
    }

    private static string BlockingFingerprint(
        IReadOnlyCollection<LinkageBlockingPass> passes)
    {
        var canonical = string.Join("\n", passes
            .OrderBy(x => x.PassId, StringComparer.Ordinal)
            .Select(x => x.PassId + "=" + string.Join(",", x.Fields)));
        return Sha(
            Version + "\n"
            + BlockingProjectionKeyProjector.MethodVersion + "\n"
            + canonical);
    }

    private static string CorpusFingerprint(
        string partition,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> observations,
        string blockingFingerprint)
    {
        var sb = new StringBuilder()
            .Append(Version).Append('\n')
            .Append(blockingFingerprint).Append('\n')
            .Append(partition).Append('\n');
        foreach (var row in observations.Where(x => x.Partition == partition)
                     .OrderBy(x => x.ObservationId))
            sb.Append(row.ObservationId).Append('|')
              .Append(row.BasePersonId).Append('|')
              .Append(row.Gestor).Append('|')
              .Append(row.PersonName).Append('|')
              .Append(row.MotherName).Append('|')
              .Append(row.BirthDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
        return Sha(sb.ToString());
    }

    private static string IdentityFingerprint(
        string partition, IReadOnlyCollection<string> identities) =>
        Sha(Version + "\nIDENTITIES\n" + partition + "\n"
            + string.Join("\n", identities.OrderBy(x => x, StringComparer.Ordinal)));

    private static string Sha(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static void RequireSha(string? sha, string label)
    {
        if (sha is null || sha.Length != 64 || sha.Any(x => !Uri.IsHexDigit(x)))
            throw new ArgumentException("SHA-256 obrigatório: " + label);
    }
}

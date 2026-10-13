using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Physical SQL join over the EXACT canonical name/birth blocking keys of the
/// combined candidate-only C1 policy. Counts ALL distinct eligible pairs in
/// that SQL union, never adjacent U sampled pairs. Does not claim an active
/// VERSIONED_ALIAS ruleset or production TF.
/// </summary>
public static class V9PhysicalSqlBlockingProbe
{
    public const string Version = "V9_PHYSICAL_SQL_COMBINED_IDENTITY_C1_FULL_CANDIDATE_CENSUS_V1";
    public const string BlockingPolicy = CombinedIdentityCandidatePlanner.MethodVersion;
    public const string Scope = "ACTUAL_SQL_JOIN_CANONICAL_KEYS_COMBINED_C1_NOT_ACTIVE_VERSIONED_ALIAS";
    private const int HardPairCap = 1_000_000;
    private sealed record Observation(int Id, string Base, string Partition, string Gestor,
        string? Name, string? Mother, DateOnly? Birth, string? Cpf,
        string? NameFull, string? MotherFull, string? NamePhonetic, string? MotherPhonetic);
    private sealed record Frozen(string Version, ulong SourceSeed, string SourceInputFingerprint,
        decimal ThresholdLogOdds, FellegiSunterJointNominalCellV9[] Cells,
        Dictionary<string, decimal> Parameters);

    public sealed record Split(
        string Partition, long EligibleTrueInterGestorNoCpfPairs,
        long SqlCandidateTruePairs, long SqlCandidateFalsePairs,
        long TruePairsLostBySqlBlocking,
        decimal FalseCandidatesPerRetrievedTrue,
        decimal FalseCandidatesPerEligibleTrue,
        decimal FalseCandidateShare,
        long AcceptedTruePairs, long AcceptedWrongPairs,
        long RejectedTruePairsIncludingLostByBlocking, long RejectedFalseCandidates,
        decimal RecallAgainstEligibleTrue,
        decimal? ConditionalFalsePositiveRateAmongFalseCandidates,
        decimal? WrongLinkRateAmongAcceptedPairs,
        decimal? WrongLinksPerCorrectLink);

    public sealed record Result(
        string Version, string Scope, string BlockingPolicy, string ProjectionVersion,
        ulong Seed, int CorpusPeople, int Observations, string CorpusInputFingerprint,
        string ObservationsSha256, string FrozenModelSha256,
        ulong FrozenSourceSeed, string FrozenSourceInputFingerprint,
        decimal FrozenThresholdLogOdds, long SqlCandidatePairsTotal,
        int CandidatePairHardCap, IReadOnlyList<Split> Splits,
        string Verdict, string OperationalConclusion);

    public static async Task<Result> RunAsync(
        SqlConnection connection, string corpusRoot, string frozenPath,
        ulong seed, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") != "Development")
            throw new InvalidOperationException("Blocking físico sintético exige DOTNET_ENVIRONMENT=Development.");
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Conexão SQL deve estar aberta.");
        await using (var verify = new SqlCommand("SELECT DB_NAME();", connection))
        {
            var database = (string?)await verify.ExecuteScalarAsync(ct);
            if (database != "JornadaHarness")
                throw new InvalidOperationException("Proibida execução fora da base sintética isolada JornadaHarness.");
        }
        var manifestPath = Path.Combine(corpusRoot, "generation-manifest.json");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, ct));
        var meta = manifest.RootElement;
        if (meta.GetProperty("seed").GetUInt64() != seed
            || meta.GetProperty("population_profile").GetString() != "DEMOGRAPHIC_PRIMARY_V1")
            throw new InvalidDataException("Fingerprint/profiling do corpus sintético diverge.");
        var fingerprint = meta.GetProperty("input_fingerprint_sha256").GetString()!;
        var outputs = meta.GetProperty("outputs");
        var expectedObservationSha = outputs.EnumerateArray()
            .Single(x => x.GetProperty("path").GetString() == "observacoes.csv")
            .GetProperty("sha256").GetString()!;
        var observationsPath = Path.Combine(corpusRoot, "observacoes.csv");
        var observationBytes = await File.ReadAllBytesAsync(observationsPath, ct);
        var observationSha = Convert.ToHexString(SHA256.HashData(observationBytes));
        if (!string.Equals(expectedObservationSha, observationSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 do corpus não coincide com manifesto.");
        var rawModel = (await File.ReadAllTextAsync(frozenPath, ct)).TrimEnd('\n', '\r');
        var modelSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawModel)));
        var frozen = JsonSerializer.Deserialize<Frozen>(rawModel)
            ?? throw new InvalidDataException("Modelo V9 congelado inválido.");
        if (frozen.Version != "V9_SYNTHETIC_FROZEN_JOINT_FS_FROM_SEED42_V1"
            || frozen.SourceSeed != 42UL
            || frozen.Cells is not { Length: 81 }
            || frozen.Parameters is null || frozen.Parameters.Count < 8
            || (seed == 42UL && !string.Equals(frozen.SourceInputFingerprint, fingerprint,
                StringComparison.OrdinalIgnoreCase))
            || (seed != 42UL && string.Equals(frozen.SourceInputFingerprint, fingerprint,
                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Fonte, modelo ou 81 células V9 congeladas inconsistentes.");
        if (Math.Abs(frozen.Cells.Sum(x => x.M)-1m) > .0000001m
            || Math.Abs(frozen.Cells.Sum(x => x.U)-1m) > .0000001m)
            throw new InvalidDataException("M/U congelado não normalizado.");
        var observationRows = ReadObservations(Encoding.UTF8.GetString(observationBytes));
        if (observationRows.Length <= 30000)
            throw new InvalidDataException("Corpus exige observações de 30.000 bases.");
        var people = File.ReadLines(Path.Combine(corpusRoot, "pessoas_verdade.csv"))
            .Skip(1).LongCount();
        if (people != 30000)
            throw new InvalidDataException("Verificação de pessoas sintéticas exige exatamente 30.000.");

        await using (var create = new SqlCommand(
            """
            CREATE TABLE #v9obs(
                obs_id INT NOT NULL PRIMARY KEY,
                part VARCHAR(12) COLLATE Latin1_General_100_BIN2 NOT NULL,
                gestor NVARCHAR(40) COLLATE Latin1_General_100_BIN2 NOT NULL,
                cpf VARCHAR(16) NULL,
                nome NVARCHAR(250) COLLATE Latin1_General_100_BIN2 NULL,
                mae NVARCHAR(250) COLLATE Latin1_General_100_BIN2 NULL,
                nome_fon NVARCHAR(150) COLLATE Latin1_General_100_BIN2 NULL,
                mae_fon NVARCHAR(150) COLLATE Latin1_General_100_BIN2 NULL,
                nascimento DATE NULL
            );
            """, connection))
            await create.ExecuteNonQueryAsync(ct);

        var table = new DataTable();
        foreach (var (name, kind) in new (string, Type)[] {
            ("obs_id", typeof(int)), ("part", typeof(string)), ("gestor", typeof(string)),
            ("cpf", typeof(string)), ("nome", typeof(string)), ("mae", typeof(string)),
            ("nome_fon", typeof(string)), ("mae_fon", typeof(string)),
            ("nascimento", typeof(DateTime)) })
            table.Columns.Add(name, kind);
        foreach (var o in observationRows)
            table.Rows.Add(o.Id, o.Partition, o.Gestor, (object?)o.Cpf ?? DBNull.Value,
                (object?)o.NameFull ?? DBNull.Value,
                (object?)o.MotherFull ?? DBNull.Value,
                (object?)o.NamePhonetic ?? DBNull.Value,
                (object?)o.MotherPhonetic ?? DBNull.Value,
                o.Birth is { } birth ? birth.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        using (var bulk = new SqlBulkCopy(connection) {
            DestinationTableName = "#v9obs", BatchSize = 15000,
            BulkCopyTimeout = 300 })
            await bulk.WriteToServerAsync(table, ct);
        table.Dispose();

        // The C1 five passes are the exact conjunctive equality predicates
        // supplied by CombinedIdentityCandidatePlanner (including D/M swap
        // and adjacent year). SQL executes ALL distinct candidates (no U proxy).
        // Same split / inter-Gestor / no CPF are explicit diagnostic frame.
        await using (var materialize = new SqlCommand(
            """
            CREATE INDEX IX_v9_name ON #v9obs(part,nome,mae,nascimento);
            CREATE INDEX IX_v9_namefon ON #v9obs(part,nome_fon,mae,nascimento);
            CREATE INDEX IX_v9_maefon ON #v9obs(part,nome,mae_fon,nascimento);

            SELECT DISTINCT q.a, q.b, q.part
            INTO #v9pairs
            FROM (
                SELECT a.obs_id a,b.obs_id b,a.part
                FROM #v9obs a JOIN #v9obs b
                    ON a.part=b.part AND a.nome=b.nome AND a.mae=b.mae
                    AND a.nascimento=b.nascimento AND a.obs_id<b.obs_id
                WHERE a.cpf IS NULL AND b.cpf IS NULL AND a.gestor<>b.gestor
                UNION ALL
                SELECT a.obs_id,b.obs_id,a.part
                FROM #v9obs a JOIN #v9obs b
                    ON a.part=b.part AND a.nome=b.nome AND a.mae=b.mae
                    AND YEAR(a.nascimento)=YEAR(b.nascimento)
                    AND DAY(a.nascimento)=MONTH(b.nascimento)
                    AND MONTH(a.nascimento)=DAY(b.nascimento)
                    AND a.obs_id<b.obs_id
                WHERE a.cpf IS NULL AND b.cpf IS NULL AND a.gestor<>b.gestor
                  AND DAY(a.nascimento)<=12 AND MONTH(a.nascimento)<=12
                  AND DAY(a.nascimento)<>MONTH(a.nascimento)
                UNION ALL
                SELECT a.obs_id,b.obs_id,a.part
                FROM #v9obs a JOIN #v9obs b
                    ON a.part=b.part AND a.nome=b.nome AND a.mae=b.mae
                    AND ABS(YEAR(a.nascimento)-YEAR(b.nascimento))=1
                    AND MONTH(a.nascimento)=MONTH(b.nascimento)
                    AND DAY(a.nascimento)=DAY(b.nascimento)
                    AND a.obs_id<b.obs_id
                WHERE a.cpf IS NULL AND b.cpf IS NULL AND a.gestor<>b.gestor
                UNION ALL
                SELECT a.obs_id,b.obs_id,a.part
                FROM #v9obs a JOIN #v9obs b
                    ON a.part=b.part AND a.nome_fon=b.nome_fon AND a.mae=b.mae
                    AND a.nascimento=b.nascimento AND a.obs_id<b.obs_id
                WHERE a.cpf IS NULL AND b.cpf IS NULL AND a.gestor<>b.gestor
                UNION ALL
                SELECT a.obs_id,b.obs_id,a.part
                FROM #v9obs a JOIN #v9obs b
                    ON a.part=b.part AND a.nome=b.nome AND a.mae_fon=b.mae_fon
                    AND a.nascimento=b.nascimento AND a.obs_id<b.obs_id
                WHERE a.cpf IS NULL AND b.cpf IS NULL AND a.gestor<>b.gestor
            ) q;
            CREATE UNIQUE CLUSTERED INDEX IX_v9_pair ON #v9pairs(a,b);
            """, connection) { CommandTimeout = 600 })
            await materialize.ExecuteNonQueryAsync(ct);

        // Count truth pairs independently of SQL blocking, preserving FN of
        // missing candidates rather than silently conditioning recall on hits.
        var bySplit = new[] { "TRAIN", "VALIDATION", "TEST" }
            .ToDictionary(x => x, x => new Acc(x), StringComparer.Ordinal);
        var frame = observationRows.Where(x => x.Cpf is null && x.Birth.HasValue)
            .GroupBy(x => (x.Partition, x.Base));
        foreach (var group in frame)
        {
            var rows = group.ToArray();
            for (var a = 0; a < rows.Length; a++)
                for (var b = a+1; b < rows.Length; b++)
                    if (rows[a].Gestor != rows[b].Gestor)
                        bySplit[group.Key.Partition].Eligible++;
        }
        long allCandidate = 0;
        await using (var command = new SqlCommand(
            "SELECT a,b,part FROM #v9pairs ORDER BY a,b;", connection) {
                CommandTimeout = 600 })
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                if (++allCandidate > HardPairCap)
                    throw new InvalidOperationException(
                        "Censo SQL completo excedeu o limite de 1 milhão; abortar, nunca amostrar U silenciosamente.");
                var a = observationRows[reader.GetInt32(0)];
                var b = observationRows[reader.GetInt32(1)];
                var partition = reader.GetString(2);
                if (partition != a.Partition || partition != b.Partition
                    || a.Gestor == b.Gestor || a.Cpf is not null || b.Cpf is not null)
                    throw new InvalidDataException("SQL gerou par fora do frame elegível.");
                var split = bySplit[partition];
                var same = a.Base == b.Base;
                if (same) split.TrueCandidate++;
                else split.FalseCandidate++;
                var score = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                    frozen.Parameters, a.Name, b.Name, a.Mother, b.Mother,
                    frozen.Cells, CompositeNominalComparisonV9.ContractVersion,
                    leftBirthDate: a.Birth, rightBirthDate: b.Birth);
                if (score.Contributions.Count(x => x.Evidence == "NOMINAL_CONJUNTO_V9") != 1)
                    throw new InvalidOperationException("V9 FS deveria ter uma única evidência nominal.");
                if (score.Score.LogOdds > frozen.ThresholdLogOdds)
                {
                    if (same) split.Tp++;
                    else split.Fp++;
                }
            }
        }
        var metrics = bySplit.Values.Select(x => x.Build()).ToArray();
        if (metrics.Any(x => x.EligibleTrueInterGestorNoCpfPairs <= 0)
            || metrics.Any(x => x.SqlCandidateTruePairs > x.EligibleTrueInterGestorNoCpfPairs))
            throw new InvalidOperationException("Gabarito ou recuperação SQL M incompatíveis.");
        return new Result(
            Version, Scope, BlockingPolicy, BlockingProjectionKeyProjector.MethodVersion,
            seed, 30000, observationRows.Length, fingerprint, observationSha, modelSha,
            frozen.SourceSeed, frozen.SourceInputFingerprint, frozen.ThresholdLogOdds,
            allCandidate, HardPairCap, metrics,
            metrics.All(x => x.SqlCandidateFalsePairs == 0)
                ? "C1_NO_FALSE_CANDIDATES_AND_LOW_RECALL_NO_ERROR_RATE_VALIDATION"
                : "MEASURED_SQL_C1_CANDIDATE_AND_LINK_ERRORS_NO_PERFORMANCE_GATE",
            "NOT_OPERATIONAL_ACTIVE_VERSIONED_ALIAS_OR_CALIBRATED_TF");
    }

    private sealed class Acc(string partition)
    {
        public long Eligible, TrueCandidate, FalseCandidate, Tp, Fp;
        public Split Build()
        {
            var accepted = Tp + Fp;
            return new Split(partition, Eligible, TrueCandidate, FalseCandidate,
                Eligible - TrueCandidate,
                TrueCandidate == 0 ? 0 : (decimal)FalseCandidate / TrueCandidate,
                Eligible == 0 ? 0 : (decimal)FalseCandidate / Eligible,
                TrueCandidate + FalseCandidate == 0 ? 0
                    : (decimal)FalseCandidate / (TrueCandidate + FalseCandidate),
                Tp, Fp, Eligible - Tp, FalseCandidate - Fp,
                Eligible == 0 ? 0 : (decimal)Tp / Eligible,
                FalseCandidate == 0 ? null : (decimal)Fp / FalseCandidate,
                accepted == 0 ? null : (decimal)Fp / accepted,
                Tp == 0 ? null : (decimal)Fp / Tp);
        }
    }

    private static Observation[] ReadObservations(string csv)
    {
        using var source = new StringReader(csv);
        var header = SplitCsv(source.ReadLine() ?? throw new InvalidDataException("CSV vazio."));
        var indexes = header.Select((name, pos) => (name, pos))
            .ToDictionary(x => x.name, x => x.pos, StringComparer.Ordinal);
        foreach (var required in new[] { "observacao_id","base_person_id","particao",
                    "gestor","nome","nome_mae","data_nascimento","cpf" })
            if (!indexes.ContainsKey(required))
                throw new InvalidDataException("CSV sem coluna: "+required);
        var rows = new List<Observation>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        string? line;
        while ((line = source.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            var cells = SplitCsv(line);
            string C(string name) => cells[indexes[name]];
            var id = C("observacao_id");
            if (!known.Add(id)) throw new InvalidDataException("Observação duplicada.");
            var birthText = C("data_nascimento");
            DateOnly? birth = null;
            if (!string.IsNullOrEmpty(birthText))
            {
                if (!DateOnly.TryParseExact(birthText,"yyyy-MM-dd",
                    CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))
                    throw new InvalidDataException("Data de nascimento sintética inválida.");
                birth = date;
            }
            var name = Empty(C("nome"));
            var mother = Empty(C("nome_mae"));
            var projected = BlockingProjectionKeyProjector.Project(name,mother,birth)
                .ToDictionary(x => x.Feature+"\u001e"+x.Value,x => x.Value,StringComparer.Ordinal);
            string? Key(string feature) => projected.Keys
                .Where(k=>k.StartsWith(feature+"\u001e",StringComparison.Ordinal))
                .Select(k=>projected[k]).FirstOrDefault();
            var part = C("particao");
            if (part is not ("TRAIN" or "VALIDATION" or "TEST"))
                throw new InvalidDataException("Partição desconhecida.");
            rows.Add(new Observation(rows.Count,C("base_person_id"),part,C("gestor"),
                name,mother,birth,Empty(C("cpf")),
                Key(BlockingFeatureNames.FullName),
                Key(BlockingFeatureNames.MotherFullName),
                Key(BlockingFeatureNames.FullNamePhoneticPtBr),
                Key(BlockingFeatureNames.MotherFullNamePhoneticPtBr)));
        }
        return rows.ToArray();
    }

    private static string? Empty(string s) => s.Length == 0 ? null : s;

    private static string[] SplitCsv(string line)
    {
        var fields = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i+1] == '"')
                    { value.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (ch == ',' && !quoted) { fields.Add(value.ToString()); value.Clear(); }
            else value.Append(ch);
        }
        if (quoted) throw new InvalidDataException("CSV com aspas sem fechar.");
        fields.Add(value.ToString());
        return fields.ToArray();
    }
}

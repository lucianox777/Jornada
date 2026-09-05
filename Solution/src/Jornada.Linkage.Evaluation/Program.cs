using System.Data;
using System.Globalization;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

const string Purpose = "DEV_HML_ONLY_NO_PUBLICATION";
var options = EvaluationOptions.Parse(args);
if (options.Help)
{
    Console.WriteLine(EvaluationOptions.Usage);
    return;
}

var connectionString = options.ConnectionString
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
    ?? throw new InvalidOperationException("Informe --connection-string ou ConnectionStrings__Jornada.");

var labels = LabelCsv.Read(options.LabelsPath!);
if (labels.Count == 0)
    throw new InvalidOperationException("A amostra rotulada está vazia.");

var operationalSql = new OperationalSqlAdapter(connectionString);
await using var connection = await operationalSql.OpenAsync();

var evaluator = new LinkageEvaluation(connection, options.CommandTimeoutSeconds);
var labeledPairs = await evaluator.LoadLabeledNoCpfPairsAsync(labels);
if (labeledPairs.Count != labels.Count)
    throw new InvalidOperationException($"Apenas {labeledPairs.Count} de {labels.Count} rótulos foram encontrados como observações SEM_CPF com UUID verdade presente na Gold.");

var cpfAnchoredPairs = await evaluator.LoadIndependentCpfAnchoredPairsAsync(options.MaxCpfAnchoredPairs, options.SamplePoolSize);
if (cpfAnchoredPairs.Count == 0)
    throw new InvalidOperationException("Não há pares CPF inter-Gestores independentes para a comparação de transportabilidade de m.");

var blocking = await evaluator.EvaluateBlockingAsync(labeledPairs, options.BirthWindowDays);
var transport = TransportabilityMetrics.Compare(cpfAnchoredPairs, labeledPairs, options.SmoothingAlpha);

var report = new
{
    generatedAtUtc = DateTimeOffset.UtcNow,
    purpose = Purpose,
    safeguards = new[]
    {
        "read-only against Jornada operational tables",
        "does not create linkage_run",
        "does not write IDENTITY_MAP/vinculo_fonte",
        "does not update Gold",
        "V2 is experimental evidence only"
    },
    input = new
    {
        labels = Path.GetFullPath(options.LabelsPath!),
        labeledNoCpfPairs = labeledPairs.Count,
        cpfAnchoredIndependentPairs = cpfAnchoredPairs.Count,
        birthWindowDays = options.BirthWindowDays,
        smoothingAlpha = options.SmoothingAlpha,
        commandTimeoutSeconds = options.CommandTimeoutSeconds
    },
    blocking,
    mTransportability = transport,
    interpretation = new
    {
        blocking = "Compare recall do UUID verdadeiro e expansão do conjunto candidato. V2 não é promovido automaticamente.",
        m = "Distâncias maiores entre distribuições CPF-ancorada e SEM_CPF indicam menor transportabilidade das probabilidades m. O relatório produz evidência; não altera parâmetros."
    }
};

var output = Path.GetFullPath(options.OutputPath!);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, EvaluationJson.Options));
Console.WriteLine($"Relatório de avaliação gravado em {output}");

internal static class EvaluationJson
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
}

internal sealed record EvaluationOptions(
    string? LabelsPath,
    string? OutputPath,
    string? ConnectionString,
    int BirthWindowDays,
    int MaxCpfAnchoredPairs,
    int SamplePoolSize,
    decimal SmoothingAlpha,
    int CommandTimeoutSeconds,
    bool Help)
{
    public static EvaluationOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var raw = args[i];
            if (raw is "--help" or "-h") return new(null, null, null, 0, 0, 0, 0m, 0, true);
            if (!raw.StartsWith("--", StringComparison.Ordinal)) continue;
            raw = raw[2..];
            var eq = raw.IndexOf('=', StringComparison.Ordinal);
            if (eq >= 0) values[raw[..eq]] = raw[(eq + 1)..];
            else values[raw] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : null;
        }

        string? Get(string name) => values.TryGetValue(name, out var v) ? v : null;
        int Int(string name, int fallback, int min, int max)
        {
            var value = Get(name);
            if (value is null) return fallback;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < min || parsed > max)
                throw new ArgumentOutOfRangeException(nameof(args), $"--{name}: valor deve estar entre {min} e {max}.");
            return parsed;
        }
        decimal Decimal(string name, decimal fallback, decimal min, decimal max)
        {
            var value = Get(name);
            if (value is null) return fallback;
            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) || parsed < min || parsed > max)
                throw new ArgumentOutOfRangeException(nameof(args), $"--{name}: valor deve estar entre {min} e {max}.");
            return parsed;
        }

        var labels = Get("labels") ?? throw new ArgumentException("--labels é obrigatório.");
        var output = Get("output") ?? "linkage-evaluation-report.json";
        return new EvaluationOptions(
            labels,
            output,
            Get("connection-string"),
            Int("birth-window-days", 7, 1, 31),
            Int("max-cpf-anchored-pairs", 50_000, 100, 1_000_000),
            Int("sample-pool-size", 500_000, 1_000, 5_000_000),
            Decimal("smoothing-alpha", 0.5m, 0.0001m, 100m),
            Int("command-timeout-seconds", 900, 1, 3600),
            false);
    }

    public static string Usage =>
        """
        Jornada.Linkage.Evaluation — DEV/HML, somente leitura, sem publicação

          --labels <arquivo.csv>              obrigatório; colunas pessoa_observacao_id,pessoa_uuid_verdade
          --output <relatorio.json>           padrão linkage-evaluation-report.json
          --connection-string <sql>           opcional; ou ConnectionStrings__Jornada
          --birth-window-days <1..31>         V2 candidato, padrão ±7 dias
          --max-cpf-anchored-pairs <N>        padrão 50000
          --sample-pool-size <N>              padrão 500000
          --smoothing-alpha <decimal>          padrão 0.5; mesmo default do Parameters Worker
          --command-timeout-seconds <1..3600> padrão 900

        O executável faz apenas SELECT nas tabelas operacionais. V2 é evidência experimental e nunca é publicado.
        """;
}

internal sealed record EvaluationLabel(long ObservationId, Guid TruthPersonUuid);

internal static class LabelCsv
{
    public static IReadOnlyList<EvaluationLabel> Read(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException("Arquivo de rótulos não encontrado.", full);
        var result = new List<EvaluationLabel>();
        var seen = new HashSet<long>();
        var lineNumber = 0;
        foreach (var raw in File.ReadLines(full))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var parts = ParseCsvLine(raw);
            if (lineNumber == 1 && parts.Count >= 2 && parts[0].Equals("pessoa_observacao_id", StringComparison.OrdinalIgnoreCase))
                continue;
            if (parts.Count < 2 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || !Guid.TryParse(parts[1], out var uuid))
                throw new InvalidDataException($"CSV inválido na linha {lineNumber}. Esperado pessoa_observacao_id,pessoa_uuid_verdade.");
            if (!seen.Add(id)) throw new InvalidDataException($"pessoa_observacao_id duplicado no CSV: {id}.");
            result.Add(new EvaluationLabel(id, uuid));
        }
        return result;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { result.Add(current.ToString().Trim()); current.Clear(); }
            else current.Append(c);
        }
        if (quoted) throw new InvalidDataException("CSV contém aspas não fechadas.");
        result.Add(current.ToString().Trim());
        return result;
    }
}

internal sealed record IdentityPair(
    long? ObservationId,
    Guid? TruthPersonUuid,
    string LeftName,
    DateOnly LeftBirthDate,
    string LeftMotherName,
    string RightName,
    DateOnly RightBirthDate,
    string RightMotherName);

internal sealed class LinkageEvaluation(SqlConnection connection, int commandTimeoutSeconds)
{
    public async Task<IReadOnlyList<IdentityPair>> LoadLabeledNoCpfPairsAsync(IReadOnlyList<EvaluationLabel> labels)
    {
        var result = new List<IdentityPair>(labels.Count);
        foreach (var label in labels)
        {
            await using var command = new SqlCommand(
                """
                SELECT po.pessoa_observacao_id,po.nome_completo,po.data_nascimento,po.nome_mae,
                       gp.pessoa_uuid,gp.nome_completo,gp.data_nascimento,gp.nome_mae
                FROM silver.pessoa_observacao po
                JOIN gold.pessoa gp ON gp.pessoa_uuid=@truth_uuid
                WHERE po.pessoa_observacao_id=@observation_id
                  AND po.cpf IS NULL;
                """, connection) { CommandTimeout = commandTimeoutSeconds };
            command.Parameters.Add("@observation_id", SqlDbType.BigInt).Value = label.ObservationId;
            command.Parameters.Add("@truth_uuid", SqlDbType.UniqueIdentifier).Value = label.TruthPersonUuid;
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) continue;
            result.Add(new IdentityPair(
                reader.GetInt64(0),
                reader.GetGuid(4),
                reader.GetString(1), DateOnly.FromDateTime(reader.GetDateTime(2)), reader.GetString(3),
                reader.GetString(5), DateOnly.FromDateTime(reader.GetDateTime(6)), reader.GetString(7)));
        }
        return result;
    }

    public async Task<IReadOnlyList<IdentityPair>> LoadIndependentCpfAnchoredPairsAsync(int sampleSize, int samplePoolSize)
    {
        await using var command = new SqlCommand(
            """
            WITH gold_sample AS (
                SELECT TOP (@pool_size) pessoa_uuid
                FROM gold.pessoa
                ORDER BY pessoa_uuid
            ), obs_por_gestor AS (
                SELECT vf.pessoa_uuid,po.gestor_id,po.pessoa_observacao_id,
                       po.nome_completo,po.data_nascimento,po.nome_mae,
                       ROW_NUMBER() OVER(PARTITION BY vf.pessoa_uuid,po.gestor_id
                                         ORDER BY po.source_as_of DESC,po.pessoa_observacao_id DESC) rn_gestor
                FROM gold_sample gs
                JOIN identidade.vinculo_fonte vf ON vf.pessoa_uuid=gs.pessoa_uuid
                     AND vf.ativo=1 AND vf.status='RESOLVIDO' AND vf.metodo_resolucao='CPF_DETERMINISTICO'
                JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=vf.pessoa_observacao_id
                WHERE po.cpf IS NOT NULL
            ), fontes_independentes AS (
                SELECT opg.*,
                       ROW_NUMBER() OVER(PARTITION BY opg.pessoa_uuid
                                         ORDER BY HASHBYTES('SHA2_256',CONCAT(CONVERT(nvarchar(36),opg.pessoa_uuid),':',CONVERT(nvarchar(20),opg.gestor_id))),
                                                  opg.gestor_id,opg.pessoa_observacao_id) rn_fonte,
                       COUNT_BIG(*) OVER(PARTITION BY opg.pessoa_uuid) qtd_fontes
                FROM obs_por_gestor opg
                WHERE opg.rn_gestor=1
            )
            SELECT TOP (@sample_size)
                   a.nome_completo,a.data_nascimento,a.nome_mae,
                   b.nome_completo,b.data_nascimento,b.nome_mae
            FROM fontes_independentes a
            JOIN fontes_independentes b ON b.pessoa_uuid=a.pessoa_uuid AND a.rn_fonte=1 AND b.rn_fonte=2
            WHERE a.qtd_fontes>=2 AND a.gestor_id<>b.gestor_id
            ORDER BY HASHBYTES('SHA2_256',CONVERT(nvarchar(36),a.pessoa_uuid)),a.pessoa_uuid;
            """, connection) { CommandTimeout = commandTimeoutSeconds };
        command.Parameters.Add("@sample_size", SqlDbType.Int).Value = sampleSize;
        command.Parameters.Add("@pool_size", SqlDbType.Int).Value = samplePoolSize;
        var result = new List<IdentityPair>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new IdentityPair(
                null, null,
                reader.GetString(0), DateOnly.FromDateTime(reader.GetDateTime(1)), reader.GetString(2),
                reader.GetString(3), DateOnly.FromDateTime(reader.GetDateTime(4)), reader.GetString(5)));
        }
        return result;
    }

    public async Task<object> EvaluateBlockingAsync(IReadOnlyList<IdentityPair> pairs, int windowDays)
    {
        var rows = new List<BlockingRow>(pairs.Count);
        foreach (var pair in pairs)
        {
            await using var command = new SqlCommand(
                """
                SELECT
                    (SELECT COUNT_BIG(*) FROM gold.pessoa WHERE data_nascimento=@birth_date) exact_candidates,
                    (SELECT COUNT_BIG(*) FROM gold.pessoa WHERE data_nascimento BETWEEN DATEADD(DAY,-@window,@birth_date) AND DATEADD(DAY,@window,@birth_date)) window_candidates;
                """, connection) { CommandTimeout = commandTimeoutSeconds };
            command.Parameters.Add("@birth_date", SqlDbType.Date).Value = pair.LeftBirthDate.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@window", SqlDbType.Int).Value = windowDays;
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            var exactCandidates = reader.GetInt64(0);
            var windowCandidates = reader.GetInt64(1);
            var birthDistance = Math.Abs(pair.LeftBirthDate.DayNumber - pair.RightBirthDate.DayNumber);
            rows.Add(new BlockingRow(birthDistance == 0, birthDistance <= windowDays, exactCandidates, windowCandidates));
        }

        return new
        {
            v1 = SummarizeBlocking(rows, r => r.V1Hit, r => r.V1Candidates),
            v2Candidate = SummarizeBlocking(rows, r => r.V2Hit, r => r.V2Candidates),
            deltaRecall = Rate(rows.Count(r => r.V2Hit), rows.Count) - Rate(rows.Count(r => r.V1Hit), rows.Count),
            warning = "V2 é somente candidato experimental. Maior recall deve ser analisado junto com expansão de candidatos/custo e falsos positivos."
        };
    }

    private static object SummarizeBlocking(IReadOnlyList<BlockingRow> rows, Func<BlockingRow, bool> hit, Func<BlockingRow, long> candidates)
    {
        var counts = rows.Select(candidates).OrderBy(x => x).ToArray();
        return new
        {
            sampleSize = rows.Count,
            trueUuidInsideBlock = rows.Count(hit),
            recall = Rate(rows.Count(hit), rows.Count),
            meanCandidates = counts.Length == 0 ? 0m : counts.Average(x => (decimal)x),
            medianCandidates = Percentile(counts, 0.50),
            p95Candidates = Percentile(counts, 0.95),
            maxCandidates = counts.Length == 0 ? 0L : counts[^1]
        };
    }

    private static decimal Rate(int numerator, int denominator) => denominator == 0 ? 0m : (decimal)numerator / denominator;
    private static long Percentile(long[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        var index = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private sealed record BlockingRow(bool V1Hit, bool V2Hit, long V1Candidates, long V2Candidates);
}

internal static class TransportabilityMetrics
{
    public static object Compare(IReadOnlyList<IdentityPair> cpfAnchored, IReadOnlyList<IdentityPair> noCpfLabeled, decimal smoothingAlpha)
    {
        var cpfName = Distribution(cpfAnchored.Select(p => IdentityComparison.CompareName(p.LeftName, p.RightName)), smoothingAlpha);
        var noCpfName = Distribution(noCpfLabeled.Select(p => IdentityComparison.CompareName(p.LeftName, p.RightName)), smoothingAlpha);
        var cpfMother = Distribution(cpfAnchored.Select(p => IdentityComparison.CompareName(p.LeftMotherName, p.RightMotherName)), smoothingAlpha);
        var noCpfMother = Distribution(noCpfLabeled.Select(p => IdentityComparison.CompareName(p.LeftMotherName, p.RightMotherName)), smoothingAlpha);
        var cpfBirthExact = ExactBirthRate(cpfAnchored, smoothingAlpha);
        var noCpfBirthExact = ExactBirthRate(noCpfLabeled, smoothingAlpha);
        return new
        {
            estimator = new { family = "match-conditional m", smoothing = "Dirichlet/Laplace", smoothingAlpha },
            cpfAnchored = new { sampleSize = cpfAnchored.Count, nome = cpfName, nomeMae = cpfMother, dataNascimentoExact = cpfBirthExact },
            noCpfLabeled = new { sampleSize = noCpfLabeled.Count, nome = noCpfName, nomeMae = noCpfMother, dataNascimentoExact = noCpfBirthExact },
            distance = new
            {
                nomeTotalVariation = TotalVariation(cpfName, noCpfName),
                nomeMaeTotalVariation = TotalVariation(cpfMother, noCpfMother),
                dataNascimentoExactAbsoluteDelta = Math.Abs(cpfBirthExact - noCpfBirthExact)
            }
        };
    }

    private static IReadOnlyDictionary<string, decimal> Distribution(IEnumerable<NameComparisonState> states, decimal alpha)
    {
        var values = states.ToArray();
        var allStates = Enum.GetValues<NameComparisonState>();
        var denominator = values.Length + alpha * allStates.Length;
        return allStates.ToDictionary(
            x => x.ToString(),
            x => ((decimal)values.Count(v => v == x) + alpha) / denominator,
            StringComparer.Ordinal);
    }

    private static decimal ExactBirthRate(IReadOnlyList<IdentityPair> pairs, decimal alpha) =>
        ((decimal)pairs.Count(p => p.LeftBirthDate == p.RightBirthDate) + alpha) / (pairs.Count + 2m * alpha);

    private static decimal TotalVariation(IReadOnlyDictionary<string, decimal> left, IReadOnlyDictionary<string, decimal> right) =>
        0.5m * left.Keys.Sum(k => Math.Abs(left[k] - right[k]));
}

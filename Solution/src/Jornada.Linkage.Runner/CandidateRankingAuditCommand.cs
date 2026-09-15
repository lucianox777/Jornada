using System.Globalization;
using System.Text;
using System.Text.Json;
using Jornada.Operational.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jornada.Linkage.Runner;

internal static class CandidateRankingAuditCommand
{
    internal const string LabelsOption = "--candidate-ranking-audit-labels";
    internal const string OutputOption = "--candidate-ranking-audit-output";

    internal static bool IsRequested(string[] args) =>
        args.Any(static arg =>
            arg.Equals(LabelsOption, StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith(LabelsOption + "=", StringComparison.OrdinalIgnoreCase));

    internal static async Task ExecuteAsync(
        string[] args,
        IConfiguration configuration,
        IOperationalSqlAdapter operationalSql,
        CancellationToken ct)
    {
        var labelsPath = ReadRequiredOption(args, LabelsOption);
        var outputPath = Path.GetFullPath(ReadRequiredOption(args, OutputOption));
        var labels = ReadLabels(labelsPath);
        if (labels.Count == 0)
            throw new InvalidOperationException("A amostra rotulada da auditoria de candidatos está vazia.");

        var linkage = new SqlProbabilisticIdentityLinkage(
            configuration,
            operationalSql,
            NullLogger<SqlProbabilisticIdentityLinkage>.Instance);
        var model = await linkage.GetActiveModelAsync(ct);

        var rows = new List<ProbabilisticCandidateRankingAudit>(labels.Count);
        foreach (var label in labels)
        {
            rows.Add(await linkage.DiagnoseCandidateRankingAsync(
                label.ObservationId,
                label.TruthPersonUuid,
                model.ModelId,
                ct));
        }

        var candidateCounts = rows
            .Select(static row => row.CandidateCount)
            .OrderBy(static count => count)
            .ToArray();
        var truthInside = rows.Count(static row => row.TruthInCandidateSet);
        var truthAbsent = rows.Count - truthInside;
        var deterministicTop1 = rows.Count(static row => row.TruthDeterministicRank == 1);
        var deterministicTop2 = rows.Count(static row => row.TruthInTop2);
        var deterministicRankGt2 = rows.Count(static row => row.TruthDeterministicRank > 2);
        var evidenceTop = rows.Count(static row => row.TruthEvidenceRank == 1);
        var evidenceRankGt2 = rows.Count(static row => row.TruthEvidenceRank > 2);
        var tiedAtBestEvidence = rows.Count(static row => row.TruthEvidenceRank == 1 && row.TruthTieCount > 1);

        var nonTop2 = rows
            .Where(static row => !row.TruthInCandidateSet || row.TruthDeterministicRank > 2)
            .Select(static row => new
            {
                pessoaObservacaoId = row.ObservationId,
                truthUuid = row.TruthPersonUuid,
                truthInCandidateSet = row.TruthInCandidateSet,
                candidateCount = row.CandidateCount,
                truthDeterministicRank = row.TruthDeterministicRank,
                truthEvidenceRank = row.TruthEvidenceRank,
                truthTieCount = row.TruthTieCount,
                truthPosterior = row.TruthPosterior,
                truthLogOdds = row.TruthLogOdds,
                topCandidateUuid = row.TopCandidateUuid,
                topPosterior = row.TopPosterior,
                topLogOdds = row.TopLogOdds,
                rankingGapToTop = row.RankingGapToTop,
                rankingSpace = row.RankingSpace
            })
            .ToArray();

        var report = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            purpose = "DEV_HML_ONLY_READ_ONLY_CANDIDATE_RANKING",
            safeguards = new[]
            {
                "read-only against Jornada operational tables",
                "does not create linkage_run",
                "does not write IDENTITY_MAP/vinculo_fonte",
                "does not update Gold",
                "reuses Runner active model, frozen ruleset, candidate loader and scorer"
            },
            model = new
            {
                modelId = model.ModelId,
                modelVersion = model.Version,
                algorithmVersion = model.AlgorithmVersion,
                blockingRuleSetVersion = model.BlockingContract?.RuleSetVersion,
                blockingRuleSetFingerprint = model.BlockingContract?.FingerprintSha256,
                projectionSchemaVersion = model.BlockingContract?.ProjectionSchemaVersion,
                projectionFingerprint = model.BlockingContract?.ProjectionFingerprintSha256
            },
            summary = new
            {
                sampleSize = rows.Count,
                truthInsideCandidateSet = truthInside,
                candidateRecallPct = Percentage(truthInside, rows.Count),
                truthAbsentFromCandidateSet = truthAbsent,
                truthDeterministicTop1 = deterministicTop1,
                truthDeterministicTop2 = deterministicTop2,
                truthPresentDeterministicRankGreaterThan2 = deterministicRankGt2,
                truthEvidenceTop = evidenceTop,
                truthPresentEvidenceRankGreaterThan2 = evidenceRankGt2,
                truthTiedAtBestEvidence = tiedAtBestEvidence,
                meanCandidateCount = rows.Count == 0 ? 0m : rows.Average(static row => (decimal)row.CandidateCount),
                p95CandidateCount = Percentile(candidateCounts, 0.95),
                maxCandidateCount = candidateCounts.Length == 0 ? 0 : candidateCounts[^1]
            },
            nonTop2,
            interpretation = new
            {
                deterministicRank = "Ordena pela evidência do modelo e usa UUID apenas para representação determinística de empates.",
                evidenceRank = "1 + quantidade de candidatos com evidência estritamente superior. Empates não pioram o rank evidencial.",
                candidateRecall = "truthAbsentFromCandidateSet isola falha de blocking/candidate recall; verdade presente com evidenceRank>2 isola perda de ranking/scoring."
            }
        };

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            outputPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
            ct);
        Console.WriteLine($"Auditoria read-only de candidate ranking gravada em {outputPath}");
    }

    private static string ReadRequiredOption(string[] args, string option)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var raw = args[i];
            if (raw.StartsWith(option + "=", StringComparison.OrdinalIgnoreCase))
            {
                var value = raw[(option.Length + 1)..];
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            if (!raw.Equals(option, StringComparison.OrdinalIgnoreCase)) continue;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return args[i + 1];
            break;
        }
        throw new ArgumentException($"{option} é obrigatório no modo de auditoria de candidate ranking.");
    }

    private static IReadOnlyList<CandidateRankingLabel> ReadLabels(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new FileNotFoundException("Arquivo de rótulos da auditoria não encontrado.", full);

        var result = new List<CandidateRankingLabel>();
        var seen = new HashSet<long>();
        var lineNumber = 0;
        foreach (var raw in File.ReadLines(full))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var parts = ParseCsvLine(raw);
            if (lineNumber == 1 && parts.Count >= 2 &&
                parts[0].Equals("pessoa_observacao_id", StringComparison.OrdinalIgnoreCase))
                continue;
            if (parts.Count < 2 ||
                !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var observationId) ||
                !Guid.TryParse(parts[1], out var truthUuid))
                throw new InvalidDataException($"CSV inválido na linha {lineNumber}. Esperado pessoa_observacao_id,pessoa_uuid_verdade.");
            if (!seen.Add(observationId))
                throw new InvalidDataException($"pessoa_observacao_id duplicado no CSV: {observationId}.");
            result.Add(new CandidateRankingLabel(observationId, truthUuid));
        }
        return result;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == ',' && !quoted)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        if (quoted) throw new InvalidDataException("CSV contém aspas não fechadas.");
        result.Add(current.ToString().Trim());
        return result;
    }

    private static decimal Percentage(int numerator, int denominator) =>
        denominator == 0 ? 0m : decimal.Round(100m * numerator / denominator, 4);

    private static int Percentile(int[] sorted, double probability)
    {
        if (sorted.Length == 0) return 0;
        var index = (int)Math.Ceiling(probability * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private sealed record CandidateRankingLabel(long ObservationId, Guid TruthPersonUuid);
}

using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Auditoria DEV/HML read-only do blocking efetivo. Reutiliza o modelo ativo, o ruleset
/// persistido, o planner e o mesmo query builder do Runner para medir cada passe sem
/// reimplementar a semântica de OR intra-atributo / AND entre atributos.
/// </summary>
internal static class BlockingPassAuditCommand
{
    internal const string LabelsOption = "--blocking-pass-audit-labels";
    internal const string OutputOption = "--blocking-pass-audit-output";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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
            throw new InvalidOperationException("A amostra rotulada da auditoria de passes está vazia.");

        var linkage = new SqlProbabilisticIdentityLinkage(
            configuration,
            operationalSql,
            NullLogger<SqlProbabilisticIdentityLinkage>.Instance);
        var model = await linkage.GetActiveModelAsync(ct);

        LinkageDynamicRuleSet ruleSet;
        await using (var ruleSetConnection = await operationalSql.OpenAsync(ct))
        {
            ruleSet = await LinkageRuleSetReader.TryLoadAsync(ruleSetConnection, model.ModelId, ct)
                ?? throw new InvalidOperationException(
                    $"Modelo ativo {model.ModelId} não possui ruleset persistido; auditoria de passes recusada.");
        }

        var timeout = Math.Max(1, configuration.GetValue("ProbabilisticLinkage:CommandTimeoutSeconds", 900));
        var observations = new List<ObservationPassAudit>(labels.Count);
        foreach (var label in labels)
        {
            var observation = await LoadObservationAsync(
                operationalSql,
                configuration,
                label.ObservationId,
                ct);
            observations.Add(await MeasureObservationAsync(
                operationalSql,
                ruleSet,
                observation,
                label,
                timeout,
                ct));
        }

        var passOrder = ruleSet.EffectiveBlockingPasses.ToArray();
        var cumulativeTruth = new HashSet<long>();
        var passes = new List<object>(passOrder.Length);
        foreach (var pass in passOrder)
        {
            var measurements = observations
                .Select(row => row.Passes.Single(item => item.PassId == pass.PassId))
                .ToArray();
            var candidateCounts = measurements.Select(static item => item.CandidateCount).Order().ToArray();
            var truthInside = measurements.Count(static item => item.TruthInsidePass);
            var exclusiveTruth = observations.Count(row =>
                row.Passes.Single(item => item.PassId == pass.PassId).TruthInsidePass &&
                row.Passes.Count(static item => item.TruthInsidePass) == 1);
            var incrementalTruth = 0;
            foreach (var row in observations)
            {
                var item = row.Passes.Single(candidate => candidate.PassId == pass.PassId);
                if (item.TruthInsidePass && cumulativeTruth.Add(row.ObservationId))
                    incrementalTruth++;
            }

            passes.Add(new
            {
                passId = pass.PassId,
                fields = pass.Fields,
                sampleSize = labels.Count,
                plannedObservations = measurements.Count(static item => item.Planned),
                zeroCandidateObservations = measurements.Count(static item => item.CandidateCount == 0),
                truthInsidePass = truthInside,
                passRecallPct = Percentage(truthInside, labels.Count),
                exclusiveTruthRecovered = exclusiveTruth,
                incrementalTruthRecovered = incrementalTruth,
                candidatePairs = measurements.Sum(static item => item.CandidateCount),
                meanCandidateCount = measurements.Length == 0
                    ? 0m
                    : decimal.Round(measurements.Average(static item => (decimal)item.CandidateCount), 4),
                p95CandidateCount = Percentile(candidateCounts, 0.95),
                maxCandidateCount = candidateCounts.Length == 0 ? 0L : candidateCounts[^1]
            });
        }

        var unionCounts = observations.Select(static row => row.UnionCandidateCount).Order().ToArray();
        var truthInsideUnion = observations.Count(static row => row.TruthInsideUnion);
        var unionCandidatePairs = observations.Sum(static row => row.UnionCandidateCount);
        var summedPassCandidatePairs = observations.Sum(static row => row.Passes.Sum(static pass => pass.CandidateCount));
        var overlapCandidatePairs = summedPassCandidatePairs - unionCandidatePairs;
        if (overlapCandidatePairs < 0)
            throw new InvalidOperationException("Contabilidade de fan-out por passe ficou menor que a união efetiva de candidatos.");

        var report = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            purpose = "DEV_HML_ONLY_READ_ONLY_BLOCKING_PASS_EVIDENCE",
            safeguards = new[]
            {
                "read-only against Jornada operational tables",
                "does not create linkage_run",
                "does not write IDENTITY_MAP/vinculo_fonte",
                "does not update Gold",
                "reuses Runner active model, frozen ruleset, BlockingRuleSetCandidatePlanner and BlockingProjectionCandidateQueryBuilder"
            },
            model = new
            {
                modelId = model.ModelId,
                modelVersion = model.Version,
                algorithmVersion = model.AlgorithmVersion,
                blockingRuleSetVersion = ruleSet.RuleSetVersion,
                blockingRuleSetFingerprint = ruleSet.FingerprintSha256,
                blockingAlgorithmVersion = ruleSet.AlgorithmVersion,
                projectionSchemaVersion = ruleSet.ProjectionSchemaVersion,
                projectionFingerprint = ruleSet.ProjectionFingerprintSha256
            },
            summary = new
            {
                sampleSize = labels.Count,
                ruleSetPassCount = passOrder.Length,
                truthInsideUnion,
                unionRecallPct = Percentage(truthInsideUnion, labels.Count),
                unionCandidatePairs,
                summedPassCandidatePairs,
                overlapCandidatePairs,
                meanUnionCandidateCount = observations.Count == 0
                    ? 0m
                    : decimal.Round(observations.Average(static row => (decimal)row.UnionCandidateCount), 4),
                p95UnionCandidateCount = Percentile(unionCounts, 0.95),
                maxUnionCandidateCount = unionCounts.Length == 0 ? 0L : unionCounts[^1]
            },
            passes,
            interpretation = new
            {
                passSemantics = "Cada passe é planejado pelo mesmo código do Runner; valores do mesmo atributo usam OR e atributos do passe usam AND.",
                passRecall = "truthInsidePass / sampleSize. Mede quanto da verdade rotulada cada passe recupera isoladamente.",
                incrementalTruthRecovered = "Quantidade de verdades ainda não recuperadas pelos passes anteriores e acrescentadas pelo passe corrente.",
                exclusiveTruthRecovered = "Quantidade de verdades recuperadas somente por este passe na amostra.",
                overlapCandidatePairs = "sum(candidatePairs por passe) - candidatePairs da união; quantifica duplicação de pares entre passes.",
                scope = "Evidência amostral DEV/HML; não cria threshold de homologação nem altera o algoritmo operacional."
            }
        };

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, JsonOptions), ct);
        Console.WriteLine($"Auditoria read-only de blocking por passe gravada em {outputPath}");
    }

    private static async Task<ObservationPassAudit> MeasureObservationAsync(
        IOperationalSqlAdapter operationalSql,
        LinkageDynamicRuleSet ruleSet,
        IdentityObservation observation,
        BlockingPassLabel label,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var planned = BlockingRuleSetCandidatePlanner.Plan(ruleSet, observation);
        var byPass = planned.ToDictionary(static pass => pass.PassId, StringComparer.Ordinal);
        var passRows = new List<PassAuditRow>(ruleSet.EffectiveBlockingPasses.Count);

        await using var connection = await operationalSql.OpenAsync(ct);
        var union = await MeasureLookupAsync(
            connection,
            planned,
            ruleSet,
            label.TruthPersonUuid,
            commandTimeoutSeconds,
            ct);

        foreach (var pass in ruleSet.EffectiveBlockingPasses)
        {
            if (!byPass.TryGetValue(pass.PassId, out var lookup))
            {
                passRows.Add(new PassAuditRow(pass.PassId, Planned: false, CandidateCount: 0, TruthInsidePass: false));
                continue;
            }

            var measured = await MeasureLookupAsync(
                connection,
                new[] { lookup },
                ruleSet,
                label.TruthPersonUuid,
                commandTimeoutSeconds,
                ct);
            passRows.Add(new PassAuditRow(pass.PassId, Planned: true, measured.CandidateCount, measured.TruthInside));
        }

        return new ObservationPassAudit(
            label.ObservationId,
            union.CandidateCount,
            union.TruthInside,
            passRows);
    }

    private static async Task<LookupMeasurement> MeasureLookupAsync(
        DbConnection connection,
        IReadOnlyList<BlockingCandidatePassLookup> passes,
        LinkageDynamicRuleSet ruleSet,
        Guid truthPersonUuid,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = commandTimeoutSeconds;
        var candidateUuidQuery = BlockingProjectionCandidateQueryBuilder.BuildCandidateUuidQuery(
            command,
            passes,
            ruleSet.ProjectionSchemaVersion,
            ruleSet.ProjectionFingerprintSha256);
        AddParameter(command, "@truth_uuid", DbType.Guid, truthPersonUuid);
        command.CommandText = $"""
            WITH candidate_uuid AS (
                {candidateUuidQuery}
            ), effective_candidate AS (
                SELECT DISTINCT g.pessoa_uuid
                  FROM candidate_uuid c
                  JOIN gold.pessoa g ON g.pessoa_uuid=c.pessoa_uuid
                 WHERE g.estado_identidade=N'REFERENCIA'
            )
            SELECT CAST(COUNT(*) AS BIGINT),
                   COALESCE(MAX(CASE WHEN pessoa_uuid=@truth_uuid THEN 1 ELSE 0 END),0)
              FROM effective_candidate;
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("Consulta de auditoria de blocking não retornou contagem.");
        var count = Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
        var truthInside = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture) == 1;
        return new LookupMeasurement(count, truthInside);
    }

    private static async Task<IdentityObservation> LoadObservationAsync(
        IOperationalSqlAdapter operationalSql,
        IConfiguration configuration,
        long observationId,
        CancellationToken ct)
    {
        await using var connection = await operationalSql.OpenAsync(ct);
        var timeout = Math.Max(1, configuration.GetValue("ProbabilisticLinkage:CommandTimeoutSeconds", 900));

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT cpf,cpf_ausente_motivo,nome_completo,data_nascimento,nome_mae
            FROM silver.pessoa_observacao
            WHERE pessoa_observacao_id=@observation_id;
            """;
        command.CommandTimeout = timeout;
        AddParameter(command, "@observation_id", DbType.Int64, observationId);

        string? cpf;
        string? cpfAbsentReason;
        string name;
        DateOnly birthDate;
        string? motherName;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException($"Observação {observationId} não encontrada.");
            cpf = reader.IsDBNull(0) ? null : reader.GetString(0);
            cpfAbsentReason = reader.IsDBNull(1) ? null : reader.GetString(1);
            name = reader.GetString(2);
            birthDate = DateOnly.FromDateTime(reader.GetDateTime(3));
            motherName = reader.IsDBNull(4) ? null : reader.GetString(4);
        }

        if (!string.IsNullOrWhiteSpace(cpf))
            throw new InvalidOperationException($"Observação {observationId} possui CPF; auditoria probabilística exige SEM_CPF.");

        var eligible = PersonResolutionContractCatalog.EligibleTransversal
            .Select(static field => field.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var attributes = new List<IdentityResolutionAttributeValue>();
        await using var attributeCommand = connection.CreateCommand();
        attributeCommand.CommandText = """
            SELECT atributo_codigo,valor
            FROM silver.pessoa_atributo_observacao
            WHERE pessoa_observacao_id=@observation_id
            ORDER BY atributo_codigo,atributo_instancia_chave,pessoa_atributo_observacao_id;
            """;
        attributeCommand.CommandTimeout = timeout;
        AddParameter(attributeCommand, "@observation_id", DbType.Int64, observationId);
        await using var attributeReader = await attributeCommand.ExecuteReaderAsync(ct);
        while (await attributeReader.ReadAsync(ct))
        {
            var code = attributeReader.GetString(0);
            if (eligible.Contains(code))
                attributes.Add(new IdentityResolutionAttributeValue(code, attributeReader.GetString(1)));
        }

        return new IdentityObservation(cpf, cpfAbsentReason, name, birthDate, motherName, attributes);
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
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
        throw new ArgumentException($"{option} é obrigatório no modo de auditoria de blocking por passe.");
    }

    private static IReadOnlyList<BlockingPassLabel> ReadLabels(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new FileNotFoundException("Arquivo de rótulos da auditoria não encontrado.", full);

        var result = new List<BlockingPassLabel>();
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
            result.Add(new BlockingPassLabel(observationId, truthUuid));
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

    private static long Percentile(long[] sorted, double probability)
    {
        if (sorted.Length == 0) return 0;
        var index = (int)Math.Ceiling(probability * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private sealed record BlockingPassLabel(long ObservationId, Guid TruthPersonUuid);
    private sealed record LookupMeasurement(long CandidateCount, bool TruthInside);
    private sealed record PassAuditRow(string PassId, bool Planned, long CandidateCount, bool TruthInsidePass);
    private sealed record ObservationPassAudit(
        long ObservationId,
        long UnionCandidateCount,
        bool TruthInsideUnion,
        IReadOnlyList<PassAuditRow> Passes);
}

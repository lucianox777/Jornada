using System.Data;
using System.Globalization;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Extensions.Configuration;

namespace Jornada.Linkage.Runner;

internal static class NameAbbreviationAuditCommand
{
    internal const string RunOption = "--name-abbreviation-audit-run";
    internal const string OutputOption = "--name-abbreviation-audit-output";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static bool IsRequested(string[] args) =>
        args.Any(static arg =>
            arg.Equals(RunOption, StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith(RunOption + "=", StringComparison.OrdinalIgnoreCase));

    internal static async Task ExecuteAsync(
        string[] args,
        IConfiguration configuration,
        IOperationalSqlAdapter operationalSql,
        CancellationToken ct)
    {
        var rawRunId = ReadRequiredOption(args, RunOption);
        if (!Guid.TryParse(rawRunId, out var runId))
            throw new ArgumentException($"{RunOption} deve ser UUID válido.");

        var outputPath = Path.GetFullPath(ReadRequiredOption(args, OutputOption));
        var timeout = Math.Max(1, configuration.GetValue("ProbabilisticLinkage:CommandTimeoutSeconds", 900));

        await using var connection = await operationalSql.OpenAsync(ct);
        string algorithmVersion;
        await using (var modelCommand = connection.CreateCommand())
        {
            modelCommand.CommandTimeout = timeout;
            modelCommand.CommandText = """
                SELECT m.algoritmo_versao
                FROM identidade.linkage_run lr
                JOIN identidade.modelo_linkage m ON m.modelo_id=lr.modelo_id
                WHERE lr.linkage_run_id=@run_id;
                """;
            AddParameter(modelCommand, "@run_id", DbType.Guid, runId);
            algorithmVersion = Convert.ToString(
                await modelCommand.ExecuteScalarAsync(ct),
                CultureInfo.InvariantCulture)
                ?? throw new InvalidOperationException($"Run {runId} não encontrado.");
        }

        var comparisonContract = LinkageParameterCatalog.NameComparisonContractForAlgorithm(algorithmVersion);
        var rows = new List<AuditRow>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandTimeout = timeout;
            command.CommandText = """
                SELECT
                    CASE
                      WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-%-POS-NAME_ABBREV-%'
                        THEN N'POS_NAME_ABBREV'
                      WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-%-NEG-MOTHER_COLLISION-%'
                        THEN N'NEG_MOTHER_COLLISION'
                    END AS scenario,
                    po.nome_completo,
                    gp.nome_completo
                FROM identidade.linkage_resultado r
                JOIN silver.pessoa_observacao po
                  ON po.pessoa_observacao_id=r.pessoa_observacao_id
                JOIN gold.pessoa gp
                  ON gp.pessoa_uuid=r.melhor_candidato_uuid
                WHERE r.linkage_run_id=@run_id
                  AND (
                    po.codigo_pessoa_origem LIKE N'SCALE-VAL-%-POS-NAME_ABBREV-%'
                    OR po.codigo_pessoa_origem LIKE N'SCALE-VAL-%-NEG-MOTHER_COLLISION-%'
                  )
                ORDER BY scenario,po.pessoa_observacao_id;
                """;
            AddParameter(command, "@run_id", DbType.Guid, runId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var scenario = reader.GetString(0);
                var observedName = reader.GetString(1);
                var candidateName = reader.GetString(2);
                rows.Add(new AuditRow(
                    scenario,
                    IdentityComparison.CompareName(observedName, candidateName, comparisonContract).ToString(),
                    IdentityComparison.IsAbbreviationCompatible(observedName, candidateName)));
            }
        }

        var positive = Summarize(rows, "POS_NAME_ABBREV");
        var negative = Summarize(rows, "NEG_MOTHER_COLLISION");
        if (positive.Total == 0 || negative.Total == 0)
            throw new InvalidOperationException(
                $"Run {runId} não contém os dois cenários necessários: NAME_ABBREV={positive.Total}; MOTHER_COLLISION={negative.Total}.");

        var report = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            purpose = "DEV_READ_ONLY_ABBREVIATION_COMPATIBILITY_DIAGNOSTIC",
            runId,
            algorithmVersion,
            currentNameComparisonContract = comparisonContract.ToString(),
            proposedDiagnostic = new
            {
                version = IdentityComparison.AbbreviationCompatibilityVersionV1,
                changesPolicy = false,
                changesScoring = false,
                changesParameters = false,
                semantics = "Positional content tokens; exact token or one-letter initial compatible with full token; at least one abbreviation; no conflicting token."
            },
            positiveNameAbbrev = positive,
            negativeMotherCollision = negative,
            separation = new
            {
                allPositiveNameAbbrevDetected = positive.Compatible == positive.Total,
                noNegativeMotherCollisionDetected = negative.Compatible == 0,
                selectedFixtureClassesSeparated =
                    positive.Compatible == positive.Total &&
                    negative.Compatible == 0
            },
            interpretation = new
            {
                scope = "Read-only DEV evidence over the current validation fixture and its best candidate only.",
                currentState = "Shows how the operational name comparator classifies the same pairs today.",
                limitation = "This does not estimate m/u for a new state and therefore does not authorize changing the scorer, threshold, prior or active algorithm."
            }
        };

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, JsonOptions), ct);
        Console.WriteLine($"Auditoria read-only de abreviação compatível gravada em {outputPath}");
    }

    private static ScenarioSummary Summarize(IReadOnlyList<AuditRow> rows, string scenario)
    {
        var selected = rows.Where(row => row.Scenario == scenario).ToArray();
        return new ScenarioSummary(
            selected.Length,
            selected.Count(static row => row.AbbreviationCompatible),
            selected
                .GroupBy(static row => row.CurrentState, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal));
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, DbType type, object value)
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
        throw new ArgumentException($"{option} é obrigatório na auditoria de abreviação.");
    }

    private sealed record AuditRow(string Scenario, string CurrentState, bool AbbreviationCompatible);
    private sealed record ScenarioSummary(
        int Total,
        int Compatible,
        IReadOnlyDictionary<string, int> CurrentStateCounts);
}

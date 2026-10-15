using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Esquema determinístico de células FS derivado do catálogo versionado.
/// Continua diagnóstico: não estima m/u/TF, não executa SQL e não ativa V9.
/// </summary>
public static class PersonResolutionFsCellSchema
{
    public const string Version = "PERSON_RESOLUTION_FS_CELL_SCHEMA_V2";

    public sealed record Cell(
        string AttributeCode,
        string ComparisonContract,
        string ComparisonLevel,
        PersonResolutionEvidenceRole EvidenceRole,
        PersonResolutionDependencyPolicy DependencyPolicy,
        NominalFrequencyUnitV9? TfUnit,
        CalibrationTfSource TfSource,
        IReadOnlyList<string> CrossCompareWith);

    public sealed record Schema(
        string Version,
        string StrategyCatalogVersion,
        string MissingPolicyVersion,
        string FingerprintSha256,
        IReadOnlyList<Cell> Cells);

    public static Schema Generate(IEnumerable<string> attributeCodes)
    {
        ArgumentNullException.ThrowIfNull(attributeCodes);
        var requested = attributeCodes.ToArray();
        if (requested.Length is < 1 or > 32 || requested.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one explicit canonical attribute is required.");
        if (requested.Distinct(StringComparer.Ordinal).Count() != requested.Length)
            throw new ArgumentException("Duplicate attribute in FS schema.");

        PersonResolutionCalibrationStrategyCatalog.ValidateAndList();
        var cells = new List<Cell>();

        foreach (var code in requested.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!PersonResolutionCalibrationStrategyCatalog.TryGet(code, out var strategy)
                || !string.Equals(code, strategy.AttributeCode, StringComparison.Ordinal))
                throw new InvalidDataException("Unknown or ineligible canonical FS attribute: " + code);

            var levels = strategy.FsComparisonLevels;
            if (levels.Distinct(StringComparer.Ordinal).Count() != levels.Count
                || !levels.Contains("Missing", StringComparer.Ordinal))
                throw new InvalidDataException("FS levels must contain one canonical Missing state.");

            foreach (var level in levels.OrderBy(x => x, StringComparer.Ordinal))
            {
                var tf = strategy.TfPolicies.SingleOrDefault(x =>
                    string.Equals(x.ComparisonLevel, level, StringComparison.Ordinal));
                cells.Add(new Cell(
                    code,
                    strategy.ComparisonContract,
                    level,
                    strategy.EvidenceRole,
                    strategy.DependencyPolicy,
                    tf?.Unit,
                    tf?.Source ?? CalibrationTfSource.None,
                    strategy.CrossCompareWith));
            }
        }

        var canonical = Version + "\n"
            + PersonResolutionCalibrationStrategyCatalog.Version + "\n"
            + PersonResolutionMissingValuePolicy.Version + "\n"
            + string.Join("\n", cells.Select(x =>
                x.AttributeCode + "\t"
                + x.ComparisonContract + "\t"
                + x.ComparisonLevel + "\t"
                + x.EvidenceRole + "\t"
                + x.DependencyPolicy + "\t"
                + (x.TfUnit?.ToString() ?? "NONE") + "\t"
                + x.TfSource + "\t"
                + string.Join(",", x.CrossCompareWith.OrderBy(v => v, StringComparer.Ordinal))));

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();

        return new Schema(
            Version,
            PersonResolutionCalibrationStrategyCatalog.Version,
            PersonResolutionMissingValuePolicy.Version,
            hash,
            cells.AsReadOnly());
    }
}

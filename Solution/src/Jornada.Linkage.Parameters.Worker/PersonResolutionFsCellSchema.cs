using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Deterministic FS cell schema from the canonical, versioned attribute strategies.
/// Schema generation is diagnostic only: no fitting, SQL execution or activation.
/// </summary>
public static class PersonResolutionFsCellSchema
{
    public const string Version = "PERSON_RESOLUTION_FS_CELL_SCHEMA_V1";

    public sealed record Cell(string AttributeCode, string ComparisonLevel, string TfSource);

    public sealed record Schema(string Version, string StrategyCatalogVersion,
        string FingerprintSha256, IReadOnlyList<Cell> Cells);

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
                || !levels.Contains("MISSING", StringComparer.Ordinal)
                || !levels.Contains("DISAGREE", StringComparer.Ordinal))
                throw new InvalidDataException("FS levels must include unique MISSING and DISAGREE states.");
            foreach (var level in levels.OrderBy(x => x, StringComparer.Ordinal))
                cells.Add(new Cell(code, level, strategy.TfSource));
        }
        var canonical = Version + "\n" + PersonResolutionCalibrationStrategyCatalog.Version
            + "\n" + string.Join("\n", cells.Select(x =>
                x.AttributeCode + "\t" + x.ComparisonLevel + "\t" + x.TfSource));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
        return new Schema(Version, PersonResolutionCalibrationStrategyCatalog.Version,
            hash, cells.AsReadOnly());
    }
}

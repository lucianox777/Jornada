using System.Globalization;
using System.Text;
using Jornada.Contracts;

namespace Jornada.Linkage.Evaluation;

/// <summary>Exports canonical, versioned C# blocking projections for immutable V9 snapshots.</summary>
public static class V9CanonicalObservationCsvExporter
{
    public const string Version = "V9_CANONICAL_CSV_EXPORTER_V1";
    public const string Header = "observacao_id,base_person_id,particao,gestor,nome,nome_mae,data_nascimento,cpf,nome_normalizado,nome_fonetico,nome_mae_normalizado,nome_mae_fonetico";
    public const string SurnameExportVersion = "V9_CANONICAL_SURNAME_EXPORT_V1";
    public const string SurnameHeader = Header +
        ",nome_ultimo_sobrenome,nome_sobrenomes_json" +
        ",mae_ultimo_sobrenome,mae_sobrenomes_json";

    public sealed record Input(string ObservationId, string BasePersonId, string Partition,
        string Gestor, string? Name, string? MotherName, DateOnly? BirthDate, string? Cpf);

    // Independent opt-in envelope; legacy canonical CSV and its fingerprints
    // remain byte-for-byte unchanged until all SQL/SQLite parity gates pass.
    public static void ExportWithCanonicalSurnames(
        TextWriter writer, IEnumerable<Input> observations) =>
        ExportCore(writer, observations, includeSurnames: true);

    public static void Export(TextWriter writer, IEnumerable<Input> observations) =>
        ExportCore(writer, observations, includeSurnames: false);

    private static void ExportCore(
        TextWriter writer, IEnumerable<Input> observations, bool includeSurnames)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(observations);
        writer.WriteLine(includeSurnames ? SurnameHeader : Header);
        foreach (var row in observations)
        {
            if (string.IsNullOrWhiteSpace(row.ObservationId) ||
                string.IsNullOrWhiteSpace(row.BasePersonId) ||
                row.Partition is not ("TRAIN" or "VALIDATION" or "TEST"))
                throw new ArgumentException("Invalid observation identity or partition");
            // The projector intentionally emits MULTIPLE surname-presence keys
            // for name_surnames and mother_name_surnames. Never ToDictionary()
            // the full set by Feature: common multi-surname names would throw.
            // This export needs only the four single-valued canonical features.
            var keys = BlockingProjectionKeyProjector.Project(
                row.Name, row.MotherName, row.BirthDate);
            string Get(string feature) =>
                keys.FirstOrDefault(x => string.Equals(
                    x.Feature, feature, StringComparison.Ordinal))?.Value ?? "";
            var fields = new[] {
                row.ObservationId, row.BasePersonId, row.Partition, row.Gestor,
                row.Name ?? "", row.MotherName ?? "",
                row.BirthDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                row.Cpf ?? "",
                Get(BlockingFeatureNames.FullName),
                Get(BlockingFeatureNames.FullNamePhoneticPtBr),
                Get(BlockingFeatureNames.MotherFullName),
                Get(BlockingFeatureNames.MotherFullNamePhoneticPtBr)
            };
            if (includeSurnames)
            {
                // Both person and mother MUST use exactly the same versioned
                // BrazilianNameComponents semantics as the physical V9 audit.
                var surnames = new[]
                {
                    V9NameRequiredSqlCellAudit.Last(row.Name) ?? "",
                    System.Text.Json.JsonSerializer.Serialize(
                        V9NameRequiredSqlCellAudit.AnySurname(row.Name)),
                    V9NameRequiredSqlCellAudit.Last(row.MotherName) ?? "",
                    System.Text.Json.JsonSerializer.Serialize(
                        V9NameRequiredSqlCellAudit.AnySurname(row.MotherName))
                };
                fields = fields.Concat(surnames).ToArray();
            }
            writer.WriteLine(string.Join(",", fields.Select(Escape)));
        }
    }

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}

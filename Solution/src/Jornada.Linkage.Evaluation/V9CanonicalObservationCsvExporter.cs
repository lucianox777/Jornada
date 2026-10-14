using System.Globalization;
using System.Text;
using Jornada.Contracts;

namespace Jornada.Linkage.Evaluation;

/// <summary>Exports canonical, versioned C# blocking projections for immutable V9 snapshots.</summary>
public static class V9CanonicalObservationCsvExporter
{
    public const string Version = "V9_CANONICAL_CSV_EXPORTER_V1";
    public const string Header = "observacao_id,base_person_id,particao,gestor,nome,nome_mae,data_nascimento,cpf,nome_normalizado,nome_fonetico,nome_mae_normalizado,nome_mae_fonetico";

    public sealed record Input(string ObservationId, string BasePersonId, string Partition,
        string Gestor, string? Name, string? MotherName, DateOnly? BirthDate, string? Cpf);

    public static void Export(TextWriter writer, IEnumerable<Input> observations)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(observations);
        writer.WriteLine(Header);
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
            writer.WriteLine(string.Join(",", fields.Select(Escape)));
        }
    }

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}

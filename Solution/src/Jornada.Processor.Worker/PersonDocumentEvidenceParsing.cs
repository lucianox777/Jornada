using System.Globalization;
using System.Text.Json;

namespace Jornada.Processor.Worker;

/// <summary>Uma data própria por ocorrência documental, nunca uma data por valor comprovado.</summary>
internal sealed record ParsedDocumentEvidence(
    string OccurrenceCode,
    string ModelCode,
    int ModelVersion,
    string EvidenceClass,
    DateOnly? EvidenceDate,
    DateTimeOffset? AttendanceDate,
    string? IssuerCode,
    string? IssuerState,
    IReadOnlyList<ParsedDocumentEvidenceValue> Values);

internal sealed record ParsedDocumentEvidenceValue(string AttributeCode, string OriginalValue);

internal static class PersonDocumentEvidenceParsing
{
    public static IReadOnlyList<ParsedDocumentEvidence> Parse(JsonElement person)
    {
        if (!person.TryGetProperty("evidenciasDocumentais", out var array)
            || array.ValueKind == JsonValueKind.Null)
            return Array.Empty<ParsedDocumentEvidence>();
        if (array.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("evidenciasDocumentais deve ser uma coleção de ocorrências.");

        var result = new List<ParsedDocumentEvidence>();
        var occurrences = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in array.EnumerateArray())
        {
            if (result.Count >= 40)
                throw new InvalidDataException("Uma Pessoa não pode conter mais de 40 ocorrências documentais.");
            var occurrence = Required(item, "ocorrenciaOrigemCodigo");
            if (!occurrences.Add(occurrence))
                throw new InvalidDataException("Ocorrência documental duplicada na Pessoa.");
            var model = Required(item, "modeloDocumentoCodigo");
            if (!item.TryGetProperty("modeloDocumentoVersao", out var version)
                || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var modelVersion) || modelVersion <= 0)
                throw new InvalidDataException("Versão do modelo documental inválida.");
            var cls = Required(item, "classeEvidencia");
            if (cls is not ("DOCUMENTO" or "AUTODECLARACAO"))
                throw new InvalidDataException("Classe de evidência documental inválida.");
            var ownDate = OptionalDate(item, "dataEvidencia");
            var attended = OptionalTime(item, "dataAtendimento");
            var issuer = Optional(item, "emissorCodigo");
            var state = Optional(item, "ufEmissor");
            if (state is not null && (state.Length != 2 || state.Any(ch => ch < 'A' || ch > 'Z')))
                throw new InvalidDataException("UF do emissor documental inválida.");

            if (!item.TryGetProperty("valores", out var values)
                || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is < 1 or > 40)
                throw new InvalidDataException("Ocorrência documental exige de 1 a 40 valores.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var collected = new List<ParsedDocumentEvidenceValue>();
            foreach (var value in values.EnumerateArray())
            {
                var key = Required(value, "atributoCodigo");
                if (!keys.Add(key))
                    throw new InvalidDataException("Atributo repetido na mesma ocorrência documental.");
                collected.Add(new ParsedDocumentEvidenceValue(key, Required(value, "valorOriginal")));
            }
            result.Add(new ParsedDocumentEvidence(occurrence, model, modelVersion, cls,
                ownDate, attended, issuer, state, collected));
        }
        return result;
    }

    private static string Required(JsonElement json, string name)
    {
        var value = Optional(json, name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Evidência documental exige {name} não vazio.");
        return value;
    }

    private static string? Optional(JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Evidência documental: {name} exige string.");
        return value.GetString();
    }

    private static DateOnly? OptionalDate(JsonElement json, string name)
    {
        var text = Optional(json, name);
        if (text is null) return null;
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            throw new InvalidDataException("dataEvidencia exige a data própria yyyy-MM-dd.");
        return date;
    }

    private static DateTimeOffset? OptionalTime(JsonElement json, string name)
    {
        var text = Optional(json, name);
        if (text is null) return null;
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var instant))
            throw new InvalidDataException("dataAtendimento inválida.");
        return instant;
    }
}

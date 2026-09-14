using System.Text.Json;

namespace Jornada.Processor.Worker;

internal sealed record ParsedPersonIdentifier(
    string Tipo,
    string? Namespace,
    string ValorOriginal,
    string ValorNormalizado,
    string? Emissor,
    string? UfEmissor,
    string StatusEvidencia,
    string? EvidenciaTipo,
    DateTimeOffset? VerificadoEm,
    bool OrigemLegada);

internal static class PersonIdentifierParsing
{
    public static IReadOnlyList<ParsedPersonIdentifier> Parse(
        JsonElement person,
        string? legacyCpf,
        string? legacyCodigoPessoaOrigem,
        string? manifestBasePessoaOrigem)
    {
        var result = new List<ParsedPersonIdentifier>();

        if (person.TryGetProperty("identificadores", out var identifiers)
            && identifiers.ValueKind == JsonValueKind.Array)
        {
            foreach (var identifier in identifiers.EnumerateArray())
                Add(result, ParseExplicit(identifier));
        }

        if (!string.IsNullOrWhiteSpace(legacyCpf))
        {
            var normalizedCpf = NormalizeDigits(legacyCpf);
            Add(result, new ParsedPersonIdentifier(
                "CPF",
                "BR",
                legacyCpf,
                normalizedCpf,
                null,
                null,
                "DECLARADO",
                null,
                null,
                true));
        }

        if (!string.IsNullOrWhiteSpace(legacyCodigoPessoaOrigem))
        {
            Add(result, new ParsedPersonIdentifier(
                "CODIGO_BASE_ORIGEM",
                string.IsNullOrWhiteSpace(manifestBasePessoaOrigem) ? null : manifestBasePessoaOrigem.Trim(),
                legacyCodigoPessoaOrigem,
                legacyCodigoPessoaOrigem.Trim(),
                null,
                null,
                "DECLARADO",
                null,
                null,
                true));
        }

        EnsureLegacyCpfConverges(result, legacyCpf);
        return result;
    }

    private static ParsedPersonIdentifier ParseExplicit(JsonElement identifier)
    {
        var type = Required(identifier, "tipo").Trim().ToUpperInvariant();
        var ns = Required(identifier, "namespace").Trim();
        var value = Required(identifier, "valor").Trim();
        var evidenceStatus = Required(identifier, "statusEvidencia").Trim().ToUpperInvariant();
        var issuer = Optional(identifier, "emissor")?.Trim();
        var issuerState = Optional(identifier, "ufEmissor")?.Trim().ToUpperInvariant();
        var evidenceType = Optional(identifier, "evidenciaTipo")?.Trim();
        var verifiedAt = OptionalDateTimeOffset(identifier, "verificadoEm");

        var normalized = type switch
        {
            "CPF" => NormalizeDigits(value),
            "CNS" => NormalizeDigits(value),
            "UUID_JORNADA" => NormalizeUuid(value),
            "RG" => NormalizeTextIdentifier(value),
            "CODIGO_BASE_ORIGEM" => value,
            "OUTRO" => value,
            _ => throw new InvalidDataException($"Tipo de identificador de Pessoa não suportado: {type}.")
        };

        if (type == "CPF" && !string.Equals(ns, "BR", StringComparison.Ordinal))
            throw new InvalidDataException("Identificador CPF exige namespace BR.");
        if (type == "CNS" && !string.Equals(ns, "BR", StringComparison.Ordinal))
            throw new InvalidDataException("Identificador CNS exige namespace BR.");
        if (type == "UUID_JORNADA" && !string.Equals(ns, "JORNADA", StringComparison.Ordinal))
            throw new InvalidDataException("Identificador UUID_JORNADA exige namespace JORNADA.");
        if (type == "RG" && (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(issuerState)))
            throw new InvalidDataException("Identificador RG exige emissor e UF do emissor.");
        if (evidenceStatus == "COMPROVADO" && !verifiedAt.HasValue)
            throw new InvalidDataException("Identificador COMPROVADO exige verificadoEm.");

        return new ParsedPersonIdentifier(
            type,
            ns,
            value,
            normalized,
            issuer,
            issuerState,
            evidenceStatus,
            evidenceType,
            verifiedAt,
            false);
    }

    private static void Add(List<ParsedPersonIdentifier> result, ParsedPersonIdentifier candidate)
    {
        var existing = result.FirstOrDefault(i =>
            string.Equals(i.Tipo, candidate.Tipo, StringComparison.Ordinal)
            && string.Equals(i.Namespace, candidate.Namespace, StringComparison.Ordinal)
            && string.Equals(i.ValorNormalizado, candidate.ValorNormalizado, StringComparison.Ordinal));
        if (existing is not null)
            return;

        result.Add(candidate);
    }

    private static void EnsureLegacyCpfConverges(IReadOnlyList<ParsedPersonIdentifier> identifiers, string? legacyCpf)
    {
        if (string.IsNullOrWhiteSpace(legacyCpf))
            return;

        var normalizedLegacy = NormalizeDigits(legacyCpf);
        var explicitCpfs = identifiers
            .Where(i => i.Tipo == "CPF" && !i.OrigemLegada)
            .Select(i => i.ValorNormalizado)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (explicitCpfs.Length > 0 && explicitCpfs.Any(cpf => !string.Equals(cpf, normalizedLegacy, StringComparison.Ordinal)))
            throw new InvalidDataException("CPF legado diverge do CPF informado em identificadores[].");
    }

    private static string NormalizeDigits(string value)
        => new(value.Where(char.IsDigit).ToArray());

    private static string NormalizeUuid(string value)
    {
        if (!Guid.TryParse(value, out var parsed))
            throw new InvalidDataException("UUID_JORNADA inválido.");
        return parsed.ToString("D");
    }

    private static string NormalizeTextIdentifier(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string Required(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Identificador exige {property}.");
        return value.GetString()!;
    }

    private static string? Optional(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static DateTimeOffset? OptionalDateTimeOffset(JsonElement element, string property)
    {
        var text = Optional(element, property);
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (!DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
            throw new InvalidDataException($"Identificador contém {property} inválido.");
        return parsed;
    }
}

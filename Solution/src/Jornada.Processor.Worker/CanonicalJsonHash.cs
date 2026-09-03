using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jornada.Processor.Worker;

/// <summary>
/// Canonicalização semântica usada para decidir versionamento interno.
/// - objetos: propriedades em ordem ordinal; propriedades nulas opcionais são omitidas;
/// - strings: Unicode NFC;
/// - números: valor numérico normalizado (600 == 600.0 == 600.00 == 6e2);
/// - arrays: ordem preservada por padrão; perfis de domínio podem declarar arrays-conjunto;
/// - campos técnicos podem ser excluídos sem afetar o conteúdo de negócio.
/// </summary>
internal static class CanonicalJsonHash
{
    private static readonly HashSet<string> PersonRecursiveExclusions = new(StringComparer.Ordinal)
    {
        // Frescor técnico informado pela origem não deve, sozinho, criar nova versão cadastral.
        "atualizadoEmOrigem"
    };

    private static readonly HashSet<string> PersonUnorderedArrays = new(StringComparer.Ordinal)
    {
        "atributosTransversais",
        "conferenciasDocumentais"
    };

    public static string Compute(JsonElement element, params string[] excludedRootProperties) =>
        ComputeInternal(element,
            excludedRootProperties.Length == 0 ? null : new HashSet<string>(excludedRootProperties, StringComparer.Ordinal),
            recursiveExclusions: null,
            unorderedArrays: null);

    public static string ComputePerson(JsonElement element) =>
        ComputeInternal(
            element,
            new HashSet<string>(new[] { "codigoPessoaOrigem", "sourceTransactionId" }, StringComparer.Ordinal),
            PersonRecursiveExclusions,
            PersonUnorderedArrays);

    private static string ComputeInternal(
        JsonElement element,
        HashSet<string>? rootExclusions,
        HashSet<string>? recursiveExclusions,
        HashSet<string>? unorderedArrays)
    {
        var canonical = CanonicalBytes(element, rootExclusions, recursiveExclusions, unorderedArrays, isRoot: true, propertyName: null);
        return Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
    }

    private static byte[] CanonicalBytes(
        JsonElement element,
        HashSet<string>? rootExclusions,
        HashSet<string>? recursiveExclusions,
        HashSet<string>? unorderedArrays,
        bool isRoot,
        string? propertyName)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(writer, element, rootExclusions, recursiveExclusions, unorderedArrays, isRoot, propertyName);
        }
        return buffer.ToArray();
    }

    private static void WriteCanonical(
        Utf8JsonWriter writer,
        JsonElement element,
        HashSet<string>? rootExclusions,
        HashSet<string>? recursiveExclusions,
        HashSet<string>? unorderedArrays,
        bool isRoot,
        string? propertyName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var properties = element.EnumerateObject()
                    .Select(p => new { Property = p, NormalizedName = p.Name.Normalize(NormalizationForm.FormC) })
                    .OrderBy(p => p.NormalizedName, StringComparer.Ordinal);
                foreach (var item in properties)
                {
                    var property = item.Property;
                    if (isRoot && rootExclusions?.Contains(property.Name) == true) continue;
                    if (recursiveExclusions?.Contains(property.Name) == true) continue;
                    // Nos contratos da Jornada, null em propriedade opcional equivale a ausência para versionamento.
                    if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
                    writer.WritePropertyName(item.NormalizedName);
                    WriteCanonical(writer, property.Value, rootExclusions, recursiveExclusions, unorderedArrays, false, property.Name);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                if (propertyName is not null && unorderedArrays?.Contains(propertyName) == true)
                {
                    var items = element.EnumerateArray()
                        .Select(x => CanonicalBytes(x, rootExclusions, recursiveExclusions, unorderedArrays, false, null))
                        .OrderBy(x => Convert.ToHexString(x), StringComparer.Ordinal)
                        .ToArray();
                    foreach (var bytes in items)
                        writer.WriteRawValue(bytes, skipInputValidation: true);
                }
                else
                {
                    foreach (var item in element.EnumerateArray())
                        WriteCanonical(writer, item, rootExclusions, recursiveExclusions, unorderedArrays, false, null);
                }
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue((element.GetString() ?? string.Empty).Normalize(NormalizationForm.FormC));
                break;

            case JsonValueKind.Number:
                if (element.TryGetDecimal(out var decimalValue))
                {
                    var canonicalDecimal = decimalValue.ToString("G29", CultureInfo.InvariantCulture);
                    writer.WriteRawValue(canonicalDecimal, skipInputValidation: true);
                }
                else
                {
                    var canonicalDouble = element.GetDouble().ToString("R", CultureInfo.InvariantCulture);
                    writer.WriteRawValue(canonicalDouble, skipInputValidation: true);
                }
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidDataException($"Tipo JSON não suportado no hash canônico: {element.ValueKind}.");
        }
    }
}

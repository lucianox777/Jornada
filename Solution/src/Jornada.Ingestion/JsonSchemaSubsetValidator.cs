using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jornada.Ingestion;

/// <summary>
/// Validador deliberadamente pequeno do subconjunto JSON Schema usado pelos contratos da Fase 1.
/// Suporta: type, required, properties, additionalProperties=false, items, enum, const,
/// minLength, maxLength, pattern, format(date/date-time), allOf e if/then/else.
/// A introdução de keywords fora deste subconjunto deve falhar na revisão de contrato/build, não ser ignorada silenciosamente.
/// </summary>
public sealed class JsonSchemaSubsetValidator
{
    private static readonly HashSet<string> SupportedKeywords = new(StringComparer.Ordinal)
    {
        "$schema", "$id", "title", "description",
        "type", "required", "properties", "additionalProperties", "items",
        "enum", "const", "minLength", "maxLength", "pattern", "format",
        "allOf", "if", "then", "else"
    };

    private readonly JsonDocument _schema;

    private JsonSchemaSubsetValidator(JsonDocument schema)
    {
        _schema = schema;
        ValidateSupportedKeywords(_schema.RootElement, "$schema");
    }

    public static JsonSchemaSubsetValidator Load(string path) =>
        new(JsonDocument.Parse(File.ReadAllText(path)));

    public JsonElement ParseAndValidate(string json, string logicalFile, long lineNumber)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{logicalFile}: linha {lineNumber}: JSON inválido.", ex);
        }

        using (document)
        {
            try
            {
                ValidateNode(document.RootElement, _schema.RootElement, "$", collectErrors: true);
            }
            catch (SchemaValidationException ex)
            {
                throw new InvalidDataException($"{logicalFile}: linha {lineNumber}: {ex.Message}", ex);
            }
            return document.RootElement.Clone();
        }
    }

    private static void ValidateNode(JsonElement value, JsonElement schema, string path, bool collectErrors)
    {
        if (schema.ValueKind != JsonValueKind.Object)
            throw new SchemaValidationException($"schema inválido em {path}.");

        ValidateType(value, schema, path);
        ValidateEnumAndConst(value, schema, path);

        if (value.ValueKind == JsonValueKind.String)
            ValidateString(value.GetString()!, schema, path);

        if (value.ValueKind == JsonValueKind.Object)
            ValidateObject(value, schema, path);

        if (value.ValueKind == JsonValueKind.Array)
            ValidateArray(value, schema, path);

        if (schema.TryGetProperty("allOf", out var allOf))
        {
            foreach (var item in allOf.EnumerateArray())
                ValidateNode(value, item, path, collectErrors);
        }

        if (schema.TryGetProperty("if", out var condition))
        {
            var matches = Matches(value, condition, path);
            if (matches && schema.TryGetProperty("then", out var thenSchema))
                ValidateNode(value, thenSchema, path, collectErrors);
            if (!matches && schema.TryGetProperty("else", out var elseSchema))
                ValidateNode(value, elseSchema, path, collectErrors);
        }
    }

    private static void ValidateType(JsonElement value, JsonElement schema, string path)
    {
        if (!schema.TryGetProperty("type", out var type)) return;
        var accepted = type.ValueKind switch
        {
            JsonValueKind.String => MatchesType(value, type.GetString()!),
            JsonValueKind.Array => type.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && MatchesType(value, x.GetString()!)),
            _ => false
        };
        if (!accepted)
            throw new SchemaValidationException($"{path}: tipo JSON incompatível com o contrato.");
    }

    private static bool MatchesType(JsonElement value, string type) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => throw new SchemaValidationException($"tipo JSON Schema não suportado: {type}.")
    };

    private static void ValidateEnumAndConst(JsonElement value, JsonElement schema, string path)
    {
        if (schema.TryGetProperty("const", out var constant) && !JsonScalarEquals(value, constant))
            throw new SchemaValidationException($"{path}: valor diferente do const exigido.");

        if (schema.TryGetProperty("enum", out var values)
            && !values.EnumerateArray().Any(x => JsonScalarEquals(value, x)))
            throw new SchemaValidationException($"{path}: valor fora do domínio permitido.");
    }

    private static bool JsonScalarEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;
        return left.ValueKind switch
        {
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => left.GetRawText() == right.GetRawText() || (left.TryGetDecimal(out var a) && right.TryGetDecimal(out var b) && a == b),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            _ => left.GetRawText() == right.GetRawText()
        };
    }

    private static void ValidateString(string value, JsonElement schema, string path)
    {
        if (schema.TryGetProperty("minLength", out var min) && value.Length < min.GetInt32())
            throw new SchemaValidationException($"{path}: comprimento menor que minLength.");
        if (schema.TryGetProperty("maxLength", out var max) && value.Length > max.GetInt32())
            throw new SchemaValidationException($"{path}: comprimento maior que maxLength.");
        if (schema.TryGetProperty("pattern", out var pattern) && !Regex.IsMatch(value, pattern.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new SchemaValidationException($"{path}: valor não atende ao pattern.");
        if (schema.TryGetProperty("format", out var format))
        {
            var name = format.GetString();
            var valid = name switch
            {
                "date" => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                "date-time" => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
                null => true,
                _ => throw new SchemaValidationException($"format JSON Schema não suportado: {name}.")
            };
            if (!valid) throw new SchemaValidationException($"{path}: formato {name} inválido.");
        }
    }

    private static void ValidateObject(JsonElement value, JsonElement schema, string path)
    {
        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var item in required.EnumerateArray())
            {
                var name = item.GetString()!;
                if (!value.TryGetProperty(name, out _))
                    throw new SchemaValidationException($"{path}.{name}: propriedade obrigatória ausente.");
            }
        }

        if (!schema.TryGetProperty("properties", out var properties)) return;

        if (schema.TryGetProperty("additionalProperties", out var additional)
            && additional.ValueKind == JsonValueKind.False)
        {
            var allowed = properties.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!allowed.Contains(property.Name))
                    throw new SchemaValidationException($"{path}.{property.Name}: propriedade adicional não permitida.");
            }
        }

        foreach (var schemaProperty in properties.EnumerateObject())
        {
            if (value.TryGetProperty(schemaProperty.Name, out var propertyValue))
                ValidateNode(propertyValue, schemaProperty.Value, $"{path}.{schemaProperty.Name}", collectErrors: true);
        }
    }

    private static void ValidateArray(JsonElement value, JsonElement schema, string path)
    {
        if (!schema.TryGetProperty("items", out var itemSchema)) return;
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            ValidateNode(item, itemSchema, $"{path}[{index}]", collectErrors: true);
            index++;
        }
    }

    private static bool Matches(JsonElement value, JsonElement condition, string path)
    {
        try
        {
            ValidateNode(value, condition, path, collectErrors: false);
            return true;
        }
        catch (SchemaValidationException)
        {
            return false;
        }
    }

    private static void ValidateSupportedKeywords(JsonElement schema, string path)
    {
        if (schema.ValueKind != JsonValueKind.Object) return;
        foreach (var property in schema.EnumerateObject())
        {
            if (!SupportedKeywords.Contains(property.Name))
                throw new InvalidDataException($"Keyword JSON Schema não suportada em {path}: {property.Name}.");

            if (property.Name == "properties")
            {
                foreach (var child in property.Value.EnumerateObject())
                    ValidateSupportedKeywords(child.Value, $"{path}.properties.{child.Name}");
            }
            else if (property.Name == "items")
            {
                ValidateSupportedKeywords(property.Value, $"{path}.items");
            }
            else if (property.Name == "allOf")
            {
                var i = 0;
                foreach (var child in property.Value.EnumerateArray())
                    ValidateSupportedKeywords(child, $"{path}.allOf[{i++}]");
            }
            else if (property.Name is "if" or "then" or "else")
            {
                ValidateSupportedKeywords(property.Value, $"{path}.{property.Name}");
            }
        }
    }

    private sealed class SchemaValidationException(string message) : Exception(message);
}


/// <summary>
/// Cache append-only de validators por arquivo/versionamento. O catálogo de versões continua dinâmico:
/// uma nova pasta vN gera um novo caminho e é carregada sob demanda sem restart. Depois do primeiro uso,
/// a versão publicada é imutável; alteração in-place do mesmo arquivo falha explicitamente.
/// </summary>
public sealed class JsonSchemaValidatorCache
{
    private sealed record CacheEntry(JsonSchemaSubsetValidator Validator, long Length, DateTime LastWriteUtc, byte[] Sha256);
    private readonly ConcurrentDictionary<string, Lazy<CacheEntry>> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Compatibilidade para testes isolados. Runtime de catálogo deve sempre fornecer expectedSha256.</summary>
    public JsonSchemaSubsetValidator Get(string path) => GetCore(path, expectedSha256: null);

    /// <summary>
    /// Valida os bytes físicos contra o digest aprovado no catálogo antes de reutilizar a versão.
    /// Assim, um restart/deploy não pode transformar silenciosamente outro conteúdo na mesma versão vN.
    /// </summary>
    public JsonSchemaSubsetValidator Get(string path, ReadOnlySpan<byte> expectedSha256)
    {
        if (expectedSha256.Length != 32) throw new InvalidOperationException("SHA-256 aprovado do JSON Schema deve ter 32 bytes.");
        return GetCore(path, expectedSha256.ToArray());
    }

    private JsonSchemaSubsetValidator GetCore(string path, byte[]? expectedSha256)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException("JSON Schema não encontrado.", full);

        var physicalHash = SHA256.HashData(File.ReadAllBytes(full));
        if (expectedSha256 is not null && !CryptographicOperations.FixedTimeEquals(physicalHash, expectedSha256))
            throw new InvalidOperationException(
                $"JSON Schema diverge do SHA-256 aprovado no catálogo: {full}. Publique uma nova versão vN e aprove seu digest.");

        var entry = _cache.GetOrAdd(full, p => new Lazy<CacheEntry>(() =>
        {
            var info = new FileInfo(p);
            return new CacheEntry(JsonSchemaSubsetValidator.Load(p), info.Length, info.LastWriteTimeUtc, physicalHash);
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        var current = new FileInfo(full);
        if (current.Length != entry.Length || current.LastWriteTimeUtc != entry.LastWriteUtc
            || !CryptographicOperations.FixedTimeEquals(physicalHash, entry.Sha256))
            throw new InvalidOperationException(
                $"JSON Schema versionado foi alterado in-place após entrar no cache: {full}. " +
                "Publique uma nova versão vN em vez de modificar uma versão já utilizada.");

        if (expectedSha256 is not null && !CryptographicOperations.FixedTimeEquals(entry.Sha256, expectedSha256))
            throw new InvalidOperationException($"Digest de catálogo mudou para o mesmo caminho versionado: {full}.");

        return entry.Validator;
    }
}

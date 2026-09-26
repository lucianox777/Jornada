using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit"), Category("OpenApiRuntime")]
public sealed class OpenApiTypedContractTests
{
    private static JsonDocument ReadContract() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "openapi", "jornada-v1.openapi.json")));

    private static readonly (string Path, string Method, string Status, string Schema, bool Array)[] TypedSuccess =
    [
        ("/health", "get", "200", "HealthStatus", false),
        ("/health/live", "get", "200", "HealthStatus", false),
        ("/health/ready", "get", "200", "ReadinessStatus", false),
        ("/api/v1/monitor/status", "get", "200", "OperationalMonitorStatus", false),
        ("/api/v1/identidade/resolver", "post", "200", "IdentityResolutionResponse", false),
        ("/api/v1/ingestao/entregas", "post", "202", "IngestionReceipt", false),
        ("/api/v1/ingestao/entregas/{entregaId}", "get", "200", "IngestionStatusResponse", false),
        ("/api/v1/identidade/conflitos/detalhe", "post", "200", "IdentityConflictDetailResponse", false),
        ("/api/v1/identidade/correcoes", "post", "200", "IdentityCorrectionResponse", false),
        ("/api/v1/identidade/casos", "post", "200", "IdentityGovernedCaseOpenResponse", false),
        ("/api/v1/identidade/casos/{casoId}/aplicar", "post", "200", "IdentityGovernedCaseApplyResponse", false),
        ("/api/v1/divergencias", "get", "200", "IdentityDivergenceDto", true),
        ("/api/v1/pessoas/{pessoaUuid}", "get", "200", "PersonProjectionResponse", false),
        ("/api/v1/pessoas/consulta", "post", "200", "PersonProjectionResponse", true),
        ("/api/v1/pessoas/{pessoaUuid}/registros", "get", "200", "RegistroJornadaDto", true),
        ("/api/v1/pessoas/{pessoaUuid}/beneficios-concedidos", "get", "200", "BeneficioConcedidoPessoaDto", true),
        ("/api/v1/pessoas/{pessoaUuid}/servicos-prestados", "get", "200", "ServicoPrestadoPessoaDto", true),
        ("/api/v1/pessoas/{pessoaUuid}/possibilidades", "get", "200", "PossibilidadesResponse", false),
        ("/api/v1/identidade/origens/consulta", "post", "200", "ProgressiveOriginQueryResponse", false)
    ];

    public static IEnumerable<TestCaseData> SuccessCases() =>
        TypedSuccess.Select(x => new TestCaseData(x.Path, x.Method, x.Status, x.Schema, x.Array)
            .SetName("OpenAPI_success_" + x.Method + "_" + x.Path.Replace('/', '_').Replace('{', '_').Replace('}', '_')));

    [TestCaseSource(nameof(SuccessCases))]
    public void Success_responses_use_the_declared_DTO_or_array(
        string path, string method, string code, string name, bool array)
    {
        using var document = ReadContract();
        var response = document.RootElement.GetProperty("paths").GetProperty(path)
            .GetProperty(method).GetProperty("responses").GetProperty(code);
        var schema = response.GetProperty("content").GetProperty("application/json").GetProperty("schema");
        if (array)
        {
            Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("array"));
            schema = schema.GetProperty("items");
        }
        Assert.That(schema.GetProperty("$ref").GetString(), Is.EqualTo("#/components/schemas/" + name));
    }

    [Test]
    public void All_21_operations_publish_a_typed_success_or_no_content()
    {
        using var document = ReadContract();
        var operations = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(method => method.Name is "get" or "post" or "put" or "delete" or "patch")
                .Select(method => (Path: path.Name, Method: method.Name, Value: method.Value)))
            .ToArray();
        Assert.That(operations, Has.Length.EqualTo(21));
        foreach (var (path, method, operation) in operations)
        {
            var successes = operation.GetProperty("responses").EnumerateObject()
                .Where(response => response.Name.StartsWith("2", StringComparison.Ordinal))
                .ToArray();
            Assert.That(successes, Has.Length.EqualTo(1), method.ToUpperInvariant() + " " + path);
            foreach (var response in successes)
            {
                var code = response.Name;
                if (code == "204")
                {
                    Assert.That(response.Value.TryGetProperty("content", out _), Is.False,
                        "204 must not claim a response body");
                    continue;
                }
                Assert.That(response.Value.TryGetProperty("content", out var content), Is.True,
                    "Success without content contract: " + method + " " + path);
                var media = path == "/monitor" ? "text/html" : "application/json";
                var schema = content.GetProperty(media).GetProperty("schema");
                Assert.That(schema.TryGetProperty("$ref", out _) ||
                    schema.TryGetProperty("type", out _), Is.True,
                    "Unspecified success schema: " + method + " " + path);
            }
        }
    }

    public static IEnumerable<TestCaseData> PublicDtoTypes()
    {
        Type[] types =
        [
            typeof(IdentityResolutionRequest), typeof(IdentityResolutionResponse),
            typeof(IdentityConflictDetailRequest), typeof(IdentityConflictCoreDto),
            typeof(IdentityConflictDetailResponse), typeof(IdentityCorrectionGroupRequest),
            typeof(IdentityDecisionEvidence), typeof(IdentityCorrectionRequest),
            typeof(IdentityCorrectionResponse), typeof(IdentityGovernedCaseOpenRequest),
            typeof(IdentityGovernedCaseOpenResponse), typeof(IdentityGovernedCaseApplyRequest),
            typeof(IdentityGovernedCaseApplyResponse), typeof(IdentityDivergenceDto),
            typeof(IdentityDivergenceDispositionRequest), typeof(IngestionReceipt),
            typeof(IngestionStatusResponse), typeof(PersonBatchQueryRequest),
            typeof(PersonProjectionMetadata), typeof(PersonProjectionResponse),
            typeof(RegistroJornadaDto), typeof(BeneficioConcedidoPessoaDto),
            typeof(ServicoPrestadoPessoaDto), typeof(PossibilidadeCompativelDto),
            typeof(ProgressiveOriginQueryRequest), typeof(ProgressiveOriginQueryResponse)
        ];
        return types.Select(type => new TestCaseData(type).SetName("OpenAPI_DTO_" + type.Name));
    }

    [TestCaseSource(nameof(PublicDtoTypes))]
    public void Published_CSharp_DTO_properties_match_the_JSON_schema(Type dto)
    {
        using var document = ReadContract();
        var schema = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty(dto.Name);
        var properties = schema.GetProperty("properties");
        var required = schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToHashSet();
        var actual = dto.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(x => x.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition != JsonIgnoreCondition.Always)
            .ToArray();
        var names = actual.Select(x => x.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ??
            JsonNamingPolicy.CamelCase.ConvertName(x.Name)).ToArray();
        Assert.That(properties.EnumerateObject().Select(x => x.Name), Is.EquivalentTo(names),
            dto.Name + ": missing or orphaned published property");
        Assert.That(required, Is.EquivalentTo(names),
            dto.Name + ": runtime serializes all positional record properties including null");
        foreach (var property in actual)
        {
            var jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ??
                JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            AssertPropertyType(property.PropertyType, properties.GetProperty(jsonName), dto.Name + "." + jsonName,
                property.GetCustomAttributes<JsonConverterAttribute>().Any(
                    attribute => attribute.ConverterType == typeof(JsonStringEnumConverter)));
        }
    }

    private static void AssertPropertyType(Type clrType, JsonElement schema, string location, bool stringEnum)
    {
        var nullable = Nullable.GetUnderlyingType(clrType);
        if (nullable is not null)
        {
            Assert.That(schema.GetProperty("nullable").GetBoolean(), Is.True, location + " must be nullable");
            clrType = nullable;
        }
        string? type = schema.TryGetProperty("type", out var jsonType) ? jsonType.GetString() : null;
        if (clrType.IsEnum)
        {
            Assert.That(type, Is.EqualTo(stringEnum ? "string" : "integer"), location);
            return;
        }
        if (clrType == typeof(string) || clrType == typeof(Guid) ||
            clrType == typeof(DateOnly) || clrType == typeof(DateTimeOffset))
        {
            Assert.That(type, Is.EqualTo("string"), location);
            var format = clrType == typeof(Guid) ? "uuid" :
                clrType == typeof(DateOnly) ? "date" :
                clrType == typeof(DateTimeOffset) ? "date-time" : null;
            if (format is not null)
                Assert.That(schema.GetProperty("format").GetString(), Is.EqualTo(format), location);
        }
        else if (clrType == typeof(long) || clrType == typeof(int))
            Assert.That(type, Is.EqualTo("integer"), location);
        else if (clrType == typeof(decimal) || clrType == typeof(double))
            Assert.That(type, Is.EqualTo("number"), location);
        else if (clrType == typeof(bool))
            Assert.That(type, Is.EqualTo("boolean"), location);
        else if (clrType == typeof(JsonElement))
            Assert.That(type, Is.EqualTo("object"), location);
        else if (clrType.IsGenericType &&
                 (clrType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ||
                  clrType.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>)))
        {
            Assert.That(type, Is.EqualTo("array"), location);
            var item = clrType.GetGenericArguments()[0];
            Assert.That(schema.GetProperty("items").GetProperty("$ref").GetString(),
                Is.EqualTo("#/components/schemas/" + item.Name), location);
        }
        else if (clrType.IsGenericType && clrType.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
            Assert.That(type, Is.EqualTo("object"), location);
        else
        {
            var reference = schema.TryGetProperty("$ref", out var direct)
                ? direct.GetString()
                : schema.GetProperty("allOf")[0].GetProperty("$ref").GetString();
            Assert.That(reference, Is.EqualTo("#/components/schemas/" + clrType.Name), location);
        }
    }

    [Test]
    public void Disposition_and_ingestion_statuses_match_the_actual_runtime()
    {
        using var document = ReadContract();
        var paths = document.RootElement.GetProperty("paths");
        var receipt = paths.GetProperty("/api/v1/ingestao/entregas").GetProperty("post").GetProperty("responses");
        Assert.That(receipt.TryGetProperty("200", out _), Is.False);
        Assert.That(receipt.GetProperty("202").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString(),
            Is.EqualTo("#/components/schemas/IngestionReceipt"));
        var disposition = paths.GetProperty("/api/v1/divergencias/{divergenciaId}/desfecho")
            .GetProperty("post").GetProperty("responses");
        Assert.That(disposition.TryGetProperty("200", out _), Is.False);
        Assert.That(disposition.GetProperty("204").TryGetProperty("content", out _), Is.False);
    }

    [Test]
    public void Dynamic_projection_is_scoped_to_schemaRef_and_does_not_publish_a_global_Person_shape()
    {
        using var document = ReadContract();
        var projection = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("PersonProjectionResponse").GetProperty("properties");
        Assert.That(projection.GetProperty("schemaRef").GetProperty("type").GetString(), Is.EqualTo("string"));
        var dados = projection.GetProperty("dados");
        Assert.Multiple(() =>
        {
            Assert.That(dados.GetProperty("type").GetString(), Is.EqualTo("object"));
            Assert.That(dados.GetProperty("additionalProperties").GetBoolean(), Is.True);
            Assert.That(dados.TryGetProperty("properties", out _), Is.False);
        });
    }

    [Test]
    public void Monitor_has_concrete_nested_records_and_diagnostic_fields_are_optional()
    {
        using var document = ReadContract();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var monitor = schemas.GetProperty("OperationalMonitorStatus").GetProperty("properties");
        Assert.That(monitor.GetProperty("monitor").GetProperty("$ref").GetString(),
            Is.EqualTo("#/components/schemas/OperationalMonitorSnapshot"));
        var conference = schemas.GetProperty("LinkageConferenceGovernanceStatus");
        Assert.That(conference.GetProperty("properties").GetProperty("splinkStatus").GetProperty("type").GetString(),
            Is.EqualTo("string"));
        Assert.That(conference.GetProperty("required").EnumerateArray().Select(x => x.GetString()),
            Does.Not.Contain("splinkStatus"));
        Assert.That(schemas.TryGetProperty("PresentedAccessCredential", out _), Is.False,
            "Internal access key must never be published as a component schema");
    }

    [Test]
    public void Every_reference_resolves_and_no_unused_private_security_DTO_is_exposed()
    {
        using var document = ReadContract();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        void Visit(JsonElement node)
        {
            switch (node.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in node.EnumerateArray()) Visit(item);
                    break;
                case JsonValueKind.Object:
                    foreach (var property in node.EnumerateObject())
                    {
                        if (property.Name == "$ref")
                        {
                            const string prefix = "#/components/schemas/";
                            var value = property.Value.GetString()!;
                            Assert.That(value.StartsWith(prefix, StringComparison.Ordinal), Is.True, value);
                            Assert.That(schemas.TryGetProperty(value[prefix.Length..], out _), Is.True, value);
                        }
                        else Visit(property.Value);
                    }
                    break;
            }
        }
        Visit(document.RootElement.GetProperty("paths"));
        Visit(schemas);
        Assert.That(schemas.TryGetProperty("AccessContext", out _), Is.False);
        Assert.That(schemas.TryGetProperty("PresentedAccessCredential", out _), Is.False);
    }
}

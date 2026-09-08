using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Jornada.Api;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
[Category("OpenApiRuntime")]
public sealed class OpenApiRuntimeConformanceTests
{
    private WebApplicationFactory<ApiEntryPointMarker>? factory;
    private HttpClient? client;
    private string? tempRoot;
    private JsonDocument? contract;

    [OneTimeSetUp]
    public void SetUp()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "jornada-openapi-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var bronze = Path.Combine(tempRoot, "bronze");
        var staging = Path.Combine(tempRoot, "staging");
        var contractRoot = TestContext.CurrentContext.TestDirectory;

        factory = new WebApplicationFactory<ApiEntryPointMarker>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Contracts:RepositoryRoot", contractRoot);
            builder.UseSetting("BronzeStorage:RootPath", bronze);
            builder.UseSetting("IngestionStaging:RootPath", staging);
            // Contrato runtime é teste de superfície: auditoria e readiness SQL usam doubles em memória.
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IApiAuditSink, InMemoryApiAuditSink>();
                services.AddSingleton<ISqlReadinessProbe>(new InMemorySqlReadinessProbe(ready: false));
            });
        });
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var openApiPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "openapi", "jornada-v1.openapi.json");
        Assert.That(File.Exists(openApiPath), Is.True, $"Contrato OpenAPI não copiado para o output de testes: {openApiPath}");
        contract = JsonDocument.Parse(File.ReadAllText(openApiPath));
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        client?.Dispose();
        factory?.Dispose();
        contract?.Dispose();
        if (tempRoot is not null)
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); }
            catch (Exception ex) { TestContext.Progress.WriteLine($"Cleanup OpenAPI runtime best-effort: {ex.GetType().Name}"); }
        }
    }

    private static readonly RuntimeProbe[] ProbeCatalog =
    [
        new(HttpMethod.Get, "/health", "/health", "get", null),
        new(HttpMethod.Get, "/health/live", "/health/live", "get", null),
        new(HttpMethod.Get, "/health/ready", "/health/ready", "get", null),
        new(HttpMethod.Post, "/api/v1/identidade/resolver", "/api/v1/identidade/resolver", "post", Json("{\"cpf\":\"52998224725\"}")),
        new(HttpMethod.Post, "/api/v1/ingestao/entregas", "/api/v1/ingestao/entregas", "post", ZipProbe()),
        new(HttpMethod.Get, "/api/v1/ingestao/entregas/11111111-1111-1111-1111-111111111111", "/api/v1/ingestao/entregas/{entregaId}", "get", null),
        new(HttpMethod.Post, "/api/v1/identidade/conflitos/detalhe", "/api/v1/identidade/conflitos/detalhe", "post", Json("{\"cpf\":\"52998224725\"}")),
        new(HttpMethod.Post, "/api/v1/identidade/correcoes", "/api/v1/identidade/correcoes", "post", Json("{\"cpf\":\"52998224725\",\"grupoTitularCpf\":\"A\",\"grupos\":[],\"atoReferencia\":\"TESTE\",\"justificativa\":\"TESTE\"}")),
        new(HttpMethod.Post, "/api/v1/identidade/casos", "/api/v1/identidade/casos", "post", Json("{\"motivo\":\"TESTE\",\"pessoaObservacaoIds\":[1],\"atoReferencia\":\"TESTE\",\"justificativa\":\"TESTE\"}")),
        new(HttpMethod.Post, "/api/v1/identidade/casos/11111111-1111-1111-1111-111111111111/aplicar", "/api/v1/identidade/casos/{casoId}/aplicar", "post", Json("{\"grupos\":[]}")),
        new(HttpMethod.Get, "/api/v1/divergencias", "/api/v1/divergencias", "get", null),
        new(HttpMethod.Post, "/api/v1/divergencias/1/desfecho", "/api/v1/divergencias/{divergenciaId}/desfecho", "post", Json("{\"status\":\"RESOLVIDA\",\"desfecho\":\"TESTE\"}")),
        new(HttpMethod.Get, "/api/v1/pessoas/11111111-1111-1111-1111-111111111111", "/api/v1/pessoas/{pessoaUuid}", "get", null),
        new(HttpMethod.Post, "/api/v1/pessoas/consulta", "/api/v1/pessoas/consulta", "post", Json("{\"pessoaUuids\":[\"11111111-1111-1111-1111-111111111111\"]}")),
        new(HttpMethod.Get, "/api/v1/pessoas/11111111-1111-1111-1111-111111111111/registros", "/api/v1/pessoas/{pessoaUuid}/registros", "get", null),
        new(HttpMethod.Get, "/api/v1/pessoas/11111111-1111-1111-1111-111111111111/beneficios-concedidos", "/api/v1/pessoas/{pessoaUuid}/beneficios-concedidos", "get", null),
        new(HttpMethod.Get, "/api/v1/pessoas/11111111-1111-1111-1111-111111111111/servicos-prestados", "/api/v1/pessoas/{pessoaUuid}/servicos-prestados", "get", null),
        new(HttpMethod.Get, "/api/v1/pessoas/11111111-1111-1111-1111-111111111111/possibilidades", "/api/v1/pessoas/{pessoaUuid}/possibilidades", "get", null),
        new(HttpMethod.Post, "/api/v1/identidade/origens/consulta", "/api/v1/identidade/origens/consulta", "post", Json("{\"codigoSistemaOrigem\":\"ASSISTENCIA\",\"codigoPessoaOrigem\":\"origem-1\"}"))
    ];

    public static IEnumerable<TestCaseData> Operations() =>
        ProbeCatalog.Select(probe => new TestCaseData(probe)
            .SetName($"OpenAPI_runtime_{probe.ContractMethod}_{probe.ContractPath.Replace('/', '_').Replace('{', '_').Replace('}', '_')}"));

    [TestCaseSource(nameof(Operations))]
    public async Task Runtime_response_matches_declared_status_media_and_json_shape(RuntimeProbe probe)
    {
        Assert.That(client, Is.Not.Null);
        Assert.That(contract, Is.Not.Null);
        using var request = new HttpRequestMessage(probe.Method, probe.RuntimePath);
        if (probe.ContentFactory is not null) request.Content = probe.ContentFactory();

        using var response = await client!.SendAsync(request);
        var operation = contract!.RootElement.GetProperty("paths").GetProperty(probe.ContractPath).GetProperty(probe.ContractMethod);
        var status = ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var responses = operation.GetProperty("responses");
        Assert.That(responses.TryGetProperty(status, out var responseContract), Is.True,
            $"Runtime retornou {status} não declarado em {probe.ContractMethod.ToUpperInvariant()} {probe.ContractPath}");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes.Length == 0) return;

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        Assert.That(mediaType, Is.Not.Null.And.Not.Empty, "Resposta com corpo deve declarar Content-Type em runtime.");
        Assert.That(IsJsonMediaType(mediaType!), Is.True, $"Corpo textual da API deve ser JSON; recebido {mediaType}.");
        Assert.DoesNotThrow(() => JsonDocument.Parse(bytes).Dispose(), "Corpo application/json deve ser JSON bem-formado.");

        if (responseContract.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object)
        {
            Assert.That(content.EnumerateObject().Any(p => MediaTypeMatches(p.Name, mediaType!)), Is.True,
                $"Content-Type runtime {mediaType} não está declarado para response {status}.");
        }
    }

    [Test]
    public void Runtime_probe_catalog_covers_every_openapi_operation_once()
    {
        Assert.That(contract, Is.Not.Null);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in contract!.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject())
            {
                if (method.Name is "get" or "post" or "put" or "patch" or "delete")
                    expected.Add(method.Name.ToUpperInvariant() + " " + path.Name);
            }
        }
        var actual = ProbeCatalog.Select(x => x.ContractMethod.ToUpperInvariant() + " " + x.ContractPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    private static Func<HttpContent> Json(string value) => () => new StringContent(value, Encoding.UTF8, "application/json");
    private static Func<HttpContent> ZipProbe() => () =>
    {
        var content = new ByteArrayContent([0x50, 0x4b, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        return content;
    };
    private static bool IsJsonMediaType(string mediaType) =>
        mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    private static bool MediaTypeMatches(string declared, string actual) =>
        declared.Equals(actual, StringComparison.OrdinalIgnoreCase) || (declared.Equals("application/json", StringComparison.OrdinalIgnoreCase) && actual.EndsWith("+json", StringComparison.OrdinalIgnoreCase));

    public sealed record RuntimeProbe(HttpMethod Method, string RuntimePath, string ContractPath, string ContractMethod, Func<HttpContent>? ContentFactory);
}

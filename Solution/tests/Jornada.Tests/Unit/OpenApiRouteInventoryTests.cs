using System.Text.Json;
using Jornada.Api;
using Jornada.Resultado.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
[Category("OpenApiRouteInventory")]
public sealed class OpenApiRouteInventoryTests
{
    [Test]
    public void Jornada_public_routes_match_versioned_contract() =>
        CompareRuntimeRoutes<ApiEntryPointMarker>("jornada-v1.openapi.json");

    [Test]
    public void Resultado_public_routes_match_versioned_contract() =>
        CompareRuntimeRoutes<ResultadoApiEntryPointMarker>("resultado-v1.openapi.json");

    private static void CompareRuntimeRoutes<TEntryPoint>(string contractFile) where TEntryPoint : class
    {
        var output = TestContext.CurrentContext.TestDirectory;
        using var contract = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "openapi", contractFile)));
        var expected = new List<string>();
        foreach (var path in contract.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject())
            {
                if (method.Name is "get" or "head" or "post" or "put" or "patch" or "delete" or "options" or "trace")
                    expected.Add(method.Name.ToUpperInvariant() + " " + path.Name);
            }
        }

        var scratch = Directory.CreateTempSubdirectory("jornada-route-contract-");
        try
        {
            using var original = new WebApplicationFactory<TEntryPoint>();
            using var host = original.WithWebHostBuilder(builder =>
            {
                // Perfil de superfície pública: não publicar rotas exclusivas de DEV no contrato.
                builder.UseEnvironment("Production");
                builder.UseSetting("Contracts:RepositoryRoot", output);
                builder.UseSetting("BronzeStorage:RootPath", Path.Combine(scratch.FullName, "bronze"));
                builder.UseSetting("IngestionStaging:RootPath", Path.Combine(scratch.FullName, "staging"));
                // O teste verifica metadados de rota e não deve conectar a uma base operacional.
                builder.UseSetting("ConnectionStrings:Jornada",
                    "Server=127.0.0.1,1;Database=JornadaE2E;Integrated Security=true;Connect Timeout=1");
                builder.UseSetting("JornadaApiBaseUrl", "http://127.0.0.1:1");
            });
            var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints;
            var actual = new List<string>();
            foreach (var endpoint in endpoints)
            {
                Assert.That(endpoint, Is.InstanceOf<RouteEndpoint>(), "Endpoint sem padrão de rota");
                var route = ((RouteEndpoint)endpoint).RoutePattern;
                var methodMetadata = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>();
                Assert.That(methodMetadata?.HttpMethods, Is.Not.Null.And.Not.Empty,
                    $"Método HTTP indeterminado em {route.RawText}");
                var path = "/" + string.Join("/", route.PathSegments.Select(segment =>
                    string.Concat(segment.Parts.Select(FormatPart))));
                actual.AddRange(methodMetadata!.HttpMethods.Select(method => method + " " + path));
            }

            Assert.That(expected, Is.Not.Empty);
            Assert.That(actual, Is.EquivalentTo(expected), $"Superfície runtime divergente de {contractFile}");
            TestContext.Progress.WriteLine($"{contractFile}: {actual.Count} operações HTTP verificadas em ambos os sentidos.");
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    private static string FormatPart(RoutePatternPart part) => part switch
    {
        RoutePatternLiteralPart literal => literal.Content,
        RoutePatternSeparatorPart separator => separator.Content,
        RoutePatternParameterPart { IsCatchAll: false, IsOptional: false, Default: null } parameter =>
            "{" + parameter.Name + "}",
        _ => throw new InvalidDataException($"Parte de rota incompatível com path OpenAPI: {part.PartKind}")
    };
}

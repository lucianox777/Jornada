using System.Net.Http.Json;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Jornada.Api;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class ApiHttpPipelineTests
{
    [Test]
    public async Task Production_without_corporate_identity_fails_closed_before_business_service()
    {
        await using var factory = new WebApplicationFactory<ApiEntryPointMarker>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IApiAuditSink, InMemoryApiAuditSink>();
                services.AddSingleton<ISqlReadinessProbe>(new InMemorySqlReadinessProbe(ready: false));
            });
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identidade/resolver")
        { Content = JsonContent.Create(new { cpf = "52998224725" }) };
        request.Headers.TryAddWithoutValidation("X-Jornada-Gestor", "SMADS");
        request.Headers.TryAddWithoutValidation("X-Jornada-Access-Key", "nao-deve-ser-aceita");
        var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Ingestion_without_access_key_is_rejected_before_zip_validation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jornada-api-http-{Guid.NewGuid():N}");
        var bronzeRoot = Path.Combine(root, "bronze");
        var stagingRoot = Path.Combine(root, "staging");
        try
        {
            await using var factory = new WebApplicationFactory<ApiEntryPointMarker>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                // UseSetting entra na configuração de host antes do Program top-level ler os caminhos.
                builder.UseSetting("BronzeStorage:RootPath", bronzeRoot);
                builder.UseSetting("IngestionStaging:RootPath", stagingRoot);
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IApiAuditSink, InMemoryApiAuditSink>();
                    services.AddSingleton<ISqlReadinessProbe>(new InMemorySqlReadinessProbe(ready: false));
                });
            });
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ingestao/entregas")
            { Content = new ByteArrayContent(new byte[1024 * 1024]) };
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
            request.Headers.TryAddWithoutValidation("X-Jornada-Gestor", "SMADS");
            request.Headers.TryAddWithoutValidation("Idempotency-Key", "pipeline-test");
            request.Headers.TryAddWithoutValidation("Content-Disposition", "attachment; filename=qualquer.zip");
            var response = await client.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Bronze_storage_failures_map_to_503_and_retry_contract()
    {
        var unavailable = BronzeStorageHttpFailureMapper.Map(
            new Jornada.Bronze.Storage.BronzeStorageUnavailableException("teste", new IOException("offline")));
        var integrity = BronzeStorageHttpFailureMapper.Map(
            new Jornada.Bronze.Storage.BronzeObjectIntegrityException("sha256/aa/bb/test.zip", "hash divergente"));

        Assert.Multiple(() =>
        {
            Assert.That(unavailable.StatusCode, Is.EqualTo(503));
            Assert.That(unavailable.Code, Is.EqualTo("BRONZE_STORAGE_INDISPONIVEL"));
            Assert.That(integrity.StatusCode, Is.EqualTo(503));
            Assert.That(integrity.Code, Is.EqualTo("BRONZE_INTEGRIDADE_DIVERGENTE"));
            Assert.That(BronzeStorageHttpFailureMapper.RetryAfterSeconds, Is.GreaterThan(0));
        });
    }

}

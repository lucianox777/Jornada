using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jornada.Api;
using Jornada.Contracts;
using Jornada.Bronze.Storage;
using Jornada.Operational.Sql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class ProgressiveOriginApiTests
{
    private static readonly Guid Initial = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1");
    private static readonly Guid Canonical = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2");
    private static readonly DateTimeOffset Created = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProgressiveOriginQueryRequest Query = new("ASSISTENCIA", "origem-1");

    [Test]
    public void Request_rejects_missing_oversized_and_control_character_keys()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProgressiveOriginApi.TryValidateRequest(Query), Is.True);
            Assert.That(ProgressiveOriginApi.TryValidateRequest(new("", "1")), Is.False);
            Assert.That(ProgressiveOriginApi.TryValidateRequest(new("ASSISTENCIA", " ")), Is.False);
            Assert.That(ProgressiveOriginApi.TryValidateRequest(new(new string('a', 81), "1")), Is.False);
            Assert.That(ProgressiveOriginApi.TryValidateRequest(new("ASSISTENCIA", new string('a', 256))), Is.False);
            Assert.That(ProgressiveOriginApi.TryValidateRequest(new("ASSISTENCIA", "1\r\n2")), Is.False);
        });
    }

    [Test]
    public void Snapshot_rejects_invalid_state_version_and_dates()
    {
        var provisional = Snapshot(ProgressiveIdentityStatus.PROVISORIA, null, 0, null);
        var reference = Snapshot(ProgressiveIdentityStatus.REFERENCIA, Canonical, 1, Created.AddMinutes(1));
        var indefinite = Snapshot(ProgressiveIdentityStatus.INDEFINIDA, null, 1, Created.AddMinutes(1));
        Assert.DoesNotThrow(() => ProgressiveOriginApi.ValidateSnapshot(provisional));
        Assert.DoesNotThrow(() => ProgressiveOriginApi.ValidateSnapshot(reference));
        Assert.DoesNotThrow(() => ProgressiveOriginApi.ValidateSnapshot(indefinite));
        Assert.Throws<InvalidOperationException>(() => ProgressiveOriginApi.ValidateSnapshot(provisional with { CanonicalUuid = Canonical }));
        Assert.Throws<InvalidOperationException>(() => ProgressiveOriginApi.ValidateSnapshot(reference with { Versao = 0 }));
        Assert.Throws<InvalidOperationException>(() => ProgressiveOriginApi.ValidateSnapshot(reference with { CanonicalUuid = null }));
        Assert.Throws<InvalidOperationException>(() => ProgressiveOriginApi.ValidateSnapshot(indefinite with { CanonicalUuid = Canonical }));
        Assert.Throws<InvalidOperationException>(() => ProgressiveOriginApi.ValidateSnapshot(reference with { Estado = (ProgressiveIdentityStatus)99 }));
        Assert.Throws<InvalidOperationException>(() => ProgressiveOriginApi.ValidateSnapshot(reference with { InitialUuid = Guid.Empty }));
        Assert.Throws<InvalidOperationException>(() => ProgressiveOriginApi.ValidateSnapshot(reference with { CriadoEm = Created.ToOffset(TimeSpan.FromHours(-3)) }));
        Assert.Throws<InvalidOperationException>(() => ProgressiveOriginApi.ValidateSnapshot(reference with { AtualizadoEm = Created.AddSeconds(-1) }));
    }

    [Test]
    public void Response_serializes_state_as_string_not_numeric_enum()
    {
        var json = JsonSerializer.Serialize(Snapshot(ProgressiveIdentityStatus.PROVISORIA, null, 0, null), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.That(json, Does.Contain("\"estado\":\"PROVISORIA\""));
        Assert.That(json, Does.Not.Contain("\"estado\":0"));
    }

    [Test]
    public void Sql_service_rejects_missing_scope_and_type_before_opening_connection()
    {
        var service = new SqlProgressiveOriginQueryService(new OperationalSqlAdapter("Server=localhost;Database=JornadaTest;Integrated Security=true;TrustServerCertificate=true"));
        var gestor = new AccessContext(Guid.NewGuid(), AccessCredentialType.GESTOR, "SMADS", "SMADS", null, [], []);
        var type = gestor with { CredentialType = AccessCredentialType.BENEFICIO, Scopes = [ProgressiveOriginApi.Permission] };
        Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await service.GetAsync(gestor, Query, CancellationToken.None));
        Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await service.GetAsync(type, Query, CancellationToken.None));
    }

    [TestCase("missing", HttpStatusCode.Unauthorized)]
    [TestCase("type", HttpStatusCode.Forbidden)]
    [TestCase("wrong_scope", HttpStatusCode.Forbidden)]
    [TestCase("wrong_owner", HttpStatusCode.NotFound)]
    [TestCase("unknown", HttpStatusCode.NotFound)]
    [TestCase("invalid", HttpStatusCode.BadRequest)]
    [TestCase("allowed", HttpStatusCode.OK)]
    public async Task Origin_route_fails_closed_and_returns_only_owner_snapshot(string scenario, HttpStatusCode expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "jornada-origin-api-" + Guid.NewGuid().ToString("N"));
        var service = new FakeOriginService();
        try
        {
            await using var factory = new WebApplicationFactory<ApiEntryPointMarker>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BronzeStorage:RootPath"] = Path.Combine(root, "bronze"),
                    ["IngestionStaging:RootPath"] = Path.Combine(root, "staging")
                }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IAccessContextResolver>();
                    services.AddSingleton<IAccessContextResolver>(new FakeAccessResolver());
                    services.RemoveAll<IPolicyEngine>();
                    services.AddSingleton<IPolicyEngine>(new FakePolicy());
                    services.RemoveAll<IProgressiveOriginQueryService>();
                    services.AddSingleton<IProgressiveOriginQueryService>(service);
                    services.AddSingleton<IApiAuditSink, InMemoryApiAuditSink>();
                    services.AddSingleton<ISqlReadinessProbe>(new InMemorySqlReadinessProbe(ready: false));
                });
            });
            using var client = factory.CreateClient();
            var query = scenario switch
            {
                "invalid" => new ProgressiveOriginQueryRequest("ASSISTENCIA", "\n"),
                "unknown" => new ProgressiveOriginQueryRequest("ASSISTENCIA", "desconhecido"),
                _ => Query
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, ProgressiveOriginApi.Route) { Content = JsonContent.Create(query) };
            if (scenario != "missing")
            {
                request.Headers.TryAddWithoutValidation(scenario == "type" ? "X-Jornada-Beneficio" : "X-Jornada-Gestor", scenario == "type" ? "AR01" : "SMADS");
                request.Headers.TryAddWithoutValidation("X-Jornada-Access-Key", scenario);
            }
            var response = await client.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(expected));
            Assert.That(service.Calls, Is.EqualTo(scenario is "wrong_owner" or "unknown" or "allowed" ? 1 : 0));
            if (scenario == "allowed")
            {
                var result = await response.Content.ReadFromJsonAsync<ProgressiveOriginQueryResponse>();
                Assert.That(result!.InitialUuid, Is.EqualTo(Initial));
                Assert.That(result.CanonicalUuid, Is.Null);
                Assert.That(result.Estado, Is.EqualTo(ProgressiveIdentityStatus.PROVISORIA));
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync();
                Assert.That(body, Does.Not.Contain("origem-1"));
                Assert.That(body, Does.Not.Contain(Initial.ToString()));
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ProgressiveOriginQueryResponse Snapshot(ProgressiveIdentityStatus status, Guid? canonical, long version, DateTimeOffset? last) =>
        new("ASSISTENCIA", "origem-1", Initial, canonical, status, version, Created, Created.AddMinutes(2), last);

    private sealed class FakeAccessResolver : IAccessContextResolver
    {
        public Task<AccessContext?> ResolveAsync(PresentedAccessCredential credential, CancellationToken ct)
        {
            var scopes = credential.AccessKey == "wrong_scope" ? Array.Empty<string>() : new[] { ProgressiveOriginApi.Permission };
            var owner = credential.AccessKey == "wrong_owner" ? "SEHAB" : "SMADS";
            return Task.FromResult<AccessContext?>(new AccessContext(Guid.NewGuid(), credential.Type, credential.PublicCode, owner, null, scopes, []));
        }
    }

    private sealed class FakePolicy : IPolicyEngine
    {
        public Task<bool> IsAllowedAsync(AccessContext context, string permission, string? resourceCode, Guid? pessoaUuid, CancellationToken ct) =>
            Task.FromResult(context.CredentialType == AccessCredentialType.GESTOR && context.Scopes.Contains(permission));
        public Task<bool> ArePersonsAllowedAsync(AccessContext context, string permission, string? resourceCode, IReadOnlyCollection<Guid> pessoaUuids, CancellationToken ct) =>
            Task.FromResult(false);
    }

    private sealed class FakeOriginService : IProgressiveOriginQueryService
    {
        public int Calls { get; private set; }
        public Task<ProgressiveOriginQueryResponse?> GetAsync(AccessContext context, ProgressiveOriginQueryRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<ProgressiveOriginQueryResponse?>(request.CodigoPessoaOrigem == "origem-1" && context.GestorCodigo == "SMADS" &&
                context.Scopes.Contains(ProgressiveOriginApi.Permission)
                ? Snapshot(ProgressiveIdentityStatus.PROVISORIA, null, 0, null) : null);
        }
    }
}

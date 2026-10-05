using System.Net;
using System.Net.Http.Json;
using Jornada.Api;
using Jornada.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class SemiblindIdentityHttpTests
{
    private const string Route = "/api/v1/identidade/candidatos";

    [TestCase("missing", HttpStatusCode.Unauthorized)]
    [TestCase("wrong_scope", HttpStatusCode.Forbidden)]
    [TestCase("allowed", HttpStatusCode.OK)]
    [TestCase("audit_failure", HttpStatusCode.ServiceUnavailable)]
    [TestCase("model_unavailable", HttpStatusCode.ServiceUnavailable)]
    [TestCase("feature_disabled", HttpStatusCode.ServiceUnavailable)]
    [TestCase("production_enabled", HttpStatusCode.ServiceUnavailable)]
    public async Task Route_enforces_access_and_pre_response_audit(string scenario, HttpStatusCode expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "jornada-semiblind-" + Guid.NewGuid().ToString("N"));
        var service = new FakeService(scenario == "model_unavailable");
        var audit = new TestAuditSink(scenario == "audit_failure");
        try
        {
            await using var factory = new WebApplicationFactory<ApiEntryPointMarker>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(scenario == "production_enabled" ? "Production" : "Development");
                // No teste Production, UseSetting prevalece sobre o appsettings.json
                // (o RootPath de produção é /var/lib/jornada, inacessível no runner CI).
                builder.UseSetting("BronzeStorage:RootPath", Path.Combine(root, "bronze"));
                builder.UseSetting("IngestionStaging:RootPath", Path.Combine(root, "staging"));
                builder.UseSetting("SemiblindIdentitySearch:Enabled",
                    scenario == "feature_disabled" ? "false" : "true");
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["BronzeStorage:RootPath"] = Path.Combine(root, "bronze"),
                        ["IngestionStaging:RootPath"] = Path.Combine(root, "staging"),
                        ["SemiblindIdentitySearch:Enabled"] = scenario == "feature_disabled" ? "false" : "true"
                    }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IAccessContextResolver>();
                    services.AddSingleton<IAccessContextResolver>(new FakeResolver());
                    services.RemoveAll<IPolicyEngine>();
                    services.AddSingleton<IPolicyEngine>(new FakePolicy());
                    services.RemoveAll<ISemiblindIdentitySearchService>();
                    services.AddSingleton<ISemiblindIdentitySearchService>(service);
                    // Serviço simulado mantém os testes HTTP independentes do SQL de DEV.
                    // O teste do gate concreto abaixo cobre ambiente, flag, banco configurado e marcador residente.
                    services.RemoveAll<ISemiblindSearchActivationGate>();
                    services.AddSingleton<ISemiblindSearchActivationGate>(
                        new FakeActivationGate(scenario != "feature_disabled"
                            && scenario != "production_enabled"));
                    services.RemoveAll<IApiAuditSink>();
                    services.AddSingleton<IApiAuditSink>(audit);
                    services.AddSingleton<ISqlReadinessProbe>(new InMemorySqlReadinessProbe(false));
                });
            });
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, Route)
            {
                Content = JsonContent.Create(new SemiblindIdentitySearchRequest(
                    "Maria", new DateOnly(1980, 1, 1), "Ana"))
            };
            if (scenario != "missing")
            {
                request.Headers.TryAddWithoutValidation("X-Jornada-Gestor", "SMADS");
                request.Headers.TryAddWithoutValidation("X-Jornada-Access-Key", scenario);
            }

            using var response = await client.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(expected));
            Assert.That(service.Calls, Is.EqualTo(scenario is "allowed" or "audit_failure" or "model_unavailable" ? 1 : 0));
            if (scenario is "feature_disabled" or "production_enabled")
            {
                var deniedBody = await response.Content.ReadAsStringAsync();
                Assert.That(deniedBody, Does.Not.Contain("Pessoa restrita"),
                    "Não divulgar PII quando o endpoint está desabilitado.");
                Assert.That(audit.Successful, Is.EqualTo(1),
                    "Até consultas desabilitadas devem ser auditadas.");
            }
            if (scenario is "missing" or "wrong_scope" or "allowed" or "model_unavailable")
                Assert.That(audit.Successful, Is.EqualTo(1),
                    "Cada consulta inclusive recusada deve produzir uma única auditoria.");
            if (scenario == "allowed")
            {
                Assert.That(audit.Successful, Is.EqualTo(1), "A busca autorizada gera exatamente um evento.");
                var body = await response.Content.ReadAsStringAsync();
                Assert.That(body, Does.Contain("nenhumDestes"));
                Assert.That(body, Does.Not.Contain("pessoaUuid"));
                Assert.That(body, Does.Not.Contain("score"));
                Assert.That(body, Does.Contain("nome_completo"));
                Assert.That(body, Does.Contain("data_nascimento"));
                Assert.That(body, Does.Contain("nome_mae"));
            }
            if (scenario == "model_unavailable")
            {
                var body = await response.Content.ReadAsStringAsync();
                Assert.That(body, Does.Not.Contain("Pessoa restrita"));
                Assert.That(body, Does.Not.Contain("modelo probabilístico"));
            }
            if (scenario == "audit_failure")
            {
                var body = await response.Content.ReadAsStringAsync();
                Assert.That(body, Does.Not.Contain("Pessoa restrita"));
                Assert.That(audit.Successful, Is.Zero);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestCase("Development", true, "JornadaLocal", "Development", true)]
    [TestCase("Development", true, "JornadaSyntheticDev", "Development", true)]
    [TestCase("Development", false, "JornadaLocal", "Development", false)]
    [TestCase("Development", true, "", "Development", false)]
    [TestCase("Development", true, "JornadaLocal", null, false)]
    [TestCase("Development", true, "JornadaLocal", "Production", false)]
    [TestCase("Production", true, "JornadaLocal", "Development", false)]
    [TestCase("HML", true, "JornadaLocal", "Development", false)]
    public void Activation_gate_requires_development_runtime_and_resident_profile(
        string environment, bool enabled, string database, string? profile, bool expected)
    {
        Assert.That(SqlSyntheticDevelopmentSemiblindSearchActivationGate.IsEligible(
            environment, enabled, database, profile), Is.EqualTo(expected));
    }

    private sealed class FakeActivationGate(bool enabled) : ISemiblindSearchActivationGate
    {
        public Task<bool> IsEnabledAsync(CancellationToken ct) => Task.FromResult(enabled);
    }

    private sealed class FakeResolver : IAccessContextResolver
    {
        public Task<AccessContext?> ResolveAsync(PresentedAccessCredential credential, CancellationToken ct) =>
            Task.FromResult<AccessContext?>(new AccessContext(
                Guid.NewGuid(), credential.Type, credential.PublicCode, "SMADS", null,
                credential.AccessKey == "wrong_scope" ? [] : ["jornada.identidade.busca.read"], []));
    }

    private sealed class FakePolicy : IPolicyEngine
    {
        public Task<bool> IsAllowedAsync(AccessContext context, string permission, string? resourceCode,
            Guid? pessoaUuid, CancellationToken ct) =>
            Task.FromResult(context.Scopes.Contains(permission));
        public Task<bool> ArePersonsAllowedAsync(AccessContext context, string permission, string? resourceCode,
            IReadOnlyCollection<Guid> pessoaUuids, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class FakeService(bool unavailable) : ISemiblindIdentitySearchService
    {
        public int Calls { get; private set; }
        public Task<SemiblindIdentitySearchResponse> SearchAsync(AccessContext context,
            SemiblindIdentitySearchRequest request, Guid correlationId, CancellationToken cancellationToken)
        {
            Calls++;
            if (unavailable) throw new InvalidOperationException("Não existe modelo probabilístico ATIVO.");
            return Task.FromResult(new SemiblindIdentitySearchResponse(correlationId,
                [new SemiblindIdentityCandidate("opcao-teste", "Pessoa restrita", new DateOnly(1980, 1, 1), null)]));
        }
    }

    private sealed class TestAuditSink(bool fail) : IApiAuditSink
    {
        public int Successful { get; private set; }
        public Task PersistAsync(Microsoft.AspNetCore.Http.HttpContext http, Guid correlationId,
            long elapsedMs, CancellationToken ct)
        {
            if (fail) throw new InvalidOperationException("Falha sintética da auditoria");
            Successful++;
            return Task.CompletedTask;
        }
    }
}

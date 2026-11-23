using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    [TestCase("complete_empty", HttpStatusCode.OK)]
    [TestCase("incomplete_no_pass", HttpStatusCode.OK)]
    [TestCase("incomplete_fanout", HttpStatusCode.OK)]
    [TestCase("incomplete_timeout", HttpStatusCode.OK)]
    [TestCase("audit_failure", HttpStatusCode.ServiceUnavailable)]
    [TestCase("model_unavailable", HttpStatusCode.ServiceUnavailable)]
    [TestCase("feature_disabled", HttpStatusCode.ServiceUnavailable)]
    [TestCase("production_enabled", HttpStatusCode.ServiceUnavailable)]
    public async Task Route_enforces_access_and_pre_response_audit(string scenario, HttpStatusCode expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "jornada-semiblind-" + Guid.NewGuid().ToString("N"));
        var service = new FakeService(scenario);
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
            Assert.That(service.Calls, Is.EqualTo(scenario is
                "allowed" or "complete_empty" or "incomplete_no_pass"
                or "incomplete_fanout" or "incomplete_timeout"
                or "audit_failure" or "model_unavailable" ? 1 : 0));
            if (scenario is "feature_disabled" or "production_enabled")
            {
                var deniedBody = await response.Content.ReadAsStringAsync();
                Assert.That(deniedBody, Does.Not.Contain("Pessoa restrita"),
                    "Não divulgar PII quando o endpoint está desabilitado.");
                Assert.That(audit.Successful, Is.EqualTo(1),
                    "Até consultas desabilitadas devem ser auditadas.");
            }
            if (scenario is "missing" or "wrong_scope" or "allowed"
                or "complete_empty" or "incomplete_no_pass"
                or "incomplete_fanout" or "incomplete_timeout"
                or "model_unavailable")
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
            if (scenario is "complete_empty" or "incomplete_no_pass"
                or "incomplete_fanout" or "incomplete_timeout")
            {
                using var parsed = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var payload = parsed.RootElement;
                Assert.Multiple((Action)(() =>
                {
                    Assert.That(payload.GetProperty("nenhumDestes").GetBoolean(), Is.True);
                    Assert.That(payload.GetProperty("candidatos").GetArrayLength(), Is.Zero);
                    Assert.That(payload.GetProperty("busca_completa").GetBoolean(),
                        Is.EqualTo(scenario == "complete_empty"));
                    Assert.That(payload.GetProperty("motivo_incompletude").ValueKind,
                        Is.EqualTo(scenario == "complete_empty"
                            ? JsonValueKind.Null : JsonValueKind.String));
                    Assert.That(payload.TryGetProperty("pessoaUuid", out _), Is.False);
                    Assert.That(payload.TryGetProperty("score", out _), Is.False);
                    Assert.That(payload.TryGetProperty("cpf", out _), Is.False);
                }));
                if (scenario != "complete_empty")
                {
                    var expectedReason = scenario switch
                    {
                        "incomplete_no_pass" => SemiblindRetrievalReasons.NoEligiblePass,
                        "incomplete_fanout" => SemiblindRetrievalReasons.FanoutLimitExceeded,
                        "incomplete_timeout" => SemiblindRetrievalReasons.Timeout,
                        _ => throw new InvalidOperationException("Unexpected semiblind test scenario")
                    };
                    Assert.That(payload.GetProperty("motivo_incompletude").GetString(),
                        Is.EqualTo(expectedReason));
                }
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

    [TestCase(false)]
    [TestCase(true)]
    public async Task Dev_http_real_service_enforces_person_authorization_and_top_five(bool denyAll)
    {
        // #1854: real HTTP endpoint + real SemiblindIdentitySearchService.
        // The retriever and person policy are test doubles; this does NOT
        // prove actual SQL candidate retrieval or institutional visibility #539.
        var root = Path.Combine(Path.GetTempPath(),
            "jornada-semiblind-real-service-" + Guid.NewGuid().ToString("N"));
        var persons = Enumerable.Range(0, 8).Select(i =>
            new SemiblindInternalCandidate(Guid.NewGuid(), $"Pessoa Sintetica {i}",
                new DateOnly(1991, 4, 13), "Mae Sintetica")).ToArray();
        var fixture = persons.Take(2).Concat([persons[1]]).Concat(persons.Skip(2)).ToArray();
        var denied = denyAll
            ? persons.Select(p => p.PessoaUuid).ToHashSet()
            : new HashSet<Guid> { persons[0].PessoaUuid, persons[2].PessoaUuid };
        var retriever = new RealServiceHttpFakeRetriever(fixture);
        var policy = new RealServiceHttpPersonPolicy(denied);
        var audit = new TestAuditSink(false);
        try
        {
            await using var factory = new WebApplicationFactory<ApiEntryPointMarker>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Development");
                    builder.UseSetting("BronzeStorage:RootPath", Path.Combine(root, "bronze"));
                    builder.UseSetting("IngestionStaging:RootPath", Path.Combine(root, "staging"));
                    builder.UseSetting("SemiblindIdentitySearch:Enabled", "true");
                    builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["BronzeStorage:RootPath"] = Path.Combine(root, "bronze"),
                            ["IngestionStaging:RootPath"] = Path.Combine(root, "staging"),
                            ["SemiblindIdentitySearch:Enabled"] = "true"
                        }));
                    builder.ConfigureTestServices(services =>
                    {
                        services.RemoveAll<IAccessContextResolver>();
                        services.AddSingleton<IAccessContextResolver>(new FakeResolver());
                        services.RemoveAll<IPolicyEngine>();
                        services.AddSingleton<IPolicyEngine>(policy);
                        services.RemoveAll<ISemiblindCandidateRetriever>();
                        services.AddSingleton<ISemiblindCandidateRetriever>(retriever);
                        services.RemoveAll<ISemiblindIdentitySearchService>();
                        services.AddSingleton<ISemiblindIdentitySearchService, SemiblindIdentitySearchService>();
                        services.RemoveAll<ISemiblindSearchActivationGate>();
                        services.AddSingleton<ISemiblindSearchActivationGate>(new FakeActivationGate(true));
                        services.RemoveAll<IApiAuditSink>();
                        services.AddSingleton<IApiAuditSink>(audit);
                        services.AddSingleton<ISqlReadinessProbe>(new InMemorySqlReadinessProbe(false));
                    });
                });
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, Route)
            {
                // No person name or CPF. Request sufficiency is the retriever's
                // versioned blocking decision, never a universal mandatory-name gate.
                Content = JsonContent.Create(new SemiblindIdentitySearchRequest(
                    null, new DateOnly(1991, 4, 13), "Mae Sintetica"))
            };
            request.Headers.TryAddWithoutValidation("X-Jornada-Gestor", "SMADS");
            request.Headers.TryAddWithoutValidation("X-Jornada-Access-Key", "allowed");
            using var response = await client.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(retriever.Calls, Is.EqualTo(1));
            Assert.That(retriever.LastRequest?.Nome, Is.Null);
            Assert.That(audit.Successful, Is.EqualTo(1),
                "The authorized HTTP response must be preceded by one audit.");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var result = document.RootElement;
            Assert.That(result.GetProperty("nenhumDestes").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("busca_completa").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("motivo_incompletude").ValueKind,
                Is.EqualTo(JsonValueKind.Null));
            var candidates = result.GetProperty("candidatos").EnumerateArray().ToArray();
            Assert.That(candidates, Has.Length.EqualTo(denyAll ? 0 : 5));
            Assert.That(result.TryGetProperty("cpf", out _), Is.False);
            Assert.That(result.TryGetProperty("pessoaUuid", out _), Is.False);
            Assert.That(result.TryGetProperty("score", out _), Is.False);
            Assert.That(policy.CheckedPersons.Distinct().Count(),
                Is.EqualTo(denyAll ? 8 : 7),
                "Every projected person must be authorized before the 5-option limit.");
            Assert.That(policy.CheckedPersons.Count, Is.EqualTo(policy.CheckedPersons.Distinct().Count()),
                "The duplicate UUID must not trigger repeated authorization or an extra option.");
            if (!denyAll)
                Assert.That(candidates.Select(c => c.GetProperty("nome_completo").GetString()),
                    Is.EquivalentTo(new[]
                    {
                        "Pessoa Sintetica 1", "Pessoa Sintetica 3", "Pessoa Sintetica 4",
                        "Pessoa Sintetica 5", "Pessoa Sintetica 6"
                    }));
            var optionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                Assert.That(candidate.TryGetProperty("pessoaUuid", out _), Is.False);
                Assert.That(candidate.TryGetProperty("cpf", out _), Is.False);
                Assert.That(candidate.TryGetProperty("score", out _), Is.False);
                Assert.That(candidate.TryGetProperty("posterior", out _), Is.False);
                var optionId = candidate.GetProperty("opcaoId").GetString();
                Assert.That(optionId, Is.Not.Null.And.Length.EqualTo(32));
                Assert.That(optionIds.Add(optionId!), Is.True);
            }
            Assert.That(optionIds.Count, Is.EqualTo(candidates.Length));
            var responseBody = document.RootElement.GetRawText();
            Assert.That(responseBody, Does.Not.Contain("Pessoa Sintetica 0"));
            Assert.That(responseBody, Does.Not.Contain("Pessoa Sintetica 2"));
            foreach (var person in persons)
                Assert.That(responseBody, Does.Not.Contain(person.PessoaUuid.ToString()),
                    "Internal UUIDs must not be exposed through the HTTP API.");
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

    private sealed class FakeService(string scenario) : ISemiblindIdentitySearchService
    {
        public int Calls { get; private set; }
        public Task<SemiblindIdentitySearchResponse> SearchAsync(AccessContext context,
            SemiblindIdentitySearchRequest request, Guid correlationId, CancellationToken cancellationToken)
        {
            Calls++;
            if (scenario == "model_unavailable")
                throw new InvalidOperationException("Não existe modelo probabilístico ATIVO.");
            if (scenario is "complete_empty" or "incomplete_no_pass"
                or "incomplete_fanout" or "incomplete_timeout")
            {
                var reason = scenario switch
                {
                    "incomplete_no_pass" => SemiblindRetrievalReasons.NoEligiblePass,
                    "incomplete_fanout" => SemiblindRetrievalReasons.FanoutLimitExceeded,
                    "incomplete_timeout" => SemiblindRetrievalReasons.Timeout,
                    _ => null
                };
                return Task.FromResult(new SemiblindIdentitySearchResponse(
                    correlationId, Array.Empty<SemiblindIdentityCandidate>(), true,
                    reason is null, reason));
            }
            return Task.FromResult(new SemiblindIdentitySearchResponse(correlationId,
                [new SemiblindIdentityCandidate("opcao-teste", "Pessoa restrita", new DateOnly(1980, 1, 1), null)]));
        }
    }

    private sealed class RealServiceHttpFakeRetriever(
        IReadOnlyList<SemiblindInternalCandidate> persons) : ISemiblindCandidateRetriever
    {
        public int Calls { get; private set; }
        public SemiblindIdentitySearchRequest? LastRequest { get; private set; }
        public Task<SemiblindCandidateRetrievalResult> RetrieveAsync(
            SemiblindIdentitySearchRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(SemiblindCandidateRetrievalResult.Complete(persons));
        }
    }

    private sealed class RealServiceHttpPersonPolicy(HashSet<Guid> denied) : IPolicyEngine
    {
        public List<Guid> CheckedPersons { get; } = [];

        public Task<bool> IsAllowedAsync(AccessContext context, string permission,
            string? resourceCode, Guid? pessoaUuid, CancellationToken cancellationToken)
        {
            if (pessoaUuid is Guid person)
                CheckedPersons.Add(person);
            return Task.FromResult(
                permission == "jornada.identidade.busca.read"
                && context.Scopes.Contains(permission)
                && resourceCode == context.TipoCodigo
                && (pessoaUuid is not Guid id || !denied.Contains(id)));
        }

        public Task<bool> ArePersonsAllowedAsync(AccessContext context, string permission,
            string? resourceCode, IReadOnlyCollection<Guid> pessoaUuids, CancellationToken cancellationToken)
            => Task.FromResult(false);
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

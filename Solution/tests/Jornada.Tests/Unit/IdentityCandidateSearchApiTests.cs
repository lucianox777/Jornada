using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jornada.Access.Security;
using Jornada.Api;
using Jornada.Bronze.Storage;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class IdentityCandidateSearchApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestCase("missing", HttpStatusCode.Unauthorized, 0)]
    [TestCase("wrong_key", HttpStatusCode.Unauthorized, 0)]
    [TestCase("deny", HttpStatusCode.Forbidden, 0)]
    [TestCase("zero", HttpStatusCode.OK, 1)]
    [TestCase("um", HttpStatusCode.OK, 1)]
    [TestCase("cinco", HttpStatusCode.OK, 1)]
    [TestCase("type", HttpStatusCode.OK, 1)]
    public async Task Query_requires_shared_auth_and_audits_every_result(
        string scenario, HttpStatusCode expected, int expectedServiceCalls)
    {
        var root = Path.Combine(Path.GetTempPath(), "jornada-candidate-api-" + Guid.NewGuid().ToString("N"));
        var audit = new InMemoryApiAuditSink();
        var service = new FakeCandidateService();
        try
        {
            await using var factory = CreateFactory(root, audit, service);
            using var client = factory.CreateClient();
            using var request = NewRequest(scenario);
            using var response = await client.SendAsync(request);

            Assert.That(response.StatusCode, Is.EqualTo(expected));
            Assert.That(service.Calls, Is.EqualTo(expectedServiceCalls),
                "Nenhuma busca SQL pode ocorrer sem autorização.");
            Assert.That(audit.Events.Count(e => e.Path == IdentityCandidateSearchApi.Route
                && e.StatusCode == (int)expected), Is.EqualTo(1),
                "O middleware central deve auditar inclusive 401/403.");
            if (expected != HttpStatusCode.OK) return;

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var rootJson = json.RootElement;
            var count = scenario switch { "zero" => 0, "um" => 1, "type" => 1, _ => 5 };
            Assert.Multiple(() =>
            {
                Assert.That(rootJson.GetProperty("candidatos").GetArrayLength(), Is.EqualTo(count));
                Assert.That(rootJson.GetProperty("nenhumDestes").GetBoolean(), Is.EqualTo(count == 0));
            });
            var body = rootJson.GetRawText().ToLowerInvariant();
            foreach (var forbidden in new[] { "\"cpf\"", "\"score\"", "\"llr\"", "\"posterior\"", "\"rank\"", "\"posicao\"" })
                Assert.That(body, Does.Not.Contain(forbidden), forbidden);
            foreach (var candidate in rootJson.GetProperty("candidatos").EnumerateArray())
            {
                Assert.That(candidate.GetProperty("pessoaUuid").GetGuid(), Is.Not.EqualTo(Guid.Empty));
                Assert.That(candidate.TryGetProperty("nome_completo", out _), Is.True);
                Assert.That(candidate.TryGetProperty("data_nascimento", out _), Is.True);
                Assert.That(candidate.TryGetProperty("nome_mae", out _), Is.True);
                Assert.That(candidate.EnumerateObject().Count(), Is.EqualTo(4),
                    "Não exportar CPF, score, LLR nem outros identificadores.");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Multiple_queries_each_produce_a_separate_audit_event()
    {
        var root = Path.Combine(Path.GetTempPath(), "jornada-candidate-audit-" + Guid.NewGuid().ToString("N"));
        var audit = new InMemoryApiAuditSink();
        var service = new FakeCandidateService();
        try
        {
            await using var factory = CreateFactory(root, audit, service);
            using var client = factory.CreateClient();
            foreach (var scenario in new[] { "zero", "um", "cinco" })
            {
                using var request = NewRequest(scenario);
                using var response = await client.SendAsync(request);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            }
            Assert.That(service.Calls, Is.EqualTo(3));
            Assert.That(audit.Events.Count(e => e.Path == IdentityCandidateSearchApi.Route
                && e.StatusCode == 200), Is.EqualTo(3));
            Assert.That(audit.Events.Select(e => e.CorrelationId).Distinct().Count(), Is.EqualTo(3));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Request_never_accepts_cpf_or_missing_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "jornada-candidate-strict-" + Guid.NewGuid().ToString("N"));
        var audit = new InMemoryApiAuditSink();
        var service = new FakeCandidateService();
        try
        {
            await using var factory = CreateFactory(root, audit, service);
            using var client = factory.CreateClient();
            foreach (var invalidJson in new[]
            {
                "{\"nomeCompleto\":\"Ana\",\"cpf\":\"52998224725\"}",
                "{\"nomeCompleto\":\" \"}",
                "{\"nomeCompleto\":\"Ana\\nOutro\"}"
            })
            {
                using var request = NewRequest("grant", invalidJson);
                using var response = await client.SendAsync(request);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            }
            Assert.That(service.Calls, Is.Zero);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Missing_birth_uses_dynamic_ruleset_when_combined_blocking_is_unavailable()
    {
        var ruleSet = LinkageDynamicRuleSet.CreateWithPasses(
            "SYNTHETIC_DYN_V1", LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
            [LinkageBlockingPass.Create("dynamic-name", [BlockingFeatureNames.FullName])],
            [new KeyValuePair<string, decimal>("dummy", 1m)]);
        var withoutBirth = new IdentityObservation(null, "NAO_INFORMADO", "Maria Silva", null, "Ana Silva");
        var passes = IdentityCandidateBlockingPlanner.Plan(ruleSet, withoutBirth);
        Assert.Multiple(() =>
        {
            Assert.That(passes.Select(p => p.PassId), Does.Contain("dynamic-name"));
            Assert.That(passes.Any(p => p.PassId.StartsWith("combined-", StringComparison.Ordinal)), Is.False);
        });
        var withBirth = withoutBirth with { DataNascimento = new DateOnly(1975, 2, 11) };
        Assert.That(IdentityCandidateBlockingPlanner.Plan(ruleSet, withBirth)
            .Any(p => p.PassId == "combined-exact"), Is.True);
    }

    [Test]
    public void Selector_limits_to_five_using_internal_rank_but_displays_neutral_uuid_order()
    {
        var ids = Enumerable.Range(1, 7).Select(i => new Guid($"00000000-0000-4000-8000-{i:D12}")).ToArray();
        var candidates = ids.Select((id, i) =>
            new SemiblindCandidateSnapshot(id, "Nome " + i, null, null)).ToArray();
        var rank = ids.Reverse().Select((id, i) =>
            new CandidateScore(id, (7 - i) / 10m, 7 - i)).ToArray();
        var result = SemiblindCandidateSelector.SelectRanked(rank, candidates);
        Assert.That(result.Select(c => c.PessoaUuid), Is.EqualTo(ids.Skip(2).ToArray()),
            "Seleciona os cinco por evidência mas os apresenta por UUID, não pelo score.");
    }

    private static WebApplicationFactory<ApiEntryPointMarker> CreateFactory(
        string root, InMemoryApiAuditSink audit, FakeCandidateService service) =>
        new WebApplicationFactory<ApiEntryPointMarker>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BronzeStorage:RootPath"] = Path.Combine(root, "bronze"),
                    ["IngestionStaging:RootPath"] = Path.Combine(root, "staging")
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAccessContextResolver>();
                services.AddSingleton<IAccessContextResolver>(new FakeResolver());
                services.RemoveAll<IIdentityCandidateSearchService>();
                services.AddSingleton<IIdentityCandidateSearchService>(service);
                services.RemoveAll<IApiAuditSink>();
                services.AddSingleton<IApiAuditSink>(audit);
                services.RemoveAll<ISqlReadinessProbe>();
                services.AddSingleton<ISqlReadinessProbe>(new InMemorySqlReadinessProbe(ready: false));
            });
        });

    private static HttpRequestMessage NewRequest(string scenario, string? json = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, IdentityCandidateSearchApi.Route)
        {
            Content = json is null
                ? JsonContent.Create(new IdentityCandidateSearchRequest(scenario, null, "Ana Silva"), options: JsonOptions)
                : new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        if (scenario == "missing") return request;
        request.Headers.TryAddWithoutValidation(
            scenario == "type" ? "X-Jornada-Beneficio" : "X-Jornada-Gestor",
            scenario == "type" ? "AR01" : "SMADS");
        request.Headers.TryAddWithoutValidation("X-Jornada-Access-Key", scenario);
        return request;
    }

    private sealed class FakeResolver : IAccessContextResolver
    {
        public Task<AccessContext?> ResolveAsync(PresentedAccessCredential credential, CancellationToken ct)
        {
            if (credential.AccessKey == "wrong_key")
                return Task.FromResult<AccessContext?>(null);
            var scopes = credential.AccessKey == "deny" ? Array.Empty<string>() : [IdentityCandidateSearchApi.Permission];
            return Task.FromResult<AccessContext?>(new AccessContext(
                Guid.Parse("11111111-1111-4111-8111-111111111112"), credential.Type,
                credential.PublicCode, "SMADS", credential.Type == AccessCredentialType.GESTOR ? null : "AR01",
                scopes, credential.Type == AccessCredentialType.GESTOR ? [] : ["AR01"]));
        }
    }

    private sealed class FakeCandidateService : IIdentityCandidateSearchService
    {
        public int Calls { get; private set; }
        public Task<IdentityCandidateSearchResponse> SearchAsync(
            AccessContext context, IdentityCandidateSearchRequest request, CancellationToken ct)
        {
            Calls++;
            var count = request.NomeCompleto switch { "zero" => 0, "um" or "type" => 1, _ => 5 };
            var candidates = Enumerable.Range(1, count).Select(i => new IdentityCandidateDto(
                new Guid($"00000000-0000-4000-8000-{i:D12}"),
                "Pessoa Sintética " + i, new DateOnly(1970, 1, i), null)).ToArray();
            return Task.FromResult(new IdentityCandidateSearchResponse(candidates, count == 0));
        }
    }
}

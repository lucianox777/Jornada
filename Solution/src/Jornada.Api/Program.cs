using Jornada.Access.Security;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using System.Net.Http.Headers;
using Jornada.Contracts;
using Jornada.Bronze.Storage;
using Jornada.Ingestion;
using Jornada.Linkage.Runner;
using Jornada.Api;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Contratos permanecem versionados no repositório/configuração da aplicação em todos os ambientes.
var configuredContractsRoot = builder.Configuration["Contracts:RepositoryRoot"];
var repositoryRoot = string.IsNullOrWhiteSpace(configuredContractsRoot)
    ? DevelopmentSecurityPaths.FindRepositoryRoot(builder.Environment.ContentRootPath)
    : Path.GetFullPath(configuredContractsRoot);

// Em Development, o pacote contém chaves sintéticas pré-geradas e fixas para teste local.
// Esse arquivo jamais é carregado fora de Development; HML/Produção permanecem deny-by-default
// até a integração com o secret store/identidade corporativa.
if (builder.Environment.IsDevelopment())
{
    var configured = builder.Configuration["DevelopmentSecurity:KeysFile"];
    var keyFile = string.IsNullOrWhiteSpace(configured)
        ? Path.Combine(repositoryRoot, "config", "security", "test-access-keys.json")
        : Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, configured));
    builder.Services.AddSingleton<IAccessContextResolver>(_ => new DevelopmentAccessContextResolver(keyFile));
}
else
{
    builder.Services.AddSingleton<IAccessContextResolver, CorporateIdentityPendingAccessContextResolver>();
}
builder.Services.AddJornadaAccessSecurity();
builder.Services.AddSingleton<IJornadaAccessVerifier, JornadaApiAccessVerifier>();

// Implementações SQL reais das superfícies que não dependem de infraestrutura corporativa externa.
var jornadaConnectionString = builder.Configuration.GetConnectionString("Jornada")
    ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
builder.Services.AddSingleton<IOperationalSqlAdapter>(new OperationalSqlAdapter(jornadaConnectionString));
builder.Services.AddSingleton<IApiAuditSink, SqlApiAuditSink>();
builder.Services.AddSingleton<ISqlReadinessProbe, SqlSchemaReadinessProbe>();
builder.Services.AddSingleton<IContractResolver>(sp => new CatalogBackedContractResolver(repositoryRoot, sp.GetRequiredService<IOperationalSqlAdapter>()));
builder.Services.AddSingleton(_ => new AgentCpfPseudonymizer(builder.Configuration, builder.Environment, repositoryRoot));

var bronzeProvider = builder.Configuration["BronzeStorage:Provider"] ?? "FileSystem";
if (!string.Equals(bronzeProvider, "FileSystem", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException($"BronzeStorage:Provider não suportado nesta distribuição: {bronzeProvider}.");
var configuredBronzeRoot = builder.Configuration["BronzeStorage:RootPath"];
var bronzeConfigurationValid = builder.Environment.IsDevelopment()
    || (!string.IsNullOrWhiteSpace(configuredBronzeRoot) && Path.IsPathRooted(configuredBronzeRoot));
var bronzeRoot = string.IsNullOrWhiteSpace(configuredBronzeRoot)
    ? Path.Combine(repositoryRoot, "data", "bronze")
    : Path.GetFullPath(Path.IsPathRooted(configuredBronzeRoot) ? configuredBronzeRoot : Path.Combine(repositoryRoot, configuredBronzeRoot));

var configuredStagingRoot = builder.Configuration["IngestionStaging:RootPath"];
var stagingConfigurationValid = builder.Environment.IsDevelopment()
    || (!string.IsNullOrWhiteSpace(configuredStagingRoot) && Path.IsPathRooted(configuredStagingRoot));
var stagingRoot = string.IsNullOrWhiteSpace(configuredStagingRoot)
    ? Path.Combine(repositoryRoot, "data", "staging")
    : Path.GetFullPath(Path.IsPathRooted(configuredStagingRoot) ? configuredStagingRoot : Path.Combine(repositoryRoot, configuredStagingRoot));

builder.Services.AddSingleton<IBronzeObjectStore>(_ =>
{
    if (!bronzeConfigurationValid)
        throw new InvalidOperationException("BronzeStorage:RootPath deve ser um caminho absoluto para armazenamento compartilhado/durável em HML/Produção.");
    return new FileSystemBronzeObjectStore(bronzeRoot);
});
builder.Services.Configure<IngestionStagingOptions>(builder.Configuration.GetSection("IngestionStaging"));
builder.Services.AddSingleton<IngestionStagingStore>(_ =>
{
    if (!stagingConfigurationValid)
        throw new InvalidOperationException("IngestionStaging:RootPath deve ser absoluto em HML/Produção.");
    return new IngestionStagingStore(stagingRoot);
});
builder.Services.AddSingleton(new ApiOperationalPaths(bronzeRoot, stagingRoot, bronzeConfigurationValid, stagingConfigurationValid));
builder.Services.AddSingleton<ApiReadinessProbe>();
builder.Services.AddHostedService<IngestionStagingCleanupWorker>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IPolicyEngine, MunicipalAccessPolicyEngine>();
}
else
{
    // HML/Produção não aceitam chaves de Development. Até o IdP/secret store corporativo ser conectado, falha fechada.
    builder.Services.AddSingleton<IPolicyEngine, DenyByDefaultPolicyEngine>();
}
builder.Services.AddSingleton<IIdentityResolutionService, SqlIdentityResolutionService>();
builder.Services.AddSingleton<SqlProbabilisticIdentityLinkage>();
builder.Services.AddSingleton<ISemiblindCandidateRetriever>(sp => sp.GetRequiredService<SqlProbabilisticIdentityLinkage>());
builder.Services.AddSingleton<ISemiblindIdentitySearchService, SemiblindIdentitySearchService>();
builder.Services.AddSingleton<ISemiblindSearchActivationGate, SqlSyntheticDevelopmentSemiblindSearchActivationGate>();
builder.Services.AddSingleton<IIdentityCorrectionService, SqlIdentityCorrectionService>();
builder.Services.AddSingleton<IIngestionService, SqlIngestionService>();
builder.Services.AddSingleton<IPersonProjectionService, SqlPersonProjectionService>();
builder.Services.AddSingleton<IPersonCanonicalResolver, SqlPersonCanonicalResolver>();
builder.Services.AddSingleton<IRegistrosQueryService, SqlRegistrosQueryService>();
builder.Services.AddSingleton<IPossibilidadesQueryService, SqlPossibilidadesQueryService>();
builder.Services.AddSingleton<IProgressiveOriginQueryService, SqlProgressiveOriginQueryService>();
var apiRateLimitOptions = builder.Configuration.GetSection(ApiRateLimitOptions.SectionName).Get<ApiRateLimitOptions>() ?? new ApiRateLimitOptions();
apiRateLimitOptions.Validate();
builder.Services.AddSingleton(apiRateLimitOptions);
builder.Services.AddSingleton<AuthenticatedRateLimitGuard>();

// X-Forwarded-For somente é aceito de proxies explicitamente confiáveis no ambiente.
var trustedProxySettings = builder.Configuration.GetSection("ReverseProxy").Get<TrustedProxyOptions>() ?? new TrustedProxyOptions();
builder.Services.Configure<TrustedProxyOptions>(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddSingleton<IConfigureOptions<ForwardedHeadersOptions>, ConfigureTrustedForwardedHeaders>();

// Proteção de borda sem usar a access key como chave de partição/log.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Borda pré-autenticação: teto alto por IP para absorver flood sem confiar em headers de credencial.
    // Os limites institucionais exatos são reaplicados por CredentialId após autenticação.
    options.AddPolicy("standard", http => ApiRateLimiting.EdgeFixedWindow(http, checked(apiRateLimitOptions.StandardPermitLimit * apiRateLimitOptions.EdgeMultiplier), "STANDARD"));
    options.AddPolicy("person-query", http => ApiRateLimiting.EdgeFixedWindow(http, checked(apiRateLimitOptions.StandardPermitLimit * apiRateLimitOptions.EdgeMultiplier), "PESSOA"));
    options.AddPolicy("identity", http => ApiRateLimiting.EdgeFixedWindow(http, checked(apiRateLimitOptions.IdentityPermitLimit * apiRateLimitOptions.EdgeMultiplier), "IDENTIDADE"));
    options.AddPolicy("ingestion", http => ApiRateLimiting.EdgeFixedWindow(http, checked(apiRateLimitOptions.IngestionPermitLimit * apiRateLimitOptions.EdgeMultiplier), "INGESTAO"));
});

var app = builder.Build();
if (TrustedProxyConfiguration.IsEnabled(trustedProxySettings))
    app.UseForwardedHeaders();
app.UseMiddleware<ApiAuditMiddleware>();

// O limite padrão do Kestrel é menor que o contrato de ingestão. Ele é elevado somente para
// a rota de Entrega, antes que qualquer byte do corpo seja lido. Os demais endpoints mantêm
// o limite padrão do servidor/reverse proxy.
app.Use(async (http, next) =>
{
    if (string.Equals(http.Request.Path.Value, "/api/v1/ingestao/entregas", StringComparison.OrdinalIgnoreCase))
    {
        var feature = http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
            feature.MaxRequestBodySize = IngestionPackageInspector.MaxCompressedBytes;
    }
    await next();
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

static object LivePayload(IHostEnvironment env) => new
{
    status = "ok",
    utc = DateTimeOffset.UtcNow,
    securityMode = env.IsDevelopment() ? "DEVELOPMENT_SYNTHETIC_KEYS" : "DENY_BY_DEFAULT_PENDING_CORPORATE_IDENTITY"
};

// /health é preservado por compatibilidade; liveness e readiness passam a ser superfícies distintas.
app.MapGet("/health", (IHostEnvironment env) => Results.Ok(LivePayload(env)));
app.MapGet("/health/live", (IHostEnvironment env) => Results.Ok(LivePayload(env)));
app.MapGet("/health/ready", async (ApiReadinessProbe probe, CancellationToken ct) =>
{
    var result = await probe.CheckAsync(ct);
    var payload = new { status = result.Ready ? "ready" : "not_ready", utc = DateTimeOffset.UtcNow, checks = result.Checks };
    if (result.Ready) return Results.Ok(payload);
    return Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapIdentityApi();

// Consulta de origem: contrato distinto, somente Gestor proprietário e escopo específico.
app.MapProgressiveOriginApi();

// ProgressiveOriginApi already maps the existing Monitor. The DEV-only master
// presentation is registered separately and never inherits monitor scopes.
app.MapModelGovernanceReadOnlyApi();

app.MapIngestionApi();
app.MapPersonApi();
app.MapRegistrosApi();
app.MapPossibilidadesApi();



app.Run();


file sealed class CorporateIdentityPendingAccessContextResolver : IAccessContextResolver
{
    public Task<AccessContext?> ResolveAsync(PresentedAccessCredential credential, CancellationToken ct) =>
        Task.FromResult<AccessContext?>(null); // HML/Produção: integrar IdP/secret store corporativo; deny-by-default enquanto pendente.
}

file sealed class DenyByDefaultPolicyEngine : IPolicyEngine
{
    public Task<bool> IsAllowedAsync(AccessContext context, string permission, string? resourceCode, Guid? pessoaUuid, CancellationToken ct) =>
        Task.FromResult(false);

    public Task<bool> ArePersonsAllowedAsync(AccessContext context, string permission, string? resourceCode, IReadOnlyCollection<Guid> pessoaUuids, CancellationToken ct) =>
        Task.FromResult(false);
}

public partial class Program { }

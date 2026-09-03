using System.Net.Http.Headers;
using Jornada.Contracts;
using Jornada.Bronze.Storage;
using Jornada.Ingestion;
using Jornada.Api;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.SqlClient;
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

// Implementações SQL reais das superfícies que não dependem de infraestrutura corporativa externa.
builder.Services.AddSingleton<SqlConnectionFactory>();
builder.Services.AddSingleton<IApiAuditSink, SqlApiAuditSink>();
builder.Services.AddSingleton<ISqlReadinessProbe, SqlSchemaReadinessProbe>();
builder.Services.AddSingleton<IContractResolver>(sp => new CatalogBackedContractResolver(repositoryRoot, sp.GetRequiredService<SqlConnectionFactory>()));
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
builder.Services.AddSingleton<IIdentityCorrectionService, SqlIdentityCorrectionService>();
builder.Services.AddSingleton<IIngestionService, SqlIngestionService>();
builder.Services.AddSingleton<IPersonProjectionService, SqlPersonProjectionService>();
builder.Services.AddSingleton<IPersonCanonicalResolver, SqlPersonCanonicalResolver>();
builder.Services.AddSingleton<IRegistrosQueryService, SqlRegistrosQueryService>();
builder.Services.AddSingleton<IPossibilidadesQueryService, SqlPossibilidadesQueryService>();
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

// CPF vai no corpo. Esta consulta somente localiza UUID existente; não constitui Pessoa/UUID.
app.MapPost("/api/v1/identidade/resolver", async (
    HttpRequest http,
    IdentityResolutionRequest request,
    IAccessContextResolver access,
    IPolicyEngine policy,
    IIdentityResolutionService service,
    CancellationToken ct) =>
{
    var auth = await AuthenticateAsync(http, access, allowTypeCredentials: true, ct);
    if (auth.Error is not null) return auth.Error;
    var context = auth.Context!;
    if (!await policy.IsAllowedAsync(context, "jornada.identidade.resolve", null, null, ct)) return Results.Forbid();

    var result = await service.ResolveAsync(context, request, ct);
    if (result.PessoaUuid is Guid resolvedUuid)
    {
        // Registra internamente o alvo resolvido. Nenhuma credencial autorizada depende de vínculo prévio com a Pessoa.
        ApiAuditContext.SetPerson(http.HttpContext, resolvedUuid);
        if (!await policy.IsAllowedAsync(context, "jornada.identidade.resolve", null, resolvedUuid, ct))
            return Results.Ok(new IdentityResolutionResponse(ResolutionStatus.NAO_RESOLVIDO, null, null, "NAO_LOCALIZADO_OU_NAO_AUTORIZADO"));
    }
    return Results.Ok(result);
}).RequireRateLimiting("identity");

// Uma única Entrega externa por ZIP, sempre com manifest.json + pessoas.jsonl + registros.jsonl.
// registros.jsonl pode estar vazio; o contexto factual é opcional nesse caso. O nome do ZIP contém seu SHA-256.
// O cliente não informa entregaId/loteSeq/loteTotal; lotes são internos.
app.MapPost("/api/v1/ingestao/entregas", async (
    HttpRequest http,
    IAccessContextResolver access,
    IPolicyEngine policy,
    IIngestionService service,
    IngestionStagingStore staging,
    CancellationToken ct) =>
{
    var auth = await AuthenticateAsync(http, access, allowTypeCredentials: false, ct);
    if (auth.Error is not null) return auth.Error;
    var context = auth.Context!;

    var idempotencyKey = http.Headers["Idempotency-Key"].ToString();
    if (string.IsNullOrWhiteSpace(idempotencyKey)) return Results.BadRequest(new { erro = "Idempotency-Key obrigatório." });
    if (idempotencyKey.Length > 200) return Results.BadRequest(new { erro = "Idempotency-Key excede 200 caracteres." });
    if (http.ContentType is null || !http.ContentType.StartsWith("application/zip", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { erro = "Content-Type deve ser application/zip." });
    if (!ContentDispositionHeaderValue.TryParse(http.Headers["Content-Disposition"].ToString(), out var contentDisposition))
        return Results.BadRequest(new { erro = "Content-Disposition com filename canônico é obrigatório." });
    var rawFileName = (contentDisposition.FileNameStar ?? contentDisposition.FileName)?.Trim('"');
    if (string.IsNullOrWhiteSpace(rawFileName))
        return Results.BadRequest(new { erro = "filename do ZIP inválido." });
    var packageFileName = rawFileName;
    if (http.ContentLength is > IngestionPackageInspector.MaxCompressedBytes)
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

    // Bloqueio genérico antes de receber bytes: uma credencial sem scope de ingestão não consome parsing/armazenamento temporário.
    if (!await policy.IsAllowedAsync(context, "jornada.ingestao.write", null, null, ct)) return Results.Forbid();

    StagedPackage temp;
    try
    {
        temp = await staging.ReceiveAsync(http.Body, IngestionPackageInspector.MaxCompressedBytes, ct);
    }
    catch (InvalidDataException)
    {
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    }
    catch (IngestionStagingUnavailableException)
    {
        http.HttpContext.Response.Headers.RetryAfter = "60";
        return Results.Json(new { codigo = "INGESTAO_STAGING_INDISPONIVEL", erro = "Staging temporário da ingestão indisponível." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    try
    {
        IngestionPackageManifest manifest;
        await using (var minimal = File.OpenRead(temp.Path))
        {
            try { manifest = IngestionPackageInspector.ParseManifestForAuthorization(minimal, temp.Length); }
            catch (InvalidDataException ex) { return Results.BadRequest(new { erro = ex.Message }); }
        }

        var resourceCode = manifest.CodigoTipo;
        ApiAuditContext.SetResourceCode(http.HttpContext, resourceCode);
        if (!await policy.IsAllowedAsync(context, "jornada.ingestao.write", resourceCode, null, ct)) return Results.Forbid();

        // Só depois da autorização do recurso executamos a validação pesada do conteúdo descompactado.
        await using (var full = File.OpenRead(temp.Path))
        {
            try { manifest = IngestionPackageInspector.ParseAndValidate(full, temp.Length); }
            catch (InvalidDataException ex) { return Results.BadRequest(new { erro = ex.Message }); }
        }

        try { IngestionPackageInspector.ValidateCanonicalFileName(packageFileName, manifest, context, temp.Sha256); }
        catch (InvalidDataException ex) { return Results.BadRequest(new { erro = ex.Message }); }

        try
        {
            await using var payload = File.OpenRead(temp.Path);
            var receipt = await service.ReceivePackageAsync(context, idempotencyKey, manifest, packageFileName, payload, temp.Length, temp.Sha256, ct);
            return Results.Accepted($"/api/v1/ingestao/entregas/{receipt.EntregaId}", receipt);
        }
        catch (IngestionConflictException ex) { return Results.Conflict(new { erro = ex.Message }); }
        catch (IngestionContractException ex) { return Results.BadRequest(new { erro = ex.Message }); }
        catch (BronzeObjectIntegrityException ex)
        {
            var mapped = BronzeStorageHttpFailureMapper.Map(ex);
            http.HttpContext.Response.Headers.RetryAfter = BronzeStorageHttpFailureMapper.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Results.Json(new { codigo = mapped.Code, erro = mapped.Message }, statusCode: mapped.StatusCode);
        }
        catch (BronzeStorageUnavailableException ex)
        {
            var mapped = BronzeStorageHttpFailureMapper.Map(ex);
            http.HttpContext.Response.Headers.RetryAfter = BronzeStorageHttpFailureMapper.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Results.Json(new { codigo = mapped.Code, erro = mapped.Message }, statusCode: mapped.StatusCode);
        }
    }
    finally
    {
        staging.TryDelete(temp.Path);
    }
}).RequireRateLimiting("ingestion");

app.MapGet("/api/v1/ingestao/entregas/{entregaId:guid}", async (
    HttpRequest http,
    Guid entregaId,
    IAccessContextResolver access,
    IPolicyEngine policy,
    IIngestionService service,
    CancellationToken ct) =>
{
    var auth = await AuthenticateAsync(http, access, allowTypeCredentials: false, ct);
    if (auth.Error is not null) return auth.Error;
    var context = auth.Context!;
    if (!await policy.IsAllowedAsync(context, "jornada.ingestao.status", null, null, ct)) return Results.Forbid();
    var result = await service.GetStatusAsync(context, entregaId, ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
}).RequireRateLimiting("standard");


// Correção governada de identidade: somente GESTOR institucional. A Jornada não autentica o agente humano da finalística.
app.MapPost("/api/v1/identidade/conflitos/detalhe", async (
    HttpRequest http, IdentityConflictDetailRequest request, IAccessContextResolver access, IPolicyEngine policy,
    IIdentityCorrectionService service, CancellationToken ct) =>
{
    var auth = await AuthenticateAsync(http, access, allowTypeCredentials: false, ct);
    if (auth.Error is not null) return auth.Error;
    var context = auth.Context!;
    if (!await policy.IsAllowedAsync(context, "jornada.identidade.conflitos.read", null, null, ct)) return Results.Forbid();
    try
    {
        var result = await service.GetConflictAsync(context, request, ct);
        if (result?.PessoaUuidAnteriormenteAssociada is Guid uuid) ApiAuditContext.SetPerson(http.HttpContext, uuid);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { erro = ex.Message }); }
}).RequireRateLimiting("identity");

app.MapPost("/api/v1/identidade/correcoes", async (
    HttpRequest http, IdentityCorrectionRequest request, IAccessContextResolver access, IPolicyEngine policy,
    IIdentityCorrectionService service, CancellationToken ct) =>
{
    var auth = await AuthenticateAsync(http, access, allowTypeCredentials: false, ct);
    if (auth.Error is not null) return auth.Error;
    var context = auth.Context!;
    if (!await policy.IsAllowedAsync(context, "jornada.identidade.corrigir", null, null, ct)) return Results.Forbid();
    var correlation = http.HttpContext.Items.TryGetValue(ApiContextItems.CorrelationId, out var value) && value is Guid id ? id : (Guid?)null;
    try
    {
        var result = await service.ApplyAsync(context, request, correlation, ct);
        ApiAuditContext.SetPerson(http.HttpContext, result.PessoaUuidTitular);
        return Results.Ok(result);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { erro = ex.Message }); }
    catch (SqlException ex) when (ex.Number is >= 51076 and <= 51085) { return Results.Conflict(new { erro = ex.Message }); }
}).RequireRateLimiting("identity");

// v3.44/v3.45: abertura manual/governada de conflito independente de CPF.
app.MapPost("/api/v1/identidade/casos", async (
    HttpRequest http, IdentityGovernedCaseOpenRequest request, IAccessContextResolver access, IPolicyEngine policy,
    IIdentityCorrectionService service, CancellationToken ct) =>
{
    var auth = await AuthenticateAsync(http, access, allowTypeCredentials: false, ct);
    if (auth.Error is not null) return auth.Error;
    var context = auth.Context!;
    if (!await policy.IsAllowedAsync(context, "jornada.identidade.corrigir", null, null, ct)) return Results.Forbid();
    var correlation = http.HttpContext.Items.TryGetValue(ApiContextItems.CorrelationId, out var value) && value is Guid id ? id : (Guid?)null;
    try { return Results.Ok(await service.OpenCaseAsync(context, request, correlation, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { erro = ex.Message }); }
    catch (SqlException ex) when (ex.Number is >= 51100 and <= 51109) { return Results.Conflict(new { erro = ex.Message }); }
}).RequireRateLimiting("identity");

app.MapPost("/api/v1/identidade/casos/{casoId:guid}/aplicar", async (
    HttpRequest http, Guid casoId, IdentityGovernedCaseApplyRequest request, IAccessContextResolver access, IPolicyEngine policy,
    IIdentityCorrectionService service, CancellationToken ct) =>
{
    var auth = await AuthenticateAsync(http, access, allowTypeCredentials: false, ct);
    if (auth.Error is not null) return auth.Error;
    var context = auth.Context!;
    if (!await policy.IsAllowedAsync(context, "jornada.identidade.corrigir", null, null, ct)) return Results.Forbid();
    var correlation = http.HttpContext.Items.TryGetValue(ApiContextItems.CorrelationId, out var value) && value is Guid id ? id : (Guid?)null;
    try { return Results.Ok(await service.ApplyCaseAsync(context, casoId, request, correlation, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { erro = ex.Message }); }
    catch (SqlException ex) when (ex.Number is >= 51110 and <= 51119) { return Results.Conflict(new { erro = ex.Message }); }
}).RequireRateLimiting("identity");

// Retorno ativo de divergências: a Jornada entrega o indício; o Gestor finalístico registra o desfecho.
app.MapGet("/api/v1/divergencias", async (
    HttpRequest http, int? limit, IAccessContextResolver access, IPolicyEngine policy,
    IIdentityCorrectionService service, CancellationToken ct) =>
{
    var auth = await AuthenticateAsync(http, access, allowTypeCredentials: false, ct);
    if (auth.Error is not null) return auth.Error;
    var context = auth.Context!;
    if (!await policy.IsAllowedAsync(context, "jornada.identidade.conflitos.read", null, null, ct)) return Results.Forbid();
    return Results.Ok(await service.ListDivergencesAsync(context, limit ?? 100, ct));
}).RequireRateLimiting("identity");

app.MapPost("/api/v1/divergencias/{divergenciaId:long}/desfecho", async (
    HttpRequest http, long divergenciaId, IdentityDivergenceDispositionRequest request, IAccessContextResolver access, IPolicyEngine policy,
    IIdentityCorrectionService service, CancellationToken ct) =>
{
    var auth = await AuthenticateAsync(http, access, allowTypeCredentials: false, ct);
    if (auth.Error is not null) return auth.Error;
    var context = auth.Context!;
    if (!await policy.IsAllowedAsync(context, "jornada.identidade.corrigir", null, null, ct)) return Results.Forbid();
    var correlation = http.HttpContext.Items.TryGetValue(ApiContextItems.CorrelationId, out var value) && value is Guid id ? id : (Guid?)null;
    try { await service.ResolveDivergenceAsync(context, divergenciaId, request, correlation, ct); return Results.NoContent(); }
    catch (ArgumentException ex) { return Results.BadRequest(new { erro = ex.Message }); }
    catch (SqlException ex) when (ex.Number is >= 51120 and <= 51129) { return Results.Conflict(new { erro = ex.Message }); }
}).RequireRateLimiting("identity");

// Pessoa: única família que admite credencial GESTOR, BENEFICIO ou SERVICO.
app.MapGet("/api/v1/pessoas/{pessoaUuid:guid}", async (
    HttpRequest http,
    Guid pessoaUuid,
    IAccessContextResolver access,
    IPolicyEngine policy,
    IContractResolver contracts,
    IPersonCanonicalResolver canonicalResolver,
    IPersonProjectionService service,
    CancellationToken ct) =>
{
    var auth = await AuthenticateAsync(http, access, allowTypeCredentials: true, ct);
    if (auth.Error is not null) return auth.Error;
    var context = auth.Context!;
    ApiAuditContext.SetResourceCode(http.HttpContext, context.TipoCodigo);
    var canonicalUuid = await canonicalResolver.ResolveAsync(pessoaUuid, ct);
    if (!canonicalUuid.HasValue) return Results.NotFound();
    ApiAuditContext.SetPersons(http.HttpContext, canonicalUuid.Value == pessoaUuid ? [pessoaUuid] : [pessoaUuid, canonicalUuid.Value]);
    if (!await policy.IsAllowedAsync(context, "jornada.pessoas.read", context.TipoCodigo, null, ct)) return Results.Forbid();
    if (!await policy.IsAllowedAsync(context, "jornada.pessoas.read", context.TipoCodigo, canonicalUuid.Value, ct)) return Results.NotFound();

    _ = await contracts.ResolvePersonSchemaAsync(context, ct); // selection is part of authorization, not a caller parameter.
    var result = await service.GetAsync(context, pessoaUuid, ct); // preserva no metadata o UUID originalmente solicitado.
    return result is null ? Results.NotFound() : Results.Ok(result);
}).RequireRateLimiting("person-query");

app.MapPost("/api/v1/pessoas/consulta", async (
    HttpRequest http,
    PersonBatchQueryRequest request,
    IAccessContextResolver access,
    IPolicyEngine policy,
    IContractResolver contracts,
    IPersonCanonicalResolver canonicalResolver,
    IPersonProjectionService service,
    CancellationToken ct) =>
{
    var auth = await AuthenticateAsync(http, access, allowTypeCredentials: true, ct);
    if (auth.Error is not null) return auth.Error;
    var context = auth.Context!;
    ApiAuditContext.SetResourceCode(http.HttpContext, context.TipoCodigo);
    if (request.PessoaUuids.Count is < 1 or > 1000) return Results.BadRequest(new { erro = "Informe de 1 a 1000 UUIDs." });
    if (request.PessoaUuids.Distinct().Count() != request.PessoaUuids.Count) return Results.BadRequest(new { erro = "UUIDs duplicados não são permitidos." });
    var canonicalByRequested = await canonicalResolver.ResolveManyAsync(request.PessoaUuids, ct);
    if (canonicalByRequested.Count != request.PessoaUuids.Count) return Results.NotFound();
    var canonicalUuids = request.PessoaUuids.Select(uuid => canonicalByRequested[uuid]).ToList();
    ApiAuditContext.SetPersons(http.HttpContext, request.PessoaUuids.Concat(canonicalUuids).Distinct().ToArray());
    if (!await policy.IsAllowedAsync(context, "jornada.pessoas.read", context.TipoCodigo, null, ct)) return Results.Forbid();
    if (!await policy.ArePersonsAllowedAsync(context, "jornada.pessoas.read", context.TipoCodigo, canonicalUuids.Distinct().ToArray(), ct)) return Results.NotFound();

    _ = await contracts.ResolvePersonSchemaAsync(context, ct);
    return Results.Ok(await service.GetManyAsync(context, request.PessoaUuids, ct));
}).RequireRateLimiting("person-query");

// Registros = Histórico da Jornada (Gold Benefícios + Gold Serviços). Contexto padrão é Gestor.
app.MapGet("/api/v1/pessoas/{pessoaUuid:guid}/registros", async (
    HttpRequest http, Guid pessoaUuid, string? natureza, string? codigo, DateOnly? desde, DateOnly? ate,
    IAccessContextResolver access, IPolicyEngine policy, IPersonCanonicalResolver canonicalResolver, IRegistrosQueryService service, CancellationToken ct) =>
{
    var auth = await AuthenticateAsync(http, access, allowTypeCredentials: false, ct);
    if (auth.Error is not null) return auth.Error;
    var context = auth.Context!;
    var filterError = ValidateQueryFilters(natureza, codigo, desde, ate);
    if (filterError is not null) return filterError;
    ApiAuditContext.SetResourceCode(http.HttpContext, codigo);
    var canonicalUuid = await canonicalResolver.ResolveAsync(pessoaUuid, ct);
    if (!canonicalUuid.HasValue) return Results.NotFound();
    ApiAuditContext.SetPersons(http.HttpContext, canonicalUuid.Value == pessoaUuid ? [pessoaUuid] : [pessoaUuid, canonicalUuid.Value]);
    if (!await policy.IsAllowedAsync(context, "jornada.registros.read", codigo, null, ct)) return Results.Forbid();
    if (!await policy.IsAllowedAsync(context, "jornada.registros.read", codigo, canonicalUuid.Value, ct)) return Results.NotFound();
    return Results.Ok(await service.GetRegistrosAsync(context, canonicalUuid.Value, natureza, codigo, desde, ate, ct));
}).RequireRateLimiting("person-query");

app.MapGet("/api/v1/pessoas/{pessoaUuid:guid}/beneficios-concedidos", async (
    HttpRequest http, Guid pessoaUuid, string? codigo, DateOnly? desde, DateOnly? ate,
    IAccessContextResolver access, IPolicyEngine policy, IPersonCanonicalResolver canonicalResolver, IRegistrosQueryService service, CancellationToken ct) =>
{
    var auth = await AuthenticateAsync(http, access, allowTypeCredentials: false, ct);
    if (auth.Error is not null) return auth.Error;
    var context = auth.Context!;
    var filterError = ValidateQueryFilters("BENEFICIO", codigo, desde, ate);
    if (filterError is not null) return filterError;
    ApiAuditContext.SetResourceCode(http.HttpContext, codigo);
    var canonicalUuid = await canonicalResolver.ResolveAsync(pessoaUuid, ct);
    if (!canonicalUuid.HasValue) return Results.NotFound();
    ApiAuditContext.SetPersons(http.HttpContext, canonicalUuid.Value == pessoaUuid ? [pessoaUuid] : [pessoaUuid, canonicalUuid.Value]);
    if (!await policy.IsAllowedAsync(context, "jornada.registros.read", codigo, null, ct)) return Results.Forbid();
    if (!await policy.IsAllowedAsync(context, "jornada.registros.read", codigo, canonicalUuid.Value, ct)) return Results.NotFound();
    return Results.Ok(await service.GetBeneficiosConcedidosAsync(context, canonicalUuid.Value, codigo, desde, ate, ct));
}).RequireRateLimiting("person-query");

app.MapGet("/api/v1/pessoas/{pessoaUuid:guid}/servicos-prestados", async (
    HttpRequest http, Guid pessoaUuid, string? codigo, DateOnly? desde, DateOnly? ate,
    IAccessContextResolver access, IPolicyEngine policy, IPersonCanonicalResolver canonicalResolver, IRegistrosQueryService service, CancellationToken ct) =>
{
    var auth = await AuthenticateAsync(http, access, allowTypeCredentials: false, ct);
    if (auth.Error is not null) return auth.Error;
    var context = auth.Context!;
    var filterError = ValidateQueryFilters("SERVICO", codigo, desde, ate);
    if (filterError is not null) return filterError;
    ApiAuditContext.SetResourceCode(http.HttpContext, codigo);
    var canonicalUuid = await canonicalResolver.ResolveAsync(pessoaUuid, ct);
    if (!canonicalUuid.HasValue) return Results.NotFound();
    ApiAuditContext.SetPersons(http.HttpContext, canonicalUuid.Value == pessoaUuid ? [pessoaUuid] : [pessoaUuid, canonicalUuid.Value]);
    if (!await policy.IsAllowedAsync(context, "jornada.registros.read", codigo, null, ct)) return Results.Forbid();
    if (!await policy.IsAllowedAsync(context, "jornada.registros.read", codigo, canonicalUuid.Value, ct)) return Results.NotFound();
    return Results.Ok(await service.GetServicosPrestadosAsync(context, canonicalUuid.Value, codigo, desde, ate, ct));
}).RequireRateLimiting("person-query");

app.MapGet("/api/v1/pessoas/{pessoaUuid:guid}/possibilidades", async (
    HttpRequest http, Guid pessoaUuid, string? natureza, string? codigo,
    IAccessContextResolver access, IPolicyEngine policy, IPersonCanonicalResolver canonicalResolver, IPossibilidadesQueryService service, CancellationToken ct) =>
{
    var auth = await AuthenticateAsync(http, access, allowTypeCredentials: false, ct);
    if (auth.Error is not null) return auth.Error;
    var context = auth.Context!;
    var filterError = ValidateQueryFilters(natureza, codigo, null, null);
    if (filterError is not null) return filterError;
    ApiAuditContext.SetResourceCode(http.HttpContext, codigo);
    var canonicalUuid = await canonicalResolver.ResolveAsync(pessoaUuid, ct);
    if (!canonicalUuid.HasValue) return Results.NotFound();
    ApiAuditContext.SetPersons(http.HttpContext, canonicalUuid.Value == pessoaUuid ? [pessoaUuid] : [pessoaUuid, canonicalUuid.Value]);
    if (!await policy.IsAllowedAsync(context, "jornada.possibilidades.read", codigo, null, ct)) return Results.Forbid();
    if (!await policy.IsAllowedAsync(context, "jornada.possibilidades.read", codigo, canonicalUuid.Value, ct)) return Results.NotFound();
    return Results.Ok(new
    {
        pessoaUuid = canonicalUuid.Value,
        pessoaUuidSolicitado = pessoaUuid,
        redirecionadoPorFusao = canonicalUuid.Value != pessoaUuid,
        sujeitoAnaliseDoGestorResponsavel = true,
        itens = await service.GetCompativeisAsync(context, canonicalUuid.Value, natureza, codigo, ct)
    });
}).RequireRateLimiting("person-query");

app.Run();


static async Task<AuthAttempt> AuthenticateAsync(
    HttpRequest http,
    IAccessContextResolver resolver,
    bool allowTypeCredentials,
    CancellationToken ct)
{
    var accessKey = http.Headers["X-Jornada-Access-Key"].ToString();
    if (string.IsNullOrWhiteSpace(accessKey)) return new(null, Results.Unauthorized());

    var gestor = http.Headers["X-Jornada-Gestor"].ToString();
    var beneficio = http.Headers["X-Jornada-Beneficio"].ToString();
    var servico = http.Headers["X-Jornada-Servico"].ToString();

    var supplied = new[] { gestor, beneficio, servico }.Count(x => !string.IsNullOrWhiteSpace(x));
    if (supplied != 1) return new(null, Results.BadRequest(new { erro = "Informe exatamente um código de credencial." }));

    PresentedAccessCredential credential;
    if (!string.IsNullOrWhiteSpace(beneficio))
    {
        if (!allowTypeCredentials) return new(null, Results.Forbid());
        credential = new PresentedAccessCredential(AccessCredentialType.BENEFICIO, beneficio, accessKey);
    }
    else if (!string.IsNullOrWhiteSpace(servico))
    {
        if (!allowTypeCredentials) return new(null, Results.Forbid());
        credential = new PresentedAccessCredential(AccessCredentialType.SERVICO, servico, accessKey);
    }
    else
    {
        credential = new PresentedAccessCredential(AccessCredentialType.GESTOR, gestor, accessKey);
    }

    var context = await resolver.ResolveAsync(credential, ct);
    if (context is null) return new(null, Results.Unauthorized());
    var credentialLimiter = http.HttpContext.RequestServices.GetRequiredService<AuthenticatedRateLimitGuard>();
    if (!credentialLimiter.TryAcquire(context, http)) return new(null, Results.StatusCode(StatusCodes.Status429TooManyRequests));
    http.HttpContext.Items[ApiContextItems.AccessContext] = context;
    return new(context, null);
}

static IResult? ValidateQueryFilters(string? natureza, string? codigo, DateOnly? desde, DateOnly? ate)
{
    if (!string.IsNullOrWhiteSpace(natureza)
        && !string.Equals(natureza, "BENEFICIO", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(natureza, "SERVICO", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { erro = "natureza deve ser BENEFICIO ou SERVICO." });

    if (!string.IsNullOrWhiteSpace(codigo)
        && !System.Text.RegularExpressions.Regex.IsMatch(codigo, "^[A-Za-z0-9]{4}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        return Results.BadRequest(new { erro = "codigo deve ter exatamente 4 caracteres alfanuméricos." });

    if (desde.HasValue && ate.HasValue && desde.Value > ate.Value)
        return Results.BadRequest(new { erro = "desde não pode ser posterior a ate." });

    return null;
}

file sealed record AuthAttempt(AccessContext? Context, IResult? Error);
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

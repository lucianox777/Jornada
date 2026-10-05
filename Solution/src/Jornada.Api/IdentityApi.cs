using Jornada.Access.Security;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Api;

public static class IdentityApi
{
    public static IEndpointRouteBuilder MapIdentityApi(this IEndpointRouteBuilder app)
    {
        // CPF vai no corpo. Esta consulta somente localiza UUID existente; não constitui Pessoa/UUID.
        app.MapPost("/api/v1/identidade/resolver", async (
            HttpRequest http,
            IdentityResolutionRequest request,
            
            IPolicyEngine policy,
            IIdentityResolutionService service,
            CancellationToken ct) =>
        {
            var context = http.HttpContext.RequireJornadaAccessContext();
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
        }).RequireRateLimiting("identity").RequireAuthorization("jornada.identidade.resolve");
        
        // Busca síncrona semicega: sem linkage_run, UUID, CPF ou score no payload.
        app.MapPost("/api/v1/identidade/candidatos", async (
            HttpRequest http,
            SemiblindIdentitySearchRequest request,
            IPolicyEngine policy,
            ISemiblindIdentitySearchService service,
            ISemiblindSearchActivationGate activation,
            IApiAuditSink auditSink,
            CancellationToken ct) =>
        {
            var context = http.HttpContext.RequireJornadaAccessContext();
            ApiAuditContext.SetResourceCode(http.HttpContext, context.TipoCodigo);
            if (!await policy.IsAllowedAsync(context, "jornada.identidade.busca.read", context.TipoCodigo, null, ct))
                return Results.Forbid();
            // Issue #539: a flag de Development não basta; verificar o perfil residente
            // Development no banco configurado ANTES de recuperar qualquer candidato.
            if (!await activation.IsEnabledAsync(ct))
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            var correlation = http.HttpContext.Items.TryGetValue(ApiContextItems.CorrelationId, out var value)
                && value is Guid id ? id : Guid.NewGuid();
            try
            {
                var result = await service.SearchAsync(context, request, correlation, ct);
                // Fail closed: persistir a consulta antes de disponibilizar qualquer candidato.
                // O middleware não grava um segundo evento após persistência bem-sucedida.
                http.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
                try
                {
                    await auditSink.PersistAsync(http.HttpContext, correlation, 0, CancellationToken.None);
                    http.HttpContext.Items[ApiContextItems.AuditAlreadyPersisted] = true;
                }
                catch
                {
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }
                return Results.Ok(result);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { erro = "Parâmetros de identidade inválidos." });
            }
            catch (InvalidOperationException)
            {
                // Modelo ativo ausente ou dependência operacional indisponível: não expor detalhes internos.
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireRateLimiting("identity")
          .RequireAuthorization("jornada.identidade.busca.read")
          .Produces<SemiblindIdentitySearchResponse>(StatusCodes.Status200OK)
          .Produces(StatusCodes.Status400BadRequest)
          .Produces(StatusCodes.Status401Unauthorized)
          .Produces(StatusCodes.Status403Forbidden)
          .Produces(StatusCodes.Status503ServiceUnavailable);
        
        
        // Correção governada de identidade: somente GESTOR institucional. A Jornada não autentica o agente humano da finalística.
        app.MapPost("/api/v1/identidade/conflitos/detalhe", async (
            HttpRequest http, IdentityConflictDetailRequest request,  IPolicyEngine policy,
            IIdentityCorrectionService service, CancellationToken ct) =>
        {
            var context = http.HttpContext.RequireJornadaAccessContext();
            if (!await policy.IsAllowedAsync(context, "jornada.identidade.conflitos.read", null, null, ct)) return Results.Forbid();
            try
            {
                var result = await service.GetConflictAsync(context, request, ct);
                if (result?.PessoaUuidAnteriormenteAssociada is Guid uuid) ApiAuditContext.SetPerson(http.HttpContext, uuid);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { erro = ex.Message }); }
        }).RequireRateLimiting("identity").RequireAuthorization("jornada.identidade.conflitos.read");
        
        app.MapPost("/api/v1/identidade/correcoes", async (
            HttpRequest http, IdentityCorrectionRequest request,  IPolicyEngine policy,
            IIdentityCorrectionService service, CancellationToken ct) =>
        {
            var context = http.HttpContext.RequireJornadaAccessContext();
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
        }).RequireRateLimiting("identity").RequireAuthorization("jornada.identidade.corrigir");
        
        // v3.44/v3.45: abertura manual/governada de conflito independente de CPF.
        app.MapPost("/api/v1/identidade/casos", async (
            HttpRequest http, IdentityGovernedCaseOpenRequest request,  IPolicyEngine policy,
            IIdentityCorrectionService service, CancellationToken ct) =>
        {
            var context = http.HttpContext.RequireJornadaAccessContext();
            if (!await policy.IsAllowedAsync(context, "jornada.identidade.corrigir", null, null, ct)) return Results.Forbid();
            var correlation = http.HttpContext.Items.TryGetValue(ApiContextItems.CorrelationId, out var value) && value is Guid id ? id : (Guid?)null;
            try { return Results.Ok(await service.OpenCaseAsync(context, request, correlation, ct)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { erro = ex.Message }); }
            catch (SqlException ex) when (ex.Number is >= 51100 and <= 51109) { return Results.Conflict(new { erro = ex.Message }); }
        }).RequireRateLimiting("identity").RequireAuthorization("jornada.identidade.corrigir");
        
        app.MapPost("/api/v1/identidade/casos/{casoId:guid}/aplicar", async (
            HttpRequest http, Guid casoId, IdentityGovernedCaseApplyRequest request,  IPolicyEngine policy,
            IIdentityCorrectionService service, CancellationToken ct) =>
        {
            var context = http.HttpContext.RequireJornadaAccessContext();
            if (!await policy.IsAllowedAsync(context, "jornada.identidade.corrigir", null, null, ct)) return Results.Forbid();
            var correlation = http.HttpContext.Items.TryGetValue(ApiContextItems.CorrelationId, out var value) && value is Guid id ? id : (Guid?)null;
            try { return Results.Ok(await service.ApplyCaseAsync(context, casoId, request, correlation, ct)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { erro = ex.Message }); }
            catch (SqlException ex) when (ex.Number is >= 51110 and <= 51119) { return Results.Conflict(new { erro = ex.Message }); }
        }).RequireRateLimiting("identity").RequireAuthorization("jornada.identidade.corrigir");
        
        // Retorno ativo de divergências: a Jornada entrega o indício; o Gestor finalístico registra o desfecho.
        app.MapGet("/api/v1/divergencias", async (
            HttpRequest http, int? limit,  IPolicyEngine policy,
            IIdentityCorrectionService service, CancellationToken ct) =>
        {
            var context = http.HttpContext.RequireJornadaAccessContext();
            if (!await policy.IsAllowedAsync(context, "jornada.identidade.conflitos.read", null, null, ct)) return Results.Forbid();
            return Results.Ok(await service.ListDivergencesAsync(context, limit ?? 100, ct));
        }).RequireRateLimiting("identity").RequireAuthorization("jornada.identidade.conflitos.read");
        
        app.MapPost("/api/v1/divergencias/{divergenciaId:long}/desfecho", async (
            HttpRequest http, long divergenciaId, IdentityDivergenceDispositionRequest request,  IPolicyEngine policy,
            IIdentityCorrectionService service, CancellationToken ct) =>
        {
            var context = http.HttpContext.RequireJornadaAccessContext();
            if (!await policy.IsAllowedAsync(context, "jornada.identidade.corrigir", null, null, ct)) return Results.Forbid();
            var correlation = http.HttpContext.Items.TryGetValue(ApiContextItems.CorrelationId, out var value) && value is Guid id ? id : (Guid?)null;
            try { await service.ResolveDivergenceAsync(context, divergenciaId, request, correlation, ct); return Results.NoContent(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { erro = ex.Message }); }
            catch (SqlException ex) when (ex.Number is >= 51120 and <= 51129) { return Results.Conflict(new { erro = ex.Message }); }
        }).RequireRateLimiting("identity").RequireAuthorization("jornada.identidade.corrigir");

        return app;
    }
}

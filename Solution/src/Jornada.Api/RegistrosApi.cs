using Jornada.Access.Security;
using Jornada.Contracts;

namespace Jornada.Api;

public static class RegistrosApi
{
    public static IEndpointRouteBuilder MapRegistrosApi(this IEndpointRouteBuilder app)
    {
        // Registros = Histórico da Jornada (Gold Benefícios + Gold Serviços). Contexto padrão é Gestor.
        app.MapGet("/api/v1/pessoas/{pessoaUuid:guid}/registros", async (
            HttpRequest http, Guid pessoaUuid, string? natureza, string? codigo, DateOnly? desde, DateOnly? ate,
             IPolicyEngine policy, IPersonCanonicalResolver canonicalResolver, IRegistrosQueryService service, CancellationToken ct) =>
        {
            var context = http.HttpContext.RequireJornadaAccessContext();
            var filterError = QueryFilterValidation.Validate(natureza, codigo, desde, ate);
            if (filterError is not null) return filterError;
            ApiAuditContext.SetResourceCode(http.HttpContext, codigo);
            var canonicalUuid = await canonicalResolver.ResolveAsync(pessoaUuid, ct);
            if (!canonicalUuid.HasValue) return Results.NotFound();
            ApiAuditContext.SetPersons(http.HttpContext, canonicalUuid.Value == pessoaUuid ? [pessoaUuid] : [pessoaUuid, canonicalUuid.Value]);
            if (!await policy.IsAllowedAsync(context, "jornada.registros.read", codigo, null, ct)) return Results.Forbid();
            if (!await policy.IsAllowedAsync(context, "jornada.registros.read", codigo, canonicalUuid.Value, ct)) return Results.NotFound();
            return Results.Ok(await service.GetRegistrosAsync(context, canonicalUuid.Value, natureza, codigo, desde, ate, ct));
        }).RequireRateLimiting("person-query").RequireAuthorization("jornada.registros.read");
        
        app.MapGet("/api/v1/pessoas/{pessoaUuid:guid}/beneficios-concedidos", async (
            HttpRequest http, Guid pessoaUuid, string? codigo, DateOnly? desde, DateOnly? ate,
             IPolicyEngine policy, IPersonCanonicalResolver canonicalResolver, IRegistrosQueryService service, CancellationToken ct) =>
        {
            var context = http.HttpContext.RequireJornadaAccessContext();
            var filterError = QueryFilterValidation.Validate("BENEFICIO", codigo, desde, ate);
            if (filterError is not null) return filterError;
            ApiAuditContext.SetResourceCode(http.HttpContext, codigo);
            var canonicalUuid = await canonicalResolver.ResolveAsync(pessoaUuid, ct);
            if (!canonicalUuid.HasValue) return Results.NotFound();
            ApiAuditContext.SetPersons(http.HttpContext, canonicalUuid.Value == pessoaUuid ? [pessoaUuid] : [pessoaUuid, canonicalUuid.Value]);
            if (!await policy.IsAllowedAsync(context, "jornada.registros.read", codigo, null, ct)) return Results.Forbid();
            if (!await policy.IsAllowedAsync(context, "jornada.registros.read", codigo, canonicalUuid.Value, ct)) return Results.NotFound();
            return Results.Ok(await service.GetBeneficiosConcedidosAsync(context, canonicalUuid.Value, codigo, desde, ate, ct));
        }).RequireRateLimiting("person-query").RequireAuthorization("jornada.registros.read");
        
        app.MapGet("/api/v1/pessoas/{pessoaUuid:guid}/servicos-prestados", async (
            HttpRequest http, Guid pessoaUuid, string? codigo, DateOnly? desde, DateOnly? ate,
             IPolicyEngine policy, IPersonCanonicalResolver canonicalResolver, IRegistrosQueryService service, CancellationToken ct) =>
        {
            var context = http.HttpContext.RequireJornadaAccessContext();
            var filterError = QueryFilterValidation.Validate("SERVICO", codigo, desde, ate);
            if (filterError is not null) return filterError;
            ApiAuditContext.SetResourceCode(http.HttpContext, codigo);
            var canonicalUuid = await canonicalResolver.ResolveAsync(pessoaUuid, ct);
            if (!canonicalUuid.HasValue) return Results.NotFound();
            ApiAuditContext.SetPersons(http.HttpContext, canonicalUuid.Value == pessoaUuid ? [pessoaUuid] : [pessoaUuid, canonicalUuid.Value]);
            if (!await policy.IsAllowedAsync(context, "jornada.registros.read", codigo, null, ct)) return Results.Forbid();
            if (!await policy.IsAllowedAsync(context, "jornada.registros.read", codigo, canonicalUuid.Value, ct)) return Results.NotFound();
            return Results.Ok(await service.GetServicosPrestadosAsync(context, canonicalUuid.Value, codigo, desde, ate, ct));
        }).RequireRateLimiting("person-query").RequireAuthorization("jornada.registros.read");

        return app;
    }
}

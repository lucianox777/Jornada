using Jornada.Access.Security;
using Jornada.Contracts;

namespace Jornada.Api;

public static class PossibilidadesApi
{
    public static IEndpointRouteBuilder MapPossibilidadesApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/pessoas/{pessoaUuid:guid}/possibilidades", async (
            HttpRequest http, Guid pessoaUuid, string? natureza, string? codigo,
             IPolicyEngine policy, IPersonCanonicalResolver canonicalResolver, IPossibilidadesQueryService service, CancellationToken ct) =>
        {
            var context = http.HttpContext.RequireJornadaAccessContext();
            var filterError = QueryFilterValidation.Validate(natureza, codigo, null, null);
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
        }).RequireRateLimiting("person-query").RequireAuthorization("jornada.possibilidades.read");

        return app;
    }
}

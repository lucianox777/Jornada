using Jornada.Access.Security;
using Jornada.Contracts;

namespace Jornada.Api;

public static class PersonApi
{
    public static IEndpointRouteBuilder MapPersonApi(this IEndpointRouteBuilder app)
    {
        // Pessoa: única família que admite credencial GESTOR, BENEFICIO ou SERVICO.
        app.MapGet("/api/v1/pessoas/{pessoaUuid:guid}", async (
            HttpRequest http,
            Guid pessoaUuid,
            
            IPolicyEngine policy,
            IContractResolver contracts,
            IPersonCanonicalResolver canonicalResolver,
            IPersonProjectionService service,
            CancellationToken ct) =>
        {
            var context = http.HttpContext.RequireJornadaAccessContext();
            ApiAuditContext.SetResourceCode(http.HttpContext, context.TipoCodigo);
            var canonicalUuid = await canonicalResolver.ResolveAsync(pessoaUuid, ct);
            if (!canonicalUuid.HasValue) return Results.NotFound();
            ApiAuditContext.SetPersons(http.HttpContext, canonicalUuid.Value == pessoaUuid ? [pessoaUuid] : [pessoaUuid, canonicalUuid.Value]);
            if (!await policy.IsAllowedAsync(context, "jornada.pessoas.read", context.TipoCodigo, null, ct)) return Results.Forbid();
            if (!await policy.IsAllowedAsync(context, "jornada.pessoas.read", context.TipoCodigo, canonicalUuid.Value, ct)) return Results.NotFound();
        
            _ = await contracts.ResolvePersonSchemaAsync(context, ct); // selection is part of authorization, not a caller parameter.
            var result = await service.GetAsync(context, pessoaUuid, ct); // preserva no metadata o UUID originalmente solicitado.
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireRateLimiting("person-query").RequireAuthorization("jornada.pessoas.read");
        
        app.MapPost("/api/v1/pessoas/consulta", async (
            HttpRequest http,
            PersonBatchQueryRequest request,
            
            IPolicyEngine policy,
            IContractResolver contracts,
            IPersonCanonicalResolver canonicalResolver,
            IPersonProjectionService service,
            CancellationToken ct) =>
        {
            var context = http.HttpContext.RequireJornadaAccessContext();
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
        }).RequireRateLimiting("person-query").RequireAuthorization("jornada.pessoas.read");

        return app;
    }
}

using Jornada.Access.Security;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using System.Text;

namespace Jornada.Api;

/// <summary>
/// Dedicated master-model presentation: a read-only Development preview. No
/// monitor credential, no production fallback, no mutation or synthetic promotion.
/// Future corporate master IdP and decision ledger need their own security review.
/// </summary>
public static class ModelGovernanceReadOnlyApi
{
    public const string Permission = "jornada.modelos.governanca.read";
    public const string PageRoute = "/governanca/modelos";
    public const string DataRoute = "/api/v1/governanca/modelos/visao";

    public static IEndpointRouteBuilder MapModelGovernanceReadOnlyApi(this IEndpointRouteBuilder app)
    {
        var environment = app.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        if (!environment.IsDevelopment())
            return app; // HML/Production: deny by absence until individual corporate identity exists.

        app.MapGet(PageRoute, (HttpContext http, IWebHostEnvironment host) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var webRoot = host.WebRootPath ?? Path.Combine(host.ContentRootPath, "wwwroot");
            var path = Path.Combine(webRoot, "governanca", "modelos", "index.html");
            return File.Exists(path)
                ? Results.File(path, "text/html; charset=utf-8")
                : Results.NotFound();
        });

        app.MapGet(DataRoute, async (
            HttpContext http,
            IPolicyEngine policy,
            IOperationalSqlAdapter sql,
            CancellationToken ct) =>
        {
            var context = http.RequireJornadaAccessContext();
            if (context.CredentialType != AccessCredentialType.GESTOR
                || !string.Equals(context.PublicCode, "MASTER_DEV", StringComparison.Ordinal)
                || !await policy.IsAllowedAsync(context, Permission, null, null, ct))
                return Results.Forbid();

            http.Response.Headers.CacheControl = "no-store";
            try
            {
                var view = await new ModelGovernanceReadOnlyService(sql).GetAsync(ct);
                return Results.Ok(view);
            }
            catch (SqlException ex) when (ex.Number is 207 or 208)
            {
                return Results.Json(new
                {
                    codigo = "GOVERNANCA_MODELOS_SCHEMA_INDISPONIVEL",
                    erro = "A estrutura de modelos e auditoria não está disponível."
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (InvalidDataException)
            {
                return Results.Json(new
                {
                    codigo = "GOVERNANCA_EVIDENCIA_INCONSISTENTE",
                    erro = "Evidência de modelo inconsistente; revisão manual necessária."
                }, statusCode: StatusCodes.Status409Conflict);
            }
            catch (InvalidOperationException)
            {
                return Results.Json(new
                {
                    codigo = "GOVERNANCA_MODELO_ALTERADO",
                    erro = "Estado dos modelos mudou durante a leitura; recarregue a comparação."
                }, statusCode: StatusCodes.Status409Conflict);
            }
        })
        .RequireRateLimiting("standard")
        .RequireAuthorization(Permission)
        .Produces<ModelGovernanceView>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        return app;
    }
}

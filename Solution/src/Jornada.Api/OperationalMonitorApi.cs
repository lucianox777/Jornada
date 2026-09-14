using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Api;

public static class OperationalMonitorApi
{
    public const string Permission = "jornada.monitor.read";
    public const string StatusRoute = "/api/v1/monitor/status";
    public const string PageRoute = "/monitor";

    public static IEndpointRouteBuilder MapOperationalMonitorApi(this IEndpointRouteBuilder app)
    {
        app.MapGet(PageRoute, (IWebHostEnvironment environment) =>
        {
            var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
            var page = Path.Combine(webRoot, "monitor", "index.html");
            return File.Exists(page)
                ? Results.File(page, "text/html; charset=utf-8")
                : Results.NotFound();
        });

        app.MapGet(StatusRoute, async (
            HttpRequest http,
            IAccessContextResolver access,
            IPolicyEngine policy,
            IOperationalSqlAdapter operationalSql,
            ApiReadinessProbe readiness,
            CancellationToken ct) =>
        {
            var key = http.Headers["X-Jornada-Access-Key"].ToString();
            if (string.IsNullOrWhiteSpace(key)) return Results.Unauthorized();

            var gestor = http.Headers["X-Jornada-Gestor"].ToString();
            if (string.IsNullOrWhiteSpace(gestor)
                || !string.IsNullOrWhiteSpace(http.Headers["X-Jornada-Beneficio"].ToString())
                || !string.IsNullOrWhiteSpace(http.Headers["X-Jornada-Servico"].ToString()))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var context = await access.ResolveAsync(
                new PresentedAccessCredential(AccessCredentialType.GESTOR, gestor, key), ct);
            if (context is null) return Results.Unauthorized();

            var limiter = http.HttpContext.RequestServices.GetRequiredService<AuthenticatedRateLimitGuard>();
            if (!limiter.TryAcquire(context, http))
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);

            http.HttpContext.Items[ApiContextItems.AccessContext] = context;
            if (context.CredentialType != AccessCredentialType.GESTOR
                || !await policy.IsAllowedAsync(context, Permission, null, null, ct))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            try
            {
                var monitor = await new OperationalMonitorService(operationalSql).GetAsync(ct);
                var ready = await readiness.CheckAsync(ct);
                return Results.Ok(new
                {
                    monitor,
                    readiness = new
                    {
                        ready = ready.Ready,
                        checks = ready.Checks
                    }
                });
            }
            catch (SqlException ex) when (OperationalMonitorService.IsSchemaUnavailable(ex))
            {
                return Results.Json(new
                {
                    codigo = "MONITOR_SCHEMA_INDISPONIVEL",
                    erro = "A migração do monitor operacional ainda não foi aplicada."
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireRateLimiting("standard");

        return app;
    }
}

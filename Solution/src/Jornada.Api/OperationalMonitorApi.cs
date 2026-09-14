using System.Text;
using System.Text.Json;
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
        app.MapGet(PageRoute, (IWebHostEnvironment environment, IConfiguration configuration) =>
        {
            var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
            var page = Path.Combine(webRoot, "monitor", "index.html");
            if (!File.Exists(page)) return Results.NotFound();

            if (!environment.IsDevelopment())
                return Results.File(page, "text/html; charset=utf-8");

            var gestor = configuration["OperationalMonitor:LocalAutoGestor"];
            var accessKey = configuration["OperationalMonitor:LocalAutoAccessKey"];
            if (string.IsNullOrWhiteSpace(gestor) || string.IsNullOrWhiteSpace(accessKey))
                return Results.File(page, "text/html; charset=utf-8");

            var html = File.ReadAllText(page, Encoding.UTF8);
            var scriptIndex = html.IndexOf("<script>", StringComparison.OrdinalIgnoreCase);
            if (scriptIndex < 0)
                return Results.File(page, "text/html; charset=utf-8");

            // Somente Development/Test recebe a credencial sintética do próprio perfil local.
            // O snapshot continua passando pela autenticação normal de /api/v1/monitor/status.
            var bootstrap =
                "<script>\n" +
                "(() => {\n" +
                "  const gestor = " + JsonSerializer.Serialize(gestor) + ";\n" +
                "  const accessKey = " + JsonSerializer.Serialize(accessKey) + ";\n" +
                "  sessionStorage.setItem('jornada.monitor.gestor', gestor);\n" +
                "  sessionStorage.setItem('jornada.monitor.key', accessKey);\n" +
                "})();\n" +
                "</script>\n";

            html = html.Insert(scriptIndex, bootstrap);
            return Results.Text(html, "text/html; charset=utf-8", Encoding.UTF8);
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

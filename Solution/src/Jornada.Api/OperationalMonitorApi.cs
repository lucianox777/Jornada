using Jornada.Access.Security;
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
    public const string SyntheticStatusRoute = "/api/v1/monitor/synthetic";
    public const string SyntheticPageRoute = "/monitor/synthetic";

    public static IEndpointRouteBuilder MapOperationalMonitorApi(this IEndpointRouteBuilder app)
    {
        var hostEnvironment = app.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var operationalSql = app.ServiceProvider.GetRequiredService<IOperationalSqlAdapter>();
        var syntheticDevelopment =
            hostEnvironment.IsDevelopment()
            && SyntheticOperationalMonitorGate.IsResidentDevelopment(operationalSql);

        app.MapGet(PageRoute, (IWebHostEnvironment environment, IConfiguration configuration) =>
        {
            var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
            var page = Path.Combine(webRoot, "monitor", "index.html");
            if (!File.Exists(page)) return Results.NotFound();

            if (!syntheticDevelopment && !environment.IsDevelopment())
                return Results.File(page, "text/html; charset=utf-8");

            var html = File.ReadAllText(page, Encoding.UTF8);
            if (syntheticDevelopment)
            {
                const string syntheticBlock =
                    "<aside id=\"synthetic-monitor-dev\" style=\"margin:16px;padding:12px;border:1px solid #d6b756;background:#fff3cd\">" +
                    "<strong>SINTÉTICO — NÃO PROMOVÍVEL</strong> · " +
                    "<a href=\"/monitor/synthetic\">abrir avaliação sintética DEV</a></aside>";
                var bodyEnd = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                html = bodyEnd >= 0 ? html.Insert(bodyEnd, syntheticBlock) : html + syntheticBlock;
            }

            if (!environment.IsDevelopment())
                return Results.Text(html, "text/html; charset=utf-8", Encoding.UTF8);

            var gestor = configuration["OperationalMonitor:LocalAutoGestor"];
            var accessKey = configuration["OperationalMonitor:LocalAutoAccessKey"];
            if (string.IsNullOrWhiteSpace(gestor) || string.IsNullOrWhiteSpace(accessKey))
                return Results.Text(html, "text/html; charset=utf-8", Encoding.UTF8);

            var scriptIndex = html.IndexOf("<script>", StringComparison.OrdinalIgnoreCase);
            if (scriptIndex < 0)
                return Results.Text(html, "text/html; charset=utf-8", Encoding.UTF8);

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
            
            IPolicyEngine policy,
            IOperationalSqlAdapter operationalSql,
            ApiReadinessProbe readiness,
            CancellationToken ct) =>
        {
            var context = http.HttpContext.RequireJornadaAccessContext();
            if (!await policy.IsAllowedAsync(context, Permission, null, null, ct)) return Results.Forbid();

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
        }).RequireRateLimiting("standard").RequireAuthorization(Permission);

        if (syntheticDevelopment)
        {
            app.MapGet(SyntheticPageRoute, () =>
                Results.Text(
                    SyntheticOperationalMonitorPage.Html,
                    "text/html; charset=utf-8",
                    Encoding.UTF8));

            app.MapGet(SyntheticStatusRoute, async (
                HttpRequest http,
                
                IPolicyEngine policy,
                IOperationalSqlAdapter operationalSql,
                CancellationToken ct) =>
            {
                var context = http.HttpContext.RequireJornadaAccessContext();
            if (!await policy.IsAllowedAsync(context, Permission, null, null, ct)) return Results.Forbid();

                try
                {
                    var monitor = await new SyntheticOperationalMonitorService(operationalSql).GetAsync(ct);
                    return Results.Ok(monitor);
                }
                catch (SqlException ex) when (OperationalMonitorService.IsSchemaUnavailable(ex))
                {
                    return Results.Json(new
                    {
                        codigo = "MONITOR_SINTETICO_SCHEMA_INDISPONIVEL",
                        erro = "O ledger de avaliação sintética ainda não foi instalado."
                    }, statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            }).RequireRateLimiting("standard").RequireAuthorization(Permission);
        }

        return app;
    }

}

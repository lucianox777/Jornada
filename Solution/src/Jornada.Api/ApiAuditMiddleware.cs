using Jornada.Contracts;
using System.Diagnostics;
using Microsoft.AspNetCore.Routing;

namespace Jornada.Api;

internal static class ApiContextItems
{
    public const string AccessContext = "Jornada.AccessContext";
    public const string CorrelationId = "Jornada.CorrelationId";
    public const string ResourceCode = "Jornada.ResourceCode";
    public const string PersonIds = "Jornada.PersonIds";
    public const string AgentCpfHash = "Jornada.AgentCpfHash";
    public const string AgentCpfHashVersion = "Jornada.AgentCpfHashVersion";
}

internal static class ApiAuditContext
{
    public static void SetResourceCode(HttpContext http, string? resourceCode)
    {
        if (!string.IsNullOrWhiteSpace(resourceCode)) http.Items[ApiContextItems.ResourceCode] = resourceCode;
    }
    public static void SetPerson(HttpContext http, Guid pessoaUuid) => SetPersons(http, [pessoaUuid]);
    public static void SetPersons(HttpContext http, IEnumerable<Guid> pessoaUuids) =>
        http.Items[ApiContextItems.PersonIds] = pessoaUuids.Distinct().Take(1000).ToArray();
}

internal sealed class ApiAuditMiddleware(RequestDelegate next, ILogger<ApiAuditMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext http, IApiAuditSink auditSink, AgentCpfPseudonymizer agentCpf)
    {
        var correlationId = TryCorrelationId(http.Request.Headers["X-Correlation-Id"].ToString()) ?? Guid.NewGuid();
        http.Items[ApiContextItems.CorrelationId] = correlationId;
        http.Response.Headers["X-Correlation-Id"] = correlationId.ToString();

        var sw = Stopwatch.StartNew();
        try
        {
            var rawAgentCpf = http.Request.Headers["X-Jornada-Agente-CPF"].ToString().Trim();
            // Remove o CPF da coleção de headers antes de seguir o pipeline para reduzir risco de log/trace acidental em texto claro.
            http.Request.Headers.Remove("X-Jornada-Agente-CPF");
            if (!string.IsNullOrWhiteSpace(rawAgentCpf))
            {
                var normalized = CpfRules.NormalizeAndValidate(rawAgentCpf);
                if (normalized is null)
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await http.Response.WriteAsJsonAsync(new { erro = "X-Jornada-Agente-CPF inválido." });
                    return;
                }

                try
                {
                    http.Items[ApiContextItems.AgentCpfHash] = agentCpf.ComputeHash(normalized);
                    http.Items[ApiContextItems.AgentCpfHashVersion] = agentCpf.KeyVersion;
                }
                catch (InvalidOperationException)
                {
                    http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await http.Response.WriteAsJsonAsync(new { erro = "Pseudonimização do identificador de agente indisponível." });
                    return;
                }
            }
            await next(http);
        }
        finally
        {
            sw.Stop();
            var telemetryEndpoint = http.GetEndpoint() as RouteEndpoint;
            var telemetryRoute = telemetryEndpoint?.RoutePattern.RawText ?? http.Request.Path.Value ?? "/";
            JornadaTelemetry.RecordApiRequest(sw.Elapsed.TotalMilliseconds, telemetryRoute, http.Request.Method, http.Response.StatusCode);
            try
            {
                var auditSw = Stopwatch.StartNew();
                await auditSink.PersistAsync(http, correlationId, sw.ElapsedMilliseconds, CancellationToken.None);
                auditSw.Stop();
                JornadaTelemetry.RecordApiAuditPersistence(auditSw.Elapsed.TotalMilliseconds);
                logger.LogInformation(
                    "API audit persisted. CorrelationId={CorrelationId}; AuditPersistenceMs={AuditPersistenceMs}",
                    correlationId, auditSw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                // Auditoria não pode derrubar uma resposta já produzida. Não registrar headers, body, CPF nem access key.
                logger.LogWarning(ex, "Falha ao persistir evento de auditoria da API. CorrelationId={CorrelationId}", correlationId);
            }
        }
    }

    private static Guid? TryCorrelationId(string raw) => Guid.TryParse(raw, out var id) ? id : null;
}

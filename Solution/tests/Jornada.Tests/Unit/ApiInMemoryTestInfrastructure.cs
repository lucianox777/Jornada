using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using Jornada.Api;

namespace Jornada.Tests.Unit;

/// <summary>
/// Doubles de infraestrutura para testes de pipeline HTTP/OpenAPI. Nenhum destes testes abre SQL Server;
/// os testes de integração continuam responsáveis por validar DDL, transações, applocks e procedures reais.
/// </summary>
internal sealed class InMemoryApiAuditSink : IApiAuditSink
{
    public ConcurrentQueue<(Guid CorrelationId, string Method, string Path, int StatusCode)> Events { get; } = new();

    public Task PersistAsync(HttpContext http, Guid correlationId, long elapsedMs, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Events.Enqueue((correlationId, http.Request.Method, http.Request.Path.Value ?? "/", http.Response.StatusCode));
        return Task.CompletedTask;
    }
}

internal sealed class InMemorySqlReadinessProbe(bool ready, string? code = null) : ISqlReadinessProbe
{
    public Task<ApiReadinessCheck> CheckAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ready
            ? new ApiReadinessCheck("sql", true)
            : new ApiReadinessCheck("sql", false, code ?? "SQL_INDISPONIVEL_TESTE_EM_MEMORIA"));
    }
}

using System.Data;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Api;

/// <summary>Leitura da identidade progressiva, sem criação ou alteração de UUID.</summary>
public interface IProgressiveOriginQueryService
{
    Task<ProgressiveOriginQueryResponse?> GetAsync(
        AccessContext context, ProgressiveOriginQueryRequest request, CancellationToken ct);
}

public sealed class SqlProgressiveOriginQueryService(IOperationalSqlAdapter connections) : IProgressiveOriginQueryService
{
    public async Task<ProgressiveOriginQueryResponse?> GetAsync(
        AccessContext context, ProgressiveOriginQueryRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        if (context.CredentialType != AccessCredentialType.GESTOR ||
            !context.Scopes.Contains(ProgressiveOriginApi.Permission, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Consulta de origem não autorizada.");
        ProgressiveOriginApi.ValidateRequest(request);
        if (string.IsNullOrWhiteSpace(context.GestorCodigo) || context.GestorCodigo.Length > 80)
            throw new UnauthorizedAccessException("Gestor inválido.");

        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        // A restrição de proprietário é aplicada no SQL, não apenas na borda.
        // O namespace da origem e o Gestor autenticado são partes obrigatórias da seleção.
        // Comparações binárias impedem que uma chave semelhante seja confundida com outra.
        command.CommandText = """
            SELECT p.sistema_origem_codigo,p.codigo_pessoa_origem,
                   p.initial_uuid,p.canonical_uuid,p.estado,p.versao,
                   p.criado_em,p.atualizado_em,p.ultima_resolucao_em
              FROM serving.v_identidade_origem_progressiva p
             WHERE p.gestor_codigo COLLATE Latin1_General_100_BIN2=@gestor
               AND p.sistema_origem_codigo COLLATE Latin1_General_100_BIN2=@sistema
               AND p.codigo_pessoa_origem COLLATE Latin1_General_100_BIN2=@codigo;
            """;
        command.Parameters.Add(new SqlParameter("@gestor", SqlDbType.NVarChar, 80) { Value = context.GestorCodigo });
        command.Parameters.Add(new SqlParameter("@sistema", SqlDbType.NVarChar, 80) { Value = request.CodigoSistemaOrigem });
        command.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 255) { Value = request.CodigoPessoaOrigem });
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var text = reader.GetString(4);
        if (!Enum.TryParse<ProgressiveIdentityStatus>(text, false, out var estado) || !Enum.IsDefined(estado))
            throw new InvalidOperationException("Estado progressivo desconhecido.");
        var result = new ProgressiveOriginQueryResponse(
            reader.GetString(0), reader.GetString(1), reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3), estado, reader.GetInt64(5),
            reader.GetDateTimeOffset(6), reader.GetDateTimeOffset(7),
            reader.IsDBNull(8) ? null : reader.GetDateTimeOffset(8));
        ProgressiveOriginApi.ValidateSnapshot(result);
        if (await reader.ReadAsync(ct))
            throw new InvalidOperationException("Identidade de origem duplicada.");
        return result;
    }
}

public static class ProgressiveOriginApi
{
    public const string Permission = "jornada.identidade.origem.read";
    public const string Route = "/api/v1/identidade/origens/consulta";

    public static IEndpointRouteBuilder MapProgressiveOriginApi(this IEndpointRouteBuilder app)
    {
        app.MapPost(Route, async (
            HttpRequest http, ProgressiveOriginQueryRequest request,
            IAccessContextResolver access, IPolicyEngine policy,
            IProgressiveOriginQueryService service, CancellationToken ct) =>
        {
            // Mesma autenticação, contexto de auditoria e limite autenticado da API.
            // Esta família aceita exclusivamente GESTOR, mesmo se um scope for concedido a um Tipo por erro.
            var key = http.Headers["X-Jornada-Access-Key"].ToString();
            if (string.IsNullOrWhiteSpace(key)) return Results.Unauthorized();
            var gestor = http.Headers["X-Jornada-Gestor"].ToString();
            if (string.IsNullOrWhiteSpace(gestor) ||
                !string.IsNullOrWhiteSpace(http.Headers["X-Jornada-Beneficio"].ToString()) ||
                !string.IsNullOrWhiteSpace(http.Headers["X-Jornada-Servico"].ToString()))
                return Results.Forbid();
            var context = await access.ResolveAsync(
                new PresentedAccessCredential(AccessCredentialType.GESTOR, gestor, key), ct);
            if (context is null) return Results.Unauthorized();
            var limiter = http.HttpContext.RequestServices.GetRequiredService<AuthenticatedRateLimitGuard>();
            if (!limiter.TryAcquire(context, http)) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            http.HttpContext.Items[ApiContextItems.AccessContext] = context;
            if (context.CredentialType != AccessCredentialType.GESTOR ||
                !await policy.IsAllowedAsync(context, Permission, null, null, ct))
                return Results.Forbid();
            // O código interno nunca é colocado na URL, em erros ou no resourceCode da auditoria.
            if (!TryValidateRequest(request))
                return Results.BadRequest(new { erro = "Códigos de origem inválidos." });
            try
            {
                var result = await service.GetAsync(context, request, ct);
                if (result is null) return Results.NotFound();
                ValidateSnapshot(result);
                ApiAuditContext.SetPersons(http.HttpContext,
                    result.CanonicalUuid is { } canonical && canonical != result.InitialUuid
                        ? [result.InitialUuid, canonical] : [result.InitialUuid]);
                return Results.Ok(result);
            }
            catch (SqlException ex) when (ex.Number is 207 or 208)
            {
                return Results.Json(new { codigo = "IDENTIDADE_PROGRESSIVA_INDISPONIVEL" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireRateLimiting("identity");
        return app;
    }

    public static bool TryValidateRequest(ProgressiveOriginQueryRequest? request) =>
        request is not null &&
        ValidCode(request.CodigoSistemaOrigem, 80) &&
        ValidCode(request.CodigoPessoaOrigem, 255);

    private static bool ValidCode(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= max &&
        !value.Any(char.IsControl);

    public static void ValidateRequest(ProgressiveOriginQueryRequest request)
    {
        if (!TryValidateRequest(request)) throw new ArgumentException("Códigos de origem inválidos.", nameof(request));
    }

    public static void ValidateSnapshot(ProgressiveOriginQueryResponse result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var consistent = result.InitialUuid != Guid.Empty && result.CanonicalUuid != Guid.Empty &&
            Enum.IsDefined(result.Estado) && result.Versao >= 0 &&
            result.CriadoEm != default && result.CriadoEm.Offset == TimeSpan.Zero &&
            result.AtualizadoEm.Offset == TimeSpan.Zero && result.AtualizadoEm >= result.CriadoEm &&
            (result.UltimaResolucaoEm is null ||
             (result.UltimaResolucaoEm.Value.Offset == TimeSpan.Zero && result.UltimaResolucaoEm >= result.CriadoEm)) &&
            (result.Estado == ProgressiveIdentityStatus.PROVISORIA &&
                 result.Versao == 0 && result.CanonicalUuid is null && result.UltimaResolucaoEm is null ||
             result.Estado == ProgressiveIdentityStatus.REFERENCIA &&
                 result.Versao > 0 && result.CanonicalUuid is not null && result.UltimaResolucaoEm is not null ||
             result.Estado == ProgressiveIdentityStatus.INDEFINIDA &&
                 result.Versao > 0 && result.CanonicalUuid is null && result.UltimaResolucaoEm is not null);
        if (!consistent) throw new InvalidOperationException("Estado progressivo inconsistente.");
    }
}

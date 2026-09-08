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
        if (context.CredentialType != AccessCredentialType.GESTOR)
            throw new UnauthorizedAccessException("Consulta de origem exige credencial GESTOR.");
        ProgressiveOriginApi.ValidateRequest(request);

        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        // A restrição de proprietário é aplicada no SQL, não apenas na borda.
        // Não consultar por UUID, nome ou CPF: o namespace técnico da origem
        // e o Gestor autenticado são partes obrigatórias da seleção.
        command.CommandText = """
            SELECT p.sistema_origem_codigo,p.codigo_pessoa_origem,
                   p.initial_uuid,p.canonical_uuid,p.estado,p.versao,
                   p.criado_em,p.atualizado_em,p.ultima_resolucao_em
              FROM serving.v_identidade_origem_progressiva p
             WHERE p.gestor_codigo=@gestor
               AND p.sistema_origem_codigo=@sistema
               AND p.codigo_pessoa_origem=@codigo;
            """;
        command.Parameters.Add(new SqlParameter("@gestor", SqlDbType.NVarChar, 80) { Value = context.GestorCodigo });
        command.Parameters.Add(new SqlParameter("@sistema", SqlDbType.NVarChar, 80) { Value = request.CodigoSistemaOrigem });
        command.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 255) { Value = request.CodigoPessoaOrigem });
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var initial = reader.GetGuid(2);
        var canonical = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3);
        var text = reader.GetString(4);
        if (!Enum.TryParse<ProgressiveIdentityStatus>(text, false, out var estado) || !Enum.IsDefined(estado))
            throw new InvalidOperationException("Estado progressivo desconhecido.");
        var versao = reader.GetInt64(5);
        var ultima = reader.IsDBNull(8) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(8);
        if (initial == Guid.Empty || canonical == Guid.Empty || versao < 0 ||
            (estado == ProgressiveIdentityStatus.PROVISORIA && (versao != 0 || canonical is not null || ultima is not null)) ||
            (estado == ProgressiveIdentityStatus.REFERENCIA && (versao == 0 || canonical is null || ultima is null)) ||
            (estado == ProgressiveIdentityStatus.INDEFINIDA && (versao == 0 || canonical is not null || ultima is null)))
            throw new InvalidOperationException("Estado progressivo inconsistente.");
        var result = new ProgressiveOriginQueryResponse(
            reader.GetString(0), reader.GetString(1), initial, canonical, estado, versao,
            reader.GetDateTimeOffset(6), reader.GetDateTimeOffset(7), ultima);
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
            // A família de origem aceita exclusivamente GESTOR; chaves de Tipo
            // não podem usar este endpoint, mesmo se receberem o scope por erro.
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
            // Nunca registrar o código interno em URL, texto de erro ou resourceCode.
            // A propriedade do sistema é verificada novamente no SELECT.
            if (!TryValidateRequest(request))
                return Results.BadRequest(new { erro = "Códigos de origem inválidos." });
            var result = await service.GetAsync(context, request, ct);
            if (result is null) return Results.NotFound();
            ApiAuditContext.SetPersons(http.HttpContext,
                result.CanonicalUuid is { } canonical && canonical != result.InitialUuid
                    ? [result.InitialUuid, canonical] : [result.InitialUuid]);
            return Results.Ok(result);
        }).RequireRateLimiting("identity");
        return app;
    }

    public static bool TryValidateRequest(ProgressiveOriginQueryRequest? request) =>
        request is not null &&
        !string.IsNullOrWhiteSpace(request.CodigoSistemaOrigem) &&
        request.CodigoSistemaOrigem.Length <= 80 &&
        !string.IsNullOrWhiteSpace(request.CodigoPessoaOrigem) &&
        request.CodigoPessoaOrigem.Length <= 255;

    public static void ValidateRequest(ProgressiveOriginQueryRequest request)
    {
        if (!TryValidateRequest(request)) throw new ArgumentException("Códigos de origem inválidos.", nameof(request));
    }
}

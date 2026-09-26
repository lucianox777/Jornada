using System.Security.Claims;
using System.Text.Encodings.Web;
using Jornada.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jornada.Access.Security;

/// <summary>Parser e esquema compartilhados. A chave nunca é incluída em claims, mensagens ou auditoria.</summary>
public static class JornadaAccessSecurity
{
    public const string Scheme = "JornadaAccessKey";
    public const string AccessContextItem = "Jornada.AccessContext";
    public const string RejectStatusItem = "Jornada.Authentication.RejectStatus";
    public const string RejectMessageItem = "Jornada.Authentication.RejectMessage";

    private static readonly (string Permission, bool AllowType)[] Permissions =
    [
        ("jornada.identidade.resolve", true),
        ("jornada.identidade.origem.read", false),
        ("jornada.identidade.conflitos.read", false),
        ("jornada.identidade.corrigir", false),
        ("jornada.ingestao.write", false),
        ("jornada.ingestao.status", false),
        ("jornada.monitor.read", false),
        ("jornada.pessoas.read", true),
        ("jornada.registros.read", false),
        ("jornada.possibilidades.read", false)
    ];

    public static IServiceCollection AddJornadaAccessSecurity(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = Scheme;
            options.DefaultChallengeScheme = Scheme;
            options.DefaultForbidScheme = Scheme;
        }).AddScheme<AuthenticationSchemeOptions, JornadaAccessAuthenticationHandler>(Scheme, _ => { });

        services.AddAuthorization(options =>
        {
            foreach (var (permission, allowType) in Permissions)
                options.AddPolicy(permission, new AuthorizationPolicyBuilder(Scheme)
                    .RequireAuthenticatedUser()
                    .AddRequirements(new JornadaScopeRequirement(permission, allowType))
                    .Build());
        });
        services.AddSingleton<IAuthorizationHandler, JornadaScopeAuthorizationHandler>();
        return services;
    }

    public static AccessContext RequireJornadaAccessContext(this HttpContext http) =>
        http.Items.TryGetValue(AccessContextItem, out var value) && value is AccessContext context
            ? context : throw new InvalidOperationException("Endpoint sem contexto autenticado.");
}

public readonly record struct JornadaHeaderParse(
    PresentedAccessCredential? Credential, int? RejectStatus, string? RejectMessage);

public static class JornadaCredentialHeaderParser
{
    public static JornadaHeaderParse Parse(IHeaderDictionary headers)
    {
        if (headers["X-Jornada-Access-Key"].Count != 1
            || string.IsNullOrWhiteSpace(headers["X-Jornada-Access-Key"].ToString()))
            return new(null, StatusCodes.Status401Unauthorized, null);

        if (headers["X-Jornada-Gestor"].Count > 1 || headers["X-Jornada-Beneficio"].Count > 1
            || headers["X-Jornada-Servico"].Count > 1)
            return new(null, StatusCodes.Status400BadRequest, "Informe exatamente um código de credencial.");

        var gestor = headers["X-Jornada-Gestor"].ToString();
        var beneficio = headers["X-Jornada-Beneficio"].ToString();
        var servico = headers["X-Jornada-Servico"].ToString();
        if (new[] { gestor, beneficio, servico }.Count(x => !string.IsNullOrWhiteSpace(x)) != 1)
            return new(null, StatusCodes.Status400BadRequest, "Informe exatamente um código de credencial.");

        var type = !string.IsNullOrWhiteSpace(beneficio) ? AccessCredentialType.BENEFICIO
            : !string.IsNullOrWhiteSpace(servico) ? AccessCredentialType.SERVICO
            : AccessCredentialType.GESTOR;
        var code = type switch
        {
            AccessCredentialType.BENEFICIO => beneficio,
            AccessCredentialType.SERVICO => servico,
            _ => gestor
        };
        return new(new PresentedAccessCredential(type, code, headers["X-Jornada-Access-Key"].ToString()), null, null);
    }
}

public sealed record JornadaAccessVerification(AccessContext? Context, int? RejectionStatus = null, string? Message = null)
{
    public static JornadaAccessVerification Accepted(AccessContext context) => new(context);
    public static JornadaAccessVerification Rejected(int status, string? message = null) => new(null, status, message);
}

public interface IJornadaAccessVerifier
{
    Task<JornadaAccessVerification> VerifyAsync(
        HttpContext http, PresentedAccessCredential credential, CancellationToken ct);
}

public sealed class JornadaAccessAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IJornadaAccessVerifier verifier)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var parsed = JornadaCredentialHeaderParser.Parse(Request.Headers);
        if (parsed.Credential is null)
        {
            Reject(parsed.RejectStatus ?? StatusCodes.Status401Unauthorized, parsed.RejectMessage);
            return AuthenticateResult.Fail("Credencial ausente ou cabeçalhos inválidos.");
        }

        var verification = await verifier.VerifyAsync(Context, parsed.Credential, Context.RequestAborted);
        if (verification.Context is null)
        {
            Reject(verification.RejectionStatus ?? StatusCodes.Status401Unauthorized, verification.Message);
            return AuthenticateResult.Fail("Credencial não validada.");
        }

        Context.Items[JornadaAccessSecurity.AccessContextItem] = verification.Context;
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, verification.Context.CredentialId.ToString("N")),
            new Claim("jornada.credential_type", verification.Context.CredentialType.ToString()),
            new Claim("jornada.public_code", verification.Context.PublicCode)
        };
        return AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, JornadaAccessSecurity.Scheme)),
            JornadaAccessSecurity.Scheme));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var status = Context.Items.TryGetValue(JornadaAccessSecurity.RejectStatusItem, out var raw)
            && raw is int code ? code : StatusCodes.Status401Unauthorized;
        Response.StatusCode = status;
        if (status == StatusCodes.Status400BadRequest)
        {
            var message = Context.Items.TryGetValue(JornadaAccessSecurity.RejectMessageItem, out var value)
                ? value as string : null;
            await Response.WriteAsJsonAsync(new { erro = message ?? "Credencial inválida." });
        }
    }

    private void Reject(int status, string? message)
    {
        Context.Items[JornadaAccessSecurity.RejectStatusItem] = status;
        if (message is not null) Context.Items[JornadaAccessSecurity.RejectMessageItem] = message;
    }
}

public sealed record JornadaScopeRequirement(string Permission, bool AllowType) : IAuthorizationRequirement;

public sealed class JornadaScopeAuthorizationHandler : AuthorizationHandler<JornadaScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, JornadaScopeRequirement requirement)
    {
        if (context.Resource is not HttpContext http
            || !http.Items.TryGetValue(JornadaAccessSecurity.AccessContextItem, out var raw)
            || raw is not AccessContext accessContext)
            return Task.CompletedTask;

        if ((!requirement.AllowType && accessContext.CredentialType != AccessCredentialType.GESTOR)
            || !accessContext.Scopes.Contains(requirement.Permission, StringComparer.OrdinalIgnoreCase))
            return Task.CompletedTask;

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

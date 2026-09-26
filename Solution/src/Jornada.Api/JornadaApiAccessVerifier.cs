using Jornada.Access.Security;
using Jornada.Contracts;

namespace Jornada.Api;

/// <summary>Resolve a chave uma única vez e preserva o limite institucional antes dos scopes.</summary>
internal sealed class JornadaApiAccessVerifier(
    IAccessContextResolver resolver, AuthenticatedRateLimitGuard limiter) : IJornadaAccessVerifier
{
    public async Task<JornadaAccessVerification> VerifyAsync(
        HttpContext http, PresentedAccessCredential credential, CancellationToken ct)
    {
        var context = await resolver.ResolveAsync(credential, ct);
        if (context is null)
            return JornadaAccessVerification.Rejected(StatusCodes.Status401Unauthorized);
        if (!limiter.TryAcquire(context, http.Request))
            return JornadaAccessVerification.Rejected(StatusCodes.Status429TooManyRequests);
        return JornadaAccessVerification.Accepted(context);
    }
}

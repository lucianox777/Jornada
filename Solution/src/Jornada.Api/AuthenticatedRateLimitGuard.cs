using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Jornada.Contracts;

namespace Jornada.Api;

/// <summary>Segunda camada de rate limit, aplicada somente depois de autenticar a credencial.</summary>
internal sealed class AuthenticatedRateLimitGuard(ApiRateLimitOptions options) : IDisposable
{
    private readonly ConcurrentDictionary<string, FixedWindowRateLimiter> limiters = new(StringComparer.Ordinal);

    public bool TryAcquire(AccessContext context, HttpRequest request)
    {
        var (bucket, limit) = ResolveBucket(request, options);
        var key = $"{context.CredentialId:N}:{bucket}";
        var limiter = limiters.GetOrAdd(key, _ => new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
        using var lease = limiter.AttemptAcquire(1);
        return lease.IsAcquired;
    }

    internal static (string Bucket, int Limit) ResolveBucket(HttpRequest request, ApiRateLimitOptions options)
    {
        var path = request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api/v1/ingestao", StringComparison.OrdinalIgnoreCase))
            return ("INGESTAO", options.IngestionPermitLimit);
        if (path.Equals("/api/v1/identidade/resolver", StringComparison.OrdinalIgnoreCase))
            return ("IDENTIDADE", options.IdentityPermitLimit);
        if (path.StartsWith("/api/v1/pessoas", StringComparison.OrdinalIgnoreCase))
            return ("PESSOA", options.StandardPermitLimit);
        return ("STANDARD", options.StandardPermitLimit);
    }

    public void Dispose()
    {
        foreach (var limiter in limiters.Values) limiter.Dispose();
        limiters.Clear();
    }
}

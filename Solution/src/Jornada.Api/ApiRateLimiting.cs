using System.Threading.RateLimiting;

namespace Jornada.Api;

internal sealed record ApiRateLimitOptions
{
    public const string SectionName = "ApiRateLimiting";

    public int StandardPermitLimit { get; init; } = 120;
    public int IdentityPermitLimit { get; init; } = 30;
    public int IngestionPermitLimit { get; init; } = 20;
    public int EdgeMultiplier { get; init; } = 10;

    public void Validate()
    {
        if (StandardPermitLimit <= 0) throw new InvalidOperationException("ApiRateLimiting:StandardPermitLimit deve ser positivo.");
        if (IdentityPermitLimit <= 0) throw new InvalidOperationException("ApiRateLimiting:IdentityPermitLimit deve ser positivo.");
        if (IngestionPermitLimit <= 0) throw new InvalidOperationException("ApiRateLimiting:IngestionPermitLimit deve ser positivo.");
        if (EdgeMultiplier <= 0) throw new InvalidOperationException("ApiRateLimiting:EdgeMultiplier deve ser positivo.");
        const int maxPermitLimit = 1_000_000;
        if (StandardPermitLimit > maxPermitLimit || IdentityPermitLimit > maxPermitLimit || IngestionPermitLimit > maxPermitLimit)
            throw new InvalidOperationException($"Limites ApiRateLimiting não podem exceder {maxPermitLimit}.");
        if (EdgeMultiplier > 1_000)
            throw new InvalidOperationException("ApiRateLimiting:EdgeMultiplier não pode exceder 1000.");
        _ = checked(StandardPermitLimit * EdgeMultiplier);
        _ = checked(IdentityPermitLimit * EdgeMultiplier);
        _ = checked(IngestionPermitLimit * EdgeMultiplier);
    }
}

internal static class ApiRateLimiting
{
    /// <summary>
    /// Limitador pré-autenticação: somente IP observado/normalizado, sem confiar em headers públicos de credencial.
    /// O limite por credencial autenticada é aplicado depois por AuthenticatedRateLimitGuard.
    /// </summary>
    internal static RateLimitPartition<string> EdgeFixedWindow(HttpContext http, int permitLimit, string bucket = "DEFAULT")
    {
        var clientIp = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var partitionKey = $"{clientIp}:{bucket}";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(permitLimit, 1),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }
}

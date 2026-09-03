using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Jornada.Api;

internal sealed record TrustedProxyOptions
{
    public string[] KnownProxies { get; init; } = [];
    public string[] KnownNetworks { get; init; } = [];
    public int ForwardLimit { get; init; } = 1;
}

/// <summary>
/// X-Forwarded-For só é aceito de endereços ou redes de proxy explicitamente configurados.
/// Sem KnownProxies/KnownNetworks, o endereço observado na conexão TCP permanece a identidade de rede usada no rate limit.
/// </summary>
internal static class TrustedProxyConfiguration
{
    internal static bool IsEnabled(TrustedProxyOptions options) =>
        (options.KnownProxies?.Any(value => !string.IsNullOrWhiteSpace(value)) ?? false)
        || (options.KnownNetworks?.Any(value => !string.IsNullOrWhiteSpace(value)) ?? false);

    internal static Microsoft.AspNetCore.HttpOverrides.IPNetwork ParseNetwork(string raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var prefix) || !int.TryParse(parts[1], out var prefixLength))
            throw new InvalidOperationException($"ReverseProxy:KnownNetworks contém CIDR inválido: '{raw}'.");
        var max = prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        if (prefixLength < 0 || prefixLength > max)
            throw new InvalidOperationException($"ReverseProxy:KnownNetworks contém prefixo CIDR inválido: '{raw}'.");
        return new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength);
    }
}

internal sealed class ConfigureTrustedForwardedHeaders(IOptions<TrustedProxyOptions> configured)
    : IConfigureOptions<ForwardedHeadersOptions>
{
    public void Configure(ForwardedHeadersOptions options)
    {
        if (!TrustedProxyConfiguration.IsEnabled(configured.Value))
            throw new InvalidOperationException("Forwarded Headers não deve ser habilitado sem ReverseProxy:KnownProxies/KnownNetworks explícito.");
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = Math.Clamp(configured.Value.ForwardLimit, 1, 5);
        options.RequireHeaderSymmetry = true;

        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();

        foreach (var raw in configured.Value.KnownProxies ?? [])
        {
            if (!IPAddress.TryParse(raw?.Trim(), out var address))
                throw new InvalidOperationException($"ReverseProxy:KnownProxies contém IP inválido: '{raw}'.");
            options.KnownProxies.Add(address);
        }
        foreach (var raw in configured.Value.KnownNetworks ?? [])
            options.KnownNetworks.Add(TrustedProxyConfiguration.ParseNetwork(raw));
    }
}

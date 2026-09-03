using System.Net;
using Jornada.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

public sealed class TrustedProxyConfigurationTests
{
    [Test]
    public void Explicit_proxies_are_the_only_trusted_addresses()
    {
        var configured = Options.Create(new TrustedProxyOptions { KnownProxies = ["10.0.0.10", "10.0.0.11"], ForwardLimit = 2 });
        var forwarded = new ForwardedHeadersOptions();
        new ConfigureTrustedForwardedHeaders(configured).Configure(forwarded);
        Assert.Multiple(() =>
        {
            Assert.That(forwarded.ForwardLimit, Is.EqualTo(2));
            Assert.That(forwarded.RequireHeaderSymmetry, Is.True);
            Assert.That(forwarded.KnownNetworks, Is.Empty);
            Assert.That(forwarded.KnownProxies, Is.EquivalentTo(new[] { IPAddress.Parse("10.0.0.10"), IPAddress.Parse("10.0.0.11") }));
        });
    }

    [Test]
    public void Explicit_cidr_networks_are_supported_without_trusting_arbitrary_forwarded_headers()
    {
        var configured = Options.Create(new TrustedProxyOptions { KnownNetworks = ["10.20.30.0/24", "2001:db8::/48"] });
        var forwarded = new ForwardedHeadersOptions();
        new ConfigureTrustedForwardedHeaders(configured).Configure(forwarded);
        Assert.Multiple(() =>
        {
            Assert.That(forwarded.KnownProxies, Is.Empty);
            Assert.That(forwarded.KnownNetworks.Count, Is.EqualTo(2));
            Assert.That(forwarded.KnownNetworks[0].Prefix, Is.EqualTo(IPAddress.Parse("10.20.30.0")));
            Assert.That(forwarded.KnownNetworks[0].PrefixLength, Is.EqualTo(24));
        });
    }

    [Test]
    public void Invalid_proxy_name_fails_closed()
    {
        var configured = Options.Create(new TrustedProxyOptions { KnownProxies = ["gateway.internal"] });
        Assert.Throws<InvalidOperationException>(() => new ConfigureTrustedForwardedHeaders(configured).Configure(new ForwardedHeadersOptions()));
    }

    [TestCase("10.0.0.0/33")]
    [TestCase("10.0.0.0")]
    [TestCase("not-a-network/24")]
    public void Invalid_cidr_fails_closed(string value)
    {
        var configured = Options.Create(new TrustedProxyOptions { KnownNetworks = [value] });
        Assert.Throws<InvalidOperationException>(() => new ConfigureTrustedForwardedHeaders(configured).Configure(new ForwardedHeadersOptions()));
    }
}

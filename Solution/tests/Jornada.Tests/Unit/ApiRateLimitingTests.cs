using System.Net;
using Jornada.Api;
using Microsoft.AspNetCore.Http;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class ApiRateLimitingTests
{
    [Test]
    public void Edge_partition_uses_only_normalized_remote_ip_not_public_credential_header()
    {
        var http = Context("10.20.30.40", "SMADS");
        var partition = ApiRateLimiting.EdgeFixedWindow(http, 100, "PESSOA");
        Assert.That(partition.PartitionKey, Is.EqualTo("10.20.30.40:PESSOA"));
        http.Request.Headers["X-Jornada-Gestor"] = "OUTRO";
        Assert.That(ApiRateLimiting.EdgeFixedWindow(http, 100, "PESSOA").PartitionKey, Is.EqualTo(partition.PartitionKey));
    }

    [Test]
    public void Raw_x_forwarded_for_header_never_changes_edge_partition()
    {
        var http = Context("10.20.30.40", "SMADS");
        http.Request.Headers["X-Forwarded-For"] = "203.0.113.99";
        Assert.That(ApiRateLimiting.EdgeFixedWindow(http, 100, "PESSOA").PartitionKey, Is.EqualTo("10.20.30.40:PESSOA"));
    }

    [Test]
    public void Authenticated_buckets_are_governed_by_endpoint_class()
    {
        var person = Context("10.20.30.40", "SMADS");
        person.Request.Path = "/api/v1/pessoas/00000000-0000-0000-0000-000000000001";
        var options = new ApiRateLimitOptions();
        var personBucket = AuthenticatedRateLimitGuard.ResolveBucket(person.Request, options);

        var identity = Context("10.20.30.40", "SMADS");
        identity.Request.Path = "/api/v1/identidade/resolver";
        var identityBucket = AuthenticatedRateLimitGuard.ResolveBucket(identity.Request, options);

        Assert.Multiple(() =>
        {
            Assert.That(personBucket.Bucket, Is.EqualTo("PESSOA"));
            Assert.That(personBucket.Limit, Is.EqualTo(options.StandardPermitLimit));
            Assert.That(identityBucket.Bucket, Is.EqualTo("IDENTIDADE"));
            Assert.That(identityBucket.Limit, Is.EqualTo(options.IdentityPermitLimit));
        });
    }


    [Test]
    public void Rate_limit_options_reject_non_positive_and_overflowing_values()
    {
        Assert.Throws<InvalidOperationException>(() => new ApiRateLimitOptions { IngestionPermitLimit = 0 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new ApiRateLimitOptions { EdgeMultiplier = 1001 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new ApiRateLimitOptions { StandardPermitLimit = 1_000_001 }.Validate());
    }

    private static DefaultHttpContext Context(string remoteIp, string gestor)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        http.Request.Headers["X-Jornada-Gestor"] = gestor;
        return http;
    }
}

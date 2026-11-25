using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

public sealed class V9BlockingMaintenanceGateTests
{
    [TestCase(null, true, false)]
    [TestCase("", true, false)]
    [TestCase("HML", true, false)]
    [TestCase("Homologation", true, false)]
    [TestCase("Production", true, false)]
    [TestCase("PROD", true, false)]
    [TestCase("development", true, false)]
    [TestCase("Development ", true, false)]
    [TestCase("Development", false, false)]
    [TestCase("Development", true, true)]
    public void Explicit_authorized_DEV_only(string? profile, bool enabled, bool expected)
    {
        Assert.That(V9BlockingMaintenanceGate.IsExplicitDevOptIn(profile, enabled,
            Guid.Parse("a0000000-0000-4000-8000-000000000001")), Is.EqualTo(expected));
        Assert.That(V9BlockingMaintenanceGate.IsExplicitDevOptIn(profile, enabled, Guid.Empty), Is.False);
    }

    [TestCase("Production", true)]
    [TestCase("HML", true)]
    [TestCase("Homologation", true)]
    [TestCase("Development", false)]
    [TestCase(null, true)]
    public void Denied_environment_fails_before_accessing_SQL(string? profile, bool enabled)
    {
        using var closed = new SqlConnection();
        var denied = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
            V9BlockingMaintenanceGate.RequireAsync(profile, enabled, Guid.NewGuid(),
                closed, null!, CancellationToken.None)));
        Assert.That(denied!.Message, Does.StartWith(V9BlockingMaintenanceGate.ProductiveNotCertified));
    }
}

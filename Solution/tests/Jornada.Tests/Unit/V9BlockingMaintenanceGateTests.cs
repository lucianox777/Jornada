using Jornada.Operational.Sql;
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
}

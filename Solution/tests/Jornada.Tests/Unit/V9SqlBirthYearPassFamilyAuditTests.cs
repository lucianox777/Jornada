using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9SqlBirthYearPassFamilyAuditTests
{
    [Test]
    public void Contract_remains_non_operational()
    {
        Assert.That(V9SqlBirthYearPassFamilyAudit.Version, Does.Contain("TRAIN_VALIDATION"));
        Assert.That(V9SqlBirthYearPassFamilyAudit.RegisteredPasses, Has.Count.EqualTo(4));
    }
}

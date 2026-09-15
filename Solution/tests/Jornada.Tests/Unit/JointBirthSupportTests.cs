using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class JointBirthSupportTests
{
    [Test]
    public void Estimate_persists_raw_support_for_joint_birth_states()
    {
        var matched = new[]
        {
            new IdentityTrainingPair("A", new DateOnly(1980, 1, 1), "M", "A", new DateOnly(1980, 1, 1), "M"),
            new IdentityTrainingPair("B", new DateOnly(1980, 1, 2), "N", "B", new DateOnly(1980, 1, 3), "N")
        };
        var unmatched = new[]
        {
            new IdentityTrainingPair("C", new DateOnly(1980, 1, 1), "O", "D", new DateOnly(1980, 1, 2), "P"),
            new IdentityTrainingPair("E", new DateOnly(1980, 1, 1), "Q", "F", new DateOnly(1981, 2, 2), "R")
        };

        var result = LinkageParameterEstimator.Estimate(
            matched,
            unmatched,
            populationSize: 100,
            distinctBirthDates: 50,
            smoothingAlpha: 0.5m,
            threshold: 0.95m,
            conflictMargin: 0.03m);

        Assert.Multiple(() =>
        {
            Assert.That(result["SUPPORT_M_NASCIMENTO_CONJUNTO_111"], Is.EqualTo(1m));
            Assert.That(result["SUPPORT_M_NASCIMENTO_CONJUNTO_110"], Is.EqualTo(1m));
            Assert.That(result["SUPPORT_U_NASCIMENTO_CONJUNTO_110"], Is.EqualTo(1m));
            Assert.That(result["SUPPORT_U_NASCIMENTO_CONJUNTO_000"], Is.EqualTo(1m));
        });
    }
}

using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class JornadaExitCodesTests
{
    [Test]
    public void Process_exit_codes_are_stable_distinct_and_documented()
    {
        var observed = new[]
        {
            JornadaExitCodes.OK,
            JornadaExitCodes.FAILURE,
            JornadaExitCodes.VERIFICATION_FAILED,
            JornadaExitCodes.INCOMPLETE,
            JornadaExitCodes.INVALID_PRECONDITION,
            JornadaExitCodes.INVALID_ARGS,
            JornadaExitCodes.CANCELLED
        };
        Assert.Multiple(() =>
        {
            Assert.That(observed, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 64, 130 }));
            Assert.That(observed.Distinct().Count(), Is.EqualTo(observed.Length));
        });
    }
}

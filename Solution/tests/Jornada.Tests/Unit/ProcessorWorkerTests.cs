using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class ProcessorWorkerTests
{
    [Test]
    public void Loop_failure_backoff_is_bounded_and_resets_from_polling_floor()
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                ProcessorWorker.CalculateLoopFailureBackoff(1, 100).TotalMilliseconds,
                Is.EqualTo(250));
            Assert.That(
                ProcessorWorker.CalculateLoopFailureBackoff(2, 1000).TotalMilliseconds,
                Is.EqualTo(2000));
            Assert.That(
                ProcessorWorker.CalculateLoopFailureBackoff(10, 1000).TotalMilliseconds,
                Is.EqualTo(30000));
            Assert.That((Func<object?>)(() => ProcessorWorker.CalculateLoopFailureBackoff(0, 1000)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }));
    }
}

using NUnit.Framework;

namespace Jornada.Tests.Integration;

/// <summary>
/// NUnit 4-safe typed assertions. Avoid overload ambiguity (CS0121) and
/// obsolete AsyncTestDelegate (CS0618) under warnings-as-errors.
/// </summary>
internal static class TestAsserts
{
    public static TException ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
        => Assert.ThrowsAsync<TException>(action)!;

    public static void Multiple(Action assertions)
        => Assert.Multiple(assertions);
}

[TestFixture, Category("Integration")]
internal sealed class TestAssertsContractTests
{
    [Test]
    public void ThrowsAsync_accepts_typed_task_delegate()
    {
        var exception = TestAsserts.ThrowsAsync<InvalidOperationException>(
            async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("expected");
            });
        Assert.That(exception.Message, Is.EqualTo("expected"));
    }

    [Test]
    public void Multiple_accepts_typed_action()
    {
        TestAsserts.Multiple(() =>
        {
            Assert.That(1 + 1, Is.EqualTo(2));
            Assert.That(true, Is.True);
        });
    }
}

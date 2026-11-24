using System.Reflection;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9ControlledDevRunnerContractTests
{
    [Test]
    public void Run_core_keeps_cancellation_token_as_last_parameter()
    {
        // Regression for the observed CI CA1068 failure on PR #1924 HEAD
        // eb296832: RunCoreAsync(request, CancellationToken, controlledDev)
        // compiles with analyzer failure and violates the standard contract.
        var method = typeof(ProbabilisticLinkageBatchRunner).GetMethod(
            "RunCoreAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        var parameters = method!.GetParameters();
        Assert.That(parameters.Select(parameter => parameter.ParameterType),
            Is.EqualTo(new[]
            {
                typeof(Jornada.Contracts.ProbabilisticLinkageRunRequest),
                typeof(bool),
                typeof(CancellationToken)
            }));
        Assert.That(parameters[^1].Name, Is.EqualTo("ct"));
    }
}

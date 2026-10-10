using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class CiTriggerPolicyTests
{
    [Test]
    public void SingleCiWorkflowDoesNotDoubleRunFeatureBranchPushesWithOpenPullRequests()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt")))
            current = current.Parent;
        Assert.That(current, Is.Not.Null, "A raiz do repositório é necessária para validar a CI real.");

        var ci = File.ReadAllText(Path.Combine(current!.FullName, ".github", "workflows", "ci.yml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var expected = string.Join("\n",
            "on:",
            "  push:",
            "    branches:",
            "      - master",
            "    tags:",
            "      - '**'",
            "  pull_request:",
            "  workflow_dispatch:");

        Assert.Multiple((Action)(() =>
        {
            Assert.That(ci, Does.Contain(expected));
            Assert.That(ci, Does.Contain("  dependency-lock:"));
            Assert.That(ci, Does.Contain("  unit:"));
            Assert.That(ci, Does.Contain("  deterministic-build:"));
            Assert.That(ci, Does.Contain("dotnet build Jornada.sln"));
            Assert.That(ci, Does.Contain("  cancel-in-progress: true"));
        }));
    }
}

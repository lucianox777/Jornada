using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9LowerNonMatchStudyCliTests
{
    [Test]
    public void Rejects_missing_cli_arguments_before_any_host_or_SQL()
    {
        Assert.ThrowsAsync<ArgumentException>((Func<Task>)(async () =>
            await V9LowerNonMatchStudyCli.RunAsync([V9LowerNonMatchStudyCli.Flag])));
    }

    [Test]
    public async Task Rejects_test_leakage_and_automatic_study_before_opening_SQL()
    {
        var root = Path.Combine(Path.GetTempPath(), "v9-lower-study-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var plan = Path.Combine(root, "grade.json");
            var outPath = Path.Combine(root, "new");
            await File.WriteAllTextAsync(plan, """
                {"CandidateLowerThresholds":[0.1,0.2,0.3],
                 "MinimumIndependentIdentityGroups":3,
                 "MinimumAlreadyRepresentedSources":2,
                 "ManualCalibrationRequested":true,"TestUsed":true}
                """);
            var args = new[]
            {
                V9LowerNonMatchStudyCli.Flag,
                Path.Combine(root, "missing.selection.json"),
                Path.Combine(root, "missing.validation.json"),
                Path.Combine(root, "missing.fit.json"),
                Path.Combine(root, "missing.csv"), plan, outPath
            };
            Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
                await V9LowerNonMatchStudyCli.RunAsync(args)));
            Assert.That(Directory.Exists(outPath), Is.False);
            await File.WriteAllTextAsync(plan, """
                {"CandidateLowerThresholds":[0.1,0.2,0.3],
                 "MinimumIndependentIdentityGroups":3,
                 "MinimumAlreadyRepresentedSources":2,
                 "ManualCalibrationRequested":false,"TestUsed":false}
                """);
            Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
                await V9LowerNonMatchStudyCli.RunAsync(args)));
            Assert.That(Directory.Exists(outPath), Is.False);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

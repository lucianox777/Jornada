using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

public sealed class BlockingFeatureDiagnosticTests
{
    [Test]
    public void Analyze_RanksHighRecallSelectiveFieldAhead()
    {
        var observations = new[]
        {
            Obs(true,  ("nome", true),  ("distrito", true)),
            Obs(true,  ("nome", true),  ("distrito", true)),
            Obs(true,  ("nome", true),  ("distrito", false)),
            Obs(true,  ("nome", false), ("distrito", true)),
            Obs(false, ("nome", false), ("distrito", true)),
            Obs(false, ("nome", false), ("distrito", true)),
            Obs(false, ("nome", false), ("distrito", false)),
            Obs(false, ("nome", true),  ("distrito", true))
        };

        var report = BlockingFeatureDiagnostic.Analyze(observations);

        Assert.Multiple(() =>
        {
            Assert.That(report.MethodVersion, Is.EqualTo(BlockingFeatureDiagnostic.MethodVersion));
            Assert.That(report.Fields[0].Field, Is.EqualTo("nome"));
            Assert.That(report.Fields.Single(x => x.Field == "nome").TrueMatchRecall, Is.EqualTo(0.75d).Within(1e-12));
            Assert.That(report.Fields.Single(x => x.Field == "nome").NonMatchRetention, Is.EqualTo(0.25d).Within(1e-12));
            Assert.That(report.Fields.Single(x => x.Field == "nome").ReductionRatio, Is.EqualTo(0.75d).Within(1e-12));
        });
    }

    [Test]
    public void Analyze_ReportsPerfectAgreementDependency()
    {
        var observations = new[]
        {
            Obs(true,  ("a", true),  ("b", true)),
            Obs(true,  ("a", false), ("b", false)),
            Obs(false, ("a", true),  ("b", true)),
            Obs(false, ("a", false), ("b", false))
        };

        var dependency = BlockingFeatureDiagnostic.Analyze(observations).Dependencies.Single();

        Assert.That(dependency.PhiAgreementCorrelation, Is.EqualTo(1d).Within(1e-12));
    }

    [Test]
    public void Analyze_CountsMissingAgainstRecallAndReportsMissingRate()
    {
        var observations = new[]
        {
            Obs(true,  ("email", true)),
            Obs(true,  ("email", null)),
            Obs(false, ("email", false)),
            Obs(false, ("email", null))
        };

        var field = BlockingFeatureDiagnostic.Analyze(observations).Fields.Single();

        Assert.Multiple(() =>
        {
            Assert.That(field.TrueMatchRecall, Is.EqualTo(0.5d).Within(1e-12));
            Assert.That(field.MissingRate, Is.EqualTo(0.5d).Within(1e-12));
            Assert.That(field.NonMatchRetention, Is.EqualTo(0d).Within(1e-12));
        });
    }

    [Test]
    public void Analyze_RejectsCorpusWithoutBothReferenceClasses()
    {
        var observations = new[]
        {
            Obs(true, ("nome", true)),
            Obs(true, ("nome", false))
        };

        Assert.That(
            () => BlockingFeatureDiagnostic.Analyze(observations),
            Throws.TypeOf<ArgumentException>());
    }

    private static BlockingFeatureObservation Obs(bool isMatch, params (string Field, bool? Agreement)[] values)
        => new(isMatch, values.ToDictionary(static x => x.Field, static x => x.Agreement, StringComparer.Ordinal));
}

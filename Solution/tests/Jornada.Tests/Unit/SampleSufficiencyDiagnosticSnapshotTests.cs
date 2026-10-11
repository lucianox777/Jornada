using System.Text.Json;
using Jornada.Linkage.Parameters.Worker;
using static Jornada.Linkage.Parameters.Worker.SampleSufficiencyAssessment;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class SampleSufficiencyDiagnosticSnapshotTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void SyntheticEvidenceCannotProduceGreenEvenIfEvaluatorSaysSufficient()
    {
        var result = Evaluate(new[] {
            new Evidence("SP", "p1", true, true, "g1"),
            new Evidence("SP", "p2", true, false, "g2")
        }, new[] { "SP" }, 1, 1);
        Assert.That(result.State, Is.EqualTo(Status.Sufficient));
        var snapshot = SampleSufficiencyDiagnosticSnapshot.Create(result, "v1", Cutoff, "SYNTHETIC");
        Assert.That(snapshot.State, Is.EqualTo("INDETERMINADO"));
        Assert.That(snapshot.ReasonsJson, Does.Contain("provenance not verified"));
        Assert.That(snapshot.ProvenanceJson, Does.Not.Contain("p1"));
    }

    [Test]
    public void VerifiedIndependentEvidenceMapsOnlyEvaluatorStatus()
    {
        var sufficient = Evaluate(new[] {
            new Evidence("SP", "m1", true, true, "g1"),
            new Evidence("SP", "u1", true, false, "g2")
        }, new[] { "SP" }, 1, 1);
        var insufficient = Evaluate(Array.Empty<Evidence>(), new[] { "SP" }, 1, 1);
        Assert.That(SampleSufficiencyDiagnosticSnapshot.Create(sufficient, "v2", Cutoff, "INDEPENDENT_REAL").State,
            Is.EqualTo("VERDE"));
        Assert.That(SampleSufficiencyDiagnosticSnapshot.Create(insufficient, "v2", Cutoff, "INDEPENDENT_REAL").State,
            Is.EqualTo("INDETERMINADO"));
        var snapshot = SampleSufficiencyDiagnosticSnapshot.Create(sufficient, "v2", Cutoff, "INDEPENDENT_REAL");
        Assert.That(JsonDocument.Parse(snapshot.CountersJson).RootElement.GetArrayLength(), Is.EqualTo(1));
        Assert.That(snapshot.PolicyVersion, Is.EqualTo("v2"));
    }

    [Test]
    public void RejectsNonUtcCutoffAndMissingPolicyVersion()
    {
        var result = Evaluate(Array.Empty<Evidence>(), new[] { "SP" }, 1, 1);
        Assert.Throws<ArgumentException>(() => SampleSufficiencyDiagnosticSnapshot.Create(result, " ", Cutoff, "INDEPENDENT_REAL"));
        Assert.Throws<ArgumentException>(() => SampleSufficiencyDiagnosticSnapshot.Create(result, "v1", Cutoff.ToOffset(TimeSpan.FromHours(-3)), "INDEPENDENT_REAL"));
    }
}

using System.Globalization;
using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests;

[TestFixture]
[Category("Unit")]
public sealed class IbgeBlockingBootstrapEstimatorTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private static ReferencePopulationEvidence N(long n) => new(n, ReferencePopulationEvidence.CalibratorCorpusMethod, "cal-run-test", Hash);

    [Test]
    public void MariaSilvaStress_ExactIntersectionIsDiagnosticAndDeterministic()
    {
        var passes = new[]
        {
            new BootstrapPassInput("exact", BootstrapPassCategory.Exact, new[]
            {
                new MarginalKeyProbability("person:first:MARIA", .10, "MUNICIPIO_SAO_PAULO_V2_CANDIDATA"),
                new MarginalKeyProbability("person:surname_any:SILVA", .20, "MUNICIPIO_SAO_PAULO_V2_CANDIDATA"),
                new MarginalKeyProbability("mother:first:MARIA", .10, "BRASIL_V1"),
                new MarginalKeyProbability("mother:surname_any:SILVA", .20, "BRASIL_V1"),
                new MarginalKeyProbability("birth:exact", 1d / 36525d, "DIAGNOSTIC_DATE_UNIFORM")
            }, "MARIA SILVA x MARIA SILVA; interseção exata primeiro.")
        };
        var a = IbgeBlockingBootstrapEstimator.Estimate(N(10_000_000), "CENSO2022_NOMES_BRASIL_V1", Hash, passes, new FrequentKeyRule(.99), SurnameParticlePolicy.ExcludePortugueseParticles);
        var b = IbgeBlockingBootstrapEstimator.Estimate(N(10_000_000), "CENSO2022_NOMES_BRASIL_V1", Hash, passes, new FrequentKeyRule(.99), SurnameParticlePolicy.ExcludePortugueseParticles);

        Assert.Multiple(() =>
        {
            Assert.That(a.Marker, Is.EqualTo("NAO_PROMOCIONAL"));
            Assert.That(a.JointDistributionObserved, Is.False);
            Assert.That(a.EstimationKind, Is.EqualTo("MARGINAL_INDEPENDENCE_DIAGNOSTIC"));
            Assert.That(a.Passes.Single().ExpectedCandidates, Is.GreaterThan(0));
            Assert.That(a.ResultFingerprintSha256, Is.EqualTo(b.ResultFingerprintSha256));
        });
    }

    [Test]
    public void PassOrder_IsExactThenDateThenNameThenIncomplete_WithoutRecallCut()
    {
        static BootstrapPassInput P(string id, BootstrapPassCategory c) =>
            new(id, c, new[] { new MarginalKeyProbability(id, .01, "scope") }, "reason");
        var proposal = IbgeBlockingBootstrapEstimator.Estimate(N(1000), "ref", Hash,
            new[] { P("incomplete", BootstrapPassCategory.IncompleteRecovery), P("name", BootstrapPassCategory.NameVariant), P("date", BootstrapPassCategory.DateVariant), P("exact", BootstrapPassCategory.Exact) },
            new FrequentKeyRule(.75), SurnameParticlePolicy.Preserve);
        Assert.That(proposal.Passes.Select(x => x.PassId), Is.EqualTo(new[] { "exact", "date", "name", "incomplete" }));
        Assert.That(proposal.Passes, Has.Count.EqualTo(4));
    }

    [TestCase(null, "SILVA", "MARIA SILVA", "2000-01-01")]
    [TestCase("MARIA SILVA", null, "MARIA SILVA", "2000-01-01")]
    [TestCase("MARIA SILVA", "MARIA SILVA", null, "2000-01-01")]
    [TestCase("MARIA SILVA", "MARIA SILVA", "MARIA SILVA", null)]
    public void NullMatrix_ProjectorNeverFabricatesKeys(string? name, string? surname, string? mother, string? date)
    {
        var fullName = name is null ? null : surname is null ? name : $"{name} {surname}";
        var birth = date is null ? (DateOnly?)null : DateOnly.Parse(date, CultureInfo.InvariantCulture);
        var keys = BlockingProjectionKeyProjector.Project(fullName, mother, birth);
        if (fullName is null) Assert.That(keys.Any(k => k.Feature.StartsWith("name_", StringComparison.Ordinal) && !k.Feature.StartsWith("mother_", StringComparison.Ordinal)), Is.False);
        if (mother is null) Assert.That(keys.Any(k => k.Feature.StartsWith("mother_", StringComparison.Ordinal)), Is.False);
        if (birth is null) Assert.That(keys.Any(k => k.Feature.StartsWith("birth_", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void DateMatrix_CombinedPlannerHandlesLeapTransposeNeighborAndFutureDeterministically()
    {
        static IdentityObservation O(DateOnly d) => new(null, "NAO_INFORMADO", "MARIA SILVA", d, "MARIA SILVA");
        var leap = CombinedIdentityCandidatePlanner.Plan(O(new DateOnly(2024, 2, 29)));
        var transpose = CombinedIdentityCandidatePlanner.Plan(O(new DateOnly(2024, 3, 4)));
        var future = CombinedIdentityCandidatePlanner.Plan(O(new DateOnly(2099, 1, 1)));

        Assert.Multiple(() =>
        {
            Assert.That(leap.Any(p => p.PassId == "combined-neighbor-year"), Is.False);
            Assert.That(transpose.Any(p => p.PassId == "combined-day-month-transpose"), Is.True);
            Assert.That(transpose.Any(p => p.PassId == "combined-neighbor-year"), Is.True);
            Assert.That(future, Is.Not.Empty, "Blocking não transforma data futura em identidade; validação pertence ao contrato de admissão.");
        });
    }

    [Test]
    public void MissingSnapshotAndInvalidHash_FailClosed()
    {
        Assert.Throws<DirectoryNotFoundException>(() => IbgeBlockingSnapshotVerifier.Verify(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        Assert.Throws<ArgumentException>(() => IbgeBlockingBootstrapEstimator.Estimate(N(100), "ref", "bad", new[]
        {
            new BootstrapPassInput("x", BootstrapPassCategory.Exact, new[] { new MarginalKeyProbability("x", .1, "scope") }, "r")
        }, new FrequentKeyRule(.9), SurnameParticlePolicy.Preserve));
    }

    [Test]
    public void NrefWithoutCalibratorProvenance_FailsClosed()
    {
        var invalid = new ReferencePopulationEvidence(100, "MANUAL", "run", Hash);
        Assert.Throws<ArgumentException>(() => IbgeBlockingBootstrapEstimator.Estimate(invalid, "ref", Hash, new[]
        {
            new BootstrapPassInput("x", BootstrapPassCategory.Exact, new[] { new MarginalKeyProbability("x", .1, "scope") }, "r")
        }, new FrequentKeyRule(.9), SurnameParticlePolicy.Preserve));
    }

    [Test]
    public void ImmutableJsonCarriesMethodNrefFingerprintAndWarning()
    {
        var proposal = IbgeBlockingBootstrapEstimator.Estimate(N(1234), "ref", Hash, new[]
        {
            new BootstrapPassInput("x", BootstrapPassCategory.Exact, new[] { new MarginalKeyProbability("x", .1, "scope") }, "r")
        }, new FrequentKeyRule(.9), SurnameParticlePolicy.Preserve);
        var json = IbgeBlockingBootstrapEstimator.ToImmutableJson(proposal);
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("NAO_PROMOCIONAL"));
            Assert.That(json, Does.Contain("MARGINAL_INDEPENDENCE_DIAGNOSTIC"));
            Assert.That(json, Does.Contain("1234"));
            Assert.That(json, Does.Contain(ReferencePopulationEvidence.CalibratorCorpusMethod));
            Assert.That(json, Does.Contain("cal-run-test"));
            Assert.That(json, Does.Contain(proposal.ResultFingerprintSha256));
        });
    }
}

using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests;

public sealed class CombinedIdentityCandidatePlannerTests
{
    [Test]
    public void Plan_ProducesExactTransposeNeighborAndPhoneticPasses()
    {
        var observation = new IdentityObservation(null, "NAO_INFORMADO",
            "Jose da Silva", new DateOnly(1975, 2, 11), "Maria da Silva");
        var passes = CombinedIdentityCandidatePlanner.Plan(observation);
        var byId = passes.ToDictionary(p => p.PassId, StringComparer.Ordinal);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(byId.Keys, Does.Contain("combined-exact"));
            Assert.That(byId.Keys, Does.Contain("combined-day-month-transpose"));
            Assert.That(byId.Keys, Does.Contain("combined-neighbor-year"));
            Assert.That(byId.Keys, Does.Contain("combined-name-phonetic"));
            Assert.That(byId.Keys, Does.Contain("combined-mother-phonetic"));
        }));
        var transposed = byId["combined-day-month-transpose"].Clauses
            .ToDictionary(c => c.Feature, c => c.Values);
        Assert.That(transposed[BlockingFeatureNames.BirthMonth], Is.EquivalentTo(new[] { "11" }));
        Assert.That(transposed[BlockingFeatureNames.BirthDay], Is.EquivalentTo(new[] { "02" }));
        var neighbors = byId["combined-neighbor-year"].Clauses.Single(c =>
            c.Feature == BlockingFeatureNames.BirthYear);
        Assert.That(neighbors.Values, Is.EquivalentTo(new[] { "1974", "1976" }));
        using var command = new SqlCommand();
        var sql = BlockingProjectionCandidateQueryBuilder.BuildCandidateUuidQuery(command, passes);
        Assert.That(sql, Does.Contain(" INTERSECT "));
        Assert.That(sql, Does.Contain(" UNION "));
    }

    [Test]
    public void Plan_DoesNotGenerateImpossibleTransposeOrLeapDayNeighbor()
    {
        var observation = new IdentityObservation(null, "NAO_INFORMADO",
            "Jose Silva", new DateOnly(2024, 2, 29), "Maria Silva");
        var passes = CombinedIdentityCandidatePlanner.Plan(observation);
        Assert.That(passes.Select(p => p.PassId), Does.Not.Contain("combined-day-month-transpose"));
        Assert.That(passes.Select(p => p.PassId), Does.Not.Contain("combined-neighbor-year"));
    }

    [Test]
    public void Plan_WithoutMotherOrBirthIsEmptyAndCpfIsRejected()
    {
        Assert.That(CombinedIdentityCandidatePlanner.Plan(new IdentityObservation(
            null, "NAO_INFORMADO", "Jose Silva", new DateOnly(1975, 2, 11), null)), Is.Empty);
        Assert.That(CombinedIdentityCandidatePlanner.Plan(new IdentityObservation(
            null, "NAO_INFORMADO", "Jose Silva", null, "Maria Silva")), Is.Empty);
        Assert.That(() => CombinedIdentityCandidatePlanner.Plan(new IdentityObservation(
            "12345678909", "NAO_INFORMADO", "Jose Silva",
            new DateOnly(1975, 2, 11), "Maria Silva")), Throws.InvalidOperationException);
    }
}

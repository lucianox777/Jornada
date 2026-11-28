using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9SixArmDirectVetoTests
{
    private static readonly IdentityObservation Source = new(null, "SEM_CPF", "MARIA SILVA",
        new DateOnly(1980, 5, 6), "ANA COSTA");

    [Test]
    public void RequiresUnanimityAtTheExactMaximum()
    {
        var dest = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var missingMother = Source with { DataNascimento = new DateOnly(1981, 5, 6), NomeMae = null };
        IdentityObservation Record(long id) => id == 1 ? missingMother : Source;
        var a = new V9ControlledDevSecureEvidenceScore.RecordScore(1, dest, .99m, 1m);
        var b = a with { ObservationId = 2 };
        Assert.That(V9Phase4Matrix.MatchesDirectVeto(Source, [a], Record), Is.True);
        Assert.That(V9Phase4Matrix.MatchesDirectVeto(Source, [a, b], Record), Is.False);
        Assert.That(V9Phase4Matrix.MatchesDirectVeto(Source,
            [a, b with { LogOdds = .9999999999999999999999999999m }], Record), Is.True);
        Assert.That(V9Phase4Matrix.MatchesDirectVeto(Source, [a with { LogOdds = null }], Record), Is.False);
        Assert.That(V9Phase4Matrix.MatchesDirectVeto(Source with { NomeMae = null }, [a], Record), Is.False);
        Assert.That(V9Phase4Matrix.MatchesDirectVeto(Source, [], Record), Is.False);
    }
}

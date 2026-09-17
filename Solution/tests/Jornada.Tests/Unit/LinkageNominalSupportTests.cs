using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class LinkageNominalSupportTests
{
    [Test]
    public void Estimate_PersistsNominalSupportCountsIncludingV6MotherMissing()
    {
        var matched = new[]
        {
            Pair("MARIA SILVA", "1980-01-01", "ANA SILVA", "MARIA SILVA", "1980-01-01", "ANA SILVA"),
            Pair("JOAO SOUZA", "1981-02-02", null, "JOAO SOUZA", "1981-02-02", null)
        };
        var unmatched = new[]
        {
            Pair("MARIA SILVA", "1980-01-01", "ANA SILVA", "MARIO SILVA", "1984-04-03", "ANA SILVA"),
            Pair("JOAO SOUZA", "1981-02-02", null, "CARLOS LIMA", "1970-05-06", null),
            Pair("PAULO COSTA", "1990-03-03", "LUCIA COSTA", "PEDRO MORAES", "1965-07-08", "MARIA MORAES")
        };

        var parameters = LinkageParameterEstimator.Estimate(
            matched,
            unmatched,
            populationSize: 1000,
            distinctBirthDates: 500,
            smoothingAlpha: 1m,
            threshold: .95m,
            conflictMargin: .03m,
            BirthScoringContract.SemanticEvidenceV5,
            decisionEvidenceV6: true);

        var nameStates = Enum.GetValues<NameComparisonState>();
        var matchedNameSupport = nameStates.Sum(state => parameters[$"SUPPORT_M_NOME_{state}"]);
        var unmatchedNameSupport = nameStates.Sum(state => parameters[$"SUPPORT_U_NOME_{state}"]);
        var matchedMotherSupport = LinkageParameterCatalog.MotherNameStates.Sum(state => parameters[$"SUPPORT_M_NOME_MAE_{state}"]);
        var unmatchedMotherSupport = LinkageParameterCatalog.MotherNameStates.Sum(state => parameters[$"SUPPORT_U_NOME_MAE_{state}"]);

        Assert.Multiple(() =>
        {
            Assert.That(matchedNameSupport, Is.EqualTo(2m));
            Assert.That(unmatchedNameSupport, Is.EqualTo(3m));
            Assert.That(matchedMotherSupport, Is.EqualTo(2m));
            Assert.That(unmatchedMotherSupport, Is.EqualTo(3m));
            Assert.That(parameters["SUPPORT_M_NOME_MAE_MISSING"], Is.EqualTo(1m));
            Assert.That(parameters["SUPPORT_U_NOME_MAE_MISSING"], Is.EqualTo(1m));
            Assert.That(parameters["SUPPORT_M_NOME_EXACT"], Is.EqualTo(2m));
            Assert.That(parameters["SUPPORT_U_NOME_LOW"], Is.GreaterThanOrEqualTo(1m));
        });
    }

    private static IdentityTrainingPair Pair(
        string leftName,
        string leftBirthDate,
        string? leftMotherName,
        string rightName,
        string rightBirthDate,
        string? rightMotherName) =>
        new(
            leftName,
            DateOnly.ParseExact(leftBirthDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            leftMotherName,
            rightName,
            DateOnly.ParseExact(rightBirthDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            rightMotherName);
}

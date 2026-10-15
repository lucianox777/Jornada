using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class PersonResolutionCalibrationStrategyCatalogTests
{
    [Test]
    public void Every_Strategy_Is_Anchored_To_Eligible_Canonical_Attribute()
    {
        var strategies = PersonResolutionCalibrationStrategyCatalog.ValidateAndList();
        Assert.That(strategies, Is.Not.Empty);
        foreach (var strategy in strategies)
        {
            Assert.That(PersonResolutionContractCatalog.IsEligible(strategy.AttributeCode), Is.True);
            Assert.That(strategy.TfSource, Is.EqualTo("TRAIN_CANDIDATE_FRAME"));
            Assert.That(strategy.FsComparisonLevels, Does.Contain("MISSING"));
            Assert.That(PersonResolutionCalibrationStrategyCatalog.TryGet(strategy.AttributeCode, out _),
                Is.True);
        }
    }

    [Test]
    public void Confidential_And_Shared_Location_Data_Remain_Ineligible()
    {
        foreach (var code in new[]
        {
            PersonResolutionContractCatalog.ConfidentialShelterAddress,
            PersonResolutionContractCatalog.ResidentialAddress,
            PersonResolutionContractCatalog.TerritorialReference,
            "UNKNOWN"
        })
            Assert.That(PersonResolutionCalibrationStrategyCatalog.TryGet(code, out _), Is.False);
    }

    [Test]
    public void Prefix_Length_Is_A_Candidate_Not_A_Permanent_Constant()
    {
        Assert.That(PersonResolutionCalibrationStrategyCatalog.TryGet(
            PersonResolutionContractCatalog.MotherName, out var mother), Is.True);
        Assert.That(mother.BlockingDerivations, Does.Contain("PREFIX_K"));
        Assert.That(mother.BlockingDerivations, Does.Not.Contain("PREFIX_3"));
    }
}

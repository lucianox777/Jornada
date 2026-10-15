using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class PersonResolutionCalibrationStrategyCatalogTests
{
    [Test]
    public void Every_Strategy_Is_Anchored_To_Eligible_Canonical_Attribute_And_One_Missing_Policy()
    {
        var strategies = PersonResolutionCalibrationStrategyCatalog.ValidateAndList();
        Assert.That(strategies, Is.Not.Empty);
        foreach (var strategy in strategies)
        {
            Assert.That(PersonResolutionContractCatalog.IsEligible(strategy.AttributeCode), Is.True);
            Assert.That(strategy.MissingPolicyVersion,
                Is.EqualTo(PersonResolutionMissingValuePolicy.Version));
            Assert.That(strategy.FsComparisonLevels, Does.Contain("Missing"));
            Assert.That(PersonResolutionCalibrationStrategyCatalog.TryGet(strategy.AttributeCode, out _),
                Is.True);
        }
    }

    [Test]
    public void Civil_And_Mother_Name_Use_The_Exact_V9_Levels_And_Tf_Units()
    {
        var expected = Enum.GetNames<CompositeNominalLevelV9>();
        foreach (var code in new[]
        {
            PersonResolutionContractCatalog.FullName,
            PersonResolutionContractCatalog.MotherName
        })
        {
            Assert.That(PersonResolutionCalibrationStrategyCatalog.TryGet(code, out var strategy), Is.True);
            Assert.That(strategy.ComparisonContract,
                Is.EqualTo(CompositeNominalComparisonV9.ContractVersion));
            Assert.That(strategy.FsComparisonLevels, Is.EqualTo(expected));

            var first = strategy.TfPolicies.Single(x =>
                x.ComparisonLevel == nameof(CompositeNominalLevelV9.ExactFirst));
            Assert.That(first.Unit, Is.EqualTo(NominalFrequencyUnitV9.FirstName));
            Assert.That(first.Source, Is.EqualTo(CalibrationTfSource.FrozenIbgePublishedMarginal));

            var last = strategy.TfPolicies.Single(x =>
                x.ComparisonLevel == nameof(CompositeNominalLevelV9.ExactLast));
            Assert.That(last.Unit, Is.EqualTo(NominalFrequencyUnitV9.LastContentSurname));
            Assert.That(last.Source, Is.EqualTo(CalibrationTfSource.FrozenIbgeSurnameAnyPositionProxy));

            var joint = strategy.TfPolicies.Single(x =>
                x.ComparisonLevel == nameof(CompositeNominalLevelV9.ExactFirstLast));
            Assert.That(joint.Unit, Is.EqualTo(NominalFrequencyUnitV9.FirstLastJoint));
            Assert.That(joint.Source, Is.EqualTo(CalibrationTfSource.TrainDistinctBase));
        }
    }

    [Test]
    public void Eligibility_Roles_Dependency_And_Social_Cross_Comparison_Are_Explicit()
    {
        Assert.That(PersonResolutionCalibrationStrategyCatalog.TryGet(
            PersonResolutionContractCatalog.FullName, out var full), Is.True);
        Assert.That(PersonResolutionCalibrationStrategyCatalog.TryGet(
            PersonResolutionContractCatalog.MotherName, out var mother), Is.True);
        Assert.That(PersonResolutionCalibrationStrategyCatalog.TryGet(
            PersonResolutionContractCatalog.BirthDate, out var birth), Is.True);
        Assert.That(PersonResolutionCalibrationStrategyCatalog.TryGet(
            PersonResolutionContractCatalog.ContactPhone, out var phone), Is.True);
        Assert.That(PersonResolutionCalibrationStrategyCatalog.TryGet(
            PersonResolutionContractCatalog.SocialName, out var social), Is.True);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(full.EvidenceRole,
                Is.EqualTo(PersonResolutionEvidenceRole.RequiredForProbabilisticEligibility));
            Assert.That(mother.EvidenceRole,
                Is.EqualTo(PersonResolutionEvidenceRole.AlternativeEligibility));
            Assert.That(birth.EvidenceRole,
                Is.EqualTo(PersonResolutionEvidenceRole.AlternativeEligibility));
            Assert.That(phone.EvidenceRole,
                Is.EqualTo(PersonResolutionEvidenceRole.ReinforcementOnly));
            Assert.That(phone.DependencyPolicy,
                Is.EqualTo(PersonResolutionDependencyPolicy.SharedContactWeak));
            Assert.That(social.DependencyPolicy,
                Is.EqualTo(PersonResolutionDependencyPolicy.CrossCivilSocialName));
            Assert.That(social.CrossCompareWith,
                Does.Contain(PersonResolutionContractCatalog.FullName));
            Assert.That(social.TfPolicies, Is.Empty,
                "Nome social não herda marginal IBGE de nome civil sem calibração própria.");
        }));
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

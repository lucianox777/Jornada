using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class PersonResolutionFsCellSchemaTests
{
    [Test]
    public void Ordering_Does_Not_Change_Frozen_Cell_Schema_Fingerprint()
    {
        var a = PersonResolutionFsCellSchema.Generate([
            PersonResolutionContractCatalog.FullName,
            PersonResolutionContractCatalog.MotherName,
            PersonResolutionContractCatalog.BirthDate]);
        var b = PersonResolutionFsCellSchema.Generate([
            PersonResolutionContractCatalog.BirthDate,
            PersonResolutionContractCatalog.FullName,
            PersonResolutionContractCatalog.MotherName]);

        Assert.That(a.FingerprintSha256, Is.EqualTo(b.FingerprintSha256));
        Assert.That(a.FingerprintSha256.Length, Is.EqualTo(64));
        Assert.That(a.MissingPolicyVersion, Is.EqualTo(PersonResolutionMissingValuePolicy.Version));
        Assert.That(a.Cells.Count, Is.EqualTo(21));

        var firstNameTf = a.Cells.Single(x =>
            x.AttributeCode == PersonResolutionContractCatalog.FullName
            && x.ComparisonLevel == nameof(CompositeNominalLevelV9.ExactFirst));
        Assert.That(firstNameTf.TfUnit, Is.EqualTo(NominalFrequencyUnitV9.FirstName));
        Assert.That(firstNameTf.TfSource,
            Is.EqualTo(CalibrationTfSource.FrozenIbgePublishedMarginal));
    }

    [Test]
    public void Ineligible_Attributes_And_Duplicate_Fields_Fail_Closed()
    {
        try
        {
            PersonResolutionFsCellSchema.Generate([PersonResolutionContractCatalog.ConfidentialShelterAddress]);
            Assert.Fail("Confidential shelter must not be eligible for FS.");
        }
        catch (InvalidDataException) { }

        try
        {
            PersonResolutionFsCellSchema.Generate([
                PersonResolutionContractCatalog.FullName,
                PersonResolutionContractCatalog.FullName]);
            Assert.Fail("Duplicate FS attributes must fail closed.");
        }
        catch (ArgumentException) { }
    }

    [Test]
    public void Nominal_Schema_Uses_V9_Levels_Not_Generic_Exact_High_Partial()
    {
        var schema = PersonResolutionFsCellSchema.Generate([
            PersonResolutionContractCatalog.MotherName]);

        Assert.That(schema.Cells.Select(x => x.ComparisonLevel),
            Is.EquivalentTo(Enum.GetNames<CompositeNominalLevelV9>()));
        Assert.That(schema.Cells.Select(x => x.ComparisonLevel), Does.Contain("ExactFull"));
        Assert.That(schema.Cells.Select(x => x.ComparisonLevel), Does.Contain("ExactFirstLast"));
        Assert.That(schema.Cells.Select(x => x.ComparisonLevel), Does.Not.Contain("PARTIAL"));
        Assert.That(schema.Cells.All(x =>
            x.ComparisonContract == CompositeNominalComparisonV9.ContractVersion), Is.True);
    }

    [Test]
    public void Schema_Carries_Role_Dependency_And_Social_Cross_Comparison_Without_Fitted_Values()
    {
        var schema = PersonResolutionFsCellSchema.Generate([
            PersonResolutionContractCatalog.ContactPhone,
            PersonResolutionContractCatalog.SocialName]);

        Assert.That(schema.Cells.Where(x =>
            x.AttributeCode == PersonResolutionContractCatalog.ContactPhone)
            .All(x => x.EvidenceRole == PersonResolutionEvidenceRole.ReinforcementOnly
                && x.DependencyPolicy == PersonResolutionDependencyPolicy.SharedContactWeak), Is.True);

        Assert.That(schema.Cells.Where(x =>
            x.AttributeCode == PersonResolutionContractCatalog.SocialName)
            .All(x => x.CrossCompareWith.Contains(PersonResolutionContractCatalog.FullName)
                && x.TfSource == CalibrationTfSource.None), Is.True);
    }
}

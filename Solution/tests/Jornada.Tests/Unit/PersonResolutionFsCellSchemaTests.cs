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
        Assert.That(a.Cells.Count, Is.EqualTo(14));
        Assert.That(a.Cells.All(x => x.TfSource == "TRAIN_CANDIDATE_FRAME"), Is.True);
    }

    [Test]
    public void Ineligible_Attributes_And_Duplicate_Fields_Fail_Closed()
    {
        Assert.That((TestDelegate)(() => { PersonResolutionFsCellSchema.Generate([
            PersonResolutionContractCatalog.ConfidentialShelterAddress]); }),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((TestDelegate)(() => { PersonResolutionFsCellSchema.Generate([
            PersonResolutionContractCatalog.FullName,
            PersonResolutionContractCatalog.FullName]); }),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Schema_Does_Not_Contain_Fitted_Mu_Tf_Or_Thresholds()
    {
        var schema = PersonResolutionFsCellSchema.Generate([
            PersonResolutionContractCatalog.MotherName]);
        Assert.That(schema.Cells.Select(x => x.ComparisonLevel),
            Does.Contain("MISSING"));
        Assert.That(schema.Cells.Select(x => x.ComparisonLevel),
            Does.Contain("DISAGREE"));
        Assert.That(schema.Cells.All(x => x.AttributeCode == PersonResolutionContractCatalog.MotherName),
            Is.True);
    }
}

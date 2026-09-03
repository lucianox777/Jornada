using Jornada.Contracts;
using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class ConfidentialShelterAddressPolicyTests
{
    [Test]
    public void Special_address_is_accepted_only_from_explicitly_enabled_service_type()
    {
        Assert.DoesNotThrow(() => ConfidentialShelterAddressPolicy.ValidateSource(Batch(IntegrationNature.SERVICO, true), ConfidentialShelterAddressPolicy.AttributeCode));
        Assert.Throws<InvalidDataException>(() => ConfidentialShelterAddressPolicy.ValidateSource(Batch(IntegrationNature.SERVICO, false), ConfidentialShelterAddressPolicy.AttributeCode));
        Assert.Throws<InvalidDataException>(() => ConfidentialShelterAddressPolicy.ValidateSource(Batch(IntegrationNature.BENEFICIO, true), ConfidentialShelterAddressPolicy.AttributeCode));
        Assert.Throws<InvalidDataException>(() => ConfidentialShelterAddressPolicy.ValidateSource(Batch(null, false), ConfidentialShelterAddressPolicy.AttributeCode));
    }

    [Test]
    public void Ordinary_address_is_not_reclassified_or_blocked()
    {
        Assert.DoesNotThrow(() => ConfidentialShelterAddressPolicy.ValidateSource(Batch(null, false), "ENDERECO_RESIDENCIAL"));
    }

    private static ReservedBatch Batch(IntegrationNature? nature, bool allowed) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test", 1, "SMADS", 1, 1, "ASSISTENCIA", 1, nature,
        nature is null ? null : 10, nature is null ? null : 20, nature is null ? null : "CAS1", nature is null ? null : 1,
        1, DateTimeOffset.Parse("2026-08-31T12:00:00-03:00", System.Globalization.CultureInfo.InvariantCulture), new string('a',64), "x.zip", "sha256/x", 1,
        "config/contracts/gestores/SMADS/pessoa/v1/pessoa.schema.json", new byte[32], null, null, null, allowed, null, null, null);
}

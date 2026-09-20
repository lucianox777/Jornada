using Jornada.Contracts;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class LinkageCalibrationAuditExchangePolicyTests
{
    [TestCase("ATIVO", true)]
    [TestCase("VALIDADO", true)]
    [TestCase("RASCUNHO", false)]
    [TestCase("GERANDO", false)]
    [TestCase("FALHOU", false)]
    [TestCase("INATIVO", false)]
    [TestCase(null, false)]
    public void Exportable_model_status_is_fail_closed(string? status, bool expected)
    {
        Assert.That(LinkageCalibrationAuditExchangePolicy.IsExportableModelStatus(status), Is.EqualTo(expected));
    }

    [Test]
    public void Exchange_contract_declares_blocking_conditioned_u_and_non_bijective_date_states()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                LinkageCalibrationAuditExchangePolicy.UProbabilitySemantics,
                Is.EqualTo("CONDITIONED_ON_DEDUPLICATED_BLOCKING_CANDIDATE_UNION"));
            Assert.That(
                LinkageCalibrationAuditExchangePolicy.UnmappedOrNonBijectiveComparisonStates,
                Does.Contain("DAY_MONTH_SWAP"));
            Assert.That(
                LinkageCalibrationAuditExchangePolicy.UnmappedOrNonBijectiveComparisonStates,
                Does.Contain("CENTURY_SHIFT"));
            Assert.That(
                LinkageCalibrationAuditExchangePolicy.UnmappedOrNonBijectiveComparisonStates,
                Does.Contain("PARTIAL_COMPONENT_AGREEMENT"));
        });
    }

    [Test]
    public void Non_exportable_model_is_rejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            LinkageCalibrationAuditExchangePolicy.EnsureExportableModelStatus(Guid.NewGuid(), "RASCUNHO"));
        Assert.That(ex!.Message, Does.Contain("ATIVO ou VALIDADO"));
    }
}

using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9ControlledDevModelPinTests
{
    private static readonly string WinnerSha = new('a', 64);

    private static ProbabilisticLinkageModelRef Model(
        Guid? modelId = null, int version = 9, string? fingerprint = null)
    {
        var id = modelId ?? V9OperationalSqlModelConverter.ModelIdFromWinnerSha(WinnerSha);
        return new ProbabilisticLinkageModelRef(
            id, version,
            LinkageParameterCatalog.CompositeNominalDecisionEvidenceAlgorithmVersion,
            .99m, .1m)
        {
            BlockingContract = new ProbabilisticLinkageBlockingContractRef(
                "V9_CONTROLLED_DEV_" + WinnerSha,
                fingerprint ?? WinnerSha, "projection-v1", new string('b', 64))
        };
    }

    [Test]
    public void Exact_SHA_model_and_ruleset_are_required_before_run_creation()
    {
        Assert.DoesNotThrow((Action)(() =>
            V9ControlledDevSelection.VerifyRunModelPin(Model(), WinnerSha)));

        Assert.That((Action)(() => V9ControlledDevSelection.VerifyRunModelPin(
                Model(modelId: Guid.NewGuid()), WinnerSha)),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.EqualTo("RUNNER_V9_DEV_MODEL_PIN_MISMATCH"));
        Assert.That((Action)(() => V9ControlledDevSelection.VerifyRunModelPin(
                Model(version: 1), WinnerSha)),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.EqualTo("RUNNER_V9_DEV_MODEL_PIN_MISMATCH"));
        Assert.That((Action)(() => V9ControlledDevSelection.VerifyRunModelPin(
                Model(fingerprint: new string('b', 64)), WinnerSha)),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.EqualTo("RUNNER_V9_DEV_MODEL_PIN_MISMATCH"));
        Assert.That((Action)(() => V9ControlledDevSelection.VerifyRunModelPin(
                Model() with { BlockingContract = null }, WinnerSha)),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.EqualTo("RUNNER_V9_DEV_MODEL_PIN_MISMATCH"));
    }

    [Test]
    public void Invalid_winner_SHA_cannot_become_a_model_or_rule_set_pin()
    {
        Assert.That((Action)(() => V9ControlledDevSelection.VerifyRunModelPin(
                Model(), new string('A', 64))),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.EqualTo("RUNNER_V9_DEV_WINNER_SHA_INVALID"));
        Assert.That((Action)(() => V9ControlledDevSelection.VerifyRunModelPin(
                Model(), "bad")),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.EqualTo("RUNNER_V9_DEV_WINNER_SHA_INVALID"));
    }
}

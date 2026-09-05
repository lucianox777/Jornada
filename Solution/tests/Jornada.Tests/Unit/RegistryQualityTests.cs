using Jornada.Contracts;
using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class RegistryQualityTests
{
    [TestCase(100.00, "VALIDO")]
    [TestCase(0.00, "DIVERGENTE")]
    [TestCase(-1.00, "DIVERGENTE")]
    public void Positive_granted_value_rule_is_deterministic(double value, string expected)
    {
        var evaluator = new PositiveGrantedValueRegistryQcEvaluator("AA01", 1);
        var fact = BenefitFact((decimal)value);
        Assert.That(evaluator.Evaluate(fact).Resultado, Is.EqualTo(expected));
    }

    [Test]
    public void Implemented_qc_without_executable_evaluator_fails_closed()
    {
        var engine = new RegistryQualityEngine(Array.Empty<IRegistryQualityEvaluator>());
        Assert.Throws<InvalidDataException>(() => engine.Evaluate(Batch("IMPLEMENTADO"), BenefitFact(10m)));
    }

    [Test]
    public void Qc_not_implemented_does_not_invent_result_when_contract_is_coherent()
    {
        var engine = new RegistryQualityEngine(Array.Empty<IRegistryQualityEvaluator>());
        Assert.That(engine.Evaluate(Batch("NAO_IMPLEMENTADO"), BenefitFact(10m)), Is.Null);
    }

    [Test]
    public void Determined_term_requires_end_date()
    {
        var engine = new RegistryQualityEngine(Array.Empty<IRegistryQualityEvaluator>());
        var result = engine.Evaluate(Batch("NAO_IMPLEMENTADO", regimeVigencia: "PRAZO_DETERMINADO"), BenefitFact(10m));
        Assert.Multiple(() =>
        {
            Assert.That(result?.Resultado, Is.EqualTo("DIVERGENTE"));
            Assert.That(result?.RegraCodigo, Is.EqualTo("DATA_FIM_CONCESSAO_OBRIGATORIA_V1"));
        });
    }

    [Test]
    public void Indeterminate_term_accepts_absent_end_date()
    {
        var engine = new RegistryQualityEngine(Array.Empty<IRegistryQualityEvaluator>());
        Assert.That(engine.Evaluate(Batch("NAO_IMPLEMENTADO", regimeVigencia: "PRAZO_INDETERMINADO"), BenefitFact(10m)), Is.Null);
    }

    [Test]
    public void Concession_outside_versioned_window_is_divergent()
    {
        var engine = new RegistryQualityEngine(Array.Empty<IRegistryQualityEvaluator>());
        var result = engine.Evaluate(
            Batch("NAO_IMPLEMENTADO", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "PRAZO_INDETERMINADO"),
            BenefitFact(10m, dataInicioConcessao: new DateOnly(2027, 1, 1)));
        Assert.Multiple(() =>
        {
            Assert.That(result?.Resultado, Is.EqualTo("DIVERGENTE"));
            Assert.That(result?.RegraCodigo, Is.EqualTo("CONCESSAO_FORA_JANELA_V1"));
        });
    }

    [Test]
    public void Configured_concession_window_without_start_date_is_not_verifiable()
    {
        var engine = new RegistryQualityEngine(Array.Empty<IRegistryQualityEvaluator>());
        var result = engine.Evaluate(
            Batch("NAO_IMPLEMENTADO", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "PRAZO_INDETERMINADO"),
            BenefitFactWithoutStart(10m));
        Assert.Multiple(() =>
        {
            Assert.That(result?.Resultado, Is.EqualTo("NAO_VERIFICAVEL"));
            Assert.That(result?.RegraCodigo, Is.EqualTo("CONCESSAO_JANELA_NAO_VERIFICAVEL_V1"));
        });
    }

    [Test]
    public void Missing_regime_is_not_silently_treated_as_valid()
    {
        var engine = new RegistryQualityEngine(Array.Empty<IRegistryQualityEvaluator>());
        var result = engine.Evaluate(Batch("NAO_IMPLEMENTADO", regimeVigencia: null), BenefitFact(10m));
        Assert.That(result?.Resultado, Is.EqualTo("NAO_VERIFICAVEL"));
    }


    [Test]
    public void Ended_concession_requires_canonical_end_reason()
    {
        var engine = new RegistryQualityEngine(Array.Empty<IRegistryQualityEvaluator>());
        var fact = new ParsedFact("P1", "R1", RegistroOperacao.INCLUSAO, new string('a',64), new DateOnly(2026,1,1), new DateOnly(2026,6,30),
            null, null, null, null, "ENCERRADA", new DateOnly(2026,6,30), null, 100m, null, null);
        var result = engine.Evaluate(Batch("NAO_IMPLEMENTADO"), fact);
        Assert.That(result?.RegraCodigo, Is.EqualTo("MOTIVO_ENCERRAMENTO_INVALIDO_V1"));
    }

    [Test]
    public void Canonical_end_reason_is_accepted()
    {
        var engine = new RegistryQualityEngine(Array.Empty<IRegistryQualityEvaluator>());
        var fact = new ParsedFact("P1", "R1", RegistroOperacao.INCLUSAO, new string('a',64), new DateOnly(2026,1,1), new DateOnly(2026,6,30),
            null, null, null, null, "ENCERRADA", new DateOnly(2026,6,30), "TERMINO_REGULAR", 100m, null, null);
        Assert.That(engine.Evaluate(Batch("NAO_IMPLEMENTADO"), fact), Is.Null);
    }

    private static ParsedFact BenefitFact(decimal value, DateOnly? dataInicioConcessao = null, DateOnly? dataFimConcessao = null) =>
        new("P1", "R1", RegistroOperacao.INCLUSAO, new string('a',64), dataInicioConcessao ?? new DateOnly(2026, 6, 1), dataFimConcessao,
            null, null, null, null, "VIGENTE", null, null, value, null, null);

    private static ParsedFact BenefitFactWithoutStart(decimal value) =>
        new("P1", "R1", RegistroOperacao.INCLUSAO, new string('a',64), null, null,
            null, null, null, null, "VIGENTE", null, null, value, null, null);

    private static ReservedBatch Batch(
        string qcStatus,
        DateOnly? dataInicioPermitida = null,
        DateOnly? dataFimPermitida = null,
        string? regimeVigencia = "PRAZO_INDETERMINADO") => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test-worker", 1,
        "SMADS", 1, 1, "ASSISTENCIA", 1, IntegrationNature.BENEFICIO,
        1, 1, "AA01", 1, 1, DateTimeOffset.UtcNow, new string('a',64),
        "ENTREGA_SMADS_ASSISTENCIA_v2_" + new string('a',64) + ".zip", "sha256/aa/test.zip", 1,
        "config/contracts/registros/AA01/v1/pessoa.schema.json", new byte[32],
        "config/contracts/registros/AA01/v1/registro.schema.json", new byte[32], qcStatus, false,
        dataInicioPermitida, dataFimPermitida, regimeVigencia);
}

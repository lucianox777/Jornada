using System.Globalization;

namespace Jornada.Contracts;

/// <summary>
/// Pré-publicação fail-closed para novos modelos V8 com TF. Compara a MENOR
/// evidência nominal EXACT possível entre os prenomes publicados com HIGH
/// (que o runtime V8 atual não ajusta por TF), mantendo mães separadas.
/// Não altera scores, ranking, probabilidades, pesos ou modelos já persistidos.
/// </summary>
public static class LinkageTermFrequencyOrderingEnvelope
{
    public sealed record AttributeResult(
        string Attribute,
        double ExactBaseLogOdds,
        double WorstPublishedExactLogOdds,
        double HighLogOdds,
        double ExactMinusHigh,
        decimal MaximumPublishedFrequency)
    {
        public bool IsStrictlyOrdered => double.IsFinite(ExactMinusHigh) && ExactMinusHigh > 1e-12;
    }

    public static IReadOnlyList<AttributeResult> Assess(
        IReadOnlyDictionary<string, decimal> parameters,
        NominalTermFrequencySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!parameters.TryGetValue(LinkageParameterCatalog.TermFrequencyScoring, out var enabled)
            || enabled < 1m)
            throw new InvalidOperationException("Envelope exige V8 com TF habilitado.");
        var weight = Require(parameters, LinkageParameterCatalog.TermFrequencyWeight);
        var minimumU = Require(parameters, LinkageParameterCatalog.TermFrequencyMinimumU);
        if (weight < 0m || minimumU <= 0m || minimumU > 1m)
            throw new InvalidOperationException("Peso ou piso TF fora do domínio.");
        if (snapshot.PersonFirstNameCount == 0 || snapshot.MotherFirstNameCount == 0)
            throw new InvalidOperationException("Envelope exige as duas distribuições TF publicadas.");

        return new[]
        {
            AssessAttribute("NOME", snapshot.PersonFirstNames.Max(x => x.Frequency)),
            AssessAttribute("NOME_MAE", snapshot.MotherFirstNames.Max(x => x.Frequency))
        };

        AttributeResult AssessAttribute(string prefix, decimal maximumFrequency)
        {
            var mExact = Require(parameters, "M_" + prefix + "_EXACT");
            var uExact = Require(parameters, "U_" + prefix + "_EXACT");
            var mHigh = Require(parameters, "M_" + prefix + "_HIGH");
            var uHigh = Require(parameters, "U_" + prefix + "_HIGH");
            foreach (var probability in new[] { mExact, uExact, mHigh, uHigh })
                if (probability <= 0m || probability > 1m)
                    throw new InvalidOperationException("Probabilidade FS fora do domínio: " + prefix);

            var baseExact = Math.Log((double)(mExact / uExact));
            var high = Math.Log((double)(mHigh / uHigh));
            // O runtime não aplica TF a HIGH. Use a frequência MÁXIMA para
            // capturar o pior ajuste (mais negativo) aplicável a EXACT.
            var worst = baseExact + SplinkCompatibleTermFrequency.LogBayesAdjustment(
                maximumFrequency, maximumFrequency, uExact, weight, minimumU);
            return new AttributeResult(prefix, baseExact, worst, high,
                worst - high, maximumFrequency);
        }
    }

    /// <summary>
    /// Deve ser chamado antes de um novo rascunho V8/TF ser gravado.
    /// Não certifica taxa de falso vínculo, calibração, recall nem limiares.
    /// </summary>
    public static void RequireStrictOrdering(
        IReadOnlyDictionary<string, decimal> parameters,
        NominalTermFrequencySnapshot snapshot)
    {
        foreach (var result in Assess(parameters, snapshot))
        {
            if (!result.IsStrictlyOrdered)
                throw new InvalidOperationException(
                    "Coerência V8 TF: EXACT pode ficar abaixo/empatar HIGH para " +
                    result.Attribute + "; diferença mínima LLR=" +
                    result.ExactMinusHigh.ToString("R", CultureInfo.InvariantCulture) +
                    ". Não publicar parâmetros até recalibrar o modelo.");
        }
    }

    private static decimal Require(IReadOnlyDictionary<string, decimal> parameters, string name)
        => parameters.TryGetValue(name, out var value) ? value
            : throw new InvalidOperationException("Envelope V8 TF exige parâmetro " + name + ".");
}

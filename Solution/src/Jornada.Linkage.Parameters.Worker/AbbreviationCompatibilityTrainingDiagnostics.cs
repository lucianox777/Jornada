using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record AbbreviationCompatibilitySupport(
    long NameDenominator,
    long NameCompatible,
    long MotherNameDenominator,
    long MotherNameCompatible);

public static class AbbreviationCompatibilityTrainingDiagnostics
{
    public const string Version = "PTBR_ABBREVIATION_TRAINING_SUPPORT_V1";

    public static AbbreviationCompatibilitySupport Measure(IReadOnlyList<IdentityTrainingPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        long nameDenominator = 0;
        long nameCompatible = 0;
        long motherDenominator = 0;
        long motherCompatible = 0;

        foreach (var pair in pairs)
        {
            nameDenominator++;
            if (IdentityComparison.IsAbbreviationCompatible(pair.LeftName, pair.RightName))
                nameCompatible++;

            if (!string.IsNullOrWhiteSpace(pair.LeftMotherName) &&
                !string.IsNullOrWhiteSpace(pair.RightMotherName))
            {
                motherDenominator++;
                if (IdentityComparison.IsAbbreviationCompatible(pair.LeftMotherName, pair.RightMotherName))
                    motherCompatible++;
            }
        }

        return new AbbreviationCompatibilitySupport(
            nameDenominator,
            nameCompatible,
            motherDenominator,
            motherCompatible);
    }

    public static IReadOnlyDictionary<string, decimal> Append(
        IReadOnlyDictionary<string, decimal> parameters,
        IReadOnlyList<IdentityTrainingPair> matchedPairs,
        IReadOnlyList<IdentityTrainingPair> unmatchedPairs)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var m = Measure(matchedPairs);
        var u = Measure(unmatchedPairs);

        var result = new Dictionary<string, decimal>(parameters, StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.AbbreviationCompatibilityDiagnosticV1] = 1m,

            ["DIAG_ABBREV_M_NOME_DENOM"] = m.NameDenominator,
            ["DIAG_ABBREV_M_NOME_SUPPORT"] = m.NameCompatible,
            ["DIAG_ABBREV_M_NOME_RATE"] = Rate(m.NameCompatible, m.NameDenominator),

            ["DIAG_ABBREV_U_NOME_DENOM"] = u.NameDenominator,
            ["DIAG_ABBREV_U_NOME_SUPPORT"] = u.NameCompatible,
            ["DIAG_ABBREV_U_NOME_RATE"] = Rate(u.NameCompatible, u.NameDenominator),

            ["DIAG_ABBREV_M_NOME_MAE_DENOM"] = m.MotherNameDenominator,
            ["DIAG_ABBREV_M_NOME_MAE_SUPPORT"] = m.MotherNameCompatible,
            ["DIAG_ABBREV_M_NOME_MAE_RATE"] = Rate(m.MotherNameCompatible, m.MotherNameDenominator),

            ["DIAG_ABBREV_U_NOME_MAE_DENOM"] = u.MotherNameDenominator,
            ["DIAG_ABBREV_U_NOME_MAE_SUPPORT"] = u.MotherNameCompatible,
            ["DIAG_ABBREV_U_NOME_MAE_RATE"] = Rate(u.MotherNameCompatible, u.MotherNameDenominator),

            ["DIAG_ABBREV_NOME_OBSERVED_IN_BOTH_M_U"] = m.NameCompatible > 0 && u.NameCompatible > 0 ? 1m : 0m,
            ["DIAG_ABBREV_NOME_MAE_OBSERVED_IN_BOTH_M_U"] = m.MotherNameCompatible > 0 && u.MotherNameCompatible > 0 ? 1m : 0m,

            // O lado m é fonte-fonte CPF determinística. O lado u abaixo é apenas a amostra
            // Gold-Gold condicionada ao blocking já usada para missingness/nascimento.
            // Não é o u nominal operacional do nome (que vem do IBGE Monte Carlo) e,
            // portanto, não autoriza calcular LLR de ABBREV_COMPATIBLE.
            ["DIAG_ABBREV_M_REFERENCE_CPF_INTERGESTOR_V1"] = 1m,
            ["DIAG_ABBREV_U_REFERENCE_BLOCKING_GOLD_GOLD_V1"] = 1m
        };

        return result;
    }

    private static decimal Rate(long numerator, long denominator) =>
        denominator == 0 ? 0m : decimal.Divide(numerator, denominator);
}

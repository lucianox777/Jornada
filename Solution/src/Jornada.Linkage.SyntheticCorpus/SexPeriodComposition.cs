namespace Jornada.Linkage.SyntheticCorpus;

public enum SexPeriodCompositionKind
{
    ObservedJoint,
    IndependentMarginals
}

public sealed record SexPeriodCompositionPlan(
    SexPeriodCompositionKind Kind,
    string MethodVersion,
    int JointCellCount,
    int SexMarginalCellCount,
    int PeriodMarginalCellCount,
    string Declaration);

/// <summary>
/// Inspeciona as dimensões realmente publicadas antes de escolher a composição sexo × período.
/// Nunca chama marginais separadas de distribuição conjunta observada.
/// </summary>
public static class SexPeriodCompositionInspector
{
    public const string InspectionVersion = "SEX_PERIOD_DIMENSION_INSPECTION_V1";
    public const string ObservedJointVersion = "OBSERVED_SEX_PERIOD_JOINT_V1";
    public const string IndependentMarginalsVersion = "INDEPENDENT_SEX_PERIOD_MARGINALS_V1";

    public static SexPeriodCompositionPlan Inspect(IEnumerable<IbgeFrequencyRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var materialized = rows.ToArray();
        var joint = materialized.Count(row => IsSpecificSex(row.Sexo) && IsSpecificPeriod(row.PeriodoNascimento));
        var sexMarginal = materialized.Count(row => IsSpecificSex(row.Sexo) && !IsSpecificPeriod(row.PeriodoNascimento));
        var periodMarginal = materialized.Count(row => !IsSpecificSex(row.Sexo) && IsSpecificPeriod(row.PeriodoNascimento));

        if (joint > 0)
        {
            return new SexPeriodCompositionPlan(
                SexPeriodCompositionKind.ObservedJoint,
                ObservedJointVersion,
                joint,
                sexMarginal,
                periodMarginal,
                "sexo × período amostrado diretamente de células conjuntas publicadas");
        }

        if (sexMarginal > 0 && periodMarginal > 0)
        {
            return new SexPeriodCompositionPlan(
                SexPeriodCompositionKind.IndependentMarginals,
                IndependentMarginalsVersion,
                joint,
                sexMarginal,
                periodMarginal,
                "sexo e período compostos por marginais separadas; não representa distribuição conjunta observada");
        }

        throw new InvalidDataException(
            $"Projeções insuficientes para compor sexo × período: joint={joint}; sexo={sexMarginal}; periodo={periodMarginal}.");
    }

    private static bool IsSpecificSex(string? value)
        => value?.Trim().ToUpperInvariant() is "MASCULINO" or "FEMININO";

    private static bool IsSpecificPeriod(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return !string.IsNullOrWhiteSpace(normalized) && normalized != "TODOS";
    }
}

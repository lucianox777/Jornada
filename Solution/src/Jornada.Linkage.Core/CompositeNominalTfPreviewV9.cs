using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Experimental ONE-UNIT TF correction to ONE joint V9 nominal FS term.
/// Values and baseline U must come from a consistent externally versioned
/// calibration. This DTO is not a certified frequency snapshot or a model.
/// </summary>
public enum CompositeNominalTfSideV9 { Person, Mother }

public sealed record CompositeNominalTfPreviewV9(
    CompositeNominalTfSideV9 Side,
    NominalFrequencyReferenceContractV9 Reference,
    string MatchedNormalizedValue,
    decimal TermFrequency,
    decimal UnitReferenceU,
    decimal Weight,
    decimal MinimumFrequency,
    long MinimumIndependentSupport);

/// <summary>
/// Evaluates one, explicitly selected, level-specific TF correction in the
/// SAME FS scorer. Never applies a first-name marginal to full-name exact.
/// No second nominal m/u contribution is introduced; the only V9 joint
/// nominal LLR receives an optional additive correction. Two uncalibrated
/// person/mother adjustments are intentionally NOT summed.
/// </summary>
internal static class CompositeNominalTfPreviewEvaluatorV9
{
    internal static double Calculate(
        CompositeNominalLevelV9 personLevel,
        CompositeNominalLevelV9 motherLevel,
        string? leftPerson,
        string? rightPerson,
        string? leftMother,
        string? rightMother,
        CompositeNominalTfPreviewV9? input)
    {
        if (input is null)
            return 0d;
        if (!Enum.IsDefined(input.Side))
            throw new InvalidOperationException("TF V9 exige lado nominal pessoa ou mãe.");
        if (input.Weight < 0m || input.Weight > 1m
            || input.TermFrequency <= 0m || input.TermFrequency > 1m
            || input.UnitReferenceU <= 0m || input.UnitReferenceU > 1m
            || input.MinimumFrequency < 0m || input.MinimumFrequency > 1m
            || input.MinimumIndependentSupport <= 0
            || string.IsNullOrWhiteSpace(input.MatchedNormalizedValue))
            throw new InvalidOperationException("TF V9 exige frequência, peso e baseline U válidos.");

        // Explicitly zero TF weight means a neutral Bayes factor of 1.
        // It cannot be confused with a missing mandatory frequency of a
        // nonzero-weight model.
        if (input.Weight == 0m)
            return 0d;

        var level = input.Side == CompositeNominalTfSideV9.Person ? personLevel : motherLevel;
        CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
            level, input.Reference, input.MinimumIndependentSupport);
        var left = input.Side == CompositeNominalTfSideV9.Person ? leftPerson : leftMother;
        var right = input.Side == CompositeNominalTfSideV9.Person ? rightPerson : rightMother;
        var expectedUnit = CompositeNominalTermFrequencyContractV9.RequiredUnit(level)!.Value;
        var leftValue = ProjectUnit(left, expectedUnit);
        var rightValue = ProjectUnit(right, expectedUnit);
        if (leftValue is null
            || !string.Equals(leftValue, rightValue, StringComparison.Ordinal)
            || !string.Equals(leftValue, input.MatchedNormalizedValue, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "TF V9 deve referenciar o mesmo valor nominal normalizado nos dois lados.");

        // UnitReferenceU is an EXPLICIT level-compatible U reference from
        // calibration, never joint-cell U or historical U_NOME_EXACT.
        // Metadata cannot attest that the supplied values were actually
        // estimated: this pathway is strictly diagnostic/preview.
        return SplinkCompatibleTermFrequency.LogBayesAdjustment(
            input.TermFrequency, input.TermFrequency,
            input.UnitReferenceU, input.Weight, input.MinimumFrequency);
    }

    private static string? ProjectUnit(string? name, NominalFrequencyUnitV9 unit)
    {
        var projection = BrazilianNameComponents.Project(name);
        if (projection is null)
            return null;
        var tokens = projection.NormalizedFull.Split(
            ' ', StringSplitOptions.RemoveEmptyEntries);
        var firstIndex = projection.TitlePrefix is null ? 0 : 1;
        var first = firstIndex < tokens.Length
            ? tokens[firstIndex].Trim('.', ',') : "";
        var nonemptyFirst = first.Length == 0 ? null : first;
        return unit switch
        {
            NominalFrequencyUnitV9.FullName => projection.NormalizedFull,
            NominalFrequencyUnitV9.FirstLastJoint =>
                nonemptyFirst is not null && projection.LastContentSurname is not null
                    ? nonemptyFirst + "|" + projection.LastContentSurname : null,
            NominalFrequencyUnitV9.FirstName => nonemptyFirst,
            NominalFrequencyUnitV9.LastContentSurname => projection.LastContentSurname,
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
    }
}

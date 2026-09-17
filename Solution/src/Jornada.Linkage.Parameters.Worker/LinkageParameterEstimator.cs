using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record IdentityTrainingPair(string LeftName, DateOnly LeftBirthDate, string? LeftMotherName, string RightName, DateOnly RightBirthDate, string? RightMotherName, string? LeftSourceCode = null, string? RightSourceCode = null, IReadOnlyList<ResolutionSourceValue>? LeftResolutionValues = null, IReadOnlyList<ResolutionSourceValue>? RightResolutionValues = null);

public enum BirthScoringContract { SingleEvidenceV3, JointEvidenceV4, SemanticEvidenceV5 }

/// <summary>
/// Estima m/u com suavização. O contrato de nascimento e o contrato de decisão são
/// independentes: V6 torna ausência de nome da mãe um estado MISSING explícito e usa
/// margem em log-odds; modelos legados continuam condicionando NOME_MAE aos pares em
/// que o atributo existe nos dois lados, sem receber flags V6 por acidente.
/// </summary>
public static class LinkageParameterEstimator
{
    private static readonly NameComparisonState[] States = Enum.GetValues<NameComparisonState>();

    public static IReadOnlyDictionary<string, decimal> Estimate(
        IReadOnlyList<IdentityTrainingPair> matchedPairs,
        IReadOnlyList<IdentityTrainingPair> unmatchedPairs,
        long populationSize,
        long distinctBirthDates,
        decimal smoothingAlpha,
        decimal threshold,
        decimal conflictMargin,
        BirthScoringContract birthScoringContract = BirthScoringContract.SemanticEvidenceV5,
        bool decisionEvidenceV6 = true)
    {
        if (matchedPairs.Count == 0) throw new InvalidOperationException("Não há pares determinísticos suficientes para estimar probabilidades m.");
        if (unmatchedPairs.Count == 0) throw new InvalidOperationException("Não há pares não-match suficientes para estimar probabilidades u.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(smoothingAlpha);
        if (!Enum.IsDefined(birthScoringContract)) throw new ArgumentOutOfRangeException(nameof(birthScoringContract));
        // V6 operacional exige o contrato semântico de nascimento. Chamadores legados
        // (V3/V4, inclusive o piloto PostgreSQL) nunca recebem flags/estados V6 por acidente.
        decisionEvidenceV6 = decisionEvidenceV6 && birthScoringContract == BirthScoringContract.SemanticEvidenceV5;

        var matchedNameStates = matchedPairs.Select(p => IdentityComparison.CompareName(p.LeftName, p.RightName)).ToArray();
        var unmatchedNameStates = unmatchedPairs.Select(p => IdentityComparison.CompareName(p.LeftName, p.RightName)).ToArray();
        var matchedSemanticBirthStates = matchedPairs.Select(p => BirthDateSemanticEvidence.Classify(p.LeftBirthDate, p.RightBirthDate)).ToArray();
        var unmatchedSemanticBirthStates = unmatchedPairs.Select(p => BirthDateSemanticEvidence.Classify(p.LeftBirthDate, p.RightBirthDate)).ToArray();
        var matchedBirthStates = matchedPairs.Select(BirthAgreementMask).ToArray();
        var unmatchedBirthStates = unmatchedPairs.Select(BirthAgreementMask).ToArray();
        var scoringFlag = birthScoringContract switch
        {
            BirthScoringContract.SemanticEvidenceV5 => LinkageParameterCatalog.BirthSemanticEvidenceScoring,
            BirthScoringContract.JointEvidenceV4 => LinkageParameterCatalog.BirthJointEvidenceScoring,
            BirthScoringContract.SingleEvidenceV3 => LinkageParameterCatalog.BirthSingleEvidenceScoring,
            _ => throw new ArgumentOutOfRangeException(nameof(birthScoringContract))
        };

        var result = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.MatchedSampleSize] = matchedPairs.Count,
            [LinkageParameterCatalog.UnmatchedSampleSize] = unmatchedPairs.Count,
            ["SMOOTHING_ALPHA"] = smoothingAlpha,
            [LinkageParameterCatalog.Threshold] = threshold,
            [LinkageParameterCatalog.ConflictMargin] = conflictMargin,
            [scoringFlag] = 1m,
            [LinkageParameterCatalog.PriorMatchProbability] = EstimateReferencePrior(populationSize, distinctBirthDates),
            [LinkageParameterCatalog.PriorBlockMin] = 0.000001m,
            [LinkageParameterCatalog.PriorBlockMax] = 0.25m
        };
        if (decisionEvidenceV6)
        {
            result[LinkageParameterCatalog.LogOddsConflictMargin] = conflictMargin;
            result[LinkageParameterCatalog.DecisionEvidenceScoring] = 1m;
        }

        AddDistribution(result, "M_NOME", matchedNameStates, smoothingAlpha);
        AddDistribution(result, "U_NOME", unmatchedNameStates, smoothingAlpha);
        AddNameSupport(result, "SUPPORT_M_NOME", matchedNameStates);
        AddNameSupport(result, "SUPPORT_U_NOME", unmatchedNameStates);

        if (decisionEvidenceV6)
        {
            AddMotherDistributionV6(result, "M_NOME_MAE", matchedPairs, smoothingAlpha);
            AddMotherDistributionV6(result, "U_NOME_MAE", unmatchedPairs, smoothingAlpha);
            AddMotherSupportV6(result, "SUPPORT_M_NOME_MAE", matchedPairs);
            AddMotherSupportV6(result, "SUPPORT_U_NOME_MAE", unmatchedPairs);
        }
        else
        {
            var matchedMotherStates = PresentMotherNameComparisons(matchedPairs).ToArray();
            var unmatchedMotherStates = PresentMotherNameComparisons(unmatchedPairs).ToArray();
            AddDistribution(result, "M_NOME_MAE", matchedMotherStates, smoothingAlpha);
            AddDistribution(result, "U_NOME_MAE", unmatchedMotherStates, smoothingAlpha);
            AddNameSupport(result, "SUPPORT_M_NOME_MAE", matchedMotherStates);
            AddNameSupport(result, "SUPPORT_U_NOME_MAE", unmatchedMotherStates);
        }

        AddSemanticBirthDistribution(result, "M_NASCIMENTO_SEMANTICO", matchedSemanticBirthStates, smoothingAlpha);
        AddSemanticBirthDistribution(result, "U_NASCIMENTO_SEMANTICO", unmatchedSemanticBirthStates, smoothingAlpha);
        AddSemanticBirthSupport(result, "SUPPORT_M_NASCIMENTO_SEMANTICO", matchedSemanticBirthStates);
        AddSemanticBirthSupport(result, "SUPPORT_U_NASCIMENTO_SEMANTICO", unmatchedSemanticBirthStates);
        AddJointBirthDistribution(result, "M_NASCIMENTO_CONJUNTO", matchedBirthStates, smoothingAlpha);
        AddJointBirthDistribution(result, "U_NASCIMENTO_CONJUNTO", unmatchedBirthStates, smoothingAlpha);
        AddJointBirthSupport(result, "SUPPORT_M_NASCIMENTO_CONJUNTO", matchedBirthStates);
        AddJointBirthSupport(result, "SUPPORT_U_NASCIMENTO_CONJUNTO", unmatchedBirthStates);
        AddBinaryDistribution(result, "M_DATA_NASCIMENTO", matchedPairs.Select(p => p.LeftBirthDate == p.RightBirthDate), smoothingAlpha);
        AddBinaryDistribution(result, "U_DATA_NASCIMENTO", unmatchedPairs.Select(p => p.LeftBirthDate == p.RightBirthDate), smoothingAlpha);
        AddBinaryDistribution(result, "M_NASC_DIA", matchedPairs.Select(p => p.LeftBirthDate.Day == p.RightBirthDate.Day), smoothingAlpha);
        AddBinaryDistribution(result, "U_NASC_DIA", unmatchedPairs.Select(p => p.LeftBirthDate.Day == p.RightBirthDate.Day), smoothingAlpha);
        AddBinaryDistribution(result, "M_NASC_MES", matchedPairs.Select(p => p.LeftBirthDate.Month == p.RightBirthDate.Month), smoothingAlpha);
        AddBinaryDistribution(result, "U_NASC_MES", unmatchedPairs.Select(p => p.LeftBirthDate.Month == p.RightBirthDate.Month), smoothingAlpha);
        AddBinaryDistribution(result, "M_NASC_ANO", matchedPairs.Select(p => p.LeftBirthDate.Year == p.RightBirthDate.Year), smoothingAlpha);
        AddBinaryDistribution(result, "U_NASC_ANO", unmatchedPairs.Select(p => p.LeftBirthDate.Year == p.RightBirthDate.Year), smoothingAlpha);
        return result;
    }

    private static IEnumerable<NameComparisonState> PresentMotherNameComparisons(IEnumerable<IdentityTrainingPair> pairs)
    {
        foreach (var pair in pairs)
        {
            if (IdentityComparison.NormalizeText(pair.LeftMotherName) is null ||
                IdentityComparison.NormalizeText(pair.RightMotherName) is null)
                continue;
            yield return IdentityComparison.CompareName(pair.LeftMotherName, pair.RightMotherName);
        }
    }

    private static void AddMotherDistributionV6(IDictionary<string, decimal> target, string prefix, IEnumerable<IdentityTrainingPair> pairs, decimal alpha)
    {
        var counts = LinkageParameterCatalog.MotherNameStates.ToDictionary(s => s, _ => 0L, StringComparer.Ordinal);
        long total = 0;
        foreach (var p in pairs)
        {
            var state = MotherNameState(p);
            counts[state]++;
            total++;
        }
        var denominator = total + alpha * counts.Count;
        foreach (var state in LinkageParameterCatalog.MotherNameStates)
            target[$"{prefix}_{state}"] = (counts[state] + alpha) / denominator;
    }

    private static void AddMotherSupportV6(IDictionary<string, decimal> target, string prefix, IEnumerable<IdentityTrainingPair> pairs)
    {
        var counts = LinkageParameterCatalog.MotherNameStates.ToDictionary(s => s, _ => 0L, StringComparer.Ordinal);
        foreach (var pair in pairs)
            counts[MotherNameState(pair)]++;
        foreach (var state in LinkageParameterCatalog.MotherNameStates)
            target[$"{prefix}_{state}"] = counts[state];
    }

    private static string MotherNameState(IdentityTrainingPair pair) =>
        IdentityComparison.NormalizeText(pair.LeftMotherName) is null || IdentityComparison.NormalizeText(pair.RightMotherName) is null
            ? "MISSING"
            : IdentityComparison.CompareName(pair.LeftMotherName, pair.RightMotherName).ToString();

    private static void AddDistribution(IDictionary<string, decimal> target, string prefix, IEnumerable<NameComparisonState> values, decimal alpha)
    {
        var counts = States.ToDictionary(s => s, _ => 0L);
        long total = 0;
        foreach (var state in values)
        {
            counts[state]++;
            total++;
        }
        var denominator = total + alpha * States.Length;
        foreach (var state in States)
            target[$"{prefix}_{state}"] = (counts[state] + alpha) / denominator;
    }

    private static void AddNameSupport(IDictionary<string, decimal> target, string prefix, IEnumerable<NameComparisonState> values)
    {
        var counts = States.ToDictionary(s => s, _ => 0L);
        foreach (var state in values)
            counts[state]++;
        foreach (var state in States)
            target[$"{prefix}_{state}"] = counts[state];
    }

    private static void AddSemanticBirthDistribution(IDictionary<string, decimal> target, string prefix, IEnumerable<string> values, decimal alpha)
    {
        var counts = BirthDateSemanticEvidence.States.ToDictionary(s => s, _ => 0L, StringComparer.Ordinal);
        long total = 0;
        foreach (var state in values)
        {
            if (!counts.TryGetValue(state, out var count)) throw new InvalidOperationException($"Estado semântico de nascimento inválido: {state}.");
            counts[state] = count + 1;
            total++;
        }
        var denominator = total + alpha * counts.Count;
        foreach (var state in BirthDateSemanticEvidence.States)
            target[$"{prefix}_{state}"] = (counts[state] + alpha) / denominator;
    }

    private static void AddSemanticBirthSupport(IDictionary<string, decimal> target, string prefix, IEnumerable<string> values)
    {
        var counts = BirthDateSemanticEvidence.States.ToDictionary(s => s, _ => 0L, StringComparer.Ordinal);
        foreach (var state in values)
        {
            if (!counts.TryGetValue(state, out var count)) throw new InvalidOperationException($"Estado semântico de nascimento inválido: {state}.");
            counts[state] = count + 1;
        }
        foreach (var state in BirthDateSemanticEvidence.States)
            target[$"{prefix}_{state}"] = counts[state];
    }

    private static void AddJointBirthDistribution(IDictionary<string, decimal> target, string prefix, IEnumerable<byte> values, decimal alpha)
    {
        var counts = new long[LinkageParameterCatalog.BirthJointStates.Count];
        long total = 0;
        foreach (var value in values)
        {
            if (value >= counts.Length) throw new InvalidOperationException("Estado conjunto de nascimento inválido.");
            counts[value]++;
            total++;
        }
        var denominator = total + alpha * counts.Length;
        for (var i = 0; i < counts.Length; i++)
            target[$"{prefix}_{LinkageParameterCatalog.BirthJointStates[i]}"] = (counts[i] + alpha) / denominator;
    }

    private static void AddJointBirthSupport(IDictionary<string, decimal> target, string prefix, IEnumerable<byte> values)
    {
        var counts = new long[LinkageParameterCatalog.BirthJointStates.Count];
        foreach (var value in values)
        {
            if (value >= counts.Length) throw new InvalidOperationException("Estado conjunto de nascimento inválido.");
            counts[value]++;
        }
        for (var i = 0; i < counts.Length; i++)
            target[$"{prefix}_{LinkageParameterCatalog.BirthJointStates[i]}"] = counts[i];
    }

    private static byte BirthAgreementMask(IdentityTrainingPair p) =>
        (byte)((p.LeftBirthDate.Day == p.RightBirthDate.Day ? 1 : 0) |
               (p.LeftBirthDate.Month == p.RightBirthDate.Month ? 2 : 0) |
               (p.LeftBirthDate.Year == p.RightBirthDate.Year ? 4 : 0));

    private static void AddBinaryDistribution(IDictionary<string, decimal> target, string prefix, IEnumerable<bool> values, decimal alpha)
    {
        long exact = 0, total = 0;
        foreach (var value in values)
        {
            if (value) exact++;
            total++;
        }
        var probability = (exact + alpha) / (total + 2m * alpha);
        target[$"{prefix}_EXACT"] = probability;
        target[$"{prefix}_DIFF"] = 1m - probability;
    }

    private static decimal EstimateReferencePrior(long populationSize, long distinctBirthDates)
    {
        if (populationSize <= 0 || distinctBirthDates <= 0) return 0.001m;
        return Math.Clamp((decimal)distinctBirthDates / populationSize, 0.000001m, 0.25m);
    }
}

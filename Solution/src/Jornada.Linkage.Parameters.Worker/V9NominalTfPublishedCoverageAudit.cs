using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Aggregated, non-PII coverage diagnostic for the PINNED IBGE marginals.
/// Never imputes an unpublished name, uses TEST, selects a TF weight or turns
/// a missing positive TF into a neutral score. Its purpose is to identify
/// which side/unit blocks the official physical TRAIN/VALIDATION study.
/// </summary>
public static class V9NominalTfPublishedCoverageAudit
{
    public sealed record SlotCoverage(
        CompositeNominalTfSideV9 Side,
        NominalFrequencyUnitV9 Unit,
        long ApplicableValidationPairs,
        long SupportedValidationPairs,
        long MissingPublishedValidationPairs,
        long MissingPublishedTruePairs,
        long MissingPublishedNonmatchPairs);

    public static IReadOnlyList<SlotCoverage> Audit(
        IReadOnlyCollection<V9FsAdaptiveCandidatePair> validationPairs,
        FrozenIbgeNominalTfV9 reference,
        IReadOnlyCollection<(CompositeNominalTfSideV9 Side, NominalFrequencyUnitV9 Unit)> activeUnits)
    {
        ArgumentNullException.ThrowIfNull(validationPairs);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(activeUnits);
        if (validationPairs.Any(pair => pair is null || pair.Partition != "VALIDATION"))
            throw new InvalidDataException("Auditoria de cobertura TF aceita apenas VALIDATION, nunca TRAIN/TEST.");
        var summaries = new List<SlotCoverage>();
        foreach (var (side, unit) in activeUnits.Distinct()
            .OrderBy(slot => slot.Side).ThenBy(slot => slot.Unit))
        {
            if (!Enum.IsDefined(side) || unit is not (NominalFrequencyUnitV9.FirstName
                or NominalFrequencyUnitV9.LastContentSurname))
                throw new InvalidDataException("Unidade/lado nominal TF desconhecido na auditoria.");
            var frequencies = reference.FrequenciesForUnit(side, unit);
            long applicable = 0, supported = 0, missing = 0, missingM = 0, missingU = 0;
            foreach (var pair in validationPairs)
            {
                var left = side == CompositeNominalTfSideV9.Person ? pair.LeftPersonName : pair.LeftMotherName;
                var right = side == CompositeNominalTfSideV9.Person ? pair.RightPersonName : pair.RightMotherName;
                var level = CompositeNominalComparisonV9.Classify(left, right);
                if (V9NominalTfUnitSelection.ForLevel(level) != unit)
                    continue;
                applicable++;
                var leftTerm = V9TrainSqlNominalUCalibration.UnitToken(left, unit);
                var rightTerm = V9TrainSqlNominalUCalibration.UnitToken(right, unit);
                if (leftTerm is null || leftTerm != rightTerm)
                    throw new InvalidDataException("Auditoria TF encontrou unidade nominal divergente.");
                if (frequencies.TryGetValue(leftTerm, out var frequency) && frequency > 0m && frequency <= 1m)
                    supported++;
                else
                {
                    missing++;
                    if (pair.IsTruePair) missingM++;
                    else missingU++;
                }
            }
            summaries.Add(new SlotCoverage(side, unit, applicable, supported,
                missing, missingM, missingU));
        }
        return summaries.AsReadOnly();
    }

    /// <summary>
    /// Counts the UNION of candidates with any missing applicable positive-TF
    /// term. Unlike the per-slot sums, each SQL candidate pair appears once,
    /// even if person and mother BOTH lack a published IBGE frequency.
    /// These are validation candidate pairs, NOT accepted links, recovered
    /// identities or a pre-blocking true-pair universe.
    /// </summary>
    public sealed record UnionAbstentionImpact(
        long TrueCandidatePairs,
        long NonmatchCandidatePairs,
        long TrueCandidatePairsWithMissingFrequency,
        long NonmatchCandidatePairsWithMissingFrequency);

    public static UnionAbstentionImpact AuditUnion(
        IReadOnlyCollection<V9FsAdaptiveCandidatePair> validationPairs,
        FrozenIbgeNominalTfV9 reference,
        IReadOnlyCollection<(CompositeNominalTfSideV9 Side, NominalFrequencyUnitV9 Unit)> activeUnits)
    {
        ArgumentNullException.ThrowIfNull(validationPairs);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(activeUnits);
        if (validationPairs.Any(pair => pair is null || pair.Partition != "VALIDATION"))
            throw new InvalidDataException("Auditoria conjunta TF aceita apenas VALIDATION, nunca TRAIN/TEST.");
        var slots = activeUnits.Distinct().ToArray();
        foreach (var (side, unit) in slots)
            if (!Enum.IsDefined(side) || unit is not (NominalFrequencyUnitV9.FirstName
                or NominalFrequencyUnitV9.LastContentSurname))
                throw new InvalidDataException("Unidade/lado nominal TF desconhecido na auditoria conjunta.");

        long trueTotal = 0, nonmatchTotal = 0, trueMissing = 0, nonmatchMissing = 0;
        foreach (var pair in validationPairs)
        {
            bool missingAny = false;
            foreach (var (side, unit) in slots)
            {
                var left = side == CompositeNominalTfSideV9.Person
                    ? pair.LeftPersonName : pair.LeftMotherName;
                var right = side == CompositeNominalTfSideV9.Person
                    ? pair.RightPersonName : pair.RightMotherName;
                if (V9NominalTfTermCoverage.MissingPublishedFrequency(
                    unit, left, right, reference.FrequenciesForUnit(side, unit)))
                {
                    missingAny = true;
                    break; // union: never count this pair twice
                }
            }
            if (pair.IsTruePair)
            {
                trueTotal++;
                if (missingAny) trueMissing++;
            }
            else
            {
                nonmatchTotal++;
                if (missingAny) nonmatchMissing++;
            }
        }
        return new(trueTotal, nonmatchTotal, trueMissing, nonmatchMissing);
    }

    public static void RequireCompleteCoverage(IReadOnlyList<SlotCoverage> audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        var gaps = audit.Where(row => row.MissingPublishedValidationPairs > 0).ToArray();
        if (gaps.Length == 0)
            return;
        // No nominal values, plaintext identifiers, observation IDs or hashes
        // of individual names in logs: those can be brute-forced from a dictionary.
        throw new InvalidDataException(
            "TF positiva sem frequência IBGE publicada (VALIDATION; contagens agregadas, sem dados nominais): "
            + string.Join("; ", gaps.Select(row => $"{row.Side}/{row.Unit}: "
                + $"aplicaveis={row.ApplicableValidationPairs},"
                + $"publicados={row.SupportedValidationPairs},"
                + $"ausentes={row.MissingPublishedValidationPairs},"
                + $"M_ausentes={row.MissingPublishedTruePairs},"
                + $"U_ausentes={row.MissingPublishedNonmatchPairs}")));
    }
}

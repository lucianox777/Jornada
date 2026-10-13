using Jornada.Contracts;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// TRAIN-only diagnostic of the complete physical SQL candidate census.
/// All candidate pairs, including false ones, enter once after SQL DISTINCT.
/// This is an empirical conditioned candidate-frame table, NOT a calibrated
/// m/u/prior model and NOT evidence of independent pair observations.
/// </summary>
public static class V9SqlTrainCandidateMuAudit
{
    public const string Version = "V9_SQL_TRAIN_CONDITIONED_NOMINAL_MU_CENSUS_V1";
    public const string Status =
        "TRAIN_SQL_CANDIDATE_CENSUS_ONLY_NO_INDEPENDENT_SUPPORT_OR_CALIBRATED_PRIOR";

    public sealed record Cell(
        CompositeNominalLevelV9 Person,
        CompositeNominalLevelV9 Mother,
        long MatchedPairs, long UnmatchedPairs,
        decimal ObservedMShare, decimal ObservedUShare);

    public sealed record Report(
        string Version, string Status, string Partition,
        string SqlFrameFingerprint,
        long SqlCandidatePairs, long MatchedCandidatePairs,
        long UnmatchedCandidatePairs, decimal RawCandidateMatchShare,
        long MatchedBirthExact, long MatchedBirthDifferent, long MatchedBirthMissing,
        long UnmatchedBirthExact, long UnmatchedBirthDifferent, long UnmatchedBirthMissing,
        int CellsWithBothClassesObserved, int CellsWithoutBilateralObservedSupport,
        IReadOnlyList<Cell> Cells);

    /// <summary>
    /// Streaming accumulator: no pair IDs, names, keys, scores or fingerprints
    /// of nominal values are retained in the report. The caller must establish
    /// ground truth and deduplicate the physical SQL candidate union first.
    /// </summary>
    public sealed class Builder
    {
        private readonly string frame;
        private readonly Dictionary<(CompositeNominalLevelV9 Person,
            CompositeNominalLevelV9 Mother), (long M, long U)> counts;
        private long matched, unmatched;
        private long mExact, mDifferent, mMissing;
        private long uExact, uDifferent, uMissing;

        public Builder(string sqlFrameFingerprint)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sqlFrameFingerprint);
            frame = sqlFrameFingerprint;
            var levels = Enum.GetValues<CompositeNominalLevelV9>();
            counts = levels.SelectMany(person => levels.Select(mother =>
                    (Person: person, Mother: mother)))
                .ToDictionary(key => key, _ => (M: 0L, U: 0L));
        }

        public void Add(
            string partition, bool sameBaseIdentity,
            string? leftName, string? rightName,
            string? leftMother, string? rightMother,
            DateOnly? leftBirth, DateOnly? rightBirth)
        {
            if (partition != "TRAIN")
                throw new InvalidDataException("Auditoria m/u SQL aceita exclusivamente TRAIN.");
            var key = (
                CompositeNominalComparisonV9.Classify(leftName, rightName),
                CompositeNominalComparisonV9.Classify(leftMother, rightMother));
            if (!counts.TryGetValue(key, out var current))
                throw new InvalidDataException("Estado nominal V9 fora do contrato.");
            var missing = !leftBirth.HasValue || !rightBirth.HasValue;
            var exact = !missing && leftBirth == rightBirth;
            checked
            {
                if (sameBaseIdentity)
                {
                    matched++;
                    counts[key] = (current.M + 1, current.U);
                    if (missing) mMissing++;
                    else if (exact) mExact++;
                    else mDifferent++;
                }
                else
                {
                    unmatched++;
                    counts[key] = (current.M, current.U + 1);
                    if (missing) uMissing++;
                    else if (exact) uExact++;
                    else uDifferent++;
                }
            }
        }

        public Report Complete()
        {
            if (matched == 0 || unmatched == 0)
                throw new InvalidOperationException(
                    "Censo SQL TRAIN exige pares M e U para estimar participações observadas.");
            var cells = counts.OrderBy(x => x.Key.Person).ThenBy(x => x.Key.Mother)
                .Select(x => new Cell(
                    x.Key.Person, x.Key.Mother, x.Value.M, x.Value.U,
                    (decimal)x.Value.M / matched,
                    (decimal)x.Value.U / unmatched)).ToArray();
            if (cells.Length != 81 ||
                cells.Sum(x => x.MatchedPairs) != matched ||
                cells.Sum(x => x.UnmatchedPairs) != unmatched ||
                mExact + mDifferent + mMissing != matched ||
                uExact + uDifferent + uMissing != unmatched)
                throw new InvalidOperationException("Censo SQL m/u não reconciliado.");
            return new Report(
                Version, Status, "TRAIN", frame,
                checked(matched + unmatched), matched, unmatched,
                (decimal)matched / (matched + unmatched),
                mExact, mDifferent, mMissing, uExact, uDifferent, uMissing,
                cells.Count(x => x.MatchedPairs > 0 && x.UnmatchedPairs > 0),
                cells.Count(x => x.MatchedPairs == 0 || x.UnmatchedPairs == 0),
                Array.AsReadOnly(cells));
        }
    }
}

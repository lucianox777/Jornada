using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// One mutually exclusive joint cell for person and mother name evidence.
/// M/U probabilities are diagnostics from the supplied TRAIN frame, not
/// approved operational Fellegi-Sunter parameters or TF corrections.
/// </summary>
public sealed record CompositeNominalJointCellV9(
    CompositeNominalLevelV9 Person,
    CompositeNominalLevelV9 Mother,
    long MatchedSupport,
    long UnmatchedSupport,
    decimal M,
    decimal U);

public sealed record CompositeNominalJointCalibrationV9(
    string ContractVersion,
    string Status,
    string BlockingFrameFingerprint,
    string LabelProvenance,
    string TrainingPartitionFingerprint,
    long MatchedPairs,
    long UnmatchedPairs,
    int BothClassesObservedCells,
    int ZeroClassSupportCells,
    IReadOnlyList<CompositeNominalJointCellV9> Cells);

/// <summary>
/// Diagnostic distribution P(personLevel, motherLevel | M/U, sample).
/// Unlike multiplying two marginal nominal factors this preserves observed
/// correlations and the MISSING state as a joint, mutually exclusive cell.
/// NEVER promoted to a scorer by this class: sampling, temporal SQL blocking
/// membership, uncertainty and independent validation are still unproven.
/// </summary>
public static class CompositeNominalJointCalibrationEstimatorV9
{
    public const string Version = "COMPOSITE_NOMINAL_PERSON_MOTHER_JOINT_DIAGNOSTIC_V9_1";
    public const string Status = "DIAGNOSTIC_ONLY_JOINT_MU_NOT_CERTIFIED";

    private static readonly CompositeNominalLevelV9[] Levels =
        Enum.GetValues<CompositeNominalLevelV9>();

    public static CompositeNominalJointCalibrationV9 Estimate(
        CompositeNominalTrainingFrameV9 frame,
        decimal smoothingAlpha,
        int minimumPairsPerClass = 1)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(frame.MatchedPairs);
        ArgumentNullException.ThrowIfNull(frame.UnmatchedPairs);
        if (string.IsNullOrWhiteSpace(frame.BlockingFrameFingerprint)
            || string.IsNullOrWhiteSpace(frame.LabelProvenance)
            || string.IsNullOrWhiteSpace(frame.TrainingPartitionFingerprint))
            throw new InvalidOperationException("V9 joint exige proveniência TRAIN, rótulos e blocking explícita.");
        if (smoothingAlpha <= 0m || smoothingAlpha > 1000m)
            throw new ArgumentOutOfRangeException(nameof(smoothingAlpha));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumPairsPerClass);
        if (frame.MatchedPairs.Count < minimumPairsPerClass
            || frame.UnmatchedPairs.Count < minimumPairsPerClass)
            throw new InvalidOperationException("V9 joint exige suporte mínimo em ambas as classes.");

        var matched = Count(frame.MatchedPairs);
        var unmatched = Count(frame.UnmatchedPairs);
        var denominatorM = frame.MatchedPairs.Count + smoothingAlpha * Levels.Length * Levels.Length;
        var denominatorU = frame.UnmatchedPairs.Count + smoothingAlpha * Levels.Length * Levels.Length;
        var cells = new List<CompositeNominalJointCellV9>(Levels.Length * Levels.Length);

        foreach (var person in Levels)
        foreach (var mother in Levels)
        {
            var key = (person, mother);
            var m = matched.GetValueOrDefault(key);
            var u = unmatched.GetValueOrDefault(key);
            cells.Add(new CompositeNominalJointCellV9(
                person, mother, m, u,
                (m + smoothingAlpha) / denominatorM,
                (u + smoothingAlpha) / denominatorU));
        }

        return new CompositeNominalJointCalibrationV9(
            Version,
            Status,
            frame.BlockingFrameFingerprint,
            frame.LabelProvenance,
            frame.TrainingPartitionFingerprint,
            frame.MatchedPairs.Count,
            frame.UnmatchedPairs.Count,
            cells.Count(cell => cell.MatchedSupport > 0 && cell.UnmatchedSupport > 0),
            cells.Count(cell => cell.MatchedSupport == 0 || cell.UnmatchedSupport == 0),
            Array.AsReadOnly(cells.ToArray()));
    }

    /// <summary>
    /// Evidence sufficiency gate, NOT authorization to compute any score.
    /// A smoothed but unobserved class must never silently count as support.
    /// </summary>
    public static CompositeNominalJointCellV9 RequireObservedInBothClasses(
        CompositeNominalJointCalibrationV9 estimate,
        CompositeNominalLevelV9 person,
        CompositeNominalLevelV9 mother,
        long minimumSupportPerClass)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumSupportPerClass);
        if (estimate.ContractVersion != Version || estimate.Status != Status
            || estimate.Cells is null || estimate.Cells.Count != Levels.Length * Levels.Length
            || !Enum.IsDefined(person) || !Enum.IsDefined(mother))
            throw new InvalidOperationException("Contrato diagnóstico V9 joint incompatível.");
        var cell = estimate.Cells.Single(c => c.Person == person && c.Mother == mother);
        if (cell.MatchedSupport < minimumSupportPerClass
            || cell.UnmatchedSupport < minimumSupportPerClass)
            throw new InvalidOperationException("Célula V9 joint carece de suporte observado nas duas classes.");
        return cell;
    }

    private static Dictionary<(CompositeNominalLevelV9 Person, CompositeNominalLevelV9 Mother), long> Count(
        IEnumerable<IdentityTrainingPair> pairs)
    {
        var counts = new Dictionary<(CompositeNominalLevelV9, CompositeNominalLevelV9), long>();
        foreach (var pair in pairs)
        {
            if (pair is null)
                throw new InvalidOperationException("Par TRAIN V9 joint nulo.");
            var person = CompositeNominalComparisonV9.Classify(pair.LeftName, pair.RightName);
            var mother = CompositeNominalComparisonV9.Classify(pair.LeftMotherName, pair.RightMotherName);
            var key = (person, mother);
            counts.TryGetValue(key, out var support);
            checked { counts[key] = support + 1; }
        }
        return counts;
    }
}

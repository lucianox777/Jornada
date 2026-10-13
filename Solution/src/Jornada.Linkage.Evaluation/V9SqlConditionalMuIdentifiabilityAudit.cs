using Jornada.Contracts;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Computes empirical TRAIN SQL M/U conditional shares only as observations,
/// NEVER smoothing absent cells into positive model parameters. Distinct base
/// groups are not independently sampled units; a snapshot is not certifiable
/// until independent support, uncertainty and validation have been demonstrated.
/// </summary>
public static class V9SqlConditionalMuIdentifiabilityAudit
{
    public const string Version = "V9_SQL_TRAIN_MU_EMPIRICAL_IDENTIFIABILITY_NO_WEIGHTS_V1";
    public const string Status =
        "INCONCLUSIVE_NO_INDEPENDENT_ESS_PRIOR_TF_VALIDATION_OR_OPERATIONAL_WEIGHTS";

    public sealed record JointCell(
        CompositeNominalLevelV9 PersonLevel,
        CompositeNominalLevelV9 MotherLevel,
        long MObservationPairs, long UObservationPairs,
        int DistinctMBasePeople, int DistinctUBasePairs, int DistinctUBasePeople,
        decimal EmpiricalMShareOfSqlCandidateM,
        decimal EmpiricalUShareOfSqlCandidateU,
        decimal? DescriptiveObservedPairRatio,
        string Identifiability);

    public sealed record BirthCell(
        string State, long MObservationPairs, long UObservationPairs,
        int DistinctMBasePeople, int DistinctUBasePairs,
        decimal EmpiricalMShareOfSqlCandidateM,
        decimal EmpiricalUShareOfSqlCandidateU,
        string Identifiability);

    public sealed record Report(
        string Version, string Status, string Partition,
        string NominalSupportVersion, string BirthSupportVersion,
        int UniqueTrainBasePeople,
        long SqlCandidateM, long SqlCandidateU,
        decimal EmpiricalPairMatchShareNotPopulationPrior,
        int JointCellsWithBothObserved, int JointCellsMissingEitherClass,
        int JointCellsWithoutAnyObservation,
        int BirthStatesWithBothObserved, int BirthStatesMissingEitherClass,
        bool MayPromoteModelParameters,
        IReadOnlyList<JointCell> JointCells,
        IReadOnlyList<BirthCell> BirthCells);

    public static Report Analyze(
        V9SqlConditionedNominalSupportAudit.Report nominal,
        V9SqlConditionedBirthSupportAudit.Report birth,
        V9SyntheticBaseFrequencyAudit.Report population)
    {
        ArgumentNullException.ThrowIfNull(nominal);
        ArgumentNullException.ThrowIfNull(birth);
        ArgumentNullException.ThrowIfNull(population);
        if (nominal.Version != V9SqlConditionedNominalSupportAudit.Version
            || birth.Version != V9SqlConditionedBirthSupportAudit.Version
            || population.Version != V9SyntheticBaseFrequencyAudit.Version
            || nominal.Partition != "TRAIN" || birth.Partition != "TRAIN"
            || population.Partition != "TRAIN"
            || nominal.SqlCandidateM < 1 || nominal.SqlCandidateU < 1
            || nominal.SqlCandidatePairs != nominal.SqlCandidateM + nominal.SqlCandidateU
            || birth.SqlCandidateM != nominal.SqlCandidateM
            || birth.SqlCandidateU != nominal.SqlCandidateU
            || birth.SqlCandidatePairs != nominal.SqlCandidatePairs
            || population.TrainingBasePeople < 1)
            throw new InvalidDataException("Suportes SQL de m/u e massa-base TRAIN incompatíveis.");

        var n = population.TrainingBasePeople;
        static string State(long m, long u) =>
            m == 0 && u == 0 ? "UNOBSERVED_BOTH_NOT_NEUTRAL" :
            m == 0 ? "UNOBSERVED_M_NOT_NEUTRAL" :
            u == 0 ? "UNOBSERVED_U_NOT_NEUTRAL" :
            "BOTH_OBSERVED_PAIR_SHARES_NOT_INDEPENDENT_ESS";

        if (nominal.Cells is null || nominal.Cells.Count != 81
            || birth.SemanticCells is null || birth.SemanticCells.Count != 7
            || nominal.Cells.Select(x => (x.PersonLevel, x.MotherLevel)).Distinct().Count() != 81
            || birth.SemanticCells.Select(x => x.SemanticBirthState).Distinct(StringComparer.Ordinal).Count() != 7
            || nominal.Cells.Sum(x => x.MCount) != nominal.SqlCandidateM
            || nominal.Cells.Sum(x => x.UCount) != nominal.SqlCandidateU
            || birth.SemanticCells.Sum(x => x.MCount) != nominal.SqlCandidateM
            || birth.SemanticCells.Sum(x => x.UCount) != nominal.SqlCandidateU)
            throw new InvalidDataException("Estados conjuntos e nascimento não exaustivos ou sem conciliação.");

        void Guard(long m, long u, int mGroups, int uPairs, int uPeople)
        {
            if (m < 0 || u < 0 || mGroups < 0 || uPairs < 0 || uPeople < 0
                || mGroups > m || mGroups > n
                || uPairs > u || uPeople > n || uPeople > 2L * uPairs
                || (m == 0 && mGroups != 0)
                || (m > 0 && mGroups == 0)
                || (u == 0 && (uPairs != 0 || uPeople != 0))
                || (u > 0 && (uPairs == 0 || uPeople < 2)))
                throw new InvalidDataException("Suporte por pessoa-base inválido para estimação empírica.");
        }

        var joint = nominal.Cells.Select(x =>
        {
            Guard(x.MCount, x.UCount, x.DistinctMBasePeople,
                x.DistinctUBasePairs, x.DistinctUBasePeople);
            var m = decimal.Divide(x.MCount, nominal.SqlCandidateM);
            var u = decimal.Divide(x.UCount, nominal.SqlCandidateU);
            // A zero observed count is NOT a validated m/u parameter, and
            // the two shares cannot be promoted as an FS Bayes factor.
            decimal? ratio = x.MCount > 0 && x.UCount > 0
                ? decimal.Divide(m, u) : null;
            return new JointCell(x.PersonLevel, x.MotherLevel,
                x.MCount, x.UCount, x.DistinctMBasePeople,
                x.DistinctUBasePairs, x.DistinctUBasePeople,
                m, u, ratio, State(x.MCount, x.UCount));
        }).OrderBy(x => x.PersonLevel).ThenBy(x => x.MotherLevel).ToArray();

        var birthCells = birth.SemanticCells.Select(x =>
        {
            Guard(x.MCount, x.UCount, x.DistinctMBasePeople,
                x.DistinctUBasePairs, x.DistinctUBasePeople);
            return new BirthCell(x.SemanticBirthState, x.MCount, x.UCount,
                x.DistinctMBasePeople, x.DistinctUBasePairs,
                decimal.Divide(x.MCount, nominal.SqlCandidateM),
                decimal.Divide(x.UCount, nominal.SqlCandidateU),
                State(x.MCount, x.UCount));
        }).OrderBy(x => x.State, StringComparer.Ordinal).ToArray();

        var jointBoth = joint.Count(x => x.MObservationPairs > 0 && x.UObservationPairs > 0);
        var birthBoth = birthCells.Count(x => x.MObservationPairs > 0 && x.UObservationPairs > 0);
        if (nominal.CellsWithBothMAndU != jointBoth
            || birth.StatesWithBothMAndU != birthBoth
            || birth.BirthExactM + birth.BirthDifferentM != birth.SqlCandidateM
            || birth.BirthExactU + birth.BirthDifferentU != birth.SqlCandidateU
            || Math.Abs(joint.Sum(x => x.EmpiricalMShareOfSqlCandidateM) - 1m) > .00000000000000000000001m
            || Math.Abs(joint.Sum(x => x.EmpiricalUShareOfSqlCandidateU) - 1m) > .00000000000000000000001m)
            throw new InvalidDataException("As participações m/u empíricas não normalizam.");

        return new Report(
            Version, Status, "TRAIN", nominal.Version, birth.Version,
            n, nominal.SqlCandidateM, nominal.SqlCandidateU,
            decimal.Divide(nominal.SqlCandidateM, nominal.SqlCandidatePairs),
            jointBoth, 81-jointBoth,
            joint.Count(x => x.MObservationPairs == 0 && x.UObservationPairs == 0),
            birthBoth, 7-birthBoth,
            false,
            Array.AsReadOnly(joint), Array.AsReadOnly(birthCells));
    }
}

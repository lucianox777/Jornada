using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// A defensive, frozen copy of the complete 81-state V9 nominal M/U table.
/// Validation and natural-log Bayes factors are calculated ONCE per model
/// preview, rather than once for every candidate in the same block.
/// This is a mathematical input snapshot, NOT a certified model, calibrated
/// TF reference, or authorization to route AlgorithmVersion=V9.
/// </summary>
internal sealed class FrozenFellegiSunterJointNominalV9
{
    private readonly Dictionary<(CompositeNominalLevelV9 Person, CompositeNominalLevelV9 Mother),
        FrozenNominalStateV9> _states;

    private FrozenFellegiSunterJointNominalV9(
        Dictionary<(CompositeNominalLevelV9, CompositeNominalLevelV9), FrozenNominalStateV9> states)
    {
        _states = states;
    }

    internal static FrozenFellegiSunterJointNominalV9 Freeze(
        IReadOnlyCollection<FellegiSunterJointNominalCellV9> jointCells,
        string nominalContractVersion)
    {
        ArgumentNullException.ThrowIfNull(jointCells);
        if (!string.Equals(nominalContractVersion,
            CompositeNominalComparisonV9.ContractVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("Contrato de comparação nominal V9 não é compatível.");

        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        if (jointCells.Count != levels.Length * levels.Length)
            throw new InvalidOperationException("V9 exige os 81 estados conjuntos nominais completos.");

        var states = new Dictionary<
            (CompositeNominalLevelV9, CompositeNominalLevelV9), FrozenNominalStateV9>(jointCells.Count);
        decimal sumM = 0m, sumU = 0m;
        foreach (var cell in jointCells)
        {
            if (cell is null || !Enum.IsDefined(cell.Person) || !Enum.IsDefined(cell.Mother)
                || cell.M <= 0m || cell.M > 1m || cell.U <= 0m || cell.U > 1m)
                throw new InvalidOperationException(
                    "V9 exige estados exclusivos e probabilidades válidas em M/U.");

            var llr = Math.Log((double)cell.M / (double)cell.U);
            if (!double.IsFinite(llr))
                throw new InvalidOperationException("V9 nominal produziu contribuição não finita.");
            if (!states.TryAdd(
                (cell.Person, cell.Mother),
                new FrozenNominalStateV9(cell.Person, cell.Mother, cell.M, cell.U, llr)))
                throw new InvalidOperationException("V9 exige estados nominais exclusivos.");

            sumM += cell.M;
            sumU += cell.U;
        }

        if (states.Count != levels.Length * levels.Length
            || Math.Abs(sumM - 1m) > 0.000001m
            || Math.Abs(sumU - 1m) > 0.000001m)
            throw new InvalidOperationException(
                "V9 exige distribuições conjuntas M/U normalizadas e completas.");

        return new FrozenFellegiSunterJointNominalV9(states);
    }

    internal FrozenNominalStateV9 Classify(
        string? leftPersonName,
        string? rightPersonName,
        string? leftMotherName,
        string? rightMotherName)
    {
        var person = CompositeNominalComparisonV9.Classify(leftPersonName, rightPersonName);
        var mother = CompositeNominalComparisonV9.Classify(leftMotherName, rightMotherName);
        if (!_states.TryGetValue((person, mother), out var state))
            throw new InvalidOperationException("Estado nominal V9 não consta do snapshot congelado.");
        return state;
    }
}

/// <summary>
/// Immutable values only; the original caller-owned collection and objects
/// are never retained. No name or candidate identifiers are stored.
/// </summary>
internal readonly record struct FrozenNominalStateV9(
    CompositeNominalLevelV9 Person,
    CompositeNominalLevelV9 Mother,
    decimal M,
    decimal U,
    double LogLikelihoodRatio);

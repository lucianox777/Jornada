using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Reverse audit of every fitted cell; smoothed probability is not observed support.</summary>
public static class V9FittedJointSupportAudit
{
    public sealed record Cell(CompositeNominalLevelV9 Person, CompositeNominalLevelV9 Mother,
        long ObservedMPairs, long ObservedUPairs);
    public sealed record Report(string SnapshotSha256, decimal SmoothingAlpha, long TrainMPairs,
        long TrainUPairs, int CellsWithM, int CellsWithU, int CellsWithBoth, int CellsWithNeither,
        IReadOnlyList<Cell> Cells, bool IndependentIdentitySupportEstablished = false);

    public static Report Reconstruct(V9FittedSnapshot snapshot, decimal smoothingAlpha,
        long physicalTrainMPairs, long physicalTrainUPairs)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (smoothingAlpha <= 0 || physicalTrainMPairs < 2 || physicalTrainUPairs < 2)
            throw new InvalidDataException("Auditoria exige alpha e contagens físicas M/U de TRAIN.");
        var declared = snapshot.Payload.JointSupport;
        if (declared is not null && (declared.SmoothingAlpha != smoothingAlpha
            || declared.TotalM != physicalTrainMPairs || declared.TotalU != physicalTrainUPairs))
            throw new InvalidDataException("Contagens físicas divergem da proveniência conjunta/marginal.");
        // V3 persists raw support explicitly. Backoff probabilities cannot be inverted
        // as if they were Laplace counts. Legacy V2 remains independently auditable.
        var cells = declared is not null
            ? declared.Cells.Select(c => new Cell(c.Person, c.Mother, c.ObservedM, c.ObservedU)).ToArray()
            : snapshot.Payload.JointCells.Select(cell => new Cell(cell.Person, cell.Mother,
                Invert(cell.M, physicalTrainMPairs), Invert(cell.U, physicalTrainUPairs))).ToArray();
        if (cells.Length != 81 || cells.Sum(x => x.ObservedMPairs) != physicalTrainMPairs
            || cells.Sum(x => x.ObservedUPairs) != physicalTrainUPairs
            || Math.Abs(snapshot.Payload.Parameters[LinkageParameterCatalog.PriorMatchProbability]
                - (physicalTrainMPairs + smoothingAlpha) / (physicalTrainMPairs + physicalTrainUPairs + 2 * smoothingAlpha)) > .00000000000000000001m)
            throw new InvalidDataException("Snapshot e contagens físicas não reconciliam.");
        return new(snapshot.ContentSha256, smoothingAlpha, physicalTrainMPairs, physicalTrainUPairs,
            cells.Count(x => x.ObservedMPairs > 0), cells.Count(x => x.ObservedUPairs > 0),
            cells.Count(x => x.ObservedMPairs > 0 && x.ObservedUPairs > 0),
            cells.Count(x => x.ObservedMPairs == 0 && x.ObservedUPairs == 0), Array.AsReadOnly(cells));

        long Invert(decimal probability, long total)
        {
            var raw = probability * (total + 81 * smoothingAlpha) - smoothingAlpha;
            var rounded = decimal.Round(raw, 0, MidpointRounding.ToEven);
            if (rounded < 0 || Math.Abs(raw - rounded) > .00000000000000000001m)
                throw new InvalidDataException("Probabilidade não corresponde à suavização/contagem declarada.");
            return checked((long)rounded);
        }
    }
}

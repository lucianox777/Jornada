namespace Jornada.Linkage.Runner;

/// <summary>
/// Aggregate-only, source-deduplicated audit of *physical publication events*
/// across Gold TRAIN manager waves and the final reevaluation. A later attempt
/// must never erase a creation already performed by an earlier run.
/// No identity labels, source identifiers or Gold UUIDs are exported.
/// </summary>
public sealed class V9GoldPhysicalCreationAudit
{
    private readonly HashSet<long> _proposals = [];
    private readonly HashSet<long> _created = [];
    private readonly HashSet<long> _falseCreated = [];
    private readonly HashSet<long> _lowerCreated = [];
    private readonly HashSet<long> _falseLowerCreated = [];

    public sealed record Counts(long LowerBandProposals, long LowerBandCreated,
        long FalseNewIdentityCreations, long FalseLowerBandCreations);

    /// <param name="sourceId">Local-only stable observation ID, never serialized.</param>
    /// <param name="trueIdentityRepresentedBeforeRun">
    /// Ground truth evaluated only AFTER the runner has scored and published.
    /// </param>
    public void Observe(long sourceId, bool trueIdentityRepresentedBeforeRun,
        bool lowerBandProposal, string publicationResult)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceId);
        if (publicationResult is not ("NOVA_IDENTIDADE" or "ASSOCIACAO_EXISTENTE" or "INDEFINIDA"))
            throw new InvalidDataException("Publicação Gold desconhecida no ledger físico.");

        if (lowerBandProposal)
            _proposals.Add(sourceId);
        if (publicationResult != "NOVA_IDENTIDADE")
            return;

        _created.Add(sourceId);
        if (trueIdentityRepresentedBeforeRun)
            _falseCreated.Add(sourceId);
        if (lowerBandProposal)
        {
            _lowerCreated.Add(sourceId);
            if (trueIdentityRepresentedBeforeRun)
                _falseLowerCreated.Add(sourceId);
        }
    }

    public Counts Snapshot() =>
        new(_proposals.Count, _lowerCreated.Count, _falseCreated.Count, _falseLowerCreated.Count);
}

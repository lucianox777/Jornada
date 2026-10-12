using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// A DECLARED, non-operational model-to-81-cell binding for mathematical V9 previews.
/// These references and the expected digest must come from an independently governed
/// manifest in a future increment; constructing this record is NOT attestation.
/// </summary>
internal sealed record V9NominalPreviewBindingManifest(
    Guid ModelId,
    long ModelVersion,
    string NominalContractVersion,
    string ExpectedContentFingerprint,
    string FrameFingerprint,
    string SelectionFingerprint,
    string LabelingReference,
    string PartitionReference,
    string EvidenceGeneration,
    string TermFrequencyStatus,
    string Status);

/// <summary>
/// Preflight that refuses accidental cross-model or cross-snapshot preview scoring.
/// Never persists, certifies, activates or routes AlgorithmVersion=V9.
/// No TF is computed: there is no compatible certified frequency distribution yet.
/// </summary>
internal sealed class BoundV9NominalPreviewModel
{
    internal const string DiagnosticOnlyStatus = "DIAGNOSTIC_ONLY_NOT_CERTIFIED";
    internal const string NoTermFrequencyStatus = "TF_NOT_CALIBRATED";

    private readonly FrozenFellegiSunterJointNominalV9 _frozen;

    internal V9NominalPreviewBindingManifest Manifest { get; }
    internal string ContentFingerprint => _frozen.ContentFingerprint;

    private BoundV9NominalPreviewModel(
        V9NominalPreviewBindingManifest manifest,
        FrozenFellegiSunterJointNominalV9 frozen)
    {
        Manifest = manifest;
        _frozen = frozen;
    }

    internal static BoundV9NominalPreviewModel Bind(
        V9NominalPreviewBindingManifest manifest,
        IReadOnlyCollection<FellegiSunterJointNominalCellV9> cells)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(cells);
        if (manifest.ModelId == Guid.Empty || manifest.ModelVersion <= 0
            || !string.Equals(manifest.NominalContractVersion,
                CompositeNominalComparisonV9.ContractVersion, StringComparison.Ordinal)
            || !string.Equals(manifest.Status, DiagnosticOnlyStatus, StringComparison.Ordinal)
            || !string.Equals(manifest.TermFrequencyStatus, NoTermFrequencyStatus, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(manifest.FrameFingerprint)
            || string.IsNullOrWhiteSpace(manifest.SelectionFingerprint)
            || string.IsNullOrWhiteSpace(manifest.LabelingReference)
            || string.IsNullOrWhiteSpace(manifest.PartitionReference)
            || string.IsNullOrWhiteSpace(manifest.EvidenceGeneration)
            || !IsLowercaseSha256(manifest.ExpectedContentFingerprint))
            throw new InvalidOperationException(
                "Manifesto V9 preview inválido ou sem referências obrigatórias.");

        var frozen = FrozenFellegiSunterJointNominalV9.Freeze(
            cells, manifest.NominalContractVersion);
        if (!string.Equals(
            frozen.ContentFingerprint, manifest.ExpectedContentFingerprint,
            StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Snapshot nominal V9 diverge do digest vinculado à versão do modelo.");

        return new BoundV9NominalPreviewModel(manifest, frozen);
    }

    internal FellegiSunterScoreBreakdown ScorePreview(
        IReadOnlyDictionary<string, decimal> parameters,
        string? leftPersonName,
        string? rightPersonName,
        string? leftMotherName,
        string? rightMotherName,
        int? blockCandidateCount = null,
        DateOnly? leftBirthDate = null,
        DateOnly? rightBirthDate = null) =>
        FellegiSunterScoring.CalculateFrozenCompositeNominalV9PreviewWithBreakdown(
            parameters, leftPersonName, rightPersonName,
            leftMotherName, rightMotherName, _frozen,
            blockCandidateCount, leftBirthDate, rightBirthDate);

    private static bool IsLowercaseSha256(string? value) =>
        value is { Length: 64 } && value.All(static c =>
            c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

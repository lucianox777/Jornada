namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Fail-closed validation for a future manually published, immutable V9 fit
/// reference. Validation alone neither loads nor activates the model.
/// </summary>
public static class V9CalibrationSnapshotManifestGuard
{
    public sealed record Manifest(string Version, string ModelFamily, string AdapterVersion,
        string FitSha256, string TrainCorpusSha256, string ValidationCorpusSha256,
        string IndependentTrainGroupsSha256, string IndependentValidationGroupsSha256,
        bool ManuallyApproved, bool UsesTestLabels, bool AutomaticRecalibrationRequested);

    public const string Version = "V9_FROZEN_FIT_REF_MANIFEST_V1";

    public static void ValidateForBootstrap(Manifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Version != Version || string.IsNullOrWhiteSpace(manifest.ModelFamily)
            || string.IsNullOrWhiteSpace(manifest.AdapterVersion)
            || !manifest.ManuallyApproved || manifest.UsesTestLabels
            || manifest.AutomaticRecalibrationRequested)
            throw new InvalidDataException("Snapshot V9 não aprovado ou bootstrap inseguro.");
        foreach (var sha in new[] { manifest.FitSha256, manifest.TrainCorpusSha256,
                     manifest.ValidationCorpusSha256, manifest.IndependentTrainGroupsSha256,
                     manifest.IndependentValidationGroupsSha256 })
            if (sha is null || sha.Length != 64 || sha.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("Proveniência SHA-256 obrigatória.");
        if (manifest.TrainCorpusSha256.Equals(manifest.ValidationCorpusSha256,
                StringComparison.OrdinalIgnoreCase)
            || manifest.IndependentTrainGroupsSha256.Equals(
                manifest.IndependentValidationGroupsSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("TRAIN e VALIDATION devem ser independentes.");
    }
}

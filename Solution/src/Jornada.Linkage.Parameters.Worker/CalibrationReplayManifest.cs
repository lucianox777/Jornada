using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Parameters.Worker;

public enum CalibrationSourceKind
{
    PersonData,
    TrainingCorpus,
    ExternalReference
}

/// <summary>
/// Identidade imutável de um conjunto de dados usado por uma calibração.
/// SourceVersion é a versão lógica declarada pela fonte; ContentFingerprint identifica o
/// conteúdo efetivamente consumido. ParserVersion congela a interpretação quando aplicável.
/// </summary>
public sealed record CalibrationSourceSnapshot(
    CalibrationSourceKind Kind,
    string SourceId,
    string SourceVersion,
    string ContentFingerprint,
    string? ParserVersion = null)
{
    public string CanonicalSourceId => CanonicalRequired(SourceId, nameof(SourceId));
    public string CanonicalSourceVersion => CanonicalRequired(SourceVersion, nameof(SourceVersion));
    public string CanonicalContentFingerprint => CanonicalRequired(ContentFingerprint, nameof(ContentFingerprint)).ToLowerInvariant();
    public string? CanonicalParserVersion => string.IsNullOrWhiteSpace(ParserVersion) ? null : ParserVersion.Trim();

    private static string CanonicalRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Valor obrigatório para snapshot de calibração.", parameterName);
        return value.Trim();
    }
}

/// <summary>
/// Artefato de proveniência necessário para replay. A projeção não conhece o plano: é o
/// manifesto do plano que referencia exatamente o schema/fingerprint das projeções, versões
/// dos catálogos e snapshots de Pessoa, corpus M/U e fontes externas.
/// </summary>
public sealed record CalibrationReplayManifest(
    string ManifestVersion,
    string CalibratorVersion,
    string ProjectionSchemaVersion,
    string ProjectionFingerprint,
    string AlgorithmCatalogVersion,
    string ComparatorCatalogVersion,
    string BlockingPlanVersion,
    string BlockingPlanFingerprint,
    CalibrationSourceSnapshot PersonSnapshot,
    CalibrationSourceSnapshot TrainingCorpusSnapshot,
    IReadOnlyList<CalibrationSourceSnapshot> ExternalSnapshots,
    string Fingerprint)
{
    public const string CurrentManifestVersion = "CALIBRATION_REPLAY_MANIFEST_V1";

    public static CalibrationReplayManifest Create(
        string calibratorVersion,
        ResolutionProjectionPlan projectionPlan,
        string blockingPlanVersion,
        string blockingPlanFingerprint,
        CalibrationSourceSnapshot personSnapshot,
        CalibrationSourceSnapshot trainingCorpusSnapshot,
        IEnumerable<CalibrationSourceSnapshot>? externalSnapshots = null)
    {
        if (string.IsNullOrWhiteSpace(calibratorVersion))
            throw new ArgumentException("Versão do Calibrador é obrigatória.", nameof(calibratorVersion));
        ArgumentNullException.ThrowIfNull(projectionPlan);
        if (string.IsNullOrWhiteSpace(blockingPlanVersion))
            throw new ArgumentException("Versão do plano é obrigatória.", nameof(blockingPlanVersion));
        if (string.IsNullOrWhiteSpace(blockingPlanFingerprint))
            throw new ArgumentException("Fingerprint do plano é obrigatório.", nameof(blockingPlanFingerprint));
        ArgumentNullException.ThrowIfNull(personSnapshot);
        ArgumentNullException.ThrowIfNull(trainingCorpusSnapshot);
        if (personSnapshot.Kind != CalibrationSourceKind.PersonData)
            throw new ArgumentException("PersonSnapshot deve ser PersonData.", nameof(personSnapshot));
        if (trainingCorpusSnapshot.Kind != CalibrationSourceKind.TrainingCorpus)
            throw new ArgumentException("TrainingCorpusSnapshot deve ser TrainingCorpus.", nameof(trainingCorpusSnapshot));

        var external = (externalSnapshots ?? Array.Empty<CalibrationSourceSnapshot>())
            .Select(static snapshot => snapshot ?? throw new ArgumentException("Snapshot externo nulo não é permitido."))
            .ToArray();
        if (external.Any(static snapshot => snapshot.Kind != CalibrationSourceKind.ExternalReference))
            throw new ArgumentException("ExternalSnapshots aceita somente ExternalReference.", nameof(externalSnapshots));

        var orderedExternal = external
            .OrderBy(static snapshot => snapshot.CanonicalSourceId, StringComparer.Ordinal)
            .ThenBy(static snapshot => snapshot.CanonicalSourceVersion, StringComparer.Ordinal)
            .ThenBy(static snapshot => snapshot.CanonicalContentFingerprint, StringComparer.Ordinal)
            .ToArray();

        var fingerprint = ComputeFingerprint(
            calibratorVersion.Trim(),
            projectionPlan,
            blockingPlanVersion.Trim(),
            blockingPlanFingerprint.Trim(),
            personSnapshot,
            trainingCorpusSnapshot,
            orderedExternal);

        return new CalibrationReplayManifest(
            CurrentManifestVersion,
            calibratorVersion.Trim(),
            projectionPlan.SchemaVersion,
            projectionPlan.Fingerprint,
            HomologatedResolutionAlgorithmCatalog.CatalogVersion,
            HomologatedResolutionComparatorCatalog.CatalogVersion,
            blockingPlanVersion.Trim(),
            blockingPlanFingerprint.Trim(),
            personSnapshot,
            trainingCorpusSnapshot,
            orderedExternal,
            fingerprint);
    }

    private static string ComputeFingerprint(
        string calibratorVersion,
        ResolutionProjectionPlan projectionPlan,
        string blockingPlanVersion,
        string blockingPlanFingerprint,
        CalibrationSourceSnapshot personSnapshot,
        CalibrationSourceSnapshot trainingCorpusSnapshot,
        IReadOnlyList<CalibrationSourceSnapshot> externalSnapshots)
    {
        var canonical = new StringBuilder()
            .Append(CurrentManifestVersion).Append('|')
            .Append(calibratorVersion).Append('|')
            .Append(projectionPlan.SchemaVersion).Append('|')
            .Append(projectionPlan.Fingerprint).Append('|')
            .Append(HomologatedResolutionAlgorithmCatalog.CatalogVersion).Append('|')
            .Append(HomologatedResolutionComparatorCatalog.CatalogVersion).Append('|')
            .Append(blockingPlanVersion).Append('|')
            .Append(blockingPlanFingerprint).AppendLine();

        AppendSnapshot(canonical, personSnapshot);
        AppendSnapshot(canonical, trainingCorpusSnapshot);
        foreach (var snapshot in externalSnapshots)
            AppendSnapshot(canonical, snapshot);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static void AppendSnapshot(StringBuilder builder, CalibrationSourceSnapshot snapshot) =>
        builder.Append("D|")
            .Append(snapshot.Kind).Append('|')
            .Append(snapshot.CanonicalSourceId).Append('|')
            .Append(snapshot.CanonicalSourceVersion).Append('|')
            .Append(snapshot.CanonicalContentFingerprint).Append('|')
            .Append(snapshot.CanonicalParserVersion)
            .AppendLine();
}

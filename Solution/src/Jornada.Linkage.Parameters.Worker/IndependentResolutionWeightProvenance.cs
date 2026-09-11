using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Parameters.Worker;

public enum IndependentResolutionWeightAdjustmentKind
{
    Selection,
    NonResponse,
    Calibration
}

public enum IndependentResolutionWeightAdjustmentState
{
    NotApplicable,
    Applied
}

/// <summary>
/// Declara se um tipo de ajuste foi aplicado ao peso final. Quando aplicado, a evidência
/// externa precisa ser referenciada e fingerprintada; quando não aplicável, nenhuma evidência
/// é aceita silenciosamente.
/// </summary>
public sealed record IndependentResolutionWeightAdjustment(
    IndependentResolutionWeightAdjustmentKind Kind,
    IndependentResolutionWeightAdjustmentState State,
    string? Reference,
    string? FingerprintSha256);

/// <summary>
/// Proveniência atestada do peso usado na avaliação. O contrato registra o peso-base e o peso
/// final, mas não infere nem prescreve a fórmula institucional que liga um ao outro.
/// </summary>
public sealed record IndependentResolutionWeightProvenance(
    string MethodVersion,
    string Reference,
    decimal BaseDesignWeight,
    decimal FinalWeight,
    DateTimeOffset AttestedAt,
    IReadOnlyList<IndependentResolutionWeightAdjustment> Adjustments);

public sealed record GovernedIndependentResolutionSurveyObservation(
    IndependentResolutionSurveyObservation SurveyObservation,
    IndependentResolutionWeightProvenance WeightProvenance);

public sealed record IndependentResolutionGovernedSurveyEvaluationReport(
    string Version,
    string BaseSurveyFingerprintSha256,
    string WeightMethodVersion,
    string WeightProvenanceFingerprintSha256,
    int Observations,
    int SelectionAdjustmentsApplied,
    int NonResponseAdjustmentsApplied,
    int CalibrationAdjustmentsApplied,
    IndependentResolutionSurveyEvaluationReport Survey,
    string FingerprintSha256);

/// <summary>
/// Gate governado sobre a avaliação ponderada. Ele não estima probabilidade de seleção,
/// resposta ou calibração e não modifica pesos. Exige apenas que o peso final já fornecido
/// tenha proveniência explícita e verificável antes de delegar ao avaliador do desenho amostral.
/// </summary>
public static class IndependentResolutionGovernedSurveyEvaluator
{
    public const string Version = "LINKAGE_INDEPENDENT_RESOLUTION_GOVERNED_SURVEY_V1";

    public static IndependentResolutionGovernedSurveyEvaluationReport Evaluate(
        IndependentRuleSetEvaluationManifest manifest,
        IReadOnlyList<GovernedIndependentResolutionSurveyObservation> observations,
        decimal threshold,
        decimal conflictMargin)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count == 0)
            throw new ArgumentException("At least one governed survey observation is required.", nameof(observations));

        var normalized = observations
            .Select(NormalizeAndValidate)
            .OrderBy(static row => row.ObservationFingerprintSha256, StringComparer.Ordinal)
            .ToArray();

        var methods = normalized
            .Select(static row => row.MethodVersion)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (methods.Length != 1)
            throw new InvalidOperationException("A governed evaluation must use one weighting method version.");

        var survey = IndependentResolutionSurveyEvaluator.Evaluate(
            manifest,
            normalized.Select(static row => row.SurveyObservation).ToArray(),
            threshold,
            conflictMargin);

        var provenanceFingerprint = ProvenanceFingerprint(normalized);
        var selectionApplied = CountApplied(normalized, IndependentResolutionWeightAdjustmentKind.Selection);
        var nonResponseApplied = CountApplied(normalized, IndependentResolutionWeightAdjustmentKind.NonResponse);
        var calibrationApplied = CountApplied(normalized, IndependentResolutionWeightAdjustmentKind.Calibration);
        var fingerprint = ReportFingerprint(
            survey.FingerprintSha256,
            methods[0],
            provenanceFingerprint,
            normalized.Length,
            selectionApplied,
            nonResponseApplied,
            calibrationApplied);

        return new IndependentResolutionGovernedSurveyEvaluationReport(
            Version,
            survey.FingerprintSha256,
            methods[0],
            provenanceFingerprint,
            normalized.Length,
            selectionApplied,
            nonResponseApplied,
            calibrationApplied,
            survey,
            fingerprint);
    }

    private static NormalizedRow NormalizeAndValidate(
        GovernedIndependentResolutionSurveyObservation row)
    {
        if (row is null || row.SurveyObservation is null || row.WeightProvenance is null)
            throw new InvalidOperationException("Governed survey observation and provenance are required.");

        var survey = row.SurveyObservation;
        var provenance = row.WeightProvenance;
        var observationFingerprint = Sha256(
            survey.Observation?.ObservationFingerprintSha256,
            "Observation fingerprint");
        var groupFingerprint = Sha256(
            survey.IndependenceGroupFingerprintSha256,
            "Independence group fingerprint");

        if (survey.DesignWeight <= 0m)
            throw new InvalidOperationException("Survey DesignWeight must be positive.");
        if (provenance.BaseDesignWeight <= 0m || provenance.FinalWeight <= 0m)
            throw new InvalidOperationException("Base and final governed weights must be positive.");
        if (provenance.FinalWeight != survey.DesignWeight)
            throw new InvalidOperationException("Governed final weight must equal the survey DesignWeight used by the evaluator.");
        if (provenance.AttestedAt == default)
            throw new InvalidOperationException("Weight provenance attestation timestamp is required.");

        var methodVersion = RequiredText(provenance.MethodVersion, "Weight method version", 100);
        var reference = RequiredText(provenance.Reference, "Weight provenance reference", 500);
        if (provenance.Adjustments is null)
            throw new InvalidOperationException("Weight adjustments are required.");

        var adjustments = new Dictionary<IndependentResolutionWeightAdjustmentKind, NormalizedAdjustment>();
        foreach (var adjustment in provenance.Adjustments)
        {
            if (adjustment is null || !Enum.IsDefined(adjustment.Kind) || !Enum.IsDefined(adjustment.State))
                throw new InvalidOperationException("Weight adjustment kind/state is invalid.");
            if (adjustments.ContainsKey(adjustment.Kind))
                throw new InvalidOperationException("Each weight adjustment kind must be declared exactly once.");

            string? adjustmentReference = null;
            string? adjustmentFingerprint = null;
            if (adjustment.State == IndependentResolutionWeightAdjustmentState.Applied)
            {
                adjustmentReference = RequiredText(adjustment.Reference, "Applied adjustment reference", 500);
                adjustmentFingerprint = Sha256(adjustment.FingerprintSha256, "Applied adjustment fingerprint");
            }
            else if (!string.IsNullOrWhiteSpace(adjustment.Reference) ||
                     !string.IsNullOrWhiteSpace(adjustment.FingerprintSha256))
            {
                throw new InvalidOperationException("NotApplicable adjustment cannot carry hidden evidence metadata.");
            }

            adjustments.Add(
                adjustment.Kind,
                new NormalizedAdjustment(
                    adjustment.Kind,
                    adjustment.State,
                    adjustmentReference,
                    adjustmentFingerprint));
        }

        var requiredKinds = Enum.GetValues<IndependentResolutionWeightAdjustmentKind>();
        if (adjustments.Count != requiredKinds.Length || requiredKinds.Any(kind => !adjustments.ContainsKey(kind)))
            throw new InvalidOperationException(
                "Selection, NonResponse and Calibration adjustment states must all be declared explicitly.");

        return new NormalizedRow(
            survey,
            observationFingerprint,
            groupFingerprint,
            methodVersion,
            reference,
            provenance.BaseDesignWeight,
            provenance.FinalWeight,
            provenance.AttestedAt.ToUniversalTime(),
            requiredKinds
                .OrderBy(static kind => kind)
                .Select(kind => adjustments[kind])
                .ToArray());
    }

    private static int CountApplied(
        IReadOnlyList<NormalizedRow> rows,
        IndependentResolutionWeightAdjustmentKind kind) =>
        rows.Count(row => row.Adjustments.Single(adjustment => adjustment.Kind == kind).State ==
            IndependentResolutionWeightAdjustmentState.Applied);

    private static string ProvenanceFingerprint(IReadOnlyList<NormalizedRow> rows)
    {
        var builder = new StringBuilder();
        Append(builder, Version);
        foreach (var row in rows)
        {
            Append(builder, row.ObservationFingerprintSha256);
            Append(builder, row.GroupFingerprintSha256);
            Append(builder, row.MethodVersion);
            Append(builder, row.Reference);
            Append(builder, Decimal(row.BaseDesignWeight));
            Append(builder, Decimal(row.FinalWeight));
            Append(builder, row.AttestedAt.ToString("O", CultureInfo.InvariantCulture));
            foreach (var adjustment in row.Adjustments)
            {
                Append(builder, adjustment.Kind.ToString());
                Append(builder, adjustment.State.ToString());
                Append(builder, adjustment.Reference ?? string.Empty);
                Append(builder, adjustment.FingerprintSha256 ?? string.Empty);
            }
        }
        return Hash(builder.ToString());
    }

    private static string ReportFingerprint(
        string surveyFingerprint,
        string methodVersion,
        string provenanceFingerprint,
        int observations,
        int selectionApplied,
        int nonResponseApplied,
        int calibrationApplied)
    {
        var builder = new StringBuilder();
        Append(builder, Version);
        Append(builder, surveyFingerprint);
        Append(builder, methodVersion);
        Append(builder, provenanceFingerprint);
        Append(builder, observations.ToString(CultureInfo.InvariantCulture));
        Append(builder, selectionApplied.ToString(CultureInfo.InvariantCulture));
        Append(builder, nonResponseApplied.ToString(CultureInfo.InvariantCulture));
        Append(builder, calibrationApplied.ToString(CultureInfo.InvariantCulture));
        return Hash(builder.ToString());
    }

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append('\n');

    private static string Decimal(decimal value) =>
        value.ToString("G29", CultureInfo.InvariantCulture);

    private static string RequiredText(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is required.");
        var normalized = value.Trim();
        if (normalized.Length > maxLength || normalized.Any(char.IsControl))
            throw new InvalidOperationException($"{name} contains unsupported characters or is too long.");
        return normalized;
    }

    private static string Sha256(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is required.");
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 64 ||
            normalized.Any(static c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidOperationException($"{name} must be a 64-character SHA-256 hex fingerprint.");
        return normalized;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record NormalizedAdjustment(
        IndependentResolutionWeightAdjustmentKind Kind,
        IndependentResolutionWeightAdjustmentState State,
        string? Reference,
        string? FingerprintSha256);

    private sealed record NormalizedRow(
        IndependentResolutionSurveyObservation SurveyObservation,
        string ObservationFingerprintSha256,
        string GroupFingerprintSha256,
        string MethodVersion,
        string Reference,
        decimal BaseDesignWeight,
        decimal FinalWeight,
        DateTimeOffset AttestedAt,
        NormalizedAdjustment[] Adjustments);
}

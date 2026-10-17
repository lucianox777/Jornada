using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Versioned model-family interface for a manually requested calibration
/// study. Every family owns fitting and validation semantics. New model families
/// register an adapter instead of changing this orchestrator.
/// The returned output is diagnostic: NEVER a deployable or activated model.
/// </summary>
public interface IAdaptiveCalibrationFamilyAdapter
{
    string ModelFamily { get; }
    string AdapterVersion { get; }

    AdaptiveCalibrationFitDraft FitTrain(
        AdaptiveCalibrationModelOption modelOption,
        AdaptiveCalibrationSlice train);

    AdaptiveCalibrationHoldoutMetrics EvaluateValidation(
        AdaptiveCalibrationFitDraft fitted,
        AdaptiveCalibrationSlice validation);
}

public sealed record AdaptiveCalibrationSlice(
    string Partition,
    string CorpusFingerprintSha256,
    string IndependentIdentityPartitionFingerprintSha256,
    int DistinctIdentityGroups,
    bool IndependentBaseIdentityPartitionAttested,
    bool UsesTestLabels);

public sealed record AdaptiveCalibrationModelOption(
    string ModelFamily,
    string AdapterVersion,
    string OptionId,
    string ConfigurationFingerprintSha256);

/// <summary>Proposal from TRAIN only, without executable credentials or data.</summary>
public sealed record AdaptiveCalibrationFitDraft(
    string ModelFamily,
    string AdapterVersion,
    string OptionId,
    string ConfigurationFingerprintSha256,
    string FittedParametersFingerprintSha256,
    string TrainingCorpusFingerprintSha256,
    bool TrainOnly);

/// <summary>
/// Held-out VALIDATION aggregate from the same frozen candidate frame.
/// U/fanout measure blocking costs; FP is a mistaken ACCEPTED link only.
/// </summary>
public sealed record AdaptiveCalibrationHoldoutMetrics(
    long TruePairsWithoutCpf,
    long TrueAcceptedLinks,
    long FalseAcceptedLinks,
    long Abstentions,
    long BlockingFalseCandidatePairs,
    double BlockingFalseDegreeP99,
    int IndependentlyLabeledIdentityGroups);

public sealed record AdaptiveCalibrationPreviewBudget(
    string Version,
    string StudyFingerprintSha256,
    double MinimumGlobalAcceptedRecall,
    double MaximumObservedAcceptedFdrPoint,
    double MaximumBlockingFalseDegreeP99,
    int MinimumValidationIdentityGroups)
{
    public const string CurrentVersion = "ADAPTIVE_CALIBRATION_PREVIEW_BUDGET_V1";
}

public sealed record AdaptiveCalibrationStudyRequest(
    string StudyFingerprintSha256,
    AdaptiveCalibrationSlice Train,
    AdaptiveCalibrationSlice Validation,
    IReadOnlyList<AdaptiveCalibrationModelOption> Models,
    bool ManuallyRequestedCalibration,
    AdaptiveCalibrationPreviewBudget? PreviewBudget);

public sealed record AdaptiveCalibrationOptionResult(
    string ModelFamily,
    string AdapterVersion,
    string OptionId,
    string FittedParametersFingerprintSha256,
    AdaptiveCalibrationHoldoutMetrics Validation,
    double GlobalAcceptedRecall,
    double? ObservedAcceptedFdrPoint,
    bool MeetsDescriptiveBudgetOnly,
    double? NominalPairwiseFdrUpper95 = null,
    bool MeetsNecessaryNominalFdrScreen = false);

public sealed record AdaptiveCalibrationStudyReport(
    string Version,
    string StudyFingerprintSha256,
    string Status,
    IReadOnlyList<AdaptiveCalibrationOptionResult> Options,
    string? SuggestedModelFamily,
    string? SuggestedOptionId,
    bool TestUsed,
    bool ConfidenceBoundForFdrMeasured,
    bool OperationalModelCreated,
    bool AutomaticRecalibrationOrActivationAllowed);

/// <summary>
/// Diagnostic pluggable model-family search: TRAIN fit, VALIDATION assessment,
/// optional *descriptive* budget ranking. There is no TEST access, no model
/// persistence, no activation API and no automatic recalibration trigger.
/// Wilson's one-sided 95% UCB is only a *necessary* nominal pairwise screen:
/// candidate pairs can share identities and are not independent trials.
/// The correlated identity-level FDR upper bound, SQL/Rank/Resolve parity and
/// unseen TEST remain unmeasured. A suggestion is NEVER approval.
/// </summary>
public static class AdaptiveCalibrationModelSearch
{
    public const string Version = "ADAPTIVE_CALIBRATION_MULTI_MODEL_TRAIN_VAL_DIAGNOSTIC_V2";

    public static AdaptiveCalibrationStudyReport Evaluate(
        AdaptiveCalibrationStudyRequest request,
        IEnumerable<IAdaptiveCalibrationFamilyAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(adapters);
        if (!request.ManuallyRequestedCalibration)
            throw new InvalidOperationException(
                "Calibração requer solicitação manual, não sinal de suficiência.");
        RequireSha(request.StudyFingerprintSha256, "study");
        if (request.Train is null || request.Validation is null
            || request.Train.Partition != "TRAIN" || request.Validation.Partition != "VALIDATION"
            || request.Train.UsesTestLabels || request.Validation.UsesTestLabels
            || !request.Train.IndependentBaseIdentityPartitionAttested
            || !request.Validation.IndependentBaseIdentityPartitionAttested
            || request.Train.DistinctIdentityGroups < 2
            || request.Validation.DistinctIdentityGroups < 2)
            throw new InvalidDataException("TRAIN/VALIDATION independentes obrigatórios, TEST proibido.");
        RequireSha(request.Train.CorpusFingerprintSha256, "TRAIN source");
        RequireSha(request.Validation.CorpusFingerprintSha256, "VALIDATION source");
        RequireSha(request.Train.IndependentIdentityPartitionFingerprintSha256, "TRAIN identities");
        RequireSha(request.Validation.IndependentIdentityPartitionFingerprintSha256, "VALIDATION identities");
        if (request.Train.CorpusFingerprintSha256 == request.Validation.CorpusFingerprintSha256
            || request.Train.IndependentIdentityPartitionFingerprintSha256
               == request.Validation.IndependentIdentityPartitionFingerprintSha256)
            throw new InvalidDataException("TRAIN e VALIDATION não podem reutilizar a mesma fonte/identidade.");

        if (request.Models is null || request.Models.Count is < 1 or > 64
            || request.Models.Any(x => x is null
                || string.IsNullOrWhiteSpace(x.ModelFamily)
                || string.IsNullOrWhiteSpace(x.AdapterVersion)
                || string.IsNullOrWhiteSpace(x.OptionId)))
            throw new ArgumentException("Modelos e opções versionadas obrigatórios.");
        foreach (var option in request.Models)
            RequireSha(option.ConfigurationFingerprintSha256, "configuration");
        if (request.Models.Select(x => (x.ModelFamily, x.OptionId)).Distinct().Count()
            != request.Models.Count)
            throw new ArgumentException("Não repetir família/opção.");

        var providers = adapters.ToArray();
        if (providers.Any(x => x is null || string.IsNullOrWhiteSpace(x.ModelFamily)
            || string.IsNullOrWhiteSpace(x.AdapterVersion)))
            throw new ArgumentException("Adaptador sem identidade/versionamento.");
        var byFamily = new Dictionary<string, IAdaptiveCalibrationFamilyAdapter>(
            StringComparer.Ordinal);
        foreach (var adapter in providers)
        {
            if (!byFamily.TryAdd(adapter.ModelFamily, adapter))
                throw new ArgumentException("Família de modelo duplicada.");
        }

        var budget = request.PreviewBudget;
        if (budget is not null)
        {
            if (budget.Version != AdaptiveCalibrationPreviewBudget.CurrentVersion
                || budget.StudyFingerprintSha256 != request.StudyFingerprintSha256
                || !double.IsFinite(budget.MinimumGlobalAcceptedRecall)
                || budget.MinimumGlobalAcceptedRecall is < 0.95 or > 1
                || !double.IsFinite(budget.MaximumObservedAcceptedFdrPoint)
                || budget.MaximumObservedAcceptedFdrPoint is < 0 or > 0.01
                || !double.IsFinite(budget.MaximumBlockingFalseDegreeP99)
                || budget.MaximumBlockingFalseDegreeP99 < 0
                || budget.MinimumValidationIdentityGroups < 2)
                throw new InvalidDataException(
                    "Orçamento de prévia explícito, versionado e vinculado à evidência requerido.");
        }

        var measured = new List<AdaptiveCalibrationOptionResult>(request.Models.Count);
        foreach (var option in request.Models.OrderBy(x => x.ModelFamily, StringComparer.Ordinal)
                     .ThenBy(x => x.OptionId, StringComparer.Ordinal))
        {
            if (!byFamily.TryGetValue(option.ModelFamily, out var provider)
                || provider.AdapterVersion != option.AdapterVersion)
                throw new InvalidOperationException(
                    "Modelo desconhecido/versionamento sem adaptador; não usar fallback.");
            // Adapter sees the TRAIN source only during parameter fitting.
            var fit = provider.FitTrain(option, request.Train);
            if (fit is null || fit.ModelFamily != option.ModelFamily
                || fit.AdapterVersion != option.AdapterVersion
                || fit.OptionId != option.OptionId
                || fit.ConfigurationFingerprintSha256 != option.ConfigurationFingerprintSha256
                || fit.TrainingCorpusFingerprintSha256 != request.Train.CorpusFingerprintSha256
                || !fit.TrainOnly)
                throw new InvalidDataException("Proposta de fit não provém de TRAIN congelado.");
            RequireSha(fit.FittedParametersFingerprintSha256, "fit");
            var validation = provider.EvaluateValidation(fit, request.Validation);
            if (validation is null || validation.TruePairsWithoutCpf <= 0
                || validation.TrueAcceptedLinks < 0 || validation.FalseAcceptedLinks < 0
                || validation.TrueAcceptedLinks > validation.TruePairsWithoutCpf
                || validation.Abstentions < 0 || validation.BlockingFalseCandidatePairs < 0
                || validation.FalseAcceptedLinks > validation.BlockingFalseCandidatePairs
                || validation.IndependentlyLabeledIdentityGroups < 0
                || validation.IndependentlyLabeledIdentityGroups > request.Validation.DistinctIdentityGroups
                || !double.IsFinite(validation.BlockingFalseDegreeP99)
                || validation.BlockingFalseDegreeP99 < 0)
                throw new InvalidDataException("Métricas de VALIDATION inválidas.");
            var accepted = checked(validation.TrueAcceptedLinks + validation.FalseAcceptedLinks);
            var recall = (double)validation.TrueAcceptedLinks / validation.TruePairsWithoutCpf;
            double? fdrPoint = accepted > 0
                ? (double)validation.FalseAcceptedLinks / accepted : null;
            var nominalUpper95 = NominalWilsonUpper95(validation.FalseAcceptedLinks, accepted);
            var eligible = budget is not null && fdrPoint.HasValue
                && recall >= budget.MinimumGlobalAcceptedRecall
                && fdrPoint.Value <= budget.MaximumObservedAcceptedFdrPoint
                && validation.BlockingFalseDegreeP99 <= budget.MaximumBlockingFalseDegreeP99
                && validation.IndependentlyLabeledIdentityGroups >= budget.MinimumValidationIdentityGroups;
            // Necessary, NOT sufficient: pair-level Wilson assumes independent Bernoulli
            // trials, which does not hold when many accepted pairs share an identity.
            // Never turn this nominal screen into an operational certificate.
            var passesNominalScreen = eligible && nominalUpper95.HasValue
                && nominalUpper95.Value <= budget!.MaximumObservedAcceptedFdrPoint;
            measured.Add(new AdaptiveCalibrationOptionResult(
                option.ModelFamily, option.AdapterVersion, option.OptionId,
                fit.FittedParametersFingerprintSha256, validation, recall, fdrPoint, eligible,
                nominalUpper95, passesNominalScreen));
        }

        var suggestion = measured.Where(x => x.MeetsNecessaryNominalFdrScreen)
            .OrderByDescending(x => x.GlobalAcceptedRecall)
            .ThenBy(x => x.ObservedAcceptedFdrPoint)
            .ThenBy(x => x.Validation.BlockingFalseDegreeP99)
            .ThenBy(x => x.ModelFamily, StringComparer.Ordinal)
            .ThenBy(x => x.OptionId, StringComparer.Ordinal)
            .FirstOrDefault();
        var status = budget is null ? "NO_APPROVED_POLICY_DESCRIPTIVE_ONLY"
            : suggestion is null ? "NO_MODEL_MEETS_PRELIMINARY_FDR_SCREEN"
            : "SUGGESTION_ONLY_UNCERTIFIED_GROUP_FDR_AND_PHYSICAL_SQL";
        return new AdaptiveCalibrationStudyReport(
            Version, request.StudyFingerprintSha256, status, measured,
            suggestion?.ModelFamily, suggestion?.OptionId,
            TestUsed: false, ConfidenceBoundForFdrMeasured: false,
            OperationalModelCreated: false,
            AutomaticRecalibrationOrActivationAllowed: false);
    }

    // One-sided 95% Wilson score limit for FP / accepted pairs (z = Phi^-1(.95)).
    // Diagnostic only. It is not valid as an identity-cluster-aware risk bound.
    // In particular, 0 FP out of 2 accepted has UCB ~0.575, not zero risk.
    private static double? NominalWilsonUpper95(long fp, long accepted)
    {
        if (accepted == 0) return null;
        const double z = 1.6448536269514722d;
        var n = (double)accepted;
        var p = fp / n;
        var zSquared = z * z;
        var variance = p * (1d - p) / n + zSquared / (4d * n * n);
        return Math.Min(1d,
            (p + zSquared / (2d * n) + z * Math.Sqrt(variance))
            / (1d + zSquared / n));
    }

    private static void RequireSha(string? sha, string label)
    {
        if (sha is null || sha.Length != 64
            || sha.Any(x => !Uri.IsHexDigit(x)))
            throw new ArgumentException("SHA-256 obrigatório: " + label);
    }
}

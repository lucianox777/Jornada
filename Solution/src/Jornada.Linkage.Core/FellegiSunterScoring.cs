using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

public readonly record struct FellegiSunterScore(decimal Posterior, decimal LogOdds);

// Somente a fronteira contratual usa decimal; a matemática usa float64.
internal readonly record struct FellegiSunterRawScore(double Posterior, double LogOdds);

public sealed record FellegiSunterEvidenceContribution(
    string Evidence,
    string State,
    decimal? MProbability,
    decimal? UProbability,
    decimal LogLikelihoodRatio);

public sealed record FellegiSunterScoreBreakdown(
    FellegiSunterScore Score,
    string PriorKind,
    decimal PriorProbability,
    decimal PriorLogOdds,
    IReadOnlyList<FellegiSunterEvidenceContribution> Contributions);

/// <summary>
/// One frozen, mutually exclusive (person,mother) nominal state for V9.
/// The 81-cell probability table comes from externally certified calibration;
/// this type is NOT an authorization to publish or activate a model.
/// </summary>
public sealed record FellegiSunterJointNominalCellV9(
    CompositeNominalLevelV9 Person,
    CompositeNominalLevelV9 Mother,
    decimal M,
    decimal U);

public static class FellegiSunterScoring
{
    /// <summary>
    /// V9 nominal term inside the EXISTING C# Fellegi-Sunter math:
    /// one exclusive joint state contributes ln(m/u) exactly once, rather
    /// than separately adding person/full/first/last/mother marginal LLRs.
    /// No thresholds, blocking, TF, prior, candidate ranking or model
    /// activation are performed by this method.
    ///
    /// The Calibrador must separately attest TRAIN/VALIDATION/TEST support,
    /// frame, source, confidence and model approval before it may persist
    /// these 81 cells. This mathematical method cannot attest provenance.
    /// </summary>
    public static double CalculateCompositeNominalJointV9(
        string? leftPersonName,
        string? rightPersonName,
        string? leftMotherName,
        string? rightMotherName,
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

        var visited = new HashSet<(CompositeNominalLevelV9 Person, CompositeNominalLevelV9 Mother)>();
        decimal sumM = 0m, sumU = 0m;
        FellegiSunterJointNominalCellV9? selected = null;
        var observedPerson = CompositeNominalComparisonV9.Classify(leftPersonName, rightPersonName);
        var observedMother = CompositeNominalComparisonV9.Classify(leftMotherName, rightMotherName);

        foreach (var cell in jointCells)
        {
            if (cell is null || !Enum.IsDefined(cell.Person) || !Enum.IsDefined(cell.Mother)
                || !visited.Add((cell.Person, cell.Mother))
                || cell.M <= 0m || cell.M > 1m || cell.U <= 0m || cell.U > 1m)
                throw new InvalidOperationException("V9 exige estados exclusivos e probabilidades válidas em M/U.");

            sumM += cell.M;
            sumU += cell.U;
            if (cell.Person == observedPerson && cell.Mother == observedMother)
                selected = cell;
        }

        if (selected is null || Math.Abs(sumM - 1m) > 0.000001m
            || Math.Abs(sumU - 1m) > 0.000001m)
            throw new InvalidOperationException("V9 exige distribuições conjuntas M/U normalizadas.");

        // Same float64 natural-log convention as the existing FS kernel.
        var llr = Math.Log((double)selected.M / (double)selected.U);
        if (!double.IsFinite(llr))
            throw new InvalidOperationException("V9 nominal produziu contribuição não finita.");
        return llr;
    }

    /// <summary>
    /// Explicit PREVIEW-only V9 entry into the same Fellegi-Sunter evidence
    /// aggregation as the runtime: one joint person/mother nominal LLR plus
    /// the existing birth evidence and prior, without adding old nominal LLRs.
    /// This does NOT dispatch AlgorithmVersion=V9 or activate a model.
    /// The caller's probability table still requires external certification.
    /// </summary>
    public static FellegiSunterScoreBreakdown CalculateCompositeNominalV9PreviewWithBreakdown(
        IReadOnlyDictionary<string, decimal> parameters,
        string? leftPersonName,
        string? rightPersonName,
        string? leftMotherName,
        string? rightMotherName,
        IReadOnlyCollection<FellegiSunterJointNominalCellV9> jointCells,
        string nominalContractVersion,
        int? blockCandidateCount = null,
        DateOnly? leftBirthDate = null,
        DateOnly? rightBirthDate = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!parameters.TryGetValue(LinkageParameterCatalog.DecisionEvidenceScoring, out var enabled)
            || enabled < 1m)
            throw new InvalidOperationException(
                "Preview V9 exige o contrato FS decision-evidence habilitado.");

        // Validate all 81 cells, their exclusivity and normalization before
        // evaluating birth, so invalid pseudo-models never return a score.
        var llr = CalculateCompositeNominalJointV9(
            leftPersonName, rightPersonName, leftMotherName, rightMotherName,
            jointCells, nominalContractVersion);
        var person = CompositeNominalComparisonV9.Classify(leftPersonName, rightPersonName);
        var mother = CompositeNominalComparisonV9.Classify(leftMotherName, rightMotherName);
        var selected = jointCells.Single(x => x.Person == person && x.Mother == mother);
        var raw = Evaluate(
            ToDoubleParameters(parameters),
            nameState: null, motherNameState: null,
            blockCandidateCount, leftBirthDate, rightBirthDate,
            captureBreakdown: true,
            v9Joint: ($"{person}:{mother}", (double)selected.M, (double)selected.U, llr));
        return RenderBreakdown(parameters, raw, selected);
    }

    public static decimal CalculatePosterior(
        IReadOnlyDictionary<string, decimal> parameters,
        NameComparisonState? nameState,
        NameComparisonState? motherNameState,
        int? blockCandidateCount = null,
        DateOnly? leftBirthDate = null,
        DateOnly? rightBirthDate = null) =>
        Calculate(parameters, nameState, motherNameState, blockCandidateCount, leftBirthDate, rightBirthDate).Posterior;

    public static FellegiSunterScore Calculate(
        IReadOnlyDictionary<string, decimal> parameters,
        NameComparisonState? nameState,
        NameComparisonState? motherNameState,
        int? blockCandidateCount = null,
        DateOnly? leftBirthDate = null,
        DateOnly? rightBirthDate = null) =>
        ToContractScore(CalculateRaw(ToDoubleParameters(parameters), nameState, motherNameState,
            blockCandidateCount, leftBirthDate, rightBirthDate));

    // Materializa o snapshot numérico uma única vez na carga do modelo.
    internal static IReadOnlyDictionary<string, double> ToDoubleParameters(
        IReadOnlyDictionary<string, decimal> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return parameters.ToDictionary(static item => item.Key,
            static item => (double)item.Value, StringComparer.Ordinal);
    }

    // Mantém a semântica legada do arredondamento exclusivamente na fronteira SQL/contrato.
    internal static FellegiSunterScore ToContractScore(FellegiSunterRawScore raw)
    {
        if (!double.IsFinite(raw.Posterior) || !double.IsFinite(raw.LogOdds))
            throw new InvalidOperationException("Score probabilístico não finito.");
        return new FellegiSunterScore(
            Math.Round((decimal)raw.Posterior, 8, MidpointRounding.AwayFromZero),
            Math.Round((decimal)raw.LogOdds, 8, MidpointRounding.AwayFromZero));
    }

    // Hot path: não materializa breakdown nem faz cast decimal de cada contribuição.
    internal static FellegiSunterRawScore CalculateRaw(
        IReadOnlyDictionary<string, double> parameters,
        NameComparisonState? nameState,
        NameComparisonState? motherNameState,
        int? blockCandidateCount = null,
        DateOnly? leftBirthDate = null,
        DateOnly? rightBirthDate = null) =>
        Evaluate(parameters, nameState, motherNameState, blockCandidateCount,
            leftBirthDate, rightBirthDate, captureBreakdown: false).Score;

    /// <summary>
    /// Mesmo núcleo double do runtime; as conversões para decimal são apenas diagnósticas.
    /// </summary>
    public static FellegiSunterScoreBreakdown CalculateWithBreakdown(
        IReadOnlyDictionary<string, decimal> parameters,
        NameComparisonState? nameState,
        NameComparisonState? motherNameState,
        int? blockCandidateCount = null,
        DateOnly? leftBirthDate = null,
        DateOnly? rightBirthDate = null)
    {
        var result = Evaluate(ToDoubleParameters(parameters), nameState, motherNameState,
            blockCandidateCount, leftBirthDate, rightBirthDate, captureBreakdown: true);
        return RenderBreakdown(parameters, result);
    }

    private static FellegiSunterScoreBreakdown RenderBreakdown(
        IReadOnlyDictionary<string, decimal> parameters,
        RawEvaluation result,
        FellegiSunterJointNominalCellV9? jointV9 = null)
    {
        // Existing parameters retain their exact persisted decimal values.
        // The explicit V9 preview has no separate M_NOME*/U_NOME* marginals:
        // its single joint nominal cell is the source of truth.
        var contributions = result.Contributions!
            .Select(item => new FellegiSunterEvidenceContribution(
                item.Evidence,
                item.State,
                item.M is null ? null : jointV9 is not null && item.Evidence == "NOMINAL_CONJUNTO_V9"
                    ? jointV9.M : Math.Clamp(
                        parameters[$"M_{item.Evidence}_{item.State}"], 0.000000001m, 0.999999999m),
                item.U is null ? null : jointV9 is not null && item.Evidence == "NOMINAL_CONJUNTO_V9"
                    ? jointV9.U : Math.Clamp(
                        parameters[$"U_{item.Evidence}_{item.State}"], 0.000000001m, 0.999999999m),
                (decimal)item.LogLikelihoodRatio))
            .ToArray();
        return new FellegiSunterScoreBreakdown(
            ToContractScore(result.Score),
            result.UsesBlockPrior ? "BLOCK_CANDIDATE_COUNT" : "MODEL_PRIOR",
            (decimal)result.Prior,
            (decimal)result.PriorLogOdds,
            contributions);
    }

    private readonly record struct RawContribution(
        string Evidence, string State, double? M, double? U, double LogLikelihoodRatio);

    private readonly record struct RawEvaluation(
        FellegiSunterRawScore Score,
        bool UsesBlockPrior,
        double Prior,
        double PriorLogOdds,
        List<RawContribution>? Contributions);

    private static RawEvaluation Evaluate(
        IReadOnlyDictionary<string, double> parameters,
        NameComparisonState? nameState,
        NameComparisonState? motherNameState,
        int? blockCandidateCount,
        DateOnly? leftBirthDate,
        DateOnly? rightBirthDate,
        bool captureBreakdown,
        (string State, double M, double U, double Llr)? v9Joint = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (blockCandidateCount is <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(blockCandidateCount),
                blockCandidateCount,
                "A cardinalidade do bloco deve ser maior que zero quando informada.");

        var decisionV6 = parameters.TryGetValue(LinkageParameterCatalog.DecisionEvidenceScoring, out var decisionFlag) && decisionFlag >= 1d;
        var neutralMissing = parameters.TryGetValue(LinkageParameterCatalog.NeutralMissingEvidenceScoring, out var neutralFlag) && neutralFlag >= 1d;
        var usesBlockPrior = !decisionV6 && blockCandidateCount.HasValue;
        var prior = usesBlockPrior
            ? CalculateBlockPrior(parameters, blockCandidateCount!.Value)
            : Get(parameters, LinkageParameterCatalog.PriorMatchProbability);
        var priorLogOdds = Logit(prior);
        var logOdds = priorLogOdds;
        var contributions = captureBreakdown ? new List<RawContribution>(6) : null;

        void Add(string evidence, string state, (double? M, double? U, double LogLikelihoodRatio) value)
        {
            logOdds += value.LogLikelihoodRatio;
            contributions?.Add(new RawContribution(
                evidence,
                state,
                value.M,
                value.U,
                value.LogLikelihoodRatio));
        }

        if (v9Joint is { } joint)
        {
            // Exactly ONE nominal LLR in V9, including a calibrated Missing
            // mother state. No legacy NOME or NOME_MAE terms are evaluated.
            Add("NOMINAL_CONJUNTO_V9", joint.State,
                (joint.M, joint.U, joint.Llr));
        }
        else
        {
            if (nameState is { } observedNameState)
            {
                Add("NOME", observedNameState.ToString(),
                    RequiredLikelihoodRatio(parameters, "NOME", observedNameState.ToString()));
            }
            else
            {
                // No calibrated NOME_MISSING in existing V8 scoring.
                Add("NOME", "MISSING_NEUTRAL", (null, null, 0d));
            }

            if (motherNameState is { } observedMotherNameState)
            {
                Add("NOME_MAE", observedMotherNameState.ToString(),
                    RequiredLikelihoodRatio(parameters, "NOME_MAE", observedMotherNameState.ToString()));
            }
            else if (decisionV6 && !neutralMissing)
            {
                Add("NOME_MAE", "MISSING", RequiredLikelihoodRatio(parameters, "NOME_MAE", "MISSING"));
            }
            else
            {
                Add("NOME_MAE", "MISSING_NEUTRAL", (null, null, 0d));
            }
        }

        if (leftBirthDate is { } left && rightBirthDate is { } right)
        {
            if (parameters.TryGetValue(LinkageParameterCatalog.BirthSemanticEvidenceScoring, out var semanticBirth) && semanticBirth >= 1d)
            {
                var state = BirthDateSemanticEvidence.Classify(left, right);
                Add("NASCIMENTO_SEMANTICO", state,
                    RequiredLikelihoodRatio(parameters, "NASCIMENTO_SEMANTICO", state));
            }
            else if (parameters.TryGetValue(LinkageParameterCatalog.BirthJointEvidenceScoring, out var jointBirth) && jointBirth >= 1d)
            {
                var mask = (left.Day == right.Day ? 1 : 0) | (left.Month == right.Month ? 2 : 0) | (left.Year == right.Year ? 4 : 0);
                var state = LinkageParameterCatalog.BirthJointStates[mask];
                Add("NASCIMENTO_CONJUNTO", state,
                    RequiredLikelihoodRatio(parameters, "NASCIMENTO_CONJUNTO", state));
            }
            else if (parameters.TryGetValue(LinkageParameterCatalog.BirthSingleEvidenceScoring, out var singleBirth) && singleBirth >= 1d)
            {
                var state = left == right ? "EXACT" : "DIFF";
                Add("DATA_NASCIMENTO", state, OptionalLikelihoodRatio(parameters, "DATA_NASCIMENTO", state));
            }
            else
            {
                AddBinary("NASC_DIA", left.Day == right.Day);
                AddBinary("NASC_MES", left.Month == right.Month);
                AddBinary("NASC_ANO", left.Year == right.Year);
            }
        }
        else
        {
            Add("NASCIMENTO", "MISSING_NEUTRAL", (null, null, 0d));
        }

        var posterior = 1d / (1d + Math.Exp(-Math.Clamp(logOdds, -40d, 40d)));
        var score = new FellegiSunterRawScore(posterior, logOdds);

        return new RawEvaluation(score, usesBlockPrior, prior, priorLogOdds, contributions);

        void AddBinary(string attribute, bool exact)
        {
            var state = exact ? "EXACT" : "DIFF";
            Add(attribute, state, OptionalLikelihoodRatio(parameters, attribute, state));
        }
    }

    private static double CalculateBlockPrior(IReadOnlyDictionary<string, double> parameters, int candidateCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(candidateCount);
        var min = parameters.TryGetValue(LinkageParameterCatalog.PriorBlockMin, out var pmin) ? pmin : 0.000001d;
        var max = parameters.TryGetValue(LinkageParameterCatalog.PriorBlockMax, out var pmax) ? pmax : 0.25d;
        return Math.Clamp(1d / candidateCount, min, max);
    }

    private static (double? M, double? U, double LogLikelihoodRatio) RequiredLikelihoodRatio(
        IReadOnlyDictionary<string, double> parameters,
        string attribute,
        string suffix)
    {
        var m = ClampProbability(Get(parameters, $"M_{attribute}_{suffix}"));
        var u = ClampProbability(Get(parameters, $"U_{attribute}_{suffix}"));
        return (m, u, Math.Log(m / u));
    }

    private static (double? M, double? U, double LogLikelihoodRatio) OptionalLikelihoodRatio(
        IReadOnlyDictionary<string, double> parameters,
        string attribute,
        string suffix)
    {
        if (!parameters.TryGetValue($"M_{attribute}_{suffix}", out var rawM) ||
            !parameters.TryGetValue($"U_{attribute}_{suffix}", out var rawU))
            return (null, null, 0d);

        var m = ClampProbability(rawM);
        var u = ClampProbability(rawU);
        return (m, u, Math.Log(m / u));
    }

    private static double Get(IReadOnlyDictionary<string, double> parameters, string name) =>
        parameters.TryGetValue(name, out var value) ? value : throw new InvalidOperationException($"Parâmetro de linkage ausente: {name}");

    private static double ClampProbability(double value) => Math.Clamp(value, 0.000000001d, 0.999999999d);

    private static double Logit(double probability)
    {
        var p = Math.Clamp(probability, 0.0000001d, 0.9999999d);
        return Math.Log(p / (1d - p));
    }
}

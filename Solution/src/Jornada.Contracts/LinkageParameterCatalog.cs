namespace Jornada.Contracts;

public static class LinkageParameterCatalog
{
    public const string DecisionEvidenceAlgorithmVersion = "FELLEGI_SUNTER_DECISION_EVIDENCE_V6";
    public const string NominalGuardDecisionEvidenceAlgorithmVersion = "FELLEGI_SUNTER_DECISION_EVIDENCE_NOMINAL_V2_V7";
    public const string SemanticBirthAlgorithmVersion = DecisionEvidenceAlgorithmVersion;
    public const string LegacySemanticBirthAlgorithmVersion = "FELLEGI_SUNTER_SEMANTIC_BIRTH_V5";
    public const string PriorMatchProbability = "PRIOR_MATCH_PROBABILITY";
    public const string PriorBlockMin = "PRIOR_BLOCK_MIN";
    public const string PriorBlockMax = "PRIOR_BLOCK_MAX";
    public const string Threshold = "T_LINKAGE";
    public const string ConflictMargin = "CONFLICT_MARGIN";
    public const string LogOddsConflictMargin = "CONFLICT_MARGIN_LOG_ODDS";
    public const string DecisionEvidenceScoring = "SCORING_DECISION_EVIDENCE_V6";
    public const string DualThresholdConflictGuard = "SCORING_DUAL_THRESHOLD_CONFLICT_V1";
    public const string OrderedNameLlrMonotonicity = "MODEL_COHERENCE_ORDERED_NAME_LLR_V1";
    public const string NameComparisonPtBrContentTokenGuardV2 = "SCORING_NAME_PTBR_CONTENT_TOKEN_GUARD_V2";
    public const string BirthSemanticEvidenceScoring = "SCORING_BIRTH_SEMANTIC_EVIDENCE_V5";
    public const string BirthJointEvidenceScoring = "SCORING_BIRTH_JOINT_EVIDENCE_V4";
    public const string BirthSingleEvidenceScoring = "SCORING_BIRTH_SINGLE_EVIDENCE_V3";
    public const string BirthComponentScoring = "SCORING_BIRTH_COMPONENTS_V2";
    public const string LegacyBirthComponentScoring = "BLOCKING_BIRTH_COMPONENTS_V2";
    public const string MatchedSampleSize = "M_SAMPLE_SIZE";
    public const string UnmatchedSampleSize = "U_SAMPLE_SIZE";
    public const string PopulationSize = "POPULATION_SIZE";
    public const string DistinctBirthDate = "DISTINCT_BIRTH_DATE";
    public static readonly IReadOnlyList<string> NameStates = ["EXACT", "HIGH", "MEDIUM", "LOW"];
    public static readonly IReadOnlyList<string> MotherNameStates = ["EXACT", "HIGH", "MEDIUM", "LOW", "MISSING"];
    public static readonly IReadOnlyList<string> BirthJointStates = ["000", "001", "010", "011", "100", "101", "110", "111"];
    public static IReadOnlyList<string> BirthSemanticStates => BirthDateSemanticEvidence.States;
    public static readonly IReadOnlyList<string> CoreScoringRequired = [PriorMatchProbability, PriorBlockMin, PriorBlockMax, Threshold, ConflictMargin, .. Distribution("M_NOME"), .. Distribution("U_NOME"), .. Distribution("M_NOME_MAE"), .. Distribution("U_NOME_MAE")];
    public static readonly IReadOnlyList<string> DecisionEvidenceRequired = [DecisionEvidenceScoring, LogOddsConflictMargin, Name("M_NOME_MAE", "MISSING"), Name("U_NOME_MAE", "MISSING")];
    public static readonly IReadOnlyList<string> NominalGuardV7Required = [NameComparisonPtBrContentTokenGuardV2];
    public static readonly IReadOnlyList<string> BirthSemanticEvidenceRequired = [.. SemanticBirthDistribution("M_NASCIMENTO_SEMANTICO"), .. SemanticBirthDistribution("U_NASCIMENTO_SEMANTICO")];
    public static readonly IReadOnlyList<string> BirthSemanticCalibrationValidationRequired = [BirthSemanticEvidenceScoring, .. BirthSemanticEvidenceRequired];
    public static readonly IReadOnlyList<string> BirthJointEvidenceRequired = [.. JointBirthDistribution("M_NASCIMENTO_CONJUNTO"), .. JointBirthDistribution("U_NASCIMENTO_CONJUNTO")];
    public static readonly IReadOnlyList<string> BirthSingleEvidenceRequired = [.. BinaryDistribution("M_DATA_NASCIMENTO"), .. BinaryDistribution("U_DATA_NASCIMENTO")];
    public static readonly IReadOnlyList<string> BirthComponentRequired = [.. BinaryDistribution("M_NASC_DIA"), .. BinaryDistribution("U_NASC_DIA"), .. BinaryDistribution("M_NASC_MES"), .. BinaryDistribution("U_NASC_MES"), .. BinaryDistribution("M_NASC_ANO"), .. BinaryDistribution("U_NASC_ANO")];
    public static readonly IReadOnlyList<string> CalibrationValidationRequired = [.. CoreScoringRequired, MatchedSampleSize, UnmatchedSampleSize, PopulationSize, DistinctBirthDate];
    public static bool UsesDecisionEvidence(string algorithmVersion) =>
        string.Equals(algorithmVersion, DecisionEvidenceAlgorithmVersion, StringComparison.Ordinal) ||
        string.Equals(algorithmVersion, NominalGuardDecisionEvidenceAlgorithmVersion, StringComparison.Ordinal);

    public static bool RequiresSemanticBirthEvidence(string algorithmVersion) =>
        string.Equals(algorithmVersion, LegacySemanticBirthAlgorithmVersion, StringComparison.Ordinal) ||
        UsesDecisionEvidence(algorithmVersion);

    public static NameComparisonContract NameComparisonContractForAlgorithm(string algorithmVersion) =>
        string.Equals(algorithmVersion, NominalGuardDecisionEvidenceAlgorithmVersion, StringComparison.Ordinal)
            ? NameComparisonContract.PtBrContentTokenGuardV2
            : NameComparisonContract.WholeNameJaroWinklerV1;

    public static string Name(string prefix, string state) => $"{prefix}_{state}";
    private static string[] Distribution(string prefix) => NameStates.Select(state => Name(prefix, state)).ToArray();
    private static string[] SemanticBirthDistribution(string prefix) => BirthSemanticStates.Select(state => Name(prefix, state)).ToArray();
    private static string[] JointBirthDistribution(string prefix) => BirthJointStates.Select(state => Name(prefix, state)).ToArray();
    private static string[] BinaryDistribution(string prefix) => [Name(prefix, "EXACT"), Name(prefix, "DIFF")];
}

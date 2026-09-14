namespace Jornada.Contracts;

/// <summary>
/// Vocabulário canônico dos parâmetros persistidos do modelo probabilístico de linkage.
/// Centraliza nomes consumidos por geração, validação e scoring para impedir deriva entre
/// componentes. Métricas de cobertura/amostra permanecem separadas do domínio probabilístico.
/// </summary>
public static class LinkageParameterCatalog
{
    public const string PriorMatchProbability = "PRIOR_MATCH_PROBABILITY";
    public const string PriorBlockMin = "PRIOR_BLOCK_MIN";
    public const string PriorBlockMax = "PRIOR_BLOCK_MAX";
    public const string Threshold = "T_LINKAGE";
    public const string ConflictMargin = "CONFLICT_MARGIN";
    public const string BirthSingleEvidenceScoring = "SCORING_BIRTH_SINGLE_EVIDENCE_V3";
    public const string BirthComponentScoring = "SCORING_BIRTH_COMPONENTS_V2";
    public const string LegacyBirthComponentScoring = "BLOCKING_BIRTH_COMPONENTS_V2";

    public const string MatchedSampleSize = "M_SAMPLE_SIZE";
    public const string UnmatchedSampleSize = "U_SAMPLE_SIZE";
    public const string PopulationSize = "POPULATION_SIZE";
    public const string DistinctBirthDate = "DISTINCT_BIRTH_DATE";

    public static readonly IReadOnlyList<string> NameStates = ["EXACT", "HIGH", "MEDIUM", "LOW"];

    public static readonly IReadOnlyList<string> CoreScoringRequired =
        [PriorMatchProbability, PriorBlockMin, PriorBlockMax, Threshold, ConflictMargin,
         .. Distribution("M_NOME"), .. Distribution("U_NOME"),
         .. Distribution("M_NOME_MAE"), .. Distribution("U_NOME_MAE")];

    public static readonly IReadOnlyList<string> BirthSingleEvidenceRequired =
        [.. BinaryDistribution("M_DATA_NASCIMENTO"), .. BinaryDistribution("U_DATA_NASCIMENTO")];

    public static readonly IReadOnlyList<string> BirthComponentRequired =
        [.. BinaryDistribution("M_NASC_DIA"), .. BinaryDistribution("U_NASC_DIA"),
         .. BinaryDistribution("M_NASC_MES"), .. BinaryDistribution("U_NASC_MES"),
         .. BinaryDistribution("M_NASC_ANO"), .. BinaryDistribution("U_NASC_ANO")];

    public static readonly IReadOnlyList<string> CalibrationValidationRequired =
        [.. CoreScoringRequired, MatchedSampleSize, UnmatchedSampleSize, PopulationSize, DistinctBirthDate];

    public static string Name(string prefix, string state) => $"{prefix}_{state}";
    private static string[] Distribution(string prefix) => NameStates.Select(state => Name(prefix, state)).ToArray();
    private static string[] BinaryDistribution(string prefix) => [Name(prefix, "EXACT"), Name(prefix, "DIFF")];
}

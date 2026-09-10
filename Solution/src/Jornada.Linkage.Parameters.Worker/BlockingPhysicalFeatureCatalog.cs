namespace Jornada.Linkage.Parameters.Worker;

public enum BlockingPhysicalStrategy
{
    DirectColumn,
    MaterializedProjection
}

/// <summary>
/// Mapeia o vocabulário lógico do otimizador para a origem física na Gold.
/// Componentes derivados não pressupõem novas colunas em gold.pessoa: eles podem ser
/// materializados em uma projeção operacional indexada de chaves de blocking.
/// </summary>
public sealed record BlockingPhysicalFeature(
    string Feature,
    string SourceColumn,
    BlockingPhysicalStrategy Strategy,
    bool MultiValued = false);

public static class BlockingPhysicalFeatureCatalog
{
    public const string MethodVersion = "BLOCKING_PHYSICAL_FEATURE_CATALOG_V1";

    private static readonly IReadOnlyDictionary<string, BlockingPhysicalFeature> Features =
        new Dictionary<string, BlockingPhysicalFeature>(StringComparer.Ordinal)
        {
            [BlockingCandidateFeatureCatalog.FullName] = new(
                BlockingCandidateFeatureCatalog.FullName,
                "nome_completo",
                BlockingPhysicalStrategy.DirectColumn),
            [BlockingCandidateFeatureCatalog.FirstName] = new(
                BlockingCandidateFeatureCatalog.FirstName,
                "nome_completo",
                BlockingPhysicalStrategy.MaterializedProjection),
            [BlockingCandidateFeatureCatalog.Surnames] = new(
                BlockingCandidateFeatureCatalog.Surnames,
                "nome_completo",
                BlockingPhysicalStrategy.MaterializedProjection,
                MultiValued: true),
            [BlockingCandidateFeatureCatalog.LastName] = new(
                BlockingCandidateFeatureCatalog.LastName,
                "nome_completo",
                BlockingPhysicalStrategy.MaterializedProjection),
            [BlockingCandidateFeatureCatalog.BirthDay] = new(
                BlockingCandidateFeatureCatalog.BirthDay,
                "data_nascimento",
                BlockingPhysicalStrategy.MaterializedProjection),
            [BlockingCandidateFeatureCatalog.BirthMonth] = new(
                BlockingCandidateFeatureCatalog.BirthMonth,
                "data_nascimento",
                BlockingPhysicalStrategy.MaterializedProjection),
            [BlockingCandidateFeatureCatalog.BirthYear] = new(
                BlockingCandidateFeatureCatalog.BirthYear,
                "data_nascimento",
                BlockingPhysicalStrategy.MaterializedProjection)
        };

    public static IReadOnlyList<BlockingPhysicalFeature> RequiredOptimizerFeatures { get; } =
        BlockingCandidateFeatureCatalog.RequiredOptimizerCandidates
            .Select(static feature => Features[feature])
            .ToArray();

    public static bool TryGet(string feature, out BlockingPhysicalFeature mapping)
    {
        if (string.IsNullOrWhiteSpace(feature))
        {
            mapping = null!;
            return false;
        }

        return Features.TryGetValue(feature.Trim(), out mapping!);
    }
}

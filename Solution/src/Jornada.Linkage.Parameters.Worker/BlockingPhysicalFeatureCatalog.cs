namespace Jornada.Linkage.Parameters.Worker;

public enum BlockingPhysicalStrategy
{
    DirectColumn,
    MaterializedProjection
}

public enum BlockingPhysicalSourceScope
{
    GoldCurrent,
    SilverObservationHistory
}

/// <summary>
/// Mapeia o vocabulário lógico do otimizador para a origem física e temporal.
/// Dados estáveis podem partir da Gold corrente. Nomes versionáveis usam o histórico
/// de observações Silver vinculado ao UUID para não perder aliases legítimos anteriores.
/// </summary>
public sealed record BlockingPhysicalFeature(
    string Feature,
    string SourceColumn,
    BlockingPhysicalStrategy Strategy,
    BlockingPhysicalSourceScope SourceScope,
    bool MultiValued = false);

public static class BlockingPhysicalFeatureCatalog
{
    public const string MethodVersion = "BLOCKING_PHYSICAL_FEATURE_CATALOG_V4";

    private static readonly IReadOnlyDictionary<string, BlockingPhysicalFeature> Features =
        new Dictionary<string, BlockingPhysicalFeature>(StringComparer.Ordinal)
        {
            [BlockingCandidateFeatureCatalog.FullName] = new(
                BlockingCandidateFeatureCatalog.FullName,
                "nome_completo",
                BlockingPhysicalStrategy.MaterializedProjection,
                BlockingPhysicalSourceScope.SilverObservationHistory),
            [BlockingCandidateFeatureCatalog.FirstName] = new(
                BlockingCandidateFeatureCatalog.FirstName,
                "nome_completo",
                BlockingPhysicalStrategy.MaterializedProjection,
                BlockingPhysicalSourceScope.SilverObservationHistory),
            [BlockingCandidateFeatureCatalog.Surnames] = new(
                BlockingCandidateFeatureCatalog.Surnames,
                "nome_completo",
                BlockingPhysicalStrategy.MaterializedProjection,
                BlockingPhysicalSourceScope.SilverObservationHistory,
                MultiValued: true),
            [BlockingCandidateFeatureCatalog.LastName] = new(
                BlockingCandidateFeatureCatalog.LastName,
                "nome_completo",
                BlockingPhysicalStrategy.MaterializedProjection,
                BlockingPhysicalSourceScope.SilverObservationHistory),
            [BlockingCandidateFeatureCatalog.MotherFullName] = new(
                BlockingCandidateFeatureCatalog.MotherFullName,
                "nome_mae",
                BlockingPhysicalStrategy.MaterializedProjection,
                BlockingPhysicalSourceScope.SilverObservationHistory),
            [BlockingCandidateFeatureCatalog.MotherFirstName] = new(
                BlockingCandidateFeatureCatalog.MotherFirstName,
                "nome_mae",
                BlockingPhysicalStrategy.MaterializedProjection,
                BlockingPhysicalSourceScope.SilverObservationHistory),
            [BlockingCandidateFeatureCatalog.MotherSurnames] = new(
                BlockingCandidateFeatureCatalog.MotherSurnames,
                "nome_mae",
                BlockingPhysicalStrategy.MaterializedProjection,
                BlockingPhysicalSourceScope.SilverObservationHistory,
                MultiValued: true),
            [BlockingCandidateFeatureCatalog.MotherLastName] = new(
                BlockingCandidateFeatureCatalog.MotherLastName,
                "nome_mae",
                BlockingPhysicalStrategy.MaterializedProjection,
                BlockingPhysicalSourceScope.SilverObservationHistory),
            [BlockingCandidateFeatureCatalog.BirthDay] = new(
                BlockingCandidateFeatureCatalog.BirthDay,
                "data_nascimento",
                BlockingPhysicalStrategy.MaterializedProjection,
                BlockingPhysicalSourceScope.GoldCurrent),
            [BlockingCandidateFeatureCatalog.BirthMonth] = new(
                BlockingCandidateFeatureCatalog.BirthMonth,
                "data_nascimento",
                BlockingPhysicalStrategy.MaterializedProjection,
                BlockingPhysicalSourceScope.GoldCurrent),
            [BlockingCandidateFeatureCatalog.BirthYear] = new(
                BlockingCandidateFeatureCatalog.BirthYear,
                "data_nascimento",
                BlockingPhysicalStrategy.MaterializedProjection,
                BlockingPhysicalSourceScope.GoldCurrent)
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

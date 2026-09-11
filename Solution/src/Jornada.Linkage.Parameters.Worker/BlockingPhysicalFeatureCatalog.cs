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

public sealed record BlockingPhysicalFeature(
    string Feature,
    string SourceColumn,
    BlockingPhysicalStrategy Strategy,
    BlockingPhysicalSourceScope SourceScope,
    bool MultiValued = false);

public static class BlockingPhysicalFeatureCatalog
{
    public const string MethodVersion = "BLOCKING_PHYSICAL_FEATURE_CATALOG_V6";

    private static readonly IReadOnlyDictionary<string, BlockingPhysicalFeature> Features =
        new Dictionary<string, BlockingPhysicalFeature>(StringComparer.Ordinal)
        {
            [BlockingCandidateFeatureCatalog.FullName] = Name(BlockingCandidateFeatureCatalog.FullName, "nome_completo"),
            [BlockingCandidateFeatureCatalog.FullNameUpper] = Name(BlockingCandidateFeatureCatalog.FullNameUpper, "nome_completo"),
            [BlockingCandidateFeatureCatalog.FullNameUpperNoDiacritics] = Name(BlockingCandidateFeatureCatalog.FullNameUpperNoDiacritics, "nome_completo"),
            [BlockingCandidateFeatureCatalog.FullNameWithoutParticles] = Name(BlockingCandidateFeatureCatalog.FullNameWithoutParticles, "nome_completo"),
            [BlockingCandidateFeatureCatalog.FullNamePhoneticPtBr] = Name(BlockingCandidateFeatureCatalog.FullNamePhoneticPtBr, "nome_completo"),
            [BlockingCandidateFeatureCatalog.FirstName] = Name(BlockingCandidateFeatureCatalog.FirstName, "nome_completo"),
            [BlockingCandidateFeatureCatalog.Surnames] = Name(BlockingCandidateFeatureCatalog.Surnames, "nome_completo", MultiValued: true),
            [BlockingCandidateFeatureCatalog.LastName] = Name(BlockingCandidateFeatureCatalog.LastName, "nome_completo"),

            [BlockingCandidateFeatureCatalog.MotherFullName] = Name(BlockingCandidateFeatureCatalog.MotherFullName, "nome_mae"),
            [BlockingCandidateFeatureCatalog.MotherFullNameUpper] = Name(BlockingCandidateFeatureCatalog.MotherFullNameUpper, "nome_mae"),
            [BlockingCandidateFeatureCatalog.MotherFullNameUpperNoDiacritics] = Name(BlockingCandidateFeatureCatalog.MotherFullNameUpperNoDiacritics, "nome_mae"),
            [BlockingCandidateFeatureCatalog.MotherFullNameWithoutParticles] = Name(BlockingCandidateFeatureCatalog.MotherFullNameWithoutParticles, "nome_mae"),
            [BlockingCandidateFeatureCatalog.MotherFullNamePhoneticPtBr] = Name(BlockingCandidateFeatureCatalog.MotherFullNamePhoneticPtBr, "nome_mae"),
            [BlockingCandidateFeatureCatalog.MotherFirstName] = Name(BlockingCandidateFeatureCatalog.MotherFirstName, "nome_mae"),
            [BlockingCandidateFeatureCatalog.MotherSurnames] = Name(BlockingCandidateFeatureCatalog.MotherSurnames, "nome_mae", MultiValued: true),
            [BlockingCandidateFeatureCatalog.MotherLastName] = Name(BlockingCandidateFeatureCatalog.MotherLastName, "nome_mae"),

            [BlockingCandidateFeatureCatalog.BirthDay] = Birth(BlockingCandidateFeatureCatalog.BirthDay),
            [BlockingCandidateFeatureCatalog.BirthMonth] = Birth(BlockingCandidateFeatureCatalog.BirthMonth),
            [BlockingCandidateFeatureCatalog.BirthYear] = Birth(BlockingCandidateFeatureCatalog.BirthYear)
        };

    public static IReadOnlyList<BlockingPhysicalFeature> CalibratorFeatures { get; } =
        BlockingCandidateFeatureCatalog.CalibratorCandidates
            .Select(static feature => Features[feature])
            .ToArray();

    public static IReadOnlyList<BlockingPhysicalFeature> RequiredOptimizerFeatures => CalibratorFeatures;

    public static bool TryGet(string feature, out BlockingPhysicalFeature mapping)
    {
        if (string.IsNullOrWhiteSpace(feature))
        {
            mapping = null!;
            return false;
        }

        return Features.TryGetValue(feature.Trim(), out mapping!);
    }

    private static BlockingPhysicalFeature Name(string feature, string sourceColumn, bool MultiValued = false) =>
        new(
            feature,
            sourceColumn,
            BlockingPhysicalStrategy.MaterializedProjection,
            BlockingPhysicalSourceScope.SilverObservationHistory,
            MultiValued);

    private static BlockingPhysicalFeature Birth(string feature) =>
        new(
            feature,
            "data_nascimento",
            BlockingPhysicalStrategy.MaterializedProjection,
            BlockingPhysicalSourceScope.GoldCurrent);
}

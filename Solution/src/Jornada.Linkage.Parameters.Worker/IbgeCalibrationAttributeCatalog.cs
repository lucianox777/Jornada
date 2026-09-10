namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Catálogo explícito das correspondências semânticas entre atributos candidatos
/// do Calibrador e fontes estatísticas externas do IBGE.
///
/// A presença de um atributo no linkage não implica suporte IBGE. Um atributo sem
/// correspondência permanece disponível para calibração usando evidência da Jornada.
/// </summary>
public static class IbgeCalibrationAttributeCatalog
{
    public const string MethodVersion = "IBGE_CALIBRATION_ATTRIBUTE_CATALOG_V1";

    private static readonly IReadOnlyDictionary<string, string> Supported =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [BlockingCandidateFeatureCatalog.NameFull] = ExternalNameFrequencyCatalog.IbgeSource,
            [BlockingCandidateFeatureCatalog.NameFirst] = ExternalNameFrequencyCatalog.IbgeSource,
            [BlockingCandidateFeatureCatalog.NameSurnames] = ExternalNameFrequencyCatalog.IbgeSource,
            [BlockingCandidateFeatureCatalog.NameLast] = ExternalNameFrequencyCatalog.IbgeSource
        };

    public static IReadOnlyCollection<string> SupportedFeatures => Supported.Keys.ToArray();

    public static bool TryGetSource(string feature, out string source)
    {
        if (string.IsNullOrWhiteSpace(feature))
        {
            source = string.Empty;
            return false;
        }

        return Supported.TryGetValue(feature.Trim(), out source!);
    }

    public static bool Supports(string feature) => TryGetSource(feature, out _);
}

namespace Jornada.Linkage.Parameters.Worker;

public enum IbgeNameStatisticKind
{
    FirstName,
    Surname
}

public sealed record IbgeCalibrationAttributeMapping(
    string Feature,
    string Source,
    IbgeNameStatisticKind StatisticKind);

/// <summary>
/// Catálogo explícito das correspondências semânticas entre atributos candidatos
/// do Calibrador e fontes estatísticas externas do IBGE.
///
/// A presença de um atributo no linkage não implica suporte IBGE. Um atributo sem
/// correspondência permanece disponível para calibração usando evidência da Jornada.
/// </summary>
public static class IbgeCalibrationAttributeCatalog
{
    public const string MethodVersion = "IBGE_CALIBRATION_ATTRIBUTE_CATALOG_V2";

    private static readonly IReadOnlyDictionary<string, IbgeCalibrationAttributeMapping> Supported =
        new Dictionary<string, IbgeCalibrationAttributeMapping>(StringComparer.Ordinal)
        {
            [BlockingCandidateFeatureCatalog.FirstName] = new(
                BlockingCandidateFeatureCatalog.FirstName,
                ExternalNameFrequencyCatalog.IbgeSource,
                IbgeNameStatisticKind.FirstName),
            [BlockingCandidateFeatureCatalog.Surnames] = new(
                BlockingCandidateFeatureCatalog.Surnames,
                ExternalNameFrequencyCatalog.IbgeSource,
                IbgeNameStatisticKind.Surname),
            [BlockingCandidateFeatureCatalog.LastName] = new(
                BlockingCandidateFeatureCatalog.LastName,
                ExternalNameFrequencyCatalog.IbgeSource,
                IbgeNameStatisticKind.Surname)
        };

    public static IReadOnlyCollection<string> SupportedFeatures => Supported.Keys.ToArray();

    public static bool TryGetMapping(string feature, out IbgeCalibrationAttributeMapping mapping)
    {
        if (string.IsNullOrWhiteSpace(feature))
        {
            mapping = null!;
            return false;
        }

        return Supported.TryGetValue(feature.Trim(), out mapping!);
    }

    public static bool TryGetSource(string feature, out string source)
    {
        if (TryGetMapping(feature, out var mapping))
        {
            source = mapping.Source;
            return true;
        }

        source = string.Empty;
        return false;
    }

    public static bool Supports(string feature) => TryGetMapping(feature, out _);
}

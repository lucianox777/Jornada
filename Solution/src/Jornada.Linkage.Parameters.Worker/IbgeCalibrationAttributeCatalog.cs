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
///
/// O produto oficial separa primeiro nome e sobrenomes. A Jornada consegue projetar
/// com semântica compatível o primeiro nome de nome_completo, mas não preserva hoje a
/// fronteira original entre nome/nome composto e sobrenomes. Por isso as features
/// internas name_surnames/name_last e equivalentes da mãe continuam disponíveis ao
/// Calibrador como heurísticas de blocking da Jornada, porém não recebem frequência
/// oficial de SOBRENOME por aproximação.
/// </summary>
public static class IbgeCalibrationAttributeCatalog
{
    public const string MethodVersion = "IBGE_CALIBRATION_ATTRIBUTE_CATALOG_V5";

    private static readonly IReadOnlyDictionary<string, IbgeCalibrationAttributeMapping> Supported =
        new Dictionary<string, IbgeCalibrationAttributeMapping>(StringComparer.Ordinal)
        {
            [BlockingCandidateFeatureCatalog.FirstName] = FirstName(BlockingCandidateFeatureCatalog.FirstName),
            [BlockingCandidateFeatureCatalog.MotherFirstName] = FirstName(BlockingCandidateFeatureCatalog.MotherFirstName)
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

    /// <summary>
    /// Resolve uma frequência somente quando o atributo da Jornada possui correspondência
    /// semântica explícita e o snapshot tipado contém a mesma classe estatística.
    /// Não converte componentes derivados de nome completo em sobrenome oficial nem usa
    /// frequência de nome completo por aproximação.
    /// </summary>
    public static bool TryGetOccurrences(
        IbgeTypedNameFrequencySnapshot snapshot,
        string feature,
        string value,
        out long occurrences)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!string.Equals(snapshot.Source, ExternalNameFrequencyCatalog.IbgeSource, StringComparison.Ordinal) ||
            !TryGetMapping(feature, out var mapping))
        {
            occurrences = 0;
            return false;
        }

        return snapshot.TryGetOccurrences(mapping.StatisticKind, value, out occurrences);
    }

    public static bool Supports(string feature) => TryGetMapping(feature, out _);

    private static IbgeCalibrationAttributeMapping FirstName(string feature) =>
        new(feature, ExternalNameFrequencyCatalog.IbgeSource, IbgeNameStatisticKind.FirstName);
}

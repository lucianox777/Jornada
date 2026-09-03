namespace Jornada.Processor.Worker;

internal enum TerritorialReferenceNature
{
    DOMICILIAR,
    ACOLHIMENTO_INSTITUCIONAL,
    REFERENCIA_TERRITORIAL_DECLARADA
}

internal enum GeographicResolutionStatus
{
    RESOLVIDA,
    FORA_MUNICIPIO,
    SEM_ENDERECO_APTO,
    NAO_RESOLVIDA_ORIGEM
}

internal enum ReferenceGeographyOrigin
{
    ORIGEM
}

internal sealed record ReferenceGeography(
    string DistritoCodigo,
    string DistritoNome,
    string SubprefeituraCodigo,
    string SubprefeituraNome,
    string ReferenciaMalha,
    ReferenceGeographyOrigin Origem,
    DateTimeOffset? ResolvidoEm);

/// <summary>
/// Fase 1: a territorialização é responsabilidade do Gestor. Não existe chamada online Pessoa-a-Pessoa
/// para PRODAM no caminho normal da ingestão. ENDERECO_RESIDENCIAL e REFERENCIA_TERRITORIAL devem
/// trazer situacaoGeografia explícita; quando RESOLVIDA, Distrito/Subprefeitura/referência da malha são
/// obrigatórios. As demais situações qualificam a ausência sem inventar geografia.
/// </summary>
internal static class OriginTerritorialGeography
{
    public static ParsedPackage ApplyResolutionTimestamp(ParsedPackage package)
    {
        var people = package.Pessoas.Select(person => person with
        {
            Atributos = person.Atributos.Select(attribute =>
                attribute.Geografia is null
                    ? attribute
                    : attribute with
                    {
                        Geografia = attribute.Geografia with
                        {
                            Origem = ReferenceGeographyOrigin.ORIGEM,
                            ResolvidoEm = attribute.Geografia.ResolvidoEm ?? package.Manifest.DataReferencia
                        }
                    }).ToArray()
        }).ToArray();

        return package with { Pessoas = people };
    }
}

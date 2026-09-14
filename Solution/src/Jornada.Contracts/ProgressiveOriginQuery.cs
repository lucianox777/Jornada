using System.Text.Json.Serialization;

namespace Jornada.Contracts;

/// <summary>
/// Consulta da identidade técnica de uma origem, sem executar resolução.
/// CodigoSistemaOrigem identifica o contexto autorizado do chamador. Quando
/// CodigoBasePessoaOrigem é informado, a identidade de origem é localizada pelo
/// namespace compartilhável (base,codigo); quando omitido, preserva-se a consulta
/// legada por (sistema,codigo).
/// </summary>
public sealed record ProgressiveOriginQueryRequest(
    string CodigoSistemaOrigem,
    string CodigoPessoaOrigem,
    string? CodigoBasePessoaOrigem = null);

/// <summary>
/// Referência progressiva corrente. InitialUuid é imutável; CanonicalUuid só é
/// publicado com REFERENCIA. Não representa atribuição de fatos nem autorização
/// para consultar a Pessoa canônica. CodigoBasePessoaOrigem explicita o namespace
/// efetivamente usado quando a consulta é base-aware.
/// </summary>
public sealed record ProgressiveOriginQueryResponse(
    string CodigoSistemaOrigem,
    string CodigoPessoaOrigem,
    Guid InitialUuid,
    Guid? CanonicalUuid,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ProgressiveIdentityStatus Estado,
    long Versao,
    DateTimeOffset CriadoEm,
    DateTimeOffset AtualizadoEm,
    DateTimeOffset? UltimaResolucaoEm,
    string? CodigoBasePessoaOrigem = null);

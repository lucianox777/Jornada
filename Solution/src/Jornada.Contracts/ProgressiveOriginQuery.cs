namespace Jornada.Contracts;

/// <summary>Consulta da identidade técnica de uma origem, sem executar resolução.</summary>
public sealed record ProgressiveOriginQueryRequest(
    string CodigoSistemaOrigem,
    string CodigoPessoaOrigem);

/// <summary>
/// Referência progressiva corrente. InitialUuid é imutável; CanonicalUuid só é
/// publicado com REFERENCIA. Não representa atribuição de fatos nem autorização
/// para consultar a Pessoa canônica.
/// </summary>
public sealed record ProgressiveOriginQueryResponse(
    string CodigoSistemaOrigem,
    string CodigoPessoaOrigem,
    Guid InitialUuid,
    Guid? CanonicalUuid,
    ProgressiveIdentityStatus Estado,
    long Versao,
    DateTimeOffset CriadoEm,
    DateTimeOffset AtualizadoEm,
    DateTimeOffset? UltimaResolucaoEm);

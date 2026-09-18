namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Fonte de universo de candidatos fornecida por processo de amostragem/rotulagem governado.
/// O contrato é independente do mecanismo de persistência e do dialeto SQL.
/// </summary>
public sealed record CandidateUniverseSource(
    Guid SourceId,
    DateOnly BirthDate,
    string Name,
    string? Mother,
    Guid? KnownPessoaUuid = null);

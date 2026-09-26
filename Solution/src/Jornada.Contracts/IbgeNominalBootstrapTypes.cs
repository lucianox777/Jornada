namespace Jornada.Linkage.Parameters.Worker;

// Tipos historicos de dominio nominal mantem namespace publico preexistente,
// mas sao compilados uma so vez em Contracts. Worker, Core, Evaluation e
// Operational.Sql consomem exatamente a MESMA identidade de tipo/assembly.
public enum IbgeNameStatisticKind
{
    FirstName,
    Surname
}

public sealed record IbgeTypedNameFrequencyEntry(
    IbgeNameStatisticKind StatisticKind,
    string Name,
    long Occurrences);

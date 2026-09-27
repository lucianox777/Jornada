using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Fronteira pública mínima sobre o MESMO rank C# Fellegi-Sunter do Runner.
/// Score, posterior, LLR e a posição no ranking nunca atravessam esta fronteira.
/// </summary>
public sealed record SemiblindCandidateSnapshot(
    Guid PessoaUuid, string? NomeCompleto, DateOnly? DataNascimento, string? NomeMae);

public static class SemiblindCandidateSelector
{
    public const int MaximumVisibleCandidates = 5;

    public static IReadOnlyList<SemiblindCandidateSnapshot> Select(
        string algorithmVersion,
        IReadOnlyDictionary<string, decimal> parameters,
        IdentityObservation observation,
        IReadOnlyList<SemiblindCandidateSnapshot> candidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithmVersion);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(candidates);
        if (!string.IsNullOrWhiteSpace(observation.Cpf))
            throw new ArgumentException("A busca probabilística semicega não aceita CPF.", nameof(observation));

        // A política de validação e o ranking são os mesmos usados pelo Runner.
        var model = LinkageModelPolicy.Create(Guid.Empty, 0, algorithmVersion, parameters);
        var ranked = ProbabilisticLinkageDecisions.Rank(
            model,
            observation,
            candidates.Select(static candidate => new LinkageCandidate(
                candidate.PessoaUuid, candidate.NomeCompleto, candidate.DataNascimento, candidate.NomeMae)).ToArray());

        return SelectRanked(ranked, candidates);
    }

    internal static IReadOnlyList<SemiblindCandidateSnapshot> SelectRanked(
        IReadOnlyList<CandidateScore> ranked,
        IReadOnlyList<SemiblindCandidateSnapshot> candidates)
    {
        var byId = candidates.ToDictionary(static candidate => candidate.PessoaUuid);
        // Primeiro escolhe os cinco de maior evidência internamente. A ordenação exibida é
        // exclusivamente por UUID, neutra em relação a score, LLR ou posterior.
        return ranked.Take(MaximumVisibleCandidates)
            .Select(score => byId[score.PessoaUuid])
            .OrderBy(static candidate => candidate.PessoaUuid)
            .ToArray();
    }
}

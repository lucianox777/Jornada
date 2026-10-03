using System.Security.Cryptography;
using Jornada.Contracts;

namespace Jornada.Api;

/// <summary>
/// Limite de exposição: UUIDs e métricas permanecem internos; opções não autorizam vínculo.
/// </summary>
public sealed class SemiblindIdentitySearchService(ISemiblindCandidateRetriever retriever, IPolicyEngine policy)
    : ISemiblindIdentitySearchService
{
    public async Task<SemiblindIdentitySearchResponse> SearchAsync(
        AccessContext context,
        SemiblindIdentitySearchRequest request,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Nome?.Length > 200 || request.NomeMae?.Length > 200)
            throw new ArgumentException("Campos nominais limitados a 200 caracteres.");

        var retrieval = await retriever.RetrieveAsync(request, cancellationToken);
        if (!retrieval.Completed)
            return new SemiblindIdentitySearchResponse(
                correlationId, Array.Empty<SemiblindIdentityCandidate>(), true, false,
                retrieval.IncompleteReason ?? SemiblindRetrievalReasons.NoEligiblePass);
        var retrieved = retrieval.Candidates;
        // Autorização por pessoa ocorre ANTES de projetar qualquer atributo identificador.
        var authorized = new List<SemiblindInternalCandidate>();
        var seenPersons = new HashSet<Guid>();
        foreach (var candidate in retrieved)
        {
            // Uma Pessoa não pode ocupar duas opções, mesmo que o retriever retorne múltiplas observações.
            if (!seenPersons.Add(candidate.PessoaUuid)) continue;
            if (await policy.IsAllowedAsync(context, "jornada.identidade.busca.read",
                context.TipoCodigo, candidate.PessoaUuid, cancellationToken))
                authorized.Add(candidate);
            if (authorized.Count == 5) break;
        }
        // Ordenação neutra criptograficamente aleatória; não retornar índice de ranking.
        var options = authorized.Select(candidate => new SemiblindIdentityCandidate(
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            candidate.Nome ?? string.Empty,
            candidate.DataNascimento,
            candidate.NomeMae)).ToList();
        for (var i = options.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (options[i], options[j]) = (options[j], options[i]);
        }

        return new SemiblindIdentitySearchResponse(correlationId, options, true);
    }
}

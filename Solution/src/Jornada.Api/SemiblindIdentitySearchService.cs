using System.Security.Cryptography;
using Jornada.Contracts;

namespace Jornada.Api;

/// <summary>
/// Limite de exposição: UUIDs e métricas permanecem internos; opções não autorizam vínculo.
/// </summary>
public sealed class SemiblindIdentitySearchService(ISemiblindCandidateRetriever retriever)
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
        if (string.IsNullOrWhiteSpace(request.Nome) || request.Nome.Length > 200
            || request.NomeMae?.Length > 200)
            throw new ArgumentException("Nome obrigatório e campos nominais limitados a 200 caracteres.");

        var retrieved = await retriever.RetrieveAsync(request, cancellationToken);
        // Ordenação neutra criptograficamente aleatória; não retornar índice de ranking.
        var options = retrieved.Take(5).Select(candidate => new SemiblindIdentityCandidate(
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

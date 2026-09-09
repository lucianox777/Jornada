using System.Collections.Immutable;

namespace Jornada.Contracts;

/// <summary>
/// Escopo factual fechado para uma origem afetada pela composição. O adapter é responsável por
/// provar a completude no armazenamento autoritativo; o planejador puro não consulta banco.
/// </summary>
public sealed record IdentityCompositionProjectionScope(
    Guid InitialUuid,
    long PessoaOrigemId,
    ImmutableArray<long> RegistroObservacaoIds,
    bool IsComplete);

/// <summary>
/// Plano puro de impacto da composição sobre projeções. Ele identifica o que precisa ser
/// revalidado/recomposto, mas deliberadamente não escolhe nem altera atribuição factual.
/// </summary>
public sealed record IdentityCompositionRecompositionPlan(
    Guid DecisionId,
    string CompositionRequestHash,
    ImmutableArray<Guid> AffectedInitialUuids,
    ImmutableArray<Guid> AffectedReferenceUuids,
    ImmutableArray<long> PessoaOrigemIds,
    ImmutableArray<long> RegistroObservacaoIds,
    bool RequiresFactualRevalidation);

/// <summary>
/// Planejador determinístico da etapa anterior à publicação Gold/Serving. A composição estrutural
/// nunca é interpretada como autorização para reatribuir fatos, vinculo_fonte ou identity_map.
/// </summary>
public static class IdentityCompositionRecompositionPlanner
{
    public const string Version = "IDENTITY_COMPOSITION_RECOMPOSITION_PLAN_V1";

    public static IdentityCompositionRecompositionPlan Prepare(
        IdentityCompositionPlan composition,
        ImmutableArray<IdentityCompositionProjectionScope> projectionScopes)
    {
        ArgumentNullException.ThrowIfNull(composition);
        if (composition.DecisionId == Guid.Empty || string.IsNullOrWhiteSpace(composition.RequestHash) ||
            composition.Changes.IsDefaultOrEmpty)
            throw new InvalidOperationException("Plano de composição aplicado ausente ou incompleto.");
        if (projectionScopes.IsDefault)
            throw new InvalidOperationException("Leitura de projeção ausente.");

        var changedInitialUuids = composition.Changes.Select(c => c.InitialUuid).ToArray();
        if (changedInitialUuids.Any(id => id == Guid.Empty) ||
            changedInitialUuids.Distinct().Count() != changedInitialUuids.Length)
            throw new InvalidOperationException("Plano de composição contém origem vazia ou duplicada.");

        var scopes = projectionScopes.GroupBy(s => s.InitialUuid).ToArray();
        if (scopes.Any(g => g.Key == Guid.Empty || g.Count() != 1))
            throw new InvalidOperationException("Escopo de projeção contém origem vazia ou duplicada.");
        var scopeByInitial = scopes.ToDictionary(g => g.Key, g => g.Single());
        var changedSet = changedInitialUuids.ToHashSet();
        if (scopeByInitial.Count != changedSet.Count ||
            scopeByInitial.Keys.Any(id => !changedSet.Contains(id)) ||
            changedSet.Any(id => !scopeByInitial.ContainsKey(id)))
            throw new InvalidOperationException("Escopo de projeção não fecha exatamente as origens alteradas.");

        var pessoaOrigemIds = new HashSet<long>();
        var registroIds = new HashSet<long>();
        foreach (var scope in scopeByInitial.Values)
        {
            if (!scope.IsComplete || scope.PessoaOrigemId <= 0 || scope.RegistroObservacaoIds.IsDefault)
                throw new InvalidOperationException("Escopo factual incompleto ou inválido.");
            if (!pessoaOrigemIds.Add(scope.PessoaOrigemId))
                throw new InvalidOperationException("Pessoa de origem aparece em mais de um UUID inicial afetado.");
            foreach (var registroId in scope.RegistroObservacaoIds)
                if (registroId <= 0 || !registroIds.Add(registroId))
                    throw new InvalidOperationException("Registro factual inválido ou duplicado no escopo afetado.");
        }

        var references = composition.Changes
            .SelectMany(c => new Guid?[] { c.BeforeUuid, c.AfterUuid })
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .Order()
            .ToImmutableArray();

        return new IdentityCompositionRecompositionPlan(
            composition.DecisionId,
            composition.RequestHash,
            changedSet.Order().ToImmutableArray(),
            references,
            pessoaOrigemIds.Order().ToImmutableArray(),
            registroIds.Order().ToImmutableArray(),
            registroIds.Count > 0);
    }
}

using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

public sealed record BlockingCandidateClause(string Feature, IReadOnlyList<string> Values);

public sealed record BlockingCandidatePassLookup(
    string PassId,
    IReadOnlyList<BlockingCandidateClause> Clauses);

/// <summary>
/// Traduz o ruleset publicado em lookups indexáveis sobre identidade.blocking_chave.
/// Não executa score nem decide identidade. OR é aplicado entre valores do mesmo atributo;
/// os atributos de um passe são combinados por AND pelo executor físico.
/// </summary>
public static class BlockingRuleSetCandidatePlanner
{
    public const string MethodVersion = "BLOCKING_RULESET_CANDIDATE_PLANNER_V2";

    public static IReadOnlyList<BlockingCandidatePassLookup> Plan(
        LinkageDynamicRuleSet ruleSet,
        IdentityObservation observation)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(observation);

        if (!string.IsNullOrWhiteSpace(observation.Cpf))
            throw new InvalidOperationException(
                "Blocking probabilístico por ruleset é exclusivo para observação sem CPF; CPF válido segue a resolução determinística.");

        var projected = BlockingProjectionKeyProjector.Project(
                observation.NomeCompleto,
                observation.NomeMae,
                observation.DataNascimento)
            .Concat(PersonResolutionBlockingProjector.Project(observation.ResolutionAttributes))
            .Distinct()
            .ToArray();

        var valuesByFeature = projected
            .GroupBy(static key => key.Feature, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<string>)group
                    .Select(static key => key.Value)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static value => value, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var planned = new List<BlockingCandidatePassLookup>();
        foreach (var pass in ruleSet.EffectiveBlockingPasses)
        {
            var clauses = new List<BlockingCandidateClause>(pass.Fields.Count);
            var complete = true;

            foreach (var feature in pass.Fields)
            {
                _ = BlockingFeatureTemporalCatalog.Get(feature);
                if (!valuesByFeature.TryGetValue(feature, out var values) || values.Count == 0)
                {
                    complete = false;
                    break;
                }

                clauses.Add(new BlockingCandidateClause(feature, values));
            }

            if (!complete)
                continue;

            planned.Add(new BlockingCandidatePassLookup(
                pass.PassId,
                clauses.OrderBy(static clause => clause.Feature, StringComparer.Ordinal).ToArray()));
        }

        return planned
            .OrderBy(static pass => pass.PassId, StringComparer.Ordinal)
            .ToArray();
    }
}

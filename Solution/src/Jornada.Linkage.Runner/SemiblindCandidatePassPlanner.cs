using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>Plano específico da busca síncrona. O batch permanece inalterado.</summary>
internal static class SemiblindCandidatePassPlanner
{
    internal static IReadOnlyList<BlockingCandidatePassLookup> Plan(
        LinkageDynamicRuleSet ruleSet, IdentityObservation observation)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(observation);
        var dynamicPasses = BlockingRuleSetCandidatePlanner.Plan(ruleSet, observation);
        // A falta de nascimento impede passes combinados, mas não suprime passes dinâmicos.
        var combinedPasses = CombinedIdentityCandidatePlanner.Plan(observation);
        return dynamicPasses.Concat(combinedPasses).ToArray();
    }
}

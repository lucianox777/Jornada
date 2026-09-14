using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Parameters.Worker;

public enum PairwiseGroupingState
{
    NoComposition,
    Eligible,
    Ambiguous
}

public sealed record PairwiseGroupingComponentAssessment(
    Guid ComponentId,
    IReadOnlyList<Guid> Members,
    PairwiseGroupingState State,
    int RequiredPairs,
    int AcceptedPairs,
    int RejectedPairs,
    int InconclusivePairs,
    int MissingPairs,
    string Reason);

public sealed record PairwiseGroupingAssessmentReport(
    string PolicyVersion,
    string DiagnosticVersion,
    string EvidenceFingerprintSha256,
    IReadOnlyList<PairwiseGroupingComponentAssessment> Components)
{
    public int EligibleComponents => Components.Count(component => component.State == PairwiseGroupingState.Eligible);
    public int AmbiguousComponents => Components.Count(component => component.State == PairwiseGroupingState.Ambiguous);
}

/// <summary>
/// Gate conservador e somente-leitura para agrupamento probabilístico.
///
/// A política não aplica fechamento transitivo. Um componente com mais de um membro só é
/// elegível quando TODAS as combinações de pares do componente foram explicitamente aceitas.
/// Aresta ausente, rejeitada ou inconclusiva torna o componente ambíguo e impede composição
/// automática. O resultado não escolhe UUID canônico, não cria IdentityCompositionDecision,
/// não grava ledger e não publica identidade.
/// </summary>
public static class ConservativePairwiseGroupingPolicy
{
    public const string Version = "PAIRWISE_GROUPING_COMPLETE_LINK_CONSERVATIVE_V1";

    public static PairwiseGroupingAssessmentReport Evaluate(IReadOnlyCollection<PairwiseLinkageDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);

        // Centraliza no diagnóstico canônico a validação de pares, orientação, duplicidade e score.
        var diagnostic = PairwiseTransitivityDiagnostic.Analyze(decisions);
        var edges = decisions.ToDictionary(
            decision => CanonicalPair(decision.LeftId, decision.RightId),
            decision => decision,
            EqualityComparer<(Guid Left, Guid Right)>.Default);

        var assessments = diagnostic.Components
            .OrderBy(component => component.ComponentId)
            .Select(component => AssessComponent(component, edges))
            .ToArray();

        return new PairwiseGroupingAssessmentReport(
            Version,
            diagnostic.Version,
            Fingerprint(decisions),
            Array.AsReadOnly(assessments));
    }

    private static PairwiseGroupingComponentAssessment AssessComponent(
        PairwiseTransitivityComponent component,
        IReadOnlyDictionary<(Guid Left, Guid Right), PairwiseLinkageDecision> edges)
    {
        if (component.Members.Count < 2)
            return new PairwiseGroupingComponentAssessment(
                component.ComponentId,
                component.Members,
                PairwiseGroupingState.NoComposition,
                0, 0, 0, 0, 0,
                "SEM_COMPONENTE_ACEITO_MULTIMEMBRO");

        var required = checked(component.Members.Count * (component.Members.Count - 1) / 2);
        var accepted = 0;
        var rejected = 0;
        var inconclusive = 0;
        var missing = 0;

        for (var i = 0; i < component.Members.Count; i++)
        for (var j = i + 1; j < component.Members.Count; j++)
        {
            var key = CanonicalPair(component.Members[i], component.Members[j]);
            if (!edges.TryGetValue(key, out var edge))
            {
                missing++;
                continue;
            }

            switch (edge.Decision)
            {
                case PairwiseLinkageDecisionState.Accepted:
                    accepted++;
                    break;
                case PairwiseLinkageDecisionState.Rejected:
                    rejected++;
                    break;
                case PairwiseLinkageDecisionState.Inconclusive:
                    inconclusive++;
                    break;
                default:
                    throw new InvalidOperationException("Estado pairwise fora do contrato validado.");
            }
        }

        if (accepted == required)
            return new PairwiseGroupingComponentAssessment(
                component.ComponentId,
                component.Members,
                PairwiseGroupingState.Eligible,
                required, accepted, rejected, inconclusive, missing,
                "COMPLETE_LINK_TODOS_PARES_ACEITOS");

        var reason = rejected > 0
            ? "AMBIGUO_FECHAMENTO_REJEITADO"
            : inconclusive > 0
                ? "AMBIGUO_FECHAMENTO_INCONCLUSIVO"
                : missing > 0
                    ? "AMBIGUO_FECHAMENTO_NAO_OBSERVADO"
                    : "AMBIGUO_COMPONENTE_NAO_FECHADO";

        return new PairwiseGroupingComponentAssessment(
            component.ComponentId,
            component.Members,
            PairwiseGroupingState.Ambiguous,
            required, accepted, rejected, inconclusive, missing,
            reason);
    }

    private static string Fingerprint(IEnumerable<PairwiseLinkageDecision> decisions)
    {
        var canonical = decisions
            .Select(decision =>
            {
                var pair = CanonicalPair(decision.LeftId, decision.RightId);
                var score = decision.Score?.ToString("G29", CultureInfo.InvariantCulture) ?? "-";
                return $"{pair.Left:D}|{pair.Right:D}|{decision.Decision}|{score}";
            })
            .Order(StringComparer.Ordinal);
        var payload = $"{Version}\n{PairwiseTransitivityDiagnostic.Version}\n{string.Join('\n', canonical)}\n";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static (Guid Left, Guid Right) CanonicalPair(Guid left, Guid right) =>
        left.CompareTo(right) < 0 ? (left, right) : (right, left);
}

using System.Data.Common;
using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// TRAIN-only, opt-in, pre-creation expansion of the physical Gold retrieval.
/// This is NOT an additional FS classifier. All 19 exploratory catalogue
/// passes come from the frozen V9 20261020 search space; the fitted passes
/// are also retained, so the search can only add candidates. Both query
/// families use the SAME pinned, physically materialized projection.
/// </summary>
public static class V9GoldPreCreationSecondSearch
{
    public const string Option = "SECOND_SEARCH=EXPANDED_19";
    public const string Version = "V9_GOLD_PRE_CREATION_SECOND_SEARCH_TRAIN_V1";
    public const string AbstentionReason = "BUSCA_AMPLIADA_CANDIDATOS_SEM_VINCULO_SEGURO";
    public const int MaximumCandidates = 5000;

    // Copied verbatim (PassId/Fields) from the immutable exploratory
    // 19-cell catalogue at data/calibration/v9/mass-support-20261020/plan.json.
    // Never infer rules or thresholds from TEST or from the Gold truth ledger.
    public static readonly IReadOnlyList<LinkageBlockingPass> Catalogue =
        Array.AsReadOnly(new[]
        {
            Pass("P_ANY_BIRTH_YEAR", "name_surnames", "birth_year"),
            Pass("P_ANY_DOB_MONTHDAY", "name_surnames", "birth_day", "birth_month"),
            Pass("P_ANY_M_PHON", "name_surnames", "mother_name_phonetic_ptbr"),
            Pass("P_FIRST_BIRTH_YEAR", "name_first", "birth_year"),
            Pass("P_FIRST_DOB_MONTHDAY", "name_first", "birth_day", "birth_month"),
            Pass("P_FIRST_M_FIRST", "name_first", "mother_name_first"),
            Pass("P_FIRST_M_LAST", "name_first", "mother_name_last"),
            Pass("P_FIRST_M_PHON", "name_first", "mother_name_phonetic_ptbr"),
            Pass("P_LAST_M_ANY", "name_last", "mother_name_surnames"),
            Pass("P_LAST_M_FIRST", "name_last", "mother_name_first"),
            Pass("P_LAST_M_PHON", "name_last", "mother_name_phonetic_ptbr"),
            Pass("P_PHON_BIRTH_YEAR", "name_phonetic_ptbr", "birth_year"),
            Pass("P_PHON_M_ANY", "name_phonetic_ptbr", "mother_name_surnames"),
            Pass("P_PHON_M_FIRST", "name_phonetic_ptbr", "mother_name_first"),
            Pass("P_PHON_M_LAST", "name_phonetic_ptbr", "mother_name_last"),
            Pass("P_PREFIX3_BIRTH_YEAR", "name_prefix_3", "birth_year"),
            Pass("P_PREFIX3_DOB", "name_prefix_3", "birth_day", "birth_month", "birth_year"),
            Pass("P_PREFIX3_M_LAST", "name_prefix_3", "mother_name_last"),
            Pass("P_PREFIX3_M_PREFIX3", "name_prefix_3", "mother_name_prefix_3")
        });

    private static LinkageBlockingPass Pass(string id, params string[] fields) =>
        new(id, Array.AsReadOnly(fields));

    /// <summary>
    /// The canonical no-candidate policy and the experimental lower-band
    /// hypothesis are the only events that may create a new reference here.
    /// A conflict, weak evidence or accepted upper-link is never widened.
    /// </summary>
    public static bool IsProspectiveCreation(ProbabilisticLinkageDecision decision) =>
        decision.Status == ResolutionStatus.NAO_RESOLVIDO &&
        (decision.Motivo?.StartsWith("SEM_CANDIDATO_", StringComparison.Ordinal) == true
         || decision.Motivo == V9GoldLowerNonMatchExperiment.PublicationReason);

    internal static IReadOnlyList<BlockingCandidatePassLookup> Plan(
        V9FittedSnapshotExecutionRuntime runtime, IdentityObservation observation)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(observation);
        var values = runtime.Project(observation)
            .GroupBy(key => key.Feature, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Value)
                .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var selected = runtime.Plan(observation).ToList();
        foreach (var pass in Catalogue)
        {
            if (!pass.Fields.All(values.ContainsKey)) continue;
            var extra = new BlockingCandidatePassLookup(pass.PassId,
                pass.Fields.Select(field => new BlockingCandidateClause(field, values[field])).ToArray());
            var existing = selected.FirstOrDefault(x => x.PassId == pass.PassId);
            if (existing is null)
            {
                selected.Add(extra);
                continue;
            }
            // A duplicate PassId with different actual clauses is not an
            // equivalent union and must never silently replace the frozen fit.
            if (existing.Clauses.Count != extra.Clauses.Count
                || existing.Clauses.Where((c, i) => c.Feature != extra.Clauses[i].Feature
                    || !c.Values.SequenceEqual(extra.Clauses[i].Values)).Any())
                throw new InvalidDataException("Catálogo ampliado colide com o passe do fit congelado.");
        }
        return selected.AsReadOnly();
    }

    public static string BuildQuery(DbCommand command, V9FittedSnapshotExecutionRuntime runtime,
        IdentityObservation observation)
    {
        ArgumentNullException.ThrowIfNull(command);
        return BlockingProjectionCandidateQueryBuilder.BuildVerifiedSnapshotCandidateUuidQuery(
            command, Plan(runtime, observation), runtime,
            BlockingProjectionCandidateQueryBuilder.DefaultMaxParameters);
    }

    /// <summary>
    /// If no *additional* reference was recovered, preserve the initial
    /// decision byte-for-byte. If new references exist, reuse frozen V9
    /// Rank/Resolve. An accepted upper link may be used; otherwise abstain
    /// rather than creating a possible duplicate. No truth labels enter.
    /// </summary>
    public static ProbabilisticLinkageDecision Decide(
        ProbabilisticLinkageDecision initiallyProposed,
        ProbabilisticLinkageDecision expandedResolve, bool addedNewCandidates)
    {
        ArgumentNullException.ThrowIfNull(initiallyProposed);
        ArgumentNullException.ThrowIfNull(expandedResolve);
        if (!IsProspectiveCreation(initiallyProposed))
            throw new InvalidOperationException("Busca ampliada aplicada fora de uma criação proposta.");
        if (!addedNewCandidates) return initiallyProposed;
        if (expandedResolve.Status is ResolutionStatus.RESOLVIDO or ResolutionStatus.CONFLITO)
            return expandedResolve;
        if (expandedResolve.Status != ResolutionStatus.NAO_RESOLVIDO
            || expandedResolve.MelhorCandidatoUuid is null)
            throw new InvalidDataException("Segundo blocking recuperou candidatos mas o FS não os avaliou.");
        return expandedResolve with { Motivo = AbstentionReason };
    }
}

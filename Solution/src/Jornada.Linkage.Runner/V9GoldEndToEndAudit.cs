namespace Jornada.Linkage.Runner;

/// <summary>
/// Independent, truth-labeled evaluation of the FINAL incremental Gold partition.
/// Source truth is used only after publication, never in blocking, FS, Resolve or
/// the production Gold algorithms. No person identifiers leave these aggregates.
/// </summary>
public static class V9GoldEndToEndAudit
{
    public sealed record Metrics(
        long Observations,
        long TrueIdentities,
        long MinimumLinksForPerfectConsolidation,
        long PublishedSources,
        long PendingSources,
        double PendingSourceRate,
        long GoldReferences,
        long TruthIdentitiesRepresented,
        long TruthIdentitiesUnrepresented,
        double TruthIdentityCoverage,
        long FragmentedTrueIdentities,
        long ImpureGoldReferences,
        long AllTrueObservationPairs,
        long CorrectGoldObservationPairs,
        long FalseGoldObservationPairs,
        double? FinalGoldPairRecall,
        double? FinalGoldPairPrecision);

    /// <summary>
    /// Each source is counted exactly once, with its final reference (if any).
    /// A person with three observations contributes THREE true pairs, not two.
    /// Thus observations-minus-persons is a minimum spanning-link count, not the
    /// denominator of pair recall. Pending sources contribute to the denominator.
    /// </summary>
    public static Metrics Measure(
        IReadOnlyDictionary<long, string> truthBySource,
        IReadOnlyDictionary<long, Guid> finalGoldBySource)
    {
        ArgumentNullException.ThrowIfNull(truthBySource);
        ArgumentNullException.ThrowIfNull(finalGoldBySource);
        if (truthBySource.Count == 0 || truthBySource.Values.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Gabarito completo por observação obrigatório.");
        foreach (var (source, uuid) in finalGoldBySource)
            if (uuid == Guid.Empty || !truthBySource.ContainsKey(source))
                throw new InvalidDataException("Gold publicou fonte ou UUID fora do corpus.");
        var truthCounts = truthBySource.GroupBy(x => x.Value, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => (long)x.Count(), StringComparer.Ordinal);
        var expectedPairs = truthCounts.Values.Sum(ChooseTwo);
        long correctPairs = 0, wrongPairs = 0, impure = 0;
        var goldReferencesByTruth = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        foreach (var gold in finalGoldBySource.GroupBy(x => x.Value))
        {
            var counts = gold.GroupBy(x => truthBySource[x.Key], StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => (long)x.Count(), StringComparer.Ordinal);
            var correctInGroup = counts.Values.Sum(ChooseTwo);
            correctPairs = checked(correctPairs + correctInGroup);
            wrongPairs = checked(wrongPairs + ChooseTwo(gold.LongCount()) - correctInGroup);
            if (counts.Count > 1) impure++;
            foreach (var person in counts.Keys)
            {
                if (!goldReferencesByTruth.TryGetValue(person, out var refs))
                    goldReferencesByTruth.Add(person, refs = []);
                refs.Add(gold.Key);
            }
        }
        if (correctPairs > expectedPairs)
            throw new InvalidDataException("Gold contém mais pares verdadeiros que o corpus.");
        var total = (long)truthBySource.Count;
        var represented = (long)goldReferencesByTruth.Count;
        return new(
            Observations: total,
            TrueIdentities: truthCounts.Count,
            MinimumLinksForPerfectConsolidation: total - truthCounts.Count,
            PublishedSources: finalGoldBySource.Count,
            PendingSources: total - finalGoldBySource.Count,
            PendingSourceRate: (double)(total - finalGoldBySource.Count) / total,
            GoldReferences: finalGoldBySource.Values.Distinct().LongCount(),
            TruthIdentitiesRepresented: represented,
            TruthIdentitiesUnrepresented: truthCounts.Count - represented,
            TruthIdentityCoverage: (double)represented / truthCounts.Count,
            FragmentedTrueIdentities: goldReferencesByTruth.Values.LongCount(x => x.Count > 1),
            ImpureGoldReferences: impure,
            AllTrueObservationPairs: expectedPairs,
            CorrectGoldObservationPairs: correctPairs,
            FalseGoldObservationPairs: wrongPairs,
            FinalGoldPairRecall: expectedPairs == 0 ? null : (double)correctPairs / expectedPairs,
            FinalGoldPairPrecision: correctPairs + wrongPairs == 0
                ? null : (double)correctPairs / (correctPairs + wrongPairs));
    }

    /// <summary>
    /// Publication decisions are not the same as raw linkage statuses.
    /// Bucket reasons into an explicitly restricted vocabulary without
    /// exporting individual names, UUIDs or free-text error strings.
    /// </summary>
    public static string ReasonCategory(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "SEM_MOTIVO";
        var r = reason.Trim().ToUpperInvariant();
        if (r.Contains("EVIDENCIA_INSUFICIENTE", StringComparison.Ordinal)
            || r.Contains("SEM_CHAVE", StringComparison.Ordinal)
            || r.Contains("SEM_PASSE", StringComparison.Ordinal))
            return "EVIDENCIA_INSUFICIENTE";
        if (r.StartsWith("SEM_CANDIDATO_", StringComparison.Ordinal))
            return "BUSCA_COMPLETA_SEM_CANDIDATO";
        if (r.Contains("TIMEOUT", StringComparison.Ordinal)
            || r.Contains("TRUNC", StringComparison.Ordinal)
            || r.Contains("ORCAMENTO", StringComparison.Ordinal)
            || r.Contains("LIMITE", StringComparison.Ordinal)
            || r.Contains("FALHA", StringComparison.Ordinal)
            || r.Contains("INCOMPLET", StringComparison.Ordinal))
            return "BUSCA_INCOMPLETA_OU_ERRO";
        if (r.Contains("CONFLITO", StringComparison.Ordinal)
            || r.Contains("AMBIGU", StringComparison.Ordinal)
            || r.Contains("MARGEM", StringComparison.Ordinal))
            return "AMBIGUIDADE_OU_CONFLITO";
        if (r.Contains("ABAIXO", StringComparison.Ordinal)
            || r.Contains("LIMIAR", StringComparison.Ordinal)
            || r.Contains("POSTERIOR", StringComparison.Ordinal)
            || r.Contains("SCORE", StringComparison.Ordinal)
            || r.Contains("LLR", StringComparison.Ordinal))
            return "CANDIDATO_NAO_APROVADO";
        if (r.Contains("PRECEDENCIA", StringComparison.Ordinal)
            || r.Contains("PROTEGID", StringComparison.Ordinal)
            || r.Contains("REFERENCIA_PROGRESSIVA", StringComparison.Ordinal))
            return "PRECEDENCIA_DE_IDENTIDADE";
        return "OUTRO_REQUER_AUDITORIA";
    }

    private static long ChooseTwo(long n) => checked(n * (n - 1) / 2);
}

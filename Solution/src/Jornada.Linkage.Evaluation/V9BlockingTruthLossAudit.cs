namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Diagnostic decomposition of independently labelled M, BEFORE blocking.
/// Inputs contain ephemeral observation ordinals only. Outputs contain strictly
/// aggregate observed agreement states, never PII or per-pair identifiers.
/// Recovered M is determined by actual SQL candidate membership, not by a
/// reconstructed/guessed ruleset. No generator-causality is inferred.
/// </summary>
public static class V9BlockingTruthLossAudit
{
    public const string Version = "V9_SQL_ELIGIBLE_TRUE_PAIR_LOSS_BY_OBSERVED_AGREEMENT_V1";
    public const string Status = "DESCRIPTIVE_ONLY_NOT_GENERATOR_CAUSALITY_OR_SQL_ALIAS_PARITY";

    public sealed record Pair(
        int LowerObservationId, int HigherObservationId, string Partition,
        bool NameMissing, bool NameExact,
        bool MotherMissing, bool MotherExact, bool BirthExact);

    public sealed record Cell(
        string Partition, string NameState, string MotherState, string BirthState,
        long EligibleTruePairs, long RetrievedTruePairs, long LostTruePairs);

    public sealed record PartitionSummary(
        string Partition, long EligibleTruePairs, long RetrievedTruePairs,
        long LostTruePairs, long SimultaneousNameMotherBirthDisagreementLost);

    public sealed record Report(
        string Version, string Status,
        IReadOnlyList<PartitionSummary> Partitions,
        IReadOnlyList<Cell> Cells);

    public static Report Analyze(
        IReadOnlyCollection<Pair> eligiblePairs,
        IReadOnlySet<(int A, int B)> recoveredTruePairs)
    {
        ArgumentNullException.ThrowIfNull(eligiblePairs);
        ArgumentNullException.ThrowIfNull(recoveredTruePairs);
        var acceptedPartitions = new HashSet<string>(
            ["TRAIN", "VALIDATION", "TEST"], StringComparer.Ordinal);
        var seen = new HashSet<(int A, int B)>();
        var counts = new Dictionary<(string Part, string Name, string Mother, string Birth),
            (long Eligible, long Recovered)>();
        foreach (var pair in eligiblePairs)
        {
            ArgumentNullException.ThrowIfNull(pair);
            if (!acceptedPartitions.Contains(pair.Partition)
                || pair.LowerObservationId < 0
                || pair.LowerObservationId >= pair.HigherObservationId
                || !seen.Add((pair.LowerObservationId, pair.HigherObservationId))
                || (pair.NameMissing && pair.NameExact)
                || (pair.MotherMissing && pair.MotherExact))
                throw new InvalidDataException("Par M diagnóstico duplicado, inválido ou fora do frame.");

            var key = (pair.Partition,
                State(pair.NameMissing, pair.NameExact),
                State(pair.MotherMissing, pair.MotherExact),
                pair.BirthExact ? "EXACT" : "DIFFERENT");
            var previous = counts.GetValueOrDefault(key);
            counts[key] = (previous.Eligible + 1,
                previous.Recovered + (recoveredTruePairs.Contains(
                    (pair.LowerObservationId, pair.HigherObservationId)) ? 1 : 0));
        }
        if (recoveredTruePairs.Count > seen.Count ||
            recoveredTruePairs.Any(pair => !seen.Contains(pair)))
            throw new InvalidDataException("SQL recuperou M fora do denominador M elegível.");

        var cells = new List<Cell>(3 * 3 * 3 * 2);
        var summaries = new List<PartitionSummary>(3);
        foreach (var partition in new[] { "TRAIN", "VALIDATION", "TEST" })
        {
            long eligible = 0, retrieved = 0, triple = 0;
            foreach (var name in new[] { "EXACT", "DIFFERENT", "MISSING" })
            foreach (var mother in new[] { "EXACT", "DIFFERENT", "MISSING" })
            foreach (var birth in new[] { "EXACT", "DIFFERENT" })
            {
                var count = counts.GetValueOrDefault((partition, name, mother, birth));
                if (count.Recovered > count.Eligible)
                    throw new InvalidDataException("M recuperados excedem M elegíveis.");
                var lost = count.Eligible - count.Recovered;
                cells.Add(new Cell(partition, name, mother, birth,
                    count.Eligible, count.Recovered, lost));
                eligible += count.Eligible;
                retrieved += count.Recovered;
                // Three pairwise observed disagreements; missing is a separate
                // category and is NEVER silently counted as a disagreement.
                if (name == "DIFFERENT" && mother == "DIFFERENT" && birth == "DIFFERENT")
                    triple += lost;
            }
            summaries.Add(new PartitionSummary(
                partition, eligible, retrieved, eligible - retrieved, triple));
        }
        if (cells.Count != 54
            || summaries.Sum(x => x.EligibleTruePairs) != eligiblePairs.Count
            || summaries.Sum(x => x.RetrievedTruePairs) != recoveredTruePairs.Count
            || summaries.Any(x => x.LostTruePairs < 0
                || x.SimultaneousNameMotherBirthDisagreementLost > x.LostTruePairs))
            throw new InvalidDataException("Auditoria M SQL não conciliou os denominadores.");
        return new Report(Version, Status, summaries, cells);
    }

    private static string State(bool missing, bool exact) =>
        missing ? "MISSING" : exact ? "EXACT" : "DIFFERENT";
}

using Jornada.Contracts;

namespace Jornada.Linkage.SyntheticCorpus;

/// <summary>
/// Reproducible 30k synthetic coverage measurement. This is NOT the SQL
/// VERSIONED_ALIAS blocking frame and does not estimate calibrated M/U.
/// Only aggregate cell counts are serialized, never names or pair IDs.
/// </summary>
public static class SyntheticJointSupportReportV9
{
    public const string Version = "SYNTHETIC_30K_JOINT_SUPPORT_PROXY_V1";
    public const string Status = "OBSERVED_SYNTHETIC_PROXY_NOT_SQL_CALIBRATION";
    public const string Selection = "M_FIRST_CROSS_GESTOR_PER_BASE;U_ADJACENT_DIFFERENT_BASE_SAME_FIRST_INITIAL_WITHIN_PARTITION";

    public static SyntheticJointSupportEvidenceV9 Build(
        IReadOnlyList<SyntheticPerson> people,
        IReadOnlyList<SyntheticObservation> observations,
        ulong seed,
        string inputFingerprint)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFingerprint);

        if (people.Count == 0 || observations.Count == 0)
            throw new InvalidOperationException("A auditoria de 30 mil exige pessoas e observações reais do gerador.");
        var baseIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var person in people)
        {
            if (!baseIds.Add(person.BasePersonId) ||
                person.Partition is not ("TRAIN" or "VALIDATION" or "TEST"))
                throw new InvalidOperationException("Partição ou identidade-base inválida/duplicada.");
        }
        var byBase = observations.GroupBy(x => x.BasePersonId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.OrderBy(o => o.ObservationId, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var partitionIndex = people.ToDictionary(x => x.BasePersonId, x => x.Partition,
            StringComparer.Ordinal);
        foreach (var row in observations)
        {
            if (!baseIds.Contains(row.BasePersonId)
                || row.Partition != peopleByPartition(row.BasePersonId))
                throw new InvalidOperationException("Observação com identidade ou partição divergente.");
        }

        string peopleByPartition(string id) => partitionIndex[id];

        var levels = Enum.GetValues<CompositeNominalLevelV9>();
        var result = new List<SyntheticJointSupportPartitionV9>(3);
        foreach (var partition in new[] { "TRAIN", "VALIDATION", "TEST" })
        {
            var counters = new Dictionary<(CompositeNominalLevelV9 Person, CompositeNominalLevelV9 Mother),
                (int Matched, int Unmatched)>();
            foreach (var a in levels)
                foreach (var b in levels)
                    counters[(a, b)] = (0, 0);

            void Add(SyntheticObservation left, SyntheticObservation right, bool isMatch)
            {
                if (left.Partition != partition || right.Partition != partition)
                    throw new InvalidOperationException("Não cruzar partições ou grupos na auditoria.");
                if ((left.BasePersonId == right.BasePersonId) != isMatch)
                    throw new InvalidOperationException("Gabarito incompatível com classe M/U.");
                var key = (
                    CompositeNominalComparisonV9.Classify(left.Name, right.Name),
                    CompositeNominalComparisonV9.Classify(left.MotherName, right.MotherName));
                var n = counters[key];
                counters[key] = isMatch ? (n.Matched + 1, n.Unmatched)
                    : (n.Matched, n.Unmatched + 1);
            }

            // One inter-manager positive per base, no replicated dependent M
            // pairs from people with 3-4 observations. Missing pairs reported.
            var representatives = new List<SyntheticObservation>();
            var matchedGroups = 0;
            var missingM = 0;
            foreach (var person in people.Where(p => p.Partition == partition)
                         .OrderBy(p => p.BasePersonId, StringComparer.Ordinal))
            {
                if (!byBase.TryGetValue(person.BasePersonId, out var rows) || rows.Length == 0)
                {
                    missingM++;
                    continue;
                }
                representatives.Add(rows[0]);
                var second = rows.FirstOrDefault(o =>
                    !string.Equals(o.Gestor, rows[0].Gestor, StringComparison.Ordinal));
                if (second is null)
                {
                    missingM++;
                    continue;
                }
                Add(rows[0], second, isMatch: true);
                matchedGroups++;
            }

            // Deterministic, limited U proxy. Each base participates in at
            // most two U pairs; NOT the real SQL passes/probability frame.
            // The first initial is a proxy bucket, not a claim of a match.
            var unmatched = 0;
            foreach (var bucket in representatives.GroupBy(
                         o => FirstInitial(o.Name), StringComparer.Ordinal))
            {
                var selected = bucket.OrderBy(o => o.BasePersonId, StringComparer.Ordinal)
                    .ToArray();
                for (var i = 1; i < selected.Length; i++)
                {
                    Add(selected[i - 1], selected[i], isMatch: false);
                    unmatched++;
                }
            }

            var cells = levels.SelectMany(a => levels.Select(b =>
            {
                var pair = counters[(a, b)];
                return new SyntheticJointSupportCellV9(a, b, pair.Matched, pair.Unmatched);
            })).ToArray();
            if (cells.Length != 81 ||
                cells.Sum(x => x.MatchedPairs) != matchedGroups ||
                cells.Sum(x => x.UnmatchedPairs) != unmatched)
                throw new InvalidOperationException("Contagem do inventário nominal inconsistente.");
            result.Add(new SyntheticJointSupportPartitionV9(
                partition,
                people.Count(p => p.Partition == partition),
                matchedGroups,
                missingM,
                unmatched,
                cells.Count(x => x.MatchedPairs == 0),
                cells.Count(x => x.UnmatchedPairs == 0),
                cells.Count(x => x.MatchedPairs == 0 || x.UnmatchedPairs == 0),
                cells));
        }
        return new SyntheticJointSupportEvidenceV9(
            Version, Status, Selection, CompositeNominalComparisonV9.ContractVersion,
            people.Count, observations.Count, seed, inputFingerprint, result);
    }

    private static string FirstInitial(string? value)
    {
        var normalized = IdentityComparison.NormalizeText(value);
        return string.IsNullOrWhiteSpace(normalized)
            ? "<MISSING>" : normalized[..1];
    }
}

public sealed record SyntheticJointSupportCellV9(
    CompositeNominalLevelV9 Person,
    CompositeNominalLevelV9 Mother,
    int MatchedPairs,
    int UnmatchedPairs);

public sealed record SyntheticJointSupportPartitionV9(
    string Partition,
    int BasePeople,
    int MatchedPairs,
    int BasePeopleWithoutCrossGestorM,
    int UnmatchedProxyPairs,
    int CellsWithoutM,
    int CellsWithoutU,
    int CellsWithoutBilateralObservation,
    IReadOnlyList<SyntheticJointSupportCellV9> Cells);

public sealed record SyntheticJointSupportEvidenceV9(
    string Version,
    string Status,
    string PairSelection,
    string NominalComparisonVersion,
    int People,
    int Observations,
    ulong Seed,
    string InputFingerprint,
    IReadOnlyList<SyntheticJointSupportPartitionV9> Partitions);

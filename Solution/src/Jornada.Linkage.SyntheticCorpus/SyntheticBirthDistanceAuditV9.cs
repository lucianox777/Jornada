using System.Globalization;
using Jornada.Contracts;

namespace Jornada.Linkage.SyntheticCorpus;

/// <summary>
/// Diagnostic of how the frozen V5 birth comparator classifies REALIZED
/// synthetic observation errors against synthetic ground truth.
/// TRAIN only: never inspect VALIDATION/TEST labels to tune errors/weights.
/// Not a distribution of population birth dates, and not an M/U estimate.
/// </summary>
/// <summary>
/// Aggregate cross-tabulation, including zero-count cells; no person IDs.
/// </summary>
public sealed record SyntheticBirthSemanticDistanceCellV9(
    string SemanticState,
    int DigitDistance,
    int ObservationCount,
    int DistinctTrainingPeople);

/// <summary>
/// Counts in one synthetic source (Gestor) from TRAIN, never validated TEST.
/// Same person can contribute to multiple sources and cells; no ESS claim.
/// </summary>
public sealed record SyntheticBirthSourceProfileV9(
    string Source,
    int Observations,
    int MissingBirthObservations,
    int ExactBirthObservations,
    int DifferingBirthObservations,
    int DistinctTrainingPeople,
    IReadOnlyList<SyntheticBirthSemanticDistanceCellV9> SemanticDistanceCells);

/// <summary>
/// Overlap of distinct synthetic TRAIN people appearing in both sources.
/// Cross-source identity sharing is descriptive, not a claim of source independence.
/// </summary>
public sealed record SyntheticBirthSourceOverlapV9(
    string LeftSource,
    string RightSource,
    int SharedTrainingPeople);

/// <summary>
/// Number of synthetic TRAIN people occurring in exactly N source labels.
/// Includes N=0 (TRAIN people with no observations). Not an ESS.
/// </summary>
public sealed record SyntheticBirthSourceMultiplicityV9(
    int DistinctSources,
    int DistinctTrainingPeople);

public sealed record SyntheticBirthDistanceAuditV9(
    string Version,
    string Status,
    string Partition,
    int BasePeopleInTraining,
    int ObservationsInTraining,
    int MissingBirthObservations,
    int ExactBirthObservations,
    int DifferingBirthObservations,
    int SameDayMonthDifferentYear,
    int AdjacentYearSameDayMonth,
    int LastTwoYearDigitsSwappedSameDayMonth,
    int ConventionalDayOneOrFifteenFromDifferentTruth,
    IReadOnlyDictionary<int, int> DigitDistances,
    IReadOnlyDictionary<string, int> SemanticStates,
    IReadOnlyDictionary<string, int> ComponentAgreementMasks,
    IReadOnlyList<SyntheticBirthSemanticDistanceCellV9> SemanticDistanceCells,
    IReadOnlyList<SyntheticBirthSourceProfileV9> SourceProfiles,
    int TrainingPeopleWithObservations,
    int TrainingPeopleInMultipleSources,
    IReadOnlyList<SyntheticBirthSourceMultiplicityV9> SourceMultiplicity,
    IReadOnlyList<SyntheticBirthSourceOverlapV9> SourceOverlaps);

public static class SyntheticBirthDistanceAuditBuilderV9
{
    public const string Version = "V9_SYNTH_BIRTH_DATE_DISTANCE_TRAIN_AUDIT_5";
    public const string Status =
        "SYNTHETIC_TRAIN_DIAGNOSTIC_NOT_EMPIRICAL_ERROR_RATES_OR_FS_MU";
    public const string Partition = "TRAIN";

    private sealed class SourceAccumulator
    {
        public int Observations;
        public int Missing;
        public int Exact;
        public int Differing;
        public HashSet<string> People { get; } = new(StringComparer.Ordinal);
        public Dictionary<(string State, int Distance), HashSet<string>> PeopleInCells { get; } = [];
        public Dictionary<(string State, int Distance), int> CellCounts { get; } = [];
    }

    public static SyntheticBirthDistanceAuditV9 Analyze(SyntheticCorpusGeneration generation)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(generation.People);
        ArgumentNullException.ThrowIfNull(generation.Observations);

        var people = new Dictionary<string, SyntheticPerson>(StringComparer.Ordinal);
        foreach (var person in generation.People)
        {
            if (person is null || string.IsNullOrWhiteSpace(person.BasePersonId)
                || person.Partition is not ("TRAIN" or "VALIDATION" or "TEST")
                || !people.TryAdd(person.BasePersonId, person))
                throw new InvalidOperationException("Inventário sintético contém pessoa/partição inválida ou duplicada.");
        }

        var trainCount = people.Values.Count(x => x.Partition == Partition);
        var distances = Enumerable.Range(0, 9).ToDictionary(x => x, _ => 0);
        var states = BirthDateSemanticEvidence.States
            .ToDictionary(x => x, _ => 0, StringComparer.Ordinal);
        var masks = Enumerable.Range(0, 8).ToDictionary(x => x.ToString("B3", CultureInfo.InvariantCulture),
            _ => 0, StringComparer.Ordinal);
        var stateDistances = BirthDateSemanticEvidence.States
            .SelectMany(state => Enumerable.Range(0, 9)
                .Select(distance => (State: state, Distance: distance)))
            .ToDictionary(cell => cell, _ => 0);
        // Repeated observations of a single synthetic person are NOT
        // independent population draws. This inventory never leaves TRAIN.
        var distinctPersonIds = new Dictionary<(string State, int Distance), HashSet<string>>();
        var sourceProfiles = new Dictionary<string, SourceAccumulator>(StringComparer.Ordinal);
        var seenObservations = new HashSet<string>(StringComparer.Ordinal);
        int total = 0, missing = 0, exact = 0, different = 0;
        int dayMonthYearDifference = 0, adjacent = 0, swapped = 0, conventional = 0;

        foreach (var observation in generation.Observations)
        {
            if (observation is null || string.IsNullOrWhiteSpace(observation.ObservationId)
                || string.IsNullOrWhiteSpace(observation.BasePersonId)
                || !seenObservations.Add(observation.ObservationId)
                || !people.TryGetValue(observation.BasePersonId, out var person)
                || observation.Partition != person.Partition)
                throw new InvalidOperationException("Observação sintética sem origem válida ou partição coerente.");

            // This is a training diagnostic, not a validation/TEST leak.
            if (person.Partition != Partition) continue;
            if (string.IsNullOrWhiteSpace(observation.Gestor))
                throw new InvalidOperationException("Observação TRAIN sem fonte sintética Gestor.");
            if (!sourceProfiles.TryGetValue(observation.Gestor, out var source))
                sourceProfiles[observation.Gestor] = source = new SourceAccumulator();
            source.Observations++;
            source.People.Add(person.BasePersonId);

            total++;
            if (observation.BirthDate is not { } observed)
            {
                missing++;
                source.Missing++;
                continue;
            }

            var truth = person.BirthDate;
            var digitDistance = BirthDateSemanticEvidence.DigitDistance(truth, observed);
            distances[digitDistance]++;
            var state = BirthDateSemanticEvidence.Classify(truth, observed);
            states[state]++;
            stateDistances[(state, digitDistance)]++;
            var cellKey = (state, digitDistance);
            source.CellCounts.TryGetValue(cellKey, out var sourceCount);
            source.CellCounts[cellKey] = sourceCount + 1;
            if (!source.PeopleInCells.TryGetValue(cellKey, out var sourcePeople))
                source.PeopleInCells[cellKey] = sourcePeople =
                    new HashSet<string>(StringComparer.Ordinal);
            sourcePeople.Add(person.BasePersonId);
            if (!distinctPersonIds.TryGetValue(cellKey, out var personIds))
                distinctPersonIds[cellKey] = personIds = new HashSet<string>(StringComparer.Ordinal);
            personIds.Add(person.BasePersonId);

            var mask = ((truth.Day == observed.Day ? 1 : 0) |
                        (truth.Month == observed.Month ? 2 : 0) |
                        (truth.Year == observed.Year ? 4 : 0));
            masks[mask.ToString("B3", CultureInfo.InvariantCulture)]++;

            if (truth == observed)
            {
                exact++;
                source.Exact++;
                continue;
            }

            different++;
            source.Differing++;
            if ((observed.Day is 1 or 15) && truth.Day != observed.Day)
                conventional++;
            if (truth.Day != observed.Day || truth.Month != observed.Month
                || truth.Year == observed.Year)
                continue;

            dayMonthYearDifference++;
            if (Math.Abs(truth.Year - observed.Year) == 1)
                adjacent++;

            var lastTwo = truth.Year % 100;
            var reversedLastTwo = (lastTwo % 10) * 10 + (lastTwo / 10);
            if (lastTwo != reversedLastTwo
                && observed.Year / 100 == truth.Year / 100
                && observed.Year % 100 == reversedLastTwo)
                swapped++;
        }

        var cells = BirthDateSemanticEvidence.States
            .SelectMany(state => Enumerable.Range(0, 9)
                .Select(distance => new SyntheticBirthSemanticDistanceCellV9(
                    state, distance, stateDistances[(state, distance)],
                    distinctPersonIds.TryGetValue((state, distance), out var peopleInCell)
                        ? peopleInCell.Count : 0)))
            .ToArray();

        var sources = sourceProfiles.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(sourceEntry =>
            {
                var source = sourceEntry.Value;
                var sourceCells = BirthDateSemanticEvidence.States
                    .SelectMany(state => Enumerable.Range(0, 9).Select(distance =>
                    {
                        var key = (state, distance);
                        source.CellCounts.TryGetValue(key, out var count);
                        var peopleCount = source.PeopleInCells.TryGetValue(key, out var group)
                            ? group.Count : 0;
                        return new SyntheticBirthSemanticDistanceCellV9(
                            state, distance, count, peopleCount);
                    })).ToArray();
                if (source.Observations != source.Missing + source.Exact + source.Differing
                    || sourceCells.Sum(x => x.ObservationCount) != source.Exact + source.Differing
                    || sourceCells.Any(x => x.DistinctTrainingPeople > x.ObservationCount
                        || x.DistinctTrainingPeople > source.People.Count))
                    throw new InvalidOperationException(
                        "Auditoria TRAIN por fonte sintética não fecha seus denominadores.");

                return new SyntheticBirthSourceProfileV9(
                    sourceEntry.Key, source.Observations, source.Missing, source.Exact,
                    source.Differing, source.People.Count, Array.AsReadOnly(sourceCells));
            }).ToArray();

        // Each synthetic person may appear in several Gestores. Count
        // unique source labels per TRAIN person without exporting identities.
        var sourceKeys = sources.Select(x => x.Source).ToArray();
        var sourceMembershipCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var profile in sourceProfiles.Values)
            foreach (var personId in profile.People)
            {
                sourceMembershipCounts.TryGetValue(personId, out var count);
                sourceMembershipCounts[personId] = count + 1;
            }

        var withObservations = sourceMembershipCounts.Count;
        var inMultipleSources = sourceMembershipCounts.Values.Count(count => count > 1);
        var sourceMultiplicity = Enumerable.Range(0, sourceKeys.Length + 1)
            .Select(sourceCount => new SyntheticBirthSourceMultiplicityV9(
                sourceCount,
                sourceCount == 0
                    ? trainCount - withObservations
                    : sourceMembershipCounts.Values.Count(value => value == sourceCount)))
            .ToArray();

        // Only unordered distinct-source pairs; omit repeated observations
        // within a source, use a deterministic lexical ordering including zero.
        var overlaps = new List<SyntheticBirthSourceOverlapV9>();
        for (var i = 0; i < sourceKeys.Length; i++)
            for (var j = i + 1; j < sourceKeys.Length; j++)
            {
                var left = sourceProfiles[sourceKeys[i]].People;
                var right = sourceProfiles[sourceKeys[j]].People;
                var shared = left.Count(right.Contains);
                overlaps.Add(new SyntheticBirthSourceOverlapV9(
                    sourceKeys[i], sourceKeys[j], shared));
            }

        // TRAIN-only metadata: this audit is intentionally not a post-blocking
        // paired M/U sample and does not estimate independence or Kish ESS.
        if (sourceMembershipCounts.Keys.Any(id =>
                !people.TryGetValue(id, out var person) || person.Partition != Partition)
            || withObservations > trainCount
            || sources.Sum(x => x.DistinctTrainingPeople) !=
                sourceMembershipCounts.Values.Sum()
            || sourceMultiplicity.Sum(x => x.DistinctTrainingPeople) != trainCount
            || sourceMultiplicity.Sum(x => x.DistinctSources * x.DistinctTrainingPeople)
                != sources.Sum(x => x.DistinctTrainingPeople)
            || sourceMultiplicity.Skip(2).Sum(x => x.DistinctTrainingPeople)
                != inMultipleSources
            || overlaps.Count != sourceKeys.Length * (sourceKeys.Length - 1) / 2
            || overlaps.Any(pair =>
                pair.SharedTrainingPeople < 0
                || pair.SharedTrainingPeople >
                    Math.Min(sourceProfiles[pair.LeftSource].People.Count,
                        sourceProfiles[pair.RightSource].People.Count)))
            throw new InvalidOperationException(
                "Sobreposições de fontes do TRAIN não reconciliam com a base sintética.");

        // Each observed TRAIN record contributes to exactly one source/cell.
        // Distinct people may appear in multiple sources and MUST NOT be summed.
        if (sources.Sum(x => x.Observations) != total
            || sources.Sum(x => x.MissingBirthObservations) != missing
            || sources.Sum(x => x.ExactBirthObservations) != exact
            || sources.Sum(x => x.DifferingBirthObservations) != different
            || cells.Any(cell =>
                sources.Sum(x => x.SemanticDistanceCells
                    .Where(c => c.SemanticState == cell.SemanticState
                        && c.DigitDistance == cell.DigitDistance)
                    .Sum(c => c.ObservationCount)) != cell.ObservationCount))
            throw new InvalidOperationException(
                "Auditoria TRAIN por fonte não reconcilia com o agregado global.");

        // Reconcile the actual joint distribution with BOTH legacy marginal
        // histograms; frequency-by-state and frequency-by-distance alone do
        // not reveal where V5 semantic precedence compresses distinct errors.
        if (cells.Any(cell => cell.DistinctTrainingPeople < 0
                || cell.DistinctTrainingPeople > cell.ObservationCount
                || (cell.ObservationCount == 0) != (cell.DistinctTrainingPeople == 0)
                || cell.DistinctTrainingPeople > trainCount)
            || cells.Length != BirthDateSemanticEvidence.States.Count * 9
            || BirthDateSemanticEvidence.States.Any(state =>
                cells.Where(c => c.SemanticState == state).Sum(c => c.ObservationCount)
                    != states[state])
            || Enumerable.Range(0, 9).Any(distance =>
                cells.Where(c => c.DigitDistance == distance).Sum(c => c.ObservationCount)
                    != distances[distance])
            || total != missing + exact + different
            || distances.Values.Sum() != exact + different
            || states.Values.Sum() != exact + different
            || masks.Values.Sum() != exact + different)
            throw new InvalidOperationException("Auditoria TRAIN de nascimento sintético não fecha seus denominadores.");

        return new SyntheticBirthDistanceAuditV9(
            Version, Status, Partition, trainCount, total, missing, exact, different,
            dayMonthYearDifference, adjacent, swapped, conventional,
            new System.Collections.ObjectModel.ReadOnlyDictionary<int, int>(distances),
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(states),
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(masks),
            Array.AsReadOnly(cells),
            Array.AsReadOnly(sources),
            withObservations,
            inMultipleSources,
            Array.AsReadOnly(sourceMultiplicity),
            Array.AsReadOnly(overlaps.ToArray()));
    }
}

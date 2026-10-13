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
    int ObservationCount);

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
    IReadOnlyList<SyntheticBirthSemanticDistanceCellV9> SemanticDistanceCells);

public static class SyntheticBirthDistanceAuditBuilderV9
{
    public const string Version = "V9_SYNTH_BIRTH_DATE_DISTANCE_TRAIN_AUDIT_2";
    public const string Status =
        "SYNTHETIC_TRAIN_DIAGNOSTIC_NOT_EMPIRICAL_ERROR_RATES_OR_FS_MU";
    public const string Partition = "TRAIN";

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
            total++;
            if (observation.BirthDate is not { } observed)
            {
                missing++;
                continue;
            }

            var truth = person.BirthDate;
            var digitDistance = BirthDateSemanticEvidence.DigitDistance(truth, observed);
            distances[digitDistance]++;
            var state = BirthDateSemanticEvidence.Classify(truth, observed);
            states[state]++;
            stateDistances[(state, digitDistance)]++;

            var mask = ((truth.Day == observed.Day ? 1 : 0) |
                        (truth.Month == observed.Month ? 2 : 0) |
                        (truth.Year == observed.Year ? 4 : 0));
            masks[mask.ToString("B3", CultureInfo.InvariantCulture)]++;

            if (truth == observed)
            {
                exact++;
                continue;
            }

            different++;
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
                    state, distance, stateDistances[(state, distance)])))
            .ToArray();

        // Reconcile the actual joint distribution with BOTH legacy marginal
        // histograms; frequency-by-state and frequency-by-distance alone do
        // not reveal where V5 semantic precedence compresses distinct errors.
        if (cells.Length != BirthDateSemanticEvidence.States.Count * 9
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
            Array.AsReadOnly(cells));
    }
}

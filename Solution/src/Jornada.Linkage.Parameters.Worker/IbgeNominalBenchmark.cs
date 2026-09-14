using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public enum BenchmarkPartition
{
    Train,
    Validation,
    Test
}

public sealed record IbgeBenchmarkPerson(
    string BasePersonId,
    string FirstName,
    string Surname,
    DateOnly? BirthDate = null);

public sealed record NominalSubstitution(
    IbgeNameStatisticKind StatisticKind,
    string SourceValue,
    string TargetValue,
    long SourceOccurrences,
    long TargetOccurrences,
    double Similarity);

public sealed record IbgeNominalBenchmarkPair(
    string PairId,
    BenchmarkPartition Partition,
    bool IsTrueMatch,
    IbgeBenchmarkPerson Left,
    IbgeBenchmarkPerson Right,
    IReadOnlyList<NominalSubstitution> Substitutions);

public sealed record IbgeNominalBenchmarkOptions(
    int Seed,
    long MinimumSupportOccurrences,
    int TrainBasisPoints = 6000,
    int ValidationBasisPoints = 2000,
    int TestBasisPoints = 2000)
{
    public const string GeneratorVersion = "IBGE_NOMINAL_BENCHMARK_V1";
}

/// <summary>
/// Gera benchmark controlado preservando o vocabulário nominal observado no IBGE.
/// Não inventa strings, não atribui probabilidades de erro administrativo e não usa
/// classes subjetivas de severidade. A variação MATCH substitui um valor por seu
/// vizinho nominal observado mais próximo; NON_MATCH usa indivíduos-base distintos.
/// </summary>
public static class IbgeNominalBenchmarkGenerator
{
    public static IReadOnlyList<IbgeNominalBenchmarkPair> Generate(
        IbgeTypedNameFrequencySnapshot snapshot,
        IEnumerable<IbgeBenchmarkPerson> people,
        IbgeNominalBenchmarkOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MinimumSupportOccurrences);

        if (options.TrainBasisPoints + options.ValidationBasisPoints + options.TestBasisPoints != 10_000)
            throw new ArgumentException("As partições devem totalizar 10.000 basis points.", nameof(options));

        var materialized = people
            .Select(NormalizePerson)
            .OrderBy(static person => person.BasePersonId, StringComparer.Ordinal)
            .ToArray();

        if (materialized.Length < 2)
            throw new ArgumentException("O benchmark requer ao menos dois indivíduos-base.", nameof(people));
        if (materialized.Select(static person => person.BasePersonId).Distinct(StringComparer.Ordinal).Count() != materialized.Length)
            throw new ArgumentException("BasePersonId deve ser único.", nameof(people));

        var firstNames = BuildSupportedUniverse(snapshot, IbgeNameStatisticKind.FirstName, options.MinimumSupportOccurrences);
        var surnames = BuildSupportedUniverse(snapshot, IbgeNameStatisticKind.Surname, options.MinimumSupportOccurrences);

        foreach (var person in materialized)
        {
            EnsureSupported(person.FirstName, IbgeNameStatisticKind.FirstName, firstNames, person.BasePersonId);
            EnsureSupported(person.Surname, IbgeNameStatisticKind.Surname, surnames, person.BasePersonId);
        }

        var result = new List<IbgeNominalBenchmarkPair>(materialized.Length * 2);
        foreach (var person in materialized)
        {
            var partition = Partition(person.BasePersonId, options);
            result.Add(CreateMatchPair(person, partition, firstNames, surnames, options.Seed));
        }

        foreach (var group in materialized.GroupBy(person => Partition(person.BasePersonId, options)))
        {
            var partitionPeople = group.OrderBy(static person => person.BasePersonId, StringComparer.Ordinal).ToArray();
            if (partitionPeople.Length < 2)
                continue;

            foreach (var person in partitionPeople)
            {
                var partner = SelectMostNominallySimilarDifferentPerson(person, partitionPeople);
                result.Add(new IbgeNominalBenchmarkPair(
                    PairId("NON_MATCH", person.BasePersonId, partner.BasePersonId, options.Seed),
                    group.Key,
                    false,
                    person,
                    partner,
                    Array.Empty<NominalSubstitution>()));
            }
        }

        return result
            .OrderBy(static pair => pair.Partition)
            .ThenBy(static pair => pair.PairId, StringComparer.Ordinal)
            .ToArray();
    }

    public static BenchmarkPartition Partition(string basePersonId, IbgeNominalBenchmarkOptions options)
    {
        if (string.IsNullOrWhiteSpace(basePersonId))
            throw new ArgumentException("BasePersonId is required.", nameof(basePersonId));
        ArgumentNullException.ThrowIfNull(options);

        var bucket = StableBucket(basePersonId, options.Seed, 10_000);
        if (bucket < options.TrainBasisPoints)
            return BenchmarkPartition.Train;
        if (bucket < options.TrainBasisPoints + options.ValidationBasisPoints)
            return BenchmarkPartition.Validation;
        return BenchmarkPartition.Test;
    }

    private static IbgeNominalBenchmarkPair CreateMatchPair(
        IbgeBenchmarkPerson person,
        BenchmarkPartition partition,
        IReadOnlyDictionary<string, long> firstNames,
        IReadOnlyDictionary<string, long> surnames,
        int seed)
    {
        var mutateSurname = StableBucket(person.BasePersonId + "|ATTRIBUTE", seed, 2) == 1;
        var kind = mutateSurname ? IbgeNameStatisticKind.Surname : IbgeNameStatisticKind.FirstName;
        var sourceValue = mutateSurname ? person.Surname : person.FirstName;
        var universe = mutateSurname ? surnames : firstNames;
        var neighbor = ClosestObservedNeighbor(sourceValue, universe);

        if (neighbor is null)
        {
            kind = mutateSurname ? IbgeNameStatisticKind.FirstName : IbgeNameStatisticKind.Surname;
            sourceValue = mutateSurname ? person.FirstName : person.Surname;
            universe = mutateSurname ? firstNames : surnames;
            neighbor = ClosestObservedNeighbor(sourceValue, universe);
        }

        if (neighbor is null)
        {
            return new IbgeNominalBenchmarkPair(
                PairId("MATCH_EXACT", person.BasePersonId, person.BasePersonId, seed),
                partition,
                true,
                person,
                person,
                Array.Empty<NominalSubstitution>());
        }

        var right = kind == IbgeNameStatisticKind.FirstName
            ? person with { FirstName = neighbor.Value.Name }
            : person with { Surname = neighbor.Value.Name };

        var substitution = new NominalSubstitution(
            kind,
            sourceValue,
            neighbor.Value.Name,
            universe[sourceValue],
            neighbor.Value.Occurrences,
            neighbor.Value.Similarity);

        return new IbgeNominalBenchmarkPair(
            PairId("MATCH", person.BasePersonId, neighbor.Value.Name, seed),
            partition,
            true,
            person,
            right,
            new[] { substitution });
    }

    private static IbgeBenchmarkPerson SelectMostNominallySimilarDifferentPerson(
        IbgeBenchmarkPerson source,
        IReadOnlyList<IbgeBenchmarkPerson> candidates) =>
        candidates
            .Where(candidate => !string.Equals(candidate.BasePersonId, source.BasePersonId, StringComparison.Ordinal))
            .Select(candidate => new
            {
                Person = candidate,
                Score = IdentityComparison.JaroWinkler(source.FirstName, candidate.FirstName) +
                        IdentityComparison.JaroWinkler(source.Surname, candidate.Surname)
            })
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.Person.BasePersonId, StringComparer.Ordinal)
            .First().Person;

    private static (string Name, long Occurrences, double Similarity)? ClosestObservedNeighbor(
        string sourceValue,
        IReadOnlyDictionary<string, long> universe)
    {
        var candidate = universe
            .Where(entry => !string.Equals(entry.Key, sourceValue, StringComparison.Ordinal))
            .Select(entry => (
                Name: entry.Key,
                Occurrences: entry.Value,
                Similarity: IdentityComparison.JaroWinkler(sourceValue, entry.Key)))
            .OrderByDescending(static entry => entry.Similarity)
            .ThenByDescending(static entry => entry.Occurrences)
            .ThenBy(static entry => entry.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        return candidate.Name is null ? null : candidate;
    }

    private static IReadOnlyDictionary<string, long> BuildSupportedUniverse(
        IbgeTypedNameFrequencySnapshot snapshot,
        IbgeNameStatisticKind kind,
        long minimumSupportOccurrences) =>
        snapshot.Entries
            .Where(entry => entry.StatisticKind == kind && entry.Occurrences >= minimumSupportOccurrences)
            .ToDictionary(static entry => entry.Name, static entry => entry.Occurrences, StringComparer.Ordinal);

    private static void EnsureSupported(
        string value,
        IbgeNameStatisticKind kind,
        IReadOnlyDictionary<string, long> universe,
        string basePersonId)
    {
        if (!universe.ContainsKey(value))
            throw new ArgumentException(
                $"{basePersonId}: valor {kind} '{value}' não pertence ao universo IBGE com suporte mínimo configurado.");
    }

    private static IbgeBenchmarkPerson NormalizePerson(IbgeBenchmarkPerson person)
    {
        if (person is null)
            throw new ArgumentException("Benchmark person cannot be null.");
        if (string.IsNullOrWhiteSpace(person.BasePersonId))
            throw new ArgumentException("BasePersonId is required.");
        if (string.IsNullOrWhiteSpace(person.FirstName))
            throw new ArgumentException("FirstName is required.");
        if (string.IsNullOrWhiteSpace(person.Surname))
            throw new ArgumentException("Surname is required.");

        return person with
        {
            BasePersonId = person.BasePersonId.Trim(),
            FirstName = person.FirstName.Trim().ToUpperInvariant(),
            Surname = person.Surname.Trim().ToUpperInvariant()
        };
    }

    private static int StableBucket(string value, int seed, int modulus)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{IbgeNominalBenchmarkOptions.GeneratorVersion}|{seed}|{value}"));
        var number = BitConverter.ToUInt32(bytes, 0);
        return (int)(number % modulus);
    }

    private static string PairId(string kind, string left, string right, int seed)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{kind}|{seed}|{left}|{right}"));
        return $"{kind}:{Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant()}";
    }
}

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record IbgeNominalUBootstrapOptions(
    int Seed,
    int PairCount)
{
    public const string MethodVersion = "IBGE_NOMINAL_U_BOOTSTRAP_V1";
    public const string JointConstructionVersion = "INDEPENDENT_FIRST_NAME_SURNAME_MARGINALS_V1";
    public const string ObservationChannelVersion = "CLEAN_PUBLISHED_REFERENCE_NO_ERROR_CHANNEL_V1";
}

public sealed record IbgeNominalUStateEstimate(
    string State,
    long Support,
    decimal Probability,
    decimal StandardError);

public sealed record IbgeNominalUBootstrapEstimate(
    string MethodVersion,
    string JointConstructionVersion,
    string ObservationChannelVersion,
    int Seed,
    int PairCount,
    long FirstNamePublishedOccurrences,
    long SurnamePublishedOccurrences,
    int FirstNameVocabularySize,
    int SurnameVocabularySize,
    decimal AnalyticExactFirstNameProbability,
    decimal AnalyticExactSurnameProbability,
    decimal AnalyticExactSyntheticFullNameProbability,
    IReadOnlyList<IbgeNominalUStateEstimate> States);

/// <summary>
/// Produz uma referência populacional sintética para u de NOME a partir das marginais
/// publicadas pelo IBGE. Prenome e sobrenome são amostrados independentemente; essa
/// composição é um bootstrap explícito e NÃO afirma que o IBGE publique a distribuição
/// conjunta de nomes completos.
///
/// O estimador não injeta ruído administrativo. Para u, cada lado do par representa uma
/// identidade distinta sorteada da população sintética. Um futuro canal de erro só pode
/// ser incorporado quando estiver versionado e sustentado por evidência independente.
/// </summary>
public static class IbgeNominalUBootstrapEstimator
{
    private static readonly NameComparisonState[] States = Enum.GetValues<NameComparisonState>();

    public static IbgeNominalUBootstrapEstimate Estimate(
        IEnumerable<IbgeTypedNameFrequencyEntry> entries,
        IbgeNominalUBootstrapOptions options,
        NameComparisonContract nameComparisonContract = NameComparisonContract.WholeNameJaroWinklerV1)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(options);
        if (options.PairCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "PairCount deve ser positivo.");
        if (!Enum.IsDefined(nameComparisonContract))
            throw new ArgumentOutOfRangeException(nameof(nameComparisonContract));

        var materialized = entries.ToArray();
        var firstNames = BuildSampler(materialized, IbgeNameStatisticKind.FirstName);
        var surnames = BuildSampler(materialized, IbgeNameStatisticKind.Surname);

        var counts = States.ToDictionary(static state => state, static _ => 0L);
        for (var pairIndex = 0; pairIndex < options.PairCount; pairIndex++)
        {
            var left = string.Concat(
                firstNames.Sample(options.Seed, pairIndex, "L_FIRST"),
                " ",
                surnames.Sample(options.Seed, pairIndex, "L_SURNAME"));
            var right = string.Concat(
                firstNames.Sample(options.Seed, pairIndex, "R_FIRST"),
                " ",
                surnames.Sample(options.Seed, pairIndex, "R_SURNAME"));

            counts[IdentityComparison.CompareName(left, right, nameComparisonContract)]++;
        }

        var stateEstimates = States
            .Select(state =>
            {
                var support = counts[state];
                var probability = (decimal)support / options.PairCount;
                var standardError = (decimal)Math.Sqrt(
                    (double)(probability * (1m - probability) / options.PairCount));
                return new IbgeNominalUStateEstimate(
                    state.ToString(),
                    support,
                    probability,
                    standardError);
            })
            .ToArray();

        var exactFirstName = firstNames.ExactCollisionProbability();
        var exactSurname = surnames.ExactCollisionProbability();

        return new IbgeNominalUBootstrapEstimate(
            IbgeNominalUBootstrapOptions.MethodVersion,
            IbgeNominalUBootstrapOptions.JointConstructionVersion,
            IbgeNominalUBootstrapOptions.ObservationChannelVersion,
            options.Seed,
            options.PairCount,
            firstNames.TotalOccurrences,
            surnames.TotalOccurrences,
            firstNames.Count,
            surnames.Count,
            exactFirstName,
            exactSurname,
            exactFirstName * exactSurname,
            stateEstimates);
    }

    private static WeightedSampler BuildSampler(
        IEnumerable<IbgeTypedNameFrequencyEntry> entries,
        IbgeNameStatisticKind kind)
    {
        var values = entries
            .Where(entry => entry.StatisticKind == kind && entry.Occurrences > 0)
            .GroupBy(entry => entry.Name.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .Select(group => new WeightedValue(group.Key, checked(group.Sum(entry => entry.Occurrences))))
            .OrderBy(static item => item.Value, StringComparer.Ordinal)
            .ToArray();

        if (values.Length == 0)
            throw new ArgumentException($"Referência IBGE não contém valores publicados positivos para {kind}.", nameof(entries));

        return new WeightedSampler(values);
    }

    private sealed record WeightedValue(string Value, long Occurrences);

    private sealed class WeightedSampler
    {
        private readonly string[] values;
        private readonly long[] cumulative;
        private readonly long[] occurrences;

        public WeightedSampler(IReadOnlyList<WeightedValue> weightedValues)
        {
            values = new string[weightedValues.Count];
            cumulative = new long[weightedValues.Count];
            occurrences = new long[weightedValues.Count];

            long total = 0;
            for (var index = 0; index < weightedValues.Count; index++)
            {
                var item = weightedValues[index];
                if (item.Occurrences <= 0)
                    throw new ArgumentOutOfRangeException(nameof(weightedValues), "Frequências devem ser positivas.");

                total = checked(total + item.Occurrences);
                values[index] = item.Value;
                occurrences[index] = item.Occurrences;
                cumulative[index] = total;
            }

            TotalOccurrences = total;
        }

        public int Count => values.Length;
        public long TotalOccurrences { get; }

        public string Sample(int seed, int pairIndex, string slot)
        {
            var draw = StablePosition(seed, pairIndex, slot, TotalOccurrences);
            var low = 0;
            var high = cumulative.Length - 1;
            while (low < high)
            {
                var middle = low + ((high - low) / 2);
                if (draw < cumulative[middle])
                    high = middle;
                else
                    low = middle + 1;
            }

            return values[low];
        }

        public decimal ExactCollisionProbability()
        {
            decimal sumSquares = 0m;
            foreach (var occurrence in occurrences)
                sumSquares += (decimal)occurrence * occurrence;

            var total = (decimal)TotalOccurrences;
            return sumSquares / (total * total);
        }

        private static long StablePosition(int seed, int pairIndex, string slot, long total)
        {
            var payload = Encoding.UTF8.GetBytes(
                $"{IbgeNominalUBootstrapOptions.MethodVersion}|{seed}|{pairIndex}|{slot}");
            var hash = SHA256.HashData(payload);
            var raw = BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(0, sizeof(ulong)));

            // Multiplicação-high evita viés de módulo e mantém replay independente de System.Random.
            var scaled = (UInt128)raw * (ulong)total;
            return checked((long)(scaled >> 64));
        }
    }
}

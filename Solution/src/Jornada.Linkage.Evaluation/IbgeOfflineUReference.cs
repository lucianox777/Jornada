using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Linkage.Evaluation;

/// <summary>Offline, public-only C# reference for comparing independently sampled Splink u.</summary>
public static class IbgeOfflineUReference
{
    public const string ReportSchema = "JORNADA_IBGE_CSHARP_OFFLINE_U_V1";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly int[] Seeds = [20261011, 20261012, 20261013];

    public static string Estimate(string marginalsJson, int pairCount)
    {
        if (pairCount is < 1 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(pairCount));
        ArgumentNullException.ThrowIfNull(marginalsJson);
        using var doc = JsonDocument.Parse(marginalsJson);
        var root = doc.RootElement;
        var required = new[] { "schema_version", "reference_code", "reference_content_sha256",
            "first_name_sex", "surname_sex", "first_names", "surnames" };
        if (root.ValueKind != JsonValueKind.Object ||
            root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(required.Order(StringComparer.Ordinal)) == false ||
            root.GetProperty("schema_version").GetString() != IbgePublicMarginalsExchange.Schema ||
            root.GetProperty("reference_code").GetString() != IbgePublicMarginalsExchange.Reference ||
            root.GetProperty("first_name_sex").GetString() is not ("TODOS" or "FEMININO") ||
            root.GetProperty("surname_sex").GetString() != "TODOS")
            throw new InvalidDataException("Unexpected public IBGE marginal contract.");
        var referenceHash = root.GetProperty("reference_content_sha256").GetString();
        if (referenceHash is null || referenceHash.Length != 64 ||
            referenceHash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("Invalid public IBGE reference hash.");
        var rows = new List<IbgeTypedNameFrequencyEntry>();
        foreach (var (property, kind) in new[] {
            ("first_names", IbgeNameStatisticKind.FirstName),
            ("surnames", IbgeNameStatisticKind.Surname) })
        {
            var values = root.GetProperty(property);
            if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() == 0)
                throw new InvalidDataException("Missing public IBGE marginal.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in values.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    item.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)
                        .SequenceEqual(new[] { "name", "occurrences" }) == false)
                    throw new InvalidDataException("Unexpected public marginal row.");
                var name = item.GetProperty("name").GetString();
                var count = item.GetProperty("occurrences").GetInt64();
                if (string.IsNullOrWhiteSpace(name) || name != name.Trim() ||
                    !string.Equals(name, name.ToUpperInvariant(), StringComparison.Ordinal) || !seen.Add(name) || count <= 0)
                    throw new InvalidDataException("Invalid public marginal row.");
                rows.Add(new(kind, name, count));
            }
        }
        var estimates = Seeds.Select(seed =>
            IbgeNominalUBootstrapEstimator.Estimate(rows,
                new IbgeNominalUBootstrapOptions(seed, pairCount))).ToArray();
        var report = new {
            schema_version = ReportSchema,
            marginals_sha256 = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(marginalsJson))).ToLowerInvariant(),
            reference_code = IbgePublicMarginalsExchange.Reference,
            reference_content_sha256 = referenceHash,
            first_name_sex = root.GetProperty("first_name_sex").GetString(),
            surname_sex = "TODOS",
            method_version = IbgeNominalUBootstrapOptions.MethodVersion,
            joint_construction = IbgeNominalUBootstrapOptions.JointConstructionVersion,
            observation_channel = IbgeNominalUBootstrapOptions.ObservationChannelVersion,
            comparison_version = "WHOLE_NAME_JARO_WINKLER_V1",
            runs = estimates.Select(x => new {
                seed = x.Seed, pair_count = x.PairCount,
                analytic_exact_collision_probability = x.AnalyticExactSyntheticFullNameProbability,
                states = x.States.Select(s => new {
                    state = s.State, support = s.Support,
                    probability = s.Probability, standard_error = s.StandardError
                }).ToArray()
            }).ToArray(),
            limitation = "Synthetic independent first-name/surname marginals; C# V1 comparator. " +
                "No real-world joint distribution, independent Splink validation or production certification."
        };
        return JsonSerializer.Serialize(report, Options) + "\n";
    }
}

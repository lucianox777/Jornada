using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Offline diagnostic of independently sampled C# and real Splink u reports.
/// Refuses mismatched public marginals, methods and incomplete runs; never promotes a model.
/// </summary>
public static class IbgeIndependentUConference
{
    public const string ReportSchema = "JORNADA_IBGE_INDEPENDENT_U_CONFERENCE_V1";
    private static readonly string[] States = ["EXACT", "HIGH", "MEDIUM", "LOW"];
    private static readonly int[] CSharpSeeds = [20261011, 20261012, 20261013];
    private static readonly int[] SplinkSeeds = [20261001, 20261002, 20261003];
    private const decimal Tolerance = 0.000000001m;

    private sealed record Run(int Seed, int PairCount, IReadOnlyDictionary<string, long> Support);

    public static string Compare(string publicMarginals, string csharpReport, string splinkReport)
    {
        ArgumentNullException.ThrowIfNull(publicMarginals);
        ArgumentNullException.ThrowIfNull(csharpReport);
        ArgumentNullException.ThrowIfNull(splinkReport);

        // Reuse the strict public-only V1 contract and its analytic collision calculation.
        using var reference = JsonDocument.Parse(IbgeOfflineUReference.Estimate(publicMarginals, 1));
        using var csharp = JsonDocument.Parse(csharpReport);
        using var splink = JsonDocument.Parse(splinkReport);
        var expected = reference.RootElement;
        var cs = csharp.RootElement;
        var sp = splink.RootElement;

        Require(cs, "schema_version", IbgeOfflineUReference.ReportSchema);
        Require(sp, "schema_version", "JORNADA_SPLINK_INDEPENDENT_U_V1");
        var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(publicMarginals)))
            .ToLowerInvariant();
        Require(cs, "marginals_sha256", sourceHash);
        Require(sp, "marginals_sha256", sourceHash);
        foreach (var property in new[] {
            "reference_code", "reference_content_sha256", "first_name_sex", "surname_sex",
            "joint_construction", "observation_channel"
        })
        {
            var value = property switch {
                "joint_construction" => IbgeNominalUBootstrapOptions.JointConstructionVersion,
                "observation_channel" => IbgeNominalUBootstrapOptions.ObservationChannelVersion,
                _ => expected.GetProperty(property).GetString()!
            };
            Require(cs, property, value);
            Require(sp, property, value);
        }
        Require(cs, "method_version", IbgeNominalUBootstrapOptions.MethodVersion);
        Require(cs, "comparison_version", "WHOLE_NAME_JARO_WINKLER_V1");
        Require(sp, "classifier", "SPLINK_4.0.17_JARO_WINKLER_0.92_0.80");

        var analytic = expected.GetProperty("runs")[0]
            .GetProperty("analytic_exact_collision_probability").GetDecimal();
        if (Math.Abs(sp.GetProperty("analytic_exact_collision_probability").GetDecimal() - analytic)
            > Tolerance)
            throw new InvalidDataException("External analytic collision differs from public marginals.");

        var csRuns = ReadRuns(cs, external: false, CSharpSeeds, analytic);
        var spRuns = ReadRuns(sp, external: true, SplinkSeeds, analytic);
        var pairs = csRuns[0].PairCount;
        if (spRuns[0].PairCount != pairs)
            throw new InvalidDataException("The two estimators must use the same pair count per seed.");

        var total = checked((long)pairs * CSharpSeeds.Length);
        var comparisons = States.Select(state => {
            var left = csRuns.Sum(x => x.Support[state]);
            var right = spRuns.Sum(x => x.Support[state]);
            var pLeft = (decimal)left / total;
            var pRight = (decimal)right / total;
            var difference = pRight - pLeft;
            // Conditional Monte Carlo uncertainty only: independent draws from the same fixed marginals.
            var standardError = (decimal)Math.Sqrt(
                (double)(pLeft * (1m - pLeft) + pRight * (1m - pRight)) / total);
            return new {
                state,
                csharp_support = left,
                splink_support = right,
                csharp_probability = pLeft,
                splink_probability = pRight,
                splink_minus_csharp = difference,
                monte_carlo_standard_error = standardError,
                monte_carlo_95_lower = difference - 1.96m * standardError,
                monte_carlo_95_upper = difference + 1.96m * standardError
            };
        }).ToArray();

        var report = new {
            schema_version = ReportSchema,
            status = "DIAGNOSTIC_ONLY_NOT_GOVERNED",
            marginals_sha256 = sourceHash,
            reference_code = expected.GetProperty("reference_code").GetString(),
            reference_content_sha256 = expected.GetProperty("reference_content_sha256").GetString(),
            first_name_sex = expected.GetProperty("first_name_sex").GetString(),
            surname_sex = "TODOS",
            joint_construction = IbgeNominalUBootstrapOptions.JointConstructionVersion,
            observation_channel = IbgeNominalUBootstrapOptions.ObservationChannelVersion,
            csharp_method = IbgeNominalUBootstrapOptions.MethodVersion,
            splink_classifier = "SPLINK_4.0.17_JARO_WINKLER_0.92_0.80",
            pair_count_per_seed = pairs,
            csharp_seeds = CSharpSeeds,
            splink_seeds = SplinkSeeds,
            analytic_exact_collision_probability = analytic,
            total_variation_of_mean_state_probabilities =
                comparisons.Sum(x => Math.Abs(x.splink_minus_csharp)) / 2m,
            states = comparisons,
            limitations = new[] {
                "Only independent synthetic identities from fixed public first-name/surname marginals.",
                "Monte Carlo intervals exclude marginal-model uncertainty and real-world sampling bias.",
                "Different C# and Splink classifiers may shift states at Jaro-Winkler boundaries.",
                "Not pairwise agreement, independent population validation, or a VALIDATE/ACTIVATE gate."
            }
        };
        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    private static Run[] ReadRuns(JsonElement document, bool external, int[] requiredSeeds,
        decimal expectedAnalytic)
    {
        var runs = document.GetProperty("runs");
        if (runs.ValueKind != JsonValueKind.Array || runs.GetArrayLength() != requiredSeeds.Length)
            throw new InvalidDataException("Expected exactly three independent runs.");
        var result = new List<Run>(requiredSeeds.Length);
        foreach (var (node, index) in runs.EnumerateArray().Select((x, i) => (x, i)))
        {
            var seed = node.GetProperty("seed").GetInt32();
            var pairCount = node.GetProperty("pair_count").GetInt32();
            if (seed != requiredSeeds[index] || pairCount is < 1 or > 100_000)
                throw new InvalidDataException("Unrecognized seed/order or pair count.");
            var support = new Dictionary<string, long>(StringComparer.Ordinal);
            var states = node.GetProperty("states");
            if (external)
            {
                if (states.ValueKind != JsonValueKind.Object ||
                    states.EnumerateObject().Count() != States.Length)
                    throw new InvalidDataException("Incomplete external states.");
                foreach (var entry in states.EnumerateObject())
                    AddState(entry.Name, entry.Value, pairCount, support, true);
                var empirical = (decimal)support["EXACT"] / pairCount - expectedAnalytic;
                if (Math.Abs(node.GetProperty("empirical_exact_minus_analytic").GetDecimal()
                    - empirical) > Tolerance)
                    throw new InvalidDataException("External analytic diagnostic is inconsistent.");
            }
            else
            {
                if (states.ValueKind != JsonValueKind.Array ||
                    states.GetArrayLength() != States.Length)
                    throw new InvalidDataException("Incomplete C# states.");
                foreach (var entry in states.EnumerateArray())
                    AddState(entry.GetProperty("state").GetString()!, entry, pairCount, support,
                        false);
                if (Math.Abs(node.GetProperty("analytic_exact_collision_probability").GetDecimal()
                    - expectedAnalytic) > Tolerance)
                    throw new InvalidDataException("C# analytic collision is inconsistent.");
            }
            if (support.Count != States.Length || support.Values.Sum() != pairCount)
                throw new InvalidDataException("State supports do not conserve the pair count.");
            if (result.Count != 0 && pairCount != result[0].PairCount)
                throw new InvalidDataException("Pair count changed between seeds.");
            result.Add(new Run(seed, pairCount, support));
        }
        return result.ToArray();
    }

    private static void AddState(string name, JsonElement value, int pairCount,
        Dictionary<string, long> support, bool external)
    {
        if (!States.Contains(name, StringComparer.Ordinal) || support.ContainsKey(name))
            throw new InvalidDataException("Unexpected or duplicate comparison state.");
        var count = value.GetProperty("support").GetInt64();
        if (count < 0 || count > pairCount ||
            Math.Abs(value.GetProperty("probability").GetDecimal() -
                (decimal)count / pairCount) > Tolerance)
            throw new InvalidDataException("Inconsistent state probability/support.");
        if (external)
        {
            var interval = value.GetProperty("wilson_95");
            if (interval.ValueKind != JsonValueKind.Array || interval.GetArrayLength() != 2 ||
                interval[0].GetDecimal() < 0m ||
                interval[0].GetDecimal() > (decimal)count / pairCount + Tolerance ||
                interval[1].GetDecimal() < (decimal)count / pairCount - Tolerance ||
                interval[1].GetDecimal() > 1m)
                throw new InvalidDataException("Invalid external Wilson interval.");
        }
        else
        {
            var probability = (decimal)count / pairCount;
            var expectedError = (decimal)Math.Sqrt(
                (double)(probability * (1m - probability) / pairCount));
            if (Math.Abs(value.GetProperty("standard_error").GetDecimal() - expectedError)
                > Tolerance)
                throw new InvalidDataException("Inconsistent C# standard error.");
        }
        support.Add(name, count);
    }

    private static void Require(JsonElement root, string key, string expected)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(key, out var value) ||
            value.ValueKind != JsonValueKind.String || value.GetString() != expected)
            throw new InvalidDataException("Mismatch in " + key + ".");
    }
}

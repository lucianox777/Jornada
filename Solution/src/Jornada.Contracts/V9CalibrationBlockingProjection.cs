using System.Security.Cryptography;
using System.Text;

namespace Jornada.Contracts;

/// <summary>
/// Opt-in calibration projection. Never changes the persisted legacy projection.
/// Prefixes are the first K UTF-16 code units of the canonical FULL normalized name,
/// including spaces/particles. Missing names and names shorter than K emit no key;
/// absence never behaves as a wildcard or an empty-key agreement.
/// </summary>
public static class V9CalibrationBlockingProjection
{
    public const string SchemaVersion = "V9_CALIBRATION_BLOCKING_PROJECTION_V1";
    public const int DefaultMaximumPrefixLength = 4;
    public const int MaximumSupportedPrefixLength = 64;
    public const string MissingAndShortValuePolicy = "OMIT_KEY_WHEN_MISSING_OR_SHORTER_THAN_K";

    private static readonly string[] LegacyFeatures =
    [
        BlockingFeatureNames.FullName, BlockingFeatureNames.FullNameUpper,
        BlockingFeatureNames.FullNameUpperNoDiacritics, BlockingFeatureNames.FullNameWithoutParticles,
        BlockingFeatureNames.FullNamePhoneticPtBr, BlockingFeatureNames.FirstName,
        BlockingFeatureNames.Surnames, BlockingFeatureNames.LastName,
        BlockingFeatureNames.MotherFullName, BlockingFeatureNames.MotherFullNameUpper,
        BlockingFeatureNames.MotherFullNameUpperNoDiacritics, BlockingFeatureNames.MotherFullNameWithoutParticles,
        BlockingFeatureNames.MotherFullNamePhoneticPtBr, BlockingFeatureNames.MotherFirstName,
        BlockingFeatureNames.MotherSurnames, BlockingFeatureNames.MotherLastName,
        BlockingFeatureNames.BirthDay, BlockingFeatureNames.BirthMonth, BlockingFeatureNames.BirthYear
    ];

    public static IReadOnlyList<string> SupportedFeatures(int maximumPrefixLength = DefaultMaximumPrefixLength)
    {
        ValidateMaximumPrefixLength(maximumPrefixLength);
        return LegacyFeatures.Concat(Enumerable.Range(1, maximumPrefixLength)
                .SelectMany(k => new[] { NamePrefixFeature(k), MotherNamePrefixFeature(k) }))
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    public static string NamePrefixFeature(int length) => PrefixFeature("name_prefix_", length);
    public static string MotherNamePrefixFeature(int length) => PrefixFeature("mother_name_prefix_", length);

    public static string ComputeSchemaFingerprint(int maximumPrefixLength = DefaultMaximumPrefixLength) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
            new[] { SchemaVersion, BlockingProjectionKeyProjector.MethodVersion,
                IdentityComparison.NormalizationVersion, MissingAndShortValuePolicy, "FULL_NORMALIZED_NAME_UTF16_PREFIX" }
                .Concat(SupportedFeatures(maximumPrefixLength)))))).ToLowerInvariant();

    public static IReadOnlyList<BlockingProjectionKey> Project(
        string? fullName, string? motherName, DateOnly? birthDate,
        int maximumPrefixLength = DefaultMaximumPrefixLength)
    {
        ValidateMaximumPrefixLength(maximumPrefixLength);
        var keys = BlockingProjectionKeyProjector.Project(fullName, motherName, birthDate).ToList();
        AddPrefixes(BlockingFeatureNames.FullName, NamePrefixFeature);
        AddPrefixes(BlockingFeatureNames.MotherFullName, MotherNamePrefixFeature);
        return keys.OrderBy(x => x.Feature, StringComparer.Ordinal)
            .ThenBy(x => x.Value, StringComparer.Ordinal).ToArray();

        void AddPrefixes(string sourceFeature, Func<int, string> feature)
        {
            var normalized = keys.FirstOrDefault(x => x.Feature == sourceFeature)?.Value;
            if (normalized is null) return;
            for (var k = 1; k <= Math.Min(maximumPrefixLength, normalized.Length); k++)
                keys.Add(new BlockingProjectionKey(feature(k), normalized[..k]));
        }
    }

    public static void ValidateMaximumPrefixLength(int maximumPrefixLength)
    {
        if (maximumPrefixLength is < 1 or > MaximumSupportedPrefixLength)
            throw new ArgumentOutOfRangeException(nameof(maximumPrefixLength));
    }

    private static string PrefixFeature(string prefix, int length)
    {
        ValidateMaximumPrefixLength(length);
        return prefix + length.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

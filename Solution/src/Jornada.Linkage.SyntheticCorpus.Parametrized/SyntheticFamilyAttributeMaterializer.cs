using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;

namespace Jornada.Linkage.SyntheticCorpus;

// Deliberately not SyntheticPerson/SyntheticObservation: no calibrated weight,
// document number or scorer-compatible output. Explicit attributes are supplied
// by a future, separately governed demographic sampler, not invented here.
public sealed record SyntheticFamilyNameInput(string FirstNames, string Surnames, string Sex);
public sealed record SyntheticFamilyAttributeRules(DateOnly EarliestBirth, DateOnly LatestBirth,
    int MinimumSiblingBirthGapDays, int MinimumMotherAgeDays, int MaximumMotherAgeDays,
    int MaximumSimilarTwinFirstNameEditDistance);
public sealed record SyntheticFamilyAttributeInput(
    IReadOnlyDictionary<int, SyntheticFamilyNameInput> Names,
    IReadOnlyDictionary<int, string> ExternalMotherNames,
    IReadOnlyDictionary<int, DateOnly> BirthDatesByEvent,
    IReadOnlyDictionary<int, string> PartitionsByPerson,
    SyntheticFamilyAttributeRules Rules);
public sealed record SyntheticFamilyAttributedPerson(int PersonOrdinal, string Name,
    string MotherName, DateOnly BirthDate, string Sex, string Partition);
public sealed record SyntheticFamilyTruthRow(int PersonOrdinal, int FamilyOrdinal,
    int? IncludedMotherOrdinal, int? MaternalTruthOrdinal, int BirthEventOrdinal,
    int? CaseOrdinal, SyntheticFamilyCaseKind? CaseKind, string Partition);
public sealed record SyntheticFamilyTruthEnvelope(string Version, bool AllowedForScoring,
    string AttributesSha256, string TruthJson, string TruthSha256);
public sealed record SyntheticFamilyAttributedFixture(
    IReadOnlyList<SyntheticFamilyAttributedPerson> People, SyntheticFamilyTruthEnvelope PrivateTruth);
public sealed record SyntheticFamilyVersionReference(string VersionId, int PersonOrdinal);
public sealed record SyntheticFamilyVersionCensus(int People, int Families, int Versions,
    int MaximumObservedVersionsPerPerson, int MaximumObservedVersionsPerFamily);

public static class SyntheticFamilyAttributeMaterializer
{
    public const string Version = "SYNTHETIC_FAMILY_EXPLICIT_ATTRIBUTES_V1";

    public static SyntheticFamilyAttributedFixture Build(SyntheticFamilyPlanRequest request,
        SyntheticFamilyAttributeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Names);
        ArgumentNullException.ThrowIfNull(input.ExternalMotherNames);
        ArgumentNullException.ThrowIfNull(input.BirthDatesByEvent);
        ArgumentNullException.ThrowIfNull(input.PartitionsByPerson);
        ArgumentNullException.ThrowIfNull(input.Rules);
        var plan = SyntheticFamilyPlanner.Build(request);
        var rules = input.Rules;
        Require(rules.EarliestBirth <= rules.LatestBirth &&
            rules.MinimumSiblingBirthGapDays > 0 && rules.MinimumMotherAgeDays > 0 &&
            rules.MaximumMotherAgeDays >= rules.MinimumMotherAgeDays &&
            rules.MaximumSimilarTwinFirstNameEditDistance > 0, "FAMILY_ATTRIBUTE_INVALID_RULES");
        ExactKeys(input.Names.Keys, plan.Members.Select(m => m.PersonOrdinal));
        ExactKeys(input.PartitionsByPerson.Keys, plan.Members.Select(m => m.PersonOrdinal));
        ExactKeys(input.BirthDatesByEvent.Keys, plan.Members.Select(m => m.BirthEventOrdinal));
        ExactKeys(input.ExternalMotherNames.Keys, plan.Members.Where(m => !m.IncludedMotherOrdinal.HasValue)
            .Select(m => m.MaternalTruthOrdinal!.Value));

        var people = new List<SyntheticFamilyAttributedPerson>(plan.Members.Count);
        foreach (var member in plan.Members)
        {
            var name = input.Names[member.PersonOrdinal];
            Require(name is not null, "FAMILY_ATTRIBUTE_MISSING_NAME");
            var fullName = FullName(name!);
            var birth = input.BirthDatesByEvent[member.BirthEventOrdinal];
            Require(birth >= rules.EarliestBirth && birth <= rules.LatestBirth,
                "FAMILY_ATTRIBUTE_DATE_OUT_OF_RANGE");
            var mother = member.IncludedMotherOrdinal is int included
                ? FullName(input.Names[included])
                : input.ExternalMotherNames[member.MaternalTruthOrdinal!.Value];
            _ = Key(mother);
            var partition = input.PartitionsByPerson[member.PersonOrdinal];
            Require(partition is "TRAIN" or "VALIDATION" or "TEST", "FAMILY_ATTRIBUTE_INVALID_PARTITION");
            people.Add(new(member.PersonOrdinal, fullName, mother, birth, name!.Sex, partition));
        }

        var byPerson = people.ToDictionary(p => p.PersonOrdinal);
        foreach (var family in plan.Members.GroupBy(m => m.FamilyOrdinal))
            Require(family.Select(m => byPerson[m.PersonOrdinal].Partition).Distinct().Count() == 1,
                "FAMILY_ATTRIBUTE_FAMILY_PARTITION_LEAKAGE");
        foreach (var group in plan.Members.Where(m => m.CaseOrdinal.HasValue).GroupBy(m => m.CaseOrdinal))
        {
            var members = group.OrderBy(m => m.PersonOrdinal).ToArray();
            var rows = members.Select(m => byPerson[m.PersonOrdinal]).ToArray();
            // Deliberately constructed unrelated collisions are also dependent
            // diagnostic units; do not distribute the pair across partitions.
            Require(rows.Select(p => p.Partition).Distinct().Count() == 1,
                "FAMILY_ATTRIBUTE_CASE_PARTITION_LEAKAGE");
            var left = rows[0];
            var right = rows[1];
            switch (members[0].CaseKind)
            {
                case SyntheticFamilyCaseKind.Siblings:
                case SyntheticFamilyCaseKind.LargerSiblingGroup:
                    var dates = rows.Select(p => p.BirthDate.DayNumber).Order().ToArray();
                    Require(rows.Select(p => Key(p.MotherName)).Distinct().Count() == 1 &&
                        rows.Select(p => Key(p.Name)).Distinct().Count() == rows.Length &&
                        dates.Zip(dates.Skip(1), (a, b) => b - a)
                            .All(gap => gap >= rules.MinimumSiblingBirthGapDays),
                        "FAMILY_ATTRIBUTE_SIBLING_MISMATCH");
                    break;
                case SyntheticFamilyCaseKind.Twins:
                case SyntheticFamilyCaseKind.SimilarNameTwins:
                    var first = input.Names[left.PersonOrdinal];
                    var second = input.Names[right.PersonOrdinal];
                    var distance = EditDistance(Key(first.FirstNames), Key(second.FirstNames));
                    Require(left.BirthDate == right.BirthDate &&
                        Key(left.MotherName) == Key(right.MotherName) &&
                        Key(first.Surnames) == Key(second.Surnames) &&
                        Key(left.Name) != Key(right.Name) && distance > 0 &&
                        (members[0].CaseKind == SyntheticFamilyCaseKind.SimilarNameTwins
                            ? distance <= rules.MaximumSimilarTwinFirstNameEditDistance
                            : distance > rules.MaximumSimilarTwinFirstNameEditDistance),
                        "FAMILY_ATTRIBUTE_TWIN_MISMATCH");
                    break;
                case SyntheticFamilyCaseKind.SameNameMotherDaughter:
                    var age = right.BirthDate.DayNumber - left.BirthDate.DayNumber;
                    Require(members[1].IncludedMotherOrdinal == left.PersonOrdinal &&
                        left.Sex == "F" && right.Sex == "F" &&
                        Key(left.Name) == Key(right.Name) && right.MotherName == left.Name &&
                        age >= rules.MinimumMotherAgeDays && age <= rules.MaximumMotherAgeDays,
                        "FAMILY_ATTRIBUTE_MOTHER_DAUGHTER_MISMATCH");
                    break;
                case SyntheticFamilyCaseKind.UnrelatedHomonyms:
                case SyntheticFamilyCaseKind.UnrelatedSameBirthHomonyms:
                    Require(Key(left.Name) == Key(right.Name) &&
                        (left.BirthDate == right.BirthDate) ==
                            (members[0].CaseKind == SyntheticFamilyCaseKind.UnrelatedSameBirthHomonyms),
                        "FAMILY_ATTRIBUTE_HOMONYM_MISMATCH");
                    break;
                case SyntheticFamilyCaseKind.UnrelatedMotherNameCollision:
                    Require(Key(left.MotherName) == Key(right.MotherName) &&
                        Key(left.Name) != Key(right.Name), "FAMILY_ATTRIBUTE_MOTHER_COLLISION_MISMATCH");
                    break;
                default:
                    throw new InvalidDataException("FAMILY_ATTRIBUTE_UNKNOWN_CASE");
            }
        }

        var rowsTruth = plan.Members.Select(m => new SyntheticFamilyTruthRow(m.PersonOrdinal,
            m.FamilyOrdinal, m.IncludedMotherOrdinal, m.MaternalTruthOrdinal, m.BirthEventOrdinal,
            m.CaseOrdinal, m.CaseKind, byPerson[m.PersonOrdinal].Partition)).ToArray();
        // Stable ordinal order; attributes are bound by hash, not copied into
        // the private truth. No files, no key exposure, no claim of encryption.
        var attributesSha = Sha(JsonSerializer.Serialize(people));
        var truth = JsonSerializer.Serialize(new { version = Version,
            allowedForScoring = false, attributesSha256 = attributesSha,
            population = request.Population, maximumPeoplePerFamily = request.MaximumPeoplePerFamily,
            maximumVersionsPerPerson = request.MaximumVersionsPerPerson,
            maximumVersionsPerFamily = request.MaximumVersionsPerFamily,
            rules, members = rowsTruth });
        return new(people.AsReadOnly(), new(Version, false, attributesSha, truth, Sha(truth)));
    }

    // Validate actual version references supplied by a future independent
    // wave oracle; nothing here creates versions or assumes the cap was met.
    public static SyntheticFamilyVersionCensus ValidateVersionReferences(SyntheticFamilyPlanRequest request,
        IReadOnlyList<SyntheticFamilyVersionReference> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        var plan = SyntheticFamilyPlanner.Build(request);
        var byPerson = plan.Members.ToDictionary(m => m.PersonOrdinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var perPerson = new Dictionary<int, int>();
        var perFamily = new Dictionary<int, int>();
        foreach (var version in versions)
        {
            if (version is null || string.IsNullOrWhiteSpace(version.VersionId) ||
                !ids.Add(version.VersionId) || !byPerson.TryGetValue(version.PersonOrdinal, out var member))
                throw new InvalidDataException("FAMILY_VERSION_INVALID_REFERENCE");
            var n = perPerson.GetValueOrDefault(version.PersonOrdinal) + 1;
            var f = perFamily.GetValueOrDefault(member.FamilyOrdinal) + 1;
            Require(n <= request.MaximumVersionsPerPerson && f <= request.MaximumVersionsPerFamily,
                "FAMILY_VERSION_CAP_EXCEEDED");
            perPerson[version.PersonOrdinal] = n;
            perFamily[member.FamilyOrdinal] = f;
        }
        Require(perPerson.Count == request.Population && perFamily.Count == plan.Families,
            "FAMILY_VERSION_INCOMPLETE_CENSUS");
        return new(perPerson.Count, perFamily.Count, ids.Count,
            perPerson.Values.Max(), perFamily.Values.Max());
    }

    private static string FullName(SyntheticFamilyNameInput name)
    {
        Require(name is not null && name.Sex is "M" or "F", "FAMILY_ATTRIBUTE_INVALID_SEX");
        _ = Key(name!.FirstNames);
        _ = Key(name.Surnames);
        return name.FirstNames.Trim() + " " + name.Surnames.Trim();
    }

    internal static string Key(string? value) =>
        PersonNameBasicNormalization.Project(value)?.WithoutPortugueseParticles
        ?? throw new InvalidDataException("FAMILY_ATTRIBUTE_EMPTY_NAME");

    internal static int EditDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }

    private static void ExactKeys(IEnumerable<int> supplied, IEnumerable<int> expected) =>
        Require(supplied.ToHashSet().SetEquals(expected), "FAMILY_ATTRIBUTE_INPUT_KEY_SET_MISMATCH");
    private static void Require(bool condition, string error)
    {
        if (!condition) throw new InvalidDataException(error);
    }
    private static string Sha(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

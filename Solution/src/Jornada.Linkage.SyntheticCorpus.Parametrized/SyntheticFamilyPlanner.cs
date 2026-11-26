namespace Jornada.Linkage.SyntheticCorpus;

// Planning only: no names, dates, observations, partitions, files or scorer inputs.
// Each non-singleton diagnostic case is explicitly allocated once. This is not a
// demographic distribution or authorization to generate a corpus.
public enum SyntheticFamilyCaseKind
{
    Siblings, Twins, SimilarNameTwins, SameNameMotherDaughter,
    UnrelatedHomonyms, UnrelatedSameBirthHomonyms, UnrelatedMotherNameCollision,
    LargerSiblingGroup
}

public sealed record SyntheticFamilyCaseQuota(SyntheticFamilyCaseKind Kind, int Groups, int PeoplePerGroup);
public sealed record SyntheticFamilyPlanRequest(
    int Population, int MaximumPeoplePerFamily, int MaximumVersionsPerPerson,
    int MaximumVersionsPerFamily, IReadOnlyList<SyntheticFamilyCaseQuota> Quotas);
public sealed record SyntheticFamilyPlanMember(
    int PersonOrdinal, int FamilyOrdinal, int? IncludedMotherOrdinal,
    int? MaternalTruthOrdinal, int BirthEventOrdinal, int? CaseOrdinal,
    SyntheticFamilyCaseKind? CaseKind);
public sealed record SyntheticFamilyPlan(
    IReadOnlyList<SyntheticFamilyPlanMember> Members, int Families,
    int SingletonFamilies, IReadOnlyDictionary<SyntheticFamilyCaseKind, int> CaseGroups);

public static class SyntheticFamilyPlanner
{
    public const string Version = "SYNTHETIC_FAMILY_ORDINAL_PLAN_V1";

    public static SyntheticFamilyPlan Build(SyntheticFamilyPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Quotas);
        if (request.Population <= 0 || request.MaximumPeoplePerFamily <= 0 ||
            request.MaximumVersionsPerPerson <= 0 || request.MaximumVersionsPerFamily <= 0 ||
            (long)request.MaximumPeoplePerFamily * request.MaximumVersionsPerPerson != request.MaximumVersionsPerFamily)
            throw new ArgumentException("FAMILY_PLAN_INVALID_CAP_OR_POPULATION");
        var quotas = request.Quotas.ToArray();
        if (quotas.Length == 0)
            throw new ArgumentException("FAMILY_PLAN_EXPLICIT_QUOTAS_REQUIRED");
        var seen = new HashSet<(SyntheticFamilyCaseKind, int)>();
        long allocated = 0;
        foreach (var quota in quotas)
        {
            if (quota is null || !Enum.IsDefined(quota.Kind) || quota.Groups <= 0 ||
                quota.PeoplePerGroup < 2 || !seen.Add((quota.Kind, quota.PeoplePerGroup)))
                throw new ArgumentException("FAMILY_PLAN_INVALID_OR_DUPLICATE_QUOTA");
            var larger = quota.Kind == SyntheticFamilyCaseKind.LargerSiblingGroup;
            if ((larger && quota.PeoplePerGroup < 3) || (!larger && quota.PeoplePerGroup != 2))
                throw new ArgumentException("FAMILY_PLAN_INVALID_CASE_SIZE");
            if (IsFamily(quota.Kind) && quota.PeoplePerGroup > request.MaximumPeoplePerFamily)
                throw new ArgumentException("FAMILY_PLAN_FAMILY_CAP_EXCEEDED");
            var size = (long)quota.Groups * quota.PeoplePerGroup;
            if (size > request.Population - allocated)
                throw new ArgumentException("FAMILY_PLAN_POPULATION_EXCEEDED");
            allocated += size;
        }

        var members = new List<SyntheticFamilyPlanMember>(request.Population);
        var caseGroups = new Dictionary<SyntheticFamilyCaseKind, int>();
        int family = 0, maternal = 0, birth = 0, caseId = 0, singletons = 0;
        // Canonical ordering makes input quota order irrelevant; no RNG is consumed.
        foreach (var quota in quotas.OrderBy(q => q.Kind).ThenBy(q => q.PeoplePerGroup))
        {
            caseGroups[quota.Kind] = caseGroups.GetValueOrDefault(quota.Kind) + quota.Groups;
            for (var group = 0; group < quota.Groups; group++)
            {
                var currentCase = ++caseId;
                if (!IsFamily(quota.Kind))
                {
                    // Unrelated cases are separate singleton families. Matching
                    // names/dates later must not imply shared maternal/birth truth.
                    for (var i = 0; i < quota.PeoplePerGroup; i++)
                    {
                        members.Add(new(members.Count + 1, ++family, null, ++maternal,
                            ++birth, currentCase, quota.Kind));
                        singletons++;
                    }
                    continue;
                }
                var currentFamily = ++family;
                var currentMaternal = ++maternal;
                if (quota.Kind == SyntheticFamilyCaseKind.SameNameMotherDaughter)
                {
                    var mother = members.Count + 1;
                    members.Add(new(mother, currentFamily, null, currentMaternal,
                        ++birth, currentCase, quota.Kind));
                    // Maternal truth for the daughter is the included mother;
                    // a separate ordinal avoids conflating mother and grandmother.
                    members.Add(new(members.Count + 1, currentFamily, mother, ++maternal,
                        ++birth, currentCase, quota.Kind));
                    continue;
                }
                var twins = quota.Kind is SyntheticFamilyCaseKind.Twins or SyntheticFamilyCaseKind.SimilarNameTwins;
                var twinBirth = twins ? ++birth : 0;
                for (var i = 0; i < quota.PeoplePerGroup; i++)
                    members.Add(new(members.Count + 1, currentFamily, null, currentMaternal,
                        twins ? twinBirth : ++birth, currentCase, quota.Kind));
            }
        }
        while (members.Count < request.Population)
        {
            members.Add(new(members.Count + 1, ++family, null, ++maternal, ++birth, null, null));
            singletons++;
        }
        return new(members.AsReadOnly(), family, singletons,
            new System.Collections.ObjectModel.ReadOnlyDictionary<SyntheticFamilyCaseKind, int>(caseGroups));
    }

    private static bool IsFamily(SyntheticFamilyCaseKind kind) => kind is
        SyntheticFamilyCaseKind.Siblings or SyntheticFamilyCaseKind.Twins or
        SyntheticFamilyCaseKind.SimilarNameTwins or SyntheticFamilyCaseKind.SameNameMotherDaughter or
        SyntheticFamilyCaseKind.LargerSiblingGroup;
}

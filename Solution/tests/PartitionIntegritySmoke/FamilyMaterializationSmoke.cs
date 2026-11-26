using Jornada.Linkage.SyntheticCorpus;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class FamilyMaterializationSmoke
{
    internal static void Run()
    {
        var quotas = Enum.GetValues<SyntheticFamilyCaseKind>().Select(k =>
            new SyntheticFamilyCaseQuota(k, 1, k == SyntheticFamilyCaseKind.LargerSiblingGroup ? 4 : 2)).ToArray();
        var request = new SyntheticFamilyPlanRequest(20, 4, 8, 32, quotas);
        var plan = SyntheticFamilyPlanner.Build(request);
        var names = new Dictionary<int, SyntheticFamilyNameInput>();
        var mothers = new Dictionary<int, string>();
        var dates = new Dictionary<int, DateOnly>();
        var partitions = new Dictionary<int, string>();
        foreach (var group in plan.Members.GroupBy(m => m.CaseOrdinal ?? -m.PersonOrdinal))
        {
            var members = group.OrderBy(m => m.PersonOrdinal).ToArray();
            for (var i = 0; i < members.Length; i++)
            {
                var m = members[i];
                var first = "PESSOA" + m.PersonOrdinal;
                var surname = "SOBRENOME";
                var date = new DateOnly(2000 + i, 1, 1);
                switch (m.CaseKind)
                {
                    case SyntheticFamilyCaseKind.Twins:
                        first = i == 0 ? "ALFA" : "OMEGA";
                        date = new DateOnly(2000, 1, 1);
                        break;
                    case SyntheticFamilyCaseKind.SimilarNameTwins:
                        first = i == 0 ? "ANA" : "ANNA";
                        date = new DateOnly(2000, 1, 1);
                        break;
                    case SyntheticFamilyCaseKind.SameNameMotherDaughter:
                        first = "NOMEIGUAL";
                        date = new DateOnly(i == 0 ? 1980 : 2000, 1, 1);
                        break;
                    case SyntheticFamilyCaseKind.UnrelatedHomonyms:
                        first = "HOMONIMO";
                        break;
                    case SyntheticFamilyCaseKind.UnrelatedSameBirthHomonyms:
                        first = "HOMONIMODATA";
                        date = new DateOnly(2000, 1, 1);
                        break;
                }
                names.Add(m.PersonOrdinal, new(first, surname, "F"));
                if (m.IncludedMotherOrdinal is null)
                    mothers[m.MaternalTruthOrdinal!.Value] = m.CaseKind == SyntheticFamilyCaseKind.UnrelatedMotherNameCollision
                        ? "MATERNA IGUAL" : "MATERNA " + m.MaternalTruthOrdinal;
                dates[m.BirthEventOrdinal] = date;
                partitions[m.PersonOrdinal] = "TRAIN";
            }
        }
        // These bounds are artificial fixture inputs, not production assumptions.
        var rules = new SyntheticFamilyAttributeRules(new(1900, 1, 1), new(2026, 1, 1),
            300, 15 * 365, 55 * 366, 1);
        var input = new SyntheticFamilyAttributeInput(names, mothers, dates, partitions, rules);
        var actual = SyntheticFamilyAttributeMaterializer.Build(request, input);
        var repeat = SyntheticFamilyAttributeMaterializer.Build(request with { Quotas = quotas.Reverse().ToArray() }, input);
        if (actual.People.Count != 20 || actual.PrivateTruth.AllowedForScoring ||
            !actual.People.SequenceEqual(repeat.People) || actual.PrivateTruth != repeat.PrivateTruth ||
            actual.PrivateTruth.TruthSha256 != Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(actual.PrivateTruth.TruthJson))))
            throw new InvalidOperationException("Family attributes deterministic/hash mismatch");
        using var truth = JsonDocument.Parse(actual.PrivateTruth.TruthJson);
        if (truth.RootElement.GetProperty("allowedForScoring").GetBoolean() ||
            truth.RootElement.GetProperty("members").GetArrayLength() != 20 ||
            truth.RootElement.GetProperty("attributesSha256").GetString() != actual.PrivateTruth.AttributesSha256 ||
            actual.PrivateTruth.TruthJson.Contains("NOMEIGUAL", StringComparison.Ordinal) ||
            actual.PrivateTruth.TruthJson.Contains("MATERNA", StringComparison.Ordinal))
            throw new InvalidOperationException("Family private truth boundary mismatch");

        void Reject(Action action)
        {
            try { action(); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException("Invalid family materialization accepted");
        }
        var twin = plan.Members.First(m => m.CaseKind == SyntheticFamilyCaseKind.Twins);
        var badNames = new Dictionary<int, SyntheticFamilyNameInput>(names)
        {
            [twin.PersonOrdinal + 1] = names[twin.PersonOrdinal]
        };
        Reject(() => SyntheticFamilyAttributeMaterializer.Build(request, input with { Names = badNames }));
        var mother = plan.Members.First(m => m.CaseKind == SyntheticFamilyCaseKind.SameNameMotherDaughter);
        var badDates = new Dictionary<int, DateOnly>(dates)
        {
            [mother.BirthEventOrdinal] = new(1999, 1, 1)
        };
        Reject(() => SyntheticFamilyAttributeMaterializer.Build(request, input with { BirthDatesByEvent = badDates }));
        var badPartitions = new Dictionary<int, string>(partitions) { [twin.PersonOrdinal] = "TEST" };
        Reject(() => SyntheticFamilyAttributeMaterializer.Build(request, input with { PartitionsByPerson = badPartitions }));
        var homonym = plan.Members.First(m => m.CaseKind == SyntheticFamilyCaseKind.UnrelatedHomonyms);
        badPartitions = new(partitions) { [homonym.PersonOrdinal] = "VALIDATION" };
        Reject(() => SyntheticFamilyAttributeMaterializer.Build(request, input with { PartitionsByPerson = badPartitions }));
        var missing = new Dictionary<int, SyntheticFamilyNameInput>(names);
        missing.Remove(twin.PersonOrdinal);
        Reject(() => SyntheticFamilyAttributeMaterializer.Build(request, input with { Names = missing }));

        var versions = plan.Members.SelectMany(m => Enumerable.Range(1, 8)
            .Select(i => new SyntheticFamilyVersionReference($"{m.PersonOrdinal}-{i}", m.PersonOrdinal))).ToArray();
        var census = SyntheticFamilyAttributeMaterializer.ValidateVersionReferences(request, versions);
        if (census.People != 20 || census.Families != 13 || census.Versions != 160 ||
            census.MaximumObservedVersionsPerPerson != 8 || census.MaximumObservedVersionsPerFamily != 32)
            throw new InvalidOperationException("Family version census mismatch");
        Reject(() => SyntheticFamilyAttributeMaterializer.ValidateVersionReferences(request,
            [..versions, new("extra", twin.PersonOrdinal)]));
        Reject(() => SyntheticFamilyAttributeMaterializer.ValidateVersionReferences(request, [..versions, versions[0]]));
        Reject(() => SyntheticFamilyAttributeMaterializer.ValidateVersionReferences(request,
            versions.Where(v => v.PersonOrdinal != twin.PersonOrdinal).ToArray()));
        Reject(() => SyntheticFamilyAttributeMaterializer.ValidateVersionReferences(request, [..versions, new("orphan", 999)]));
        Console.WriteLine("FAMILY_ATTRIBUTE_AND_PRIVATE_TRUTH_SMOKE_PASS");
    }
}

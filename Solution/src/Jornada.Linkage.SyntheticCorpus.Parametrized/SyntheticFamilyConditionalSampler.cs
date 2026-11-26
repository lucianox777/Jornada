using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jornada.Linkage.SyntheticCorpus;

// Conditional diagnostic sampler only. No estimate of joint demographic
// probabilities, importance weights, civil documents, or scorer-ready corpus.
public sealed record SyntheticFamilySamplingOptions(ulong Seed,
    SyntheticFamilyAttributeRules AttributeRules, int MaximumSiblingSpanDays,
    int MaximumAttemptsPerConstraint, double FemaleProbability,
    double TrainPercent, double ValidationPercent, double TestPercent);
public sealed record SyntheticFamilySamplingReceipt(string Version, string ProvenanceJson,
    string ProvenanceSha256, string AttributesSha256, string TruthSha256);
public sealed record SyntheticFamilySample(SyntheticFamilyAttributedFixture Fixture,
    SyntheticFamilySamplingReceipt Receipt);

public static class SyntheticFamilyConditionalSampler
{
    public const string Version = "SYNTHETIC_FAMILY_CONDITIONAL_DIAGNOSTIC_V1";

    // No CLI/workflow uses this path. The caller must obtain the required
    // preregistration and authorization before generating a real DEV profile.
    public static async Task<SyntheticFamilySample> SampleFromReferencesAsync(
        string referenceRoot, string birthPath, long minimumFrequency,
        SyntheticFamilyPlanRequest request, SyntheticFamilySamplingOptions options,
        CancellationToken cancellationToken = default)
    {
        Validate(request, options);
        var corpusOptions = new SyntheticCorpusOptions(People: request.Population,
            Seed: options.Seed, MinFrequency: minimumFrequency, TailOversample: 1);
        var nominal = await SyntheticCorpusSourceLoader.LoadDemographicPrimaryAsync(
            referenceRoot, corpusOptions, cancellationToken);
        var births = await SyntheticDailyBirthDistribution.LoadAsync(birthPath, cancellationToken);
        return Sample(request, options, nominal, births, minimumFrequency, cancellationToken);
    }

    // Public for isolated technical fixtures using artificial reference tables.
    // Supplied samplers do not by themselves establish official source identity.
    public static SyntheticFamilySample Sample(SyntheticFamilyPlanRequest request,
        SyntheticFamilySamplingOptions options, SyntheticCorpusDemographicNominalSource nominal,
        SyntheticDailyBirthDistribution births, long minimumFrequency,
        CancellationToken cancellationToken = default)
    {
        Validate(request, options);
        ArgumentNullException.ThrowIfNull(nominal);
        ArgumentNullException.ThrowIfNull(births);
        if (minimumFrequency <= 0 || new[] { nominal.PersonFirstNames, nominal.PersonSurnames,
            nominal.MotherFirstNames, nominal.MotherSurnames }.Any(s => s.TailCutFrequency != 0))
            throw new ArgumentException("FAMILY_SAMPLING_REQUIRES_NO_TAIL_OVERSAMPLING");
        var plan = SyntheticFamilyPlanner.Build(request);
        var random = new Xoshiro256StarStar(options.Seed);
        var rules = options.AttributeRules;
        var names = new Dictionary<int, SyntheticFamilyNameInput>();
        var mothers = new Dictionary<int, string>();
        var dates = new Dictionary<int, DateOnly>();
        var partitions = new Dictionary<int, string>();
        var attempts = new Dictionary<string, long>(StringComparer.Ordinal);

        // Bounded rejection: never silently replace an impossible constraint,
        // modify a quota, invent a name/date, or return a partial population.
        T DrawUntil<T>(string label, Func<T> draw, Func<T, bool> accept)
        {
            for (var i = 0; i < options.MaximumAttemptsPerConstraint; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempts[label] = attempts.GetValueOrDefault(label) + 1;
                var candidate = draw();
                if (accept(candidate)) return candidate;
            }
            throw new InvalidDataException("FAMILY_SAMPLING_SUPPORT_EXHAUSTED_" + label);
        }
        string Key(string value) => SyntheticFamilyAttributeMaterializer.Key(value);
        string Sex() => random.NextUnitInterval() < options.FemaleProbability ? "F" : "M";
        string First() => nominal.PersonFirstNames.Draw(random);
        string Surname() => nominal.PersonSurnames.Draw(random);
        string MotherName() => nominal.MotherFirstNames.Draw(random) + " " +
            nominal.MotherSurnames.Draw(random);
        DateOnly Birth() => births.Draw(random);
        bool InRange(DateOnly date) => date >= rules.EarliestBirth && date <= rules.LatestBirth;
        DateOnly BoundedBirth() => DrawUntil("BIRTH_RANGE", Birth, InRange);
        string Partition()
        {
            var p = random.NextUnitInterval() * 100;
            return p < options.TrainPercent ? "TRAIN"
                : p < options.TrainPercent + options.ValidationPercent ? "VALIDATION" : "TEST";
        }
        void SetName(SyntheticFamilyPlanMember member, string first, string surname, string sex) =>
            names.Add(member.PersonOrdinal, new(first, surname, sex));
        void SetBirth(SyntheticFamilyPlanMember member, DateOnly date) =>
            dates.Add(member.BirthEventOrdinal, date);
        void SetMother(SyntheticFamilyPlanMember member, string mother) =>
            mothers.Add(member.MaternalTruthOrdinal!.Value, mother);

        foreach (var group in plan.Members.GroupBy(m => m.CaseOrdinal ?? -m.PersonOrdinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = group.OrderBy(m => m.PersonOrdinal).ToArray();
            var partition = Partition();
            foreach (var row in rows) partitions.Add(row.PersonOrdinal, partition);
            var left = rows[0];
            switch (left.CaseKind)
            {
                case null:
                    SetName(left, First(), Surname(), Sex());
                    SetBirth(left, BoundedBirth());
                    SetMother(left, MotherName());
                    break;
                case SyntheticFamilyCaseKind.Twins:
                case SyntheticFamilyCaseKind.SimilarNameTwins:
                    // Shared maternal surname is an explicit engineering model,
                    // not a measured surname transmission probability.
                    var twinSurname = Surname();
                    var similar = left.CaseKind == SyntheticFamilyCaseKind.SimilarNameTwins;
                    // Draw both names jointly: freezing a first name without a
                    // compatible neighbor would incorrectly exhaust valid support.
                    var twinNames = DrawUntil("TWIN_FIRST_NAME",
                        () => (First: First(), Second: First()), candidate =>
                        {
                            var distance = SyntheticFamilyAttributeMaterializer.EditDistance(
                                Key(candidate.First), Key(candidate.Second));
                            return distance > 0 && (similar
                                ? distance <= rules.MaximumSimilarTwinFirstNameEditDistance
                                : distance > rules.MaximumSimilarTwinFirstNameEditDistance);
                        });
                    SetName(left, twinNames.First, twinSurname, Sex());
                    SetName(rows[1], twinNames.Second, twinSurname, Sex());
                    SetBirth(left, BoundedBirth()); // one event shared by both twins
                    SetMother(left, nominal.MotherFirstNames.Draw(random) + " " + twinSurname);
                    break;
                case SyntheticFamilyCaseKind.Siblings:
                case SyntheticFamilyCaseKind.LargerSiblingGroup:
                    var surname = Surname();
                    var used = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var row in rows)
                    {
                        var first = DrawUntil("SIBLING_FIRST_NAME", First, candidate => !used.Contains(Key(candidate)));
                        used.Add(Key(first));
                        SetName(row, first, surname, Sex());
                    }
                    // Draw an entire date vector from the existing reference and
                    // condition on chronological separation and maximum span.
                    // Order dates randomly by draw order, not by person ordinal.
                    var siblingDates = DrawUntil("SIBLING_BIRTHS",
                        () => rows.Select(_ => Birth()).ToArray(), candidate =>
                        {
                            if (!candidate.All(InRange)) return false;
                            var ordered = candidate.Select(d => d.DayNumber).Order().ToArray();
                            return ordered[^1] - ordered[0] <= options.MaximumSiblingSpanDays &&
                                ordered.Zip(ordered.Skip(1), (a, b) => b - a)
                                    .All(gap => gap >= rules.MinimumSiblingBirthGapDays);
                        });
                    for (var i = 0; i < rows.Length; i++) SetBirth(rows[i], siblingDates[i]);
                    SetMother(left, nominal.MotherFirstNames.Draw(random) + " " + surname);
                    break;
                case SyntheticFamilyCaseKind.SameNameMotherDaughter:
                    var sharedFirst = nominal.MotherFirstNames.Draw(random);
                    var sharedSurname = Surname();
                    SetName(left, sharedFirst, sharedSurname, "F");
                    SetName(rows[1], sharedFirst, sharedSurname, "F");
                    var motherDaughter = DrawUntil("MOTHER_DAUGHTER_BIRTHS",
                        () => (Mother: Birth(), Daughter: Birth()), candidate =>
                        {
                            var age = candidate.Daughter.DayNumber - candidate.Mother.DayNumber;
                            return InRange(candidate.Mother) && InRange(candidate.Daughter) &&
                                age >= rules.MinimumMotherAgeDays && age <= rules.MaximumMotherAgeDays;
                        });
                    SetBirth(left, motherDaughter.Mother);
                    SetBirth(rows[1], motherDaughter.Daughter);
                    SetMother(left, MotherName()); // the grandmother; daughter's mother is included
                    break;
                case SyntheticFamilyCaseKind.UnrelatedHomonyms:
                case SyntheticFamilyCaseKind.UnrelatedSameBirthHomonyms:
                    var homonymFirst = First();
                    var homonymSurname = Surname();
                    SetName(left, homonymFirst, homonymSurname, Sex());
                    SetName(rows[1], homonymFirst, homonymSurname, Sex());
                    var firstDate = BoundedBirth();
                    SetBirth(left, firstDate);
                    SetBirth(rows[1], left.CaseKind == SyntheticFamilyCaseKind.UnrelatedSameBirthHomonyms
                        ? firstDate : DrawUntil("HOMONYM_DISTINCT_BIRTH", Birth,
                            candidate => InRange(candidate) && candidate != firstDate));
                    SetMother(left, MotherName());
                    SetMother(rows[1], MotherName()); // distinct truth even if text collides
                    break;
                case SyntheticFamilyCaseKind.UnrelatedMotherNameCollision:
                    var firstPair = (First: First(), Surname: Surname());
                    var secondPair = DrawUntil("MOTHER_COLLISION_DISTINCT_NAME",
                        () => (First: First(), Surname: Surname()),
                        candidate => Key(candidate.First + " " + candidate.Surname) !=
                            Key(firstPair.First + " " + firstPair.Surname));
                    SetName(left, firstPair.First, firstPair.Surname, Sex());
                    SetName(rows[1], secondPair.First, secondPair.Surname, Sex());
                    SetBirth(left, BoundedBirth());
                    SetBirth(rows[1], BoundedBirth());
                    var sharedMotherName = MotherName();
                    SetMother(left, sharedMotherName);
                    SetMother(rows[1], sharedMotherName);
                    break;
                default:
                    throw new InvalidDataException("FAMILY_SAMPLING_UNKNOWN_CASE");
            }
        }

        var fixture = SyntheticFamilyAttributeMaterializer.Build(request,
            new(names, mothers, dates, partitions, rules));
        // Receipt identifies the inputs and result; it does not certify a
        // population or estimate joint inclusion probabilities.
        var provenance = JsonSerializer.Serialize(new
        {
            version = Version,
            plannerVersion = SyntheticFamilyPlanner.Version,
            rng = Xoshiro256StarStar.AlgorithmVersion,
            frequencySampler = SyntheticFrequencySampler.MethodVersion,
            scope = "CONDITIONAL_DIAGNOSTIC_NOT_DEMOGRAPHICALLY_CERTIFIED",
            jointWeightsAvailable = false,
            options,
            minimumFrequency,
            population = request.Population,
            maximumPeoplePerFamily = request.MaximumPeoplePerFamily,
            maximumVersionsPerPerson = request.MaximumVersionsPerPerson,
            maximumVersionsPerFamily = request.MaximumVersionsPerFamily,
            quotas = request.Quotas.OrderBy(q => q.Kind).ThenBy(q => q.PeoplePerGroup).ToArray(),
            nominal.ReferenceCode,
            nominal.PersonPhysicalSha256, nominal.PersonCanonicalContentSha256,
            nominal.MotherFirstPhysicalSha256, nominal.MotherFirstCanonicalContentSha256,
            nominal.MotherSurnamePhysicalSha256, nominal.MotherSurnameCanonicalContentSha256,
            birthSha256 = births.Provenance.Sha256,
            birthSchema = births.Provenance.SchemaVersion,
            birthRows = births.Provenance.RowCount,
            attempts = attempts.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            fixture.PrivateTruth.AttributesSha256,
            fixture.PrivateTruth.TruthSha256
        });
        return new(fixture, new(Version, provenance,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(provenance))),
            fixture.PrivateTruth.AttributesSha256, fixture.PrivateTruth.TruthSha256));
    }

    private static void Validate(SyntheticFamilyPlanRequest request, SyntheticFamilySamplingOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.AttributeRules);
        var rules = options.AttributeRules;
        if (rules.EarliestBirth > rules.LatestBirth || rules.MinimumSiblingBirthGapDays <= 0 ||
            rules.MinimumMotherAgeDays <= 0 || rules.MaximumMotherAgeDays < rules.MinimumMotherAgeDays ||
            rules.MaximumSimilarTwinFirstNameEditDistance <= 0 ||
            options.MaximumSiblingSpanDays < rules.MinimumSiblingBirthGapDays ||
            options.MaximumAttemptsPerConstraint <= 0 || !double.IsFinite(options.FemaleProbability) ||
            options.FemaleProbability is < 0 or > 1)
            throw new ArgumentException("FAMILY_SAMPLING_INVALID_OPTIONS");
        new SyntheticCorpusOptions(People: request.Population, Seed: options.Seed,
            TrainPercent: options.TrainPercent, ValidationPercent: options.ValidationPercent,
            TestPercent: options.TestPercent).Validate();
    }
}

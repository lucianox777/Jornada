using System.Globalization;

namespace Jornada.Linkage.SyntheticCorpus;

public sealed class SyntheticCorpusGenerator
{
    private static readonly int[] SurnameCounts = [1, 2, 3];
    private static readonly ulong[] SurnameCountWeights = [35, 45, 20];
    private static readonly int[] ObservationCounts = [1, 2, 3, 4];
    private static readonly ulong[] ObservationCountWeights = [40, 34, 18, 8];

    private readonly SyntheticFrequencySampler firstNames;
    private readonly SyntheticFrequencySampler surnames;

    public SyntheticCorpusGenerator(
        SyntheticFrequencySampler firstNames,
        SyntheticFrequencySampler surnames)
    {
        this.firstNames = firstNames ?? throw new ArgumentNullException(nameof(firstNames));
        this.surnames = surnames ?? throw new ArgumentNullException(nameof(surnames));
    }

    public SyntheticCorpusGeneration Generate(SyntheticCorpusOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var random = new Xoshiro256StarStar(options.Seed);
        var profile = SyntheticCorpusV2Rules.Profiles[options.ErrorProfile];

        var people = new List<SyntheticPerson>(options.People);
        for (var i = 0; i < options.People; i++)
        {
            var person = MakePerson(
                $"P{i:D7}",
                random,
                options.CpfBasePrevalence,
                options.CnsBasePrevalence,
                options.BrazilianNameErrors?.AgnomeBasePrevalence ?? 0);

            var split = random.NextUnitInterval();
            person.Partition = split < .6 ? "TRAIN" : split < .8 ? "VALIDATION" : "TEST";
            people.Add(person);
        }

        ApplyCnsScenarios(
            people,
            random,
            options.CnsInvalidRate,
            options.CnsReuseRate,
            options.CnsDobConflictRate);

        var observations = new List<SyntheticObservation>();
        foreach (var person in people)
        {
            var count = SyntheticCorpusV2Rules.WeightedChoice(
                random,
                ObservationCounts,
                ObservationCountWeights);

            var gestorIndexes = Enumerable.Range(0, options.Gestores).ToArray();
            var selected = SyntheticCorpusV2Rules.SampleWithoutReplacement(
                random,
                gestorIndexes,
                Math.Min(count, options.Gestores));

            for (var sequence = 0; sequence < count; sequence++)
            {
                var gestor = $"G{selected[sequence % selected.Count]}";
                var observation = Observe(
                    person,
                    random,
                    profile,
                    gestor,
                    sequence,
                    options.CpfObservationRetention,
                    options.CnsObservationRetention,
                    options.BrazilianNameErrors);
                if (options.StratifiedErrors is { } strata)
                    SyntheticStratifiedErrorOverlay.Apply(observation, random, strata);
                observations.Add(observation);
            }
        }

        return new SyntheticCorpusGeneration(
            people,
            observations,
            ComputeEmpiricalM(observations),
            options);
    }

    public SyntheticPerson MakePerson(
        string basePersonId,
        Xoshiro256StarStar random,
        double cpfPrevalence,
        double cnsPrevalence,
        double agnomePrevalence = 0)
    {
        var surnameCount = SyntheticCorpusV2Rules.WeightedChoice(
            random,
            SurnameCounts,
            SurnameCountWeights);

        var surnameComponents = new List<string>(surnameCount + 1);
        for (var i = 0; i < surnameCount; i++)
            surnameComponents.Add(surnames.Draw(random));

        if (surnameComponents.Count > 1 && SyntheticCorpusV2Rules.NextBernoulli(random, .45))
        {
            surnameComponents.Insert(
                1,
                SyntheticCorpusV2Rules.Choose(random, SyntheticCorpusV2Rules.SurnameParticles));
        }

        var first = firstNames.Draw(random);
        if (SyntheticCorpusV2Rules.NextBernoulli(random, .18))
            first += " " + firstNames.Draw(random);

        var motherFirst = firstNames.Draw(random);
        var motherExtra = SyntheticCorpusV2Rules.NextBernoulli(random, .4)
            ? surnames.Draw(random)
            : null;
        var mother = motherFirst
            + (motherExtra is null ? string.Empty : " " + motherExtra)
            + " "
            + surnameComponents[^1];

        var year = 1935 + random.NextInt32(2020 - 1935 + 1);
        var birthDate = new DateOnly(year, 1, 1).AddDays(random.NextInt32(365));

        var correctionComponents = new List<double>();
        correctionComponents.AddRange(
            first.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(firstNames.Weight));
        correctionComponents.AddRange(
            surnameComponents
                .Where(component => !SyntheticCorpusV2Rules.SurnameParticles.Contains(component, StringComparer.Ordinal))
                .Select(surnames.Weight));
        correctionComponents.Add(firstNames.Weight(motherFirst));
        correctionComponents.Add(surnames.Weight(surnameComponents[^1]));

        var evaluationWeight = 1.0;
        foreach (var correction in correctionComponents)
            evaluationWeight *= correction;

        var fullName = first + " " + string.Join(" ", surnameComponents);
        if (agnomePrevalence > 0)
            fullName = SyntheticBrazilianNameErrors.AppendTrueAgnome(fullName, random, agnomePrevalence) ?? fullName;

        return new SyntheticPerson
        {
            BasePersonId = basePersonId,
            Partition = string.Empty,
            Name = fullName,
            MotherName = mother,
            BirthDate = birthDate,
            Sex = SyntheticCorpusV2Rules.Choose(random, new[] { "M", "F" }),
            Cpf = SyntheticCorpusV2Rules.NextBernoulli(random, cpfPrevalence)
                ? SyntheticCorpusV2Rules.GenerateCpf(random)
                : null,
            Cns = SyntheticCorpusV2Rules.NextBernoulli(random, cnsPrevalence)
                ? SyntheticCorpusV2Rules.GenerateCns(random)
                : null,
            EvaluationWeight = Math.Round(evaluationWeight, 8, MidpointRounding.ToEven)
        };
    }

    public static SyntheticObservation Observe(
        SyntheticPerson person,
        Xoshiro256StarStar random,
        SyntheticErrorProfile profile,
        string gestor,
        int sequence,
        double cpfRetention,
        double cnsRetention,
        SyntheticBrazilianNameErrorConfig? brazilianNameErrors = null)
    {
        var observation = new SyntheticObservation
        {
            ObservationId = $"{person.BasePersonId}-{gestor}-{sequence}",
            BasePersonId = person.BasePersonId,
            Partition = person.Partition,
            Gestor = gestor,
            Name = person.Name,
            MotherName = person.MotherName,
            BirthDate = person.BirthDate,
            Sex = person.Sex,
            Cpf = person.Cpf,
            Cns = person.Cns,
            EvaluationWeight = person.EvaluationWeight,
            Corruptions = string.Empty
        };

        var labels = new List<string>();
        var common = SyntheticCorpusV2Rules.NextBernoulli(random, profile.CommonCorruptionProbability);

        if (common || SyntheticCorpusV2Rules.NextBernoulli(random, profile.NameCorruptionProbability))
        {
            var corrupted = SyntheticCorpusV2Rules.CorruptText(observation.Name!, random);
            if (corrupted.Operation is not null && corrupted.Value != observation.Name)
            {
                observation.Name = corrupted.Value;
                labels.Add("NOME_" + corrupted.Operation);
            }
        }

        if (common || SyntheticCorpusV2Rules.NextBernoulli(random, profile.MotherCorruptionProbability))
        {
            var corrupted = SyntheticCorpusV2Rules.CorruptText(observation.MotherName!, random);
            if (corrupted.Operation is not null && corrupted.Value != observation.MotherName)
            {
                observation.MotherName = corrupted.Value;
                labels.Add("MAE_" + corrupted.Operation);
            }
        }

        if (common || SyntheticCorpusV2Rules.NextBernoulli(random, profile.DateCorruptionProbability))
        {
            var corrupted = SyntheticCorpusV2Rules.CorruptDate(observation.BirthDate!.Value, random);
            if (corrupted.Operation is not null && corrupted.Value != observation.BirthDate)
            {
                observation.BirthDate = corrupted.Value;
                labels.Add(corrupted.Operation);
            }
        }

        if (SyntheticCorpusV2Rules.NextBernoulli(random, profile.MissingMotherProbability))
        {
            observation.MotherName = null;
            labels.Add("MAE_MISSING");
        }

        if (SyntheticCorpusV2Rules.NextBernoulli(random, profile.MissingDateProbability))
        {
            observation.BirthDate = null;
            labels.Add("DATE_MISSING");
        }

        if (observation.Cpf is not null
            && random.NextUnitInterval() > cpfRetention)
            observation.Cpf = null;

        if (observation.Cns is not null
            && random.NextUnitInterval() > cnsRetention)
            observation.Cns = null;

        observation.Corruptions = string.Join("|", labels);
        if (brazilianNameErrors is not null)
            SyntheticBrazilianNameErrors.Apply(observation, random, brazilianNameErrors);
        return observation;
    }

    public static void ApplyCnsScenarios(
        IReadOnlyList<SyntheticPerson> people,
        Xoshiro256StarStar random,
        double invalidRate,
        double reuseRate,
        double dobConflictRate)
    {
        var withCns = people.Where(person => person.Cns is not null).ToArray();

        var invalidCount = Math.Min(
            withCns.Length,
            checked((int)Math.Round(withCns.Length * invalidRate, MidpointRounding.ToEven)));
        foreach (var person in SyntheticCorpusV2Rules.SampleWithoutReplacement(random, withCns, invalidCount))
        {
            person.Cns = SyntheticCorpusV2Rules.InvalidateCheckDigit(person.Cns);
            person.CnsScenario = "INVALID_CHECK_DIGIT";
        }

        var valid = withCns.Where(person => person.CnsScenario is null).ToArray();
        var reuseCount = Math.Min(
            valid.Length / 2,
            checked((int)Math.Round(valid.Length * reuseRate, MidpointRounding.ToEven)));
        for (var i = 0; i < reuseCount; i++)
        {
            var source = valid[2 * i];
            var target = valid[(2 * i) + 1];
            target.Cns = source.Cns;
            source.CnsScenario = "REUSED";
            target.CnsScenario = "REUSED";
        }

        var remaining = valid
            .Skip(2 * reuseCount)
            .Where(person => person.CnsScenario is null)
            .ToArray();
        var conflictCount = Math.Min(
            remaining.Length / 2,
            checked((int)Math.Round(remaining.Length * dobConflictRate, MidpointRounding.ToEven)));

        var used = new HashSet<string>(StringComparer.Ordinal);
        var conflictsCreated = 0;
        foreach (var first in remaining)
        {
            if (conflictsCreated >= conflictCount)
                break;
            if (used.Contains(first.BasePersonId))
                continue;

            var candidates = remaining
                .Where(second =>
                    !ReferenceEquals(second, first)
                    && !used.Contains(second.BasePersonId)
                    && Math.Abs(first.BirthDate.DayNumber - second.BirthDate.DayNumber) >= 3650)
                .ToArray();
            if (candidates.Length == 0)
                continue;

            var second = SyntheticCorpusV2Rules.Choose(random, candidates);
            second.Cns = first.Cns;
            first.CnsScenario = "DOB_CONFLICT_REUSE";
            second.CnsScenario = "DOB_CONFLICT_REUSE";
            used.Add(first.BasePersonId);
            used.Add(second.BasePersonId);
            conflictsCreated++;
        }

        foreach (var person in people)
            person.CnsScenario ??= person.Cns is null ? "ABSENT" : "CLEAN";
    }

    public static IReadOnlyDictionary<string, SyntheticEmpiricalM> ComputeEmpiricalM(
        IReadOnlyList<SyntheticObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var groups = observations
            .GroupBy(x => x.BasePersonId, StringComparer.Ordinal)
            .ToArray();

        return new Dictionary<string, SyntheticEmpiricalM>(StringComparer.Ordinal)
        {
            ["NOME"] = ComputeField(groups, x => x.Name),
            ["NOME_MAE"] = ComputeField(groups, x => x.MotherName),
            ["NASCIMENTO"] = ComputeField(groups, x => x.BirthDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        };
    }

    private static SyntheticEmpiricalM ComputeField(
        IEnumerable<IGrouping<string, SyntheticObservation>> groups,
        Func<SyntheticObservation, string?> selector)
    {
        long eligible = 0;
        long exact = 0;
        var weightedDenominator = 0.0;
        var weightedNumerator = 0.0;

        foreach (var group in groups)
        {
            var rows = group.ToArray();
            for (var i = 0; i < rows.Length; i++)
            {
                for (var j = i + 1; j < rows.Length; j++)
                {
                    var left = selector(rows[i]);
                    var right = selector(rows[j]);
                    if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
                        continue;

                    eligible++;
                    var weight = (rows[i].EvaluationWeight + rows[j].EvaluationWeight) / 2.0;
                    weightedDenominator += weight;
                    if (string.Equals(left, right, StringComparison.Ordinal))
                    {
                        exact++;
                        weightedNumerator += weight;
                    }
                }
            }
        }

        return new SyntheticEmpiricalM(
            eligible,
            exact,
            eligible == 0
                ? null
                : Math.Round(exact / (double)eligible, 6, MidpointRounding.ToEven),
            weightedDenominator == 0
                ? null
                : Math.Round(weightedNumerator / weightedDenominator, 6, MidpointRounding.ToEven));
    }
}

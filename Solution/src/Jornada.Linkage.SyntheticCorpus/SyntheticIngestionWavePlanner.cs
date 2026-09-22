using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.SyntheticCorpus;

/// <summary>DEV-only incremental scenario. Truth IDs never enter delivery JSON.</summary>
public static class SyntheticIngestionWavePlanner
{
    public const string ScenarioVersion = "SYNTHETIC_INGESTION_WAVES_V1";

    public static IReadOnlyList<SyntheticIngestionWave> Plan(
        SyntheticCorpusGeneration source, SyntheticWaveScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(scenario);
        scenario.Validate();
        var truth = source.People.ToDictionary(x => x.BasePersonId, StringComparer.Ordinal);
        var groups = source.Observations
            .GroupBy(x => (x.BasePersonId, x.Gestor))
            .OrderBy(x => x.Key.BasePersonId, StringComparer.Ordinal)
            .ThenBy(x => x.Key.Gestor, StringComparer.Ordinal)
            .Select(x => new
            {
                x.Key,
                Versions = x.OrderBy(y => y.ObservationId, StringComparer.Ordinal).ToArray(),
                Arrival = Draw(source.Options.Seed, x.Key.BasePersonId, x.Key.Gestor,
                    0, "ARRIVAL", scenario.DelayedArrivalProbability) ? 1 : 0
            }).ToArray();
        var latest = new Dictionary<(string BasePersonId, string Gestor), SyntheticObservation>();
        var output = new List<SyntheticIngestionWave>();
        foreach (var group in groups)
            if (!truth.ContainsKey(group.Key.BasePersonId))
                throw new InvalidDataException("Observação sem pessoa verdadeira no gabarito.");

        for (var wave = 0; wave < scenario.WaveCount; wave++)
        {
            var snapshots = new List<SyntheticObservation>();
            var arrivals = 0; var updates = 0; var revealed = 0; var recovered = 0;
            foreach (var group in groups)
            {
                if (wave < group.Arrival) continue;
                var stage = wave - group.Arrival;
                var first = group.Versions[0];
                var id = first.ObservationId + "-W" + (wave + 1);
                if (stage == 0)
                {
                    var initial = Copy(first, id);
                    latest.Add(group.Key, initial);
                    snapshots.Add(initial);
                    arrivals++;
                    continue;
                }

                var prior = latest[group.Key];
                var current = Copy(prior, id);
                if (stage < group.Versions.Length)
                {
                    var observed = group.Versions[stage];
                    current.Name = observed.Name ?? current.Name;
                    current.MotherName = observed.MotherName ?? current.MotherName;
                    current.BirthDate = observed.BirthDate ?? current.BirthDate;
                    current.Cpf = observed.Cpf ?? current.Cpf;
                    current.Cns = observed.Cns ?? current.Cns;
                    current.Corruptions = observed.Corruptions;
                }

                var actual = truth[group.Key.BasePersonId];
                bool Select(string attribute, double rate) =>
                    Draw(source.Options.Seed, group.Key.BasePersonId, group.Key.Gestor, wave, attribute, rate);
                if (current.Cpf is null && actual.Cpf is not null && Select("CPF", scenario.CpfRevealProbability))
                    current.Cpf = actual.Cpf;
                if (current.Name != actual.Name && Select("NAME", scenario.NameCorrectionProbability))
                    current.Name = actual.Name;
                if (current.MotherName != actual.MotherName && Select("MOTHER", scenario.MotherCorrectionProbability))
                    current.MotherName = actual.MotherName;
                if (current.BirthDate is null && Select("BIRTH", scenario.BirthDateRecoveryProbability))
                    current.BirthDate = actual.BirthDate;

                if (Same(prior, current)) continue; // Gravação incremental: somente mudança factual.
                if (prior.Cpf is null && current.Cpf is not null) revealed++;
                if (prior.BirthDate is null && current.BirthDate is not null) recovered++;
                latest[group.Key] = current;
                snapshots.Add(current);
                updates++;
            }
            output.Add(new SyntheticIngestionWave(
                wave, arrivals, updates, revealed, recovered,
                new SyntheticCorpusGeneration(source.People, snapshots,
                    SyntheticCorpusGenerator.ComputeEmpiricalM(snapshots), source.Options)));
        }
        return output;
    }

    private static SyntheticObservation Copy(SyntheticObservation x, string id) => new()
    {
        ObservationId = id, BasePersonId = x.BasePersonId, Partition = x.Partition,
        Gestor = x.Gestor, Name = x.Name, MotherName = x.MotherName,
        BirthDate = x.BirthDate, Sex = x.Sex, Cpf = x.Cpf, Cns = x.Cns,
        EvaluationWeight = x.EvaluationWeight, Corruptions = x.Corruptions
    };

    private static bool Same(SyntheticObservation a, SyntheticObservation b)
        => a.Name == b.Name && a.MotherName == b.MotherName
           && a.BirthDate == b.BirthDate && a.Cpf == b.Cpf && a.Cns == b.Cns;

    private static bool Draw(ulong seed, string person, string gestor, int wave, string operation, double rate)
    {
        if (rate <= 0) return false;
        if (rate >= 1) return true;
        var value = string.Join("|", ScenarioVersion, seed, person, gestor, wave, operation);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(0, 8))
            / (double)ulong.MaxValue < rate;
    }
}

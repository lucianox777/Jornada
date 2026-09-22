namespace Jornada.Linkage.SyntheticCorpus;

public sealed record SyntheticWaveScenario(int WaveCount = 3, double DelayedArrivalProbability = .25, double CpfRevealProbability = .75, double NameCorrectionProbability = .35, double MotherCorrectionProbability = .35, double BirthDateRecoveryProbability = .70)
{
    public void Validate()
    {
        if (WaveCount is < 2 or > 12) throw new ArgumentOutOfRangeException(nameof(WaveCount));
        foreach (var rate in new[] { DelayedArrivalProbability, CpfRevealProbability, NameCorrectionProbability, MotherCorrectionProbability, BirthDateRecoveryProbability })
            if (!double.IsFinite(rate) || rate < 0 || rate > 1) throw new ArgumentOutOfRangeException(nameof(rate));
    }
}

public sealed record SyntheticIngestionWave(int WaveNumber, int NewSourceCount, int UpdatedSourceCount, int CpfRevealedCount, int BirthDateRecoveredCount, SyntheticCorpusGeneration Generation);

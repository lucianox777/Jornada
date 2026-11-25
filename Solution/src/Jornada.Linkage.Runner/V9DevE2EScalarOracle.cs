using System.Data;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// First-FULL DEV oracle for the pinned winner. The shared classifier supplies
/// the frozen nominal event and the name component contract supplies its term;
/// the scalar odds, TF, ranking and ledger comparison are independent of FS,
/// RankPreview, Resolve, b1 and r1. No model/identity write is possible here.
/// </summary>
public static class V9DevE2EScalarOracle
{
    public sealed record Pair(decimal Posterior, decimal LogOdds);
    public sealed record Rank(long Id, Guid? Best, decimal BestScore,
        Guid? Second, decimal? SecondScore, decimal? Margin, int ScoreableRecords,
        int PeopleWithoutScoreableEvidence);
    public sealed record LedgerReceipt(int Observations, long ScoreableRecords,
        long PeopleWithoutScoreableEvidence, int RankRowsEqual,
        bool IndependentRankVerified, bool IndependentR1DecisionVerified = false);

    public static Pair EvaluateOdds(double odds)
    {
        if (!double.IsFinite(odds) || odds <= 0d)
            throw new InvalidDataException("RUNNER_V9_E2E_SCALAR_ODDS_INVALID");
        var log = Math.Log(odds);
        // Preserve the frozen publication precision and posterior saturation.
        var boundedOdds = Math.Exp(Math.Clamp(log, -40d, 40d));
        return new(Math.Round((decimal)(boundedOdds / (1d + boundedOdds)), 8, MidpointRounding.AwayFromZero),
            Math.Round((decimal)log, 8, MidpointRounding.AwayFromZero));
    }

    public static Pair? Score(V9FittedSnapshotPayload p, IdentityObservation a, IdentityObservation b)
    {
        // Narrow oracle: winner #38 decision-evidence, joint cells, single
        // birth event, bilateral TF4 and explicit candidate abstention.
        if (p.Parameters.GetValueOrDefault(LinkageParameterCatalog.DecisionEvidenceScoring) != 1m
            || p.Parameters.GetValueOrDefault(LinkageParameterCatalog.BirthSingleEvidenceScoring) != 1m
            || p.Parameters.GetValueOrDefault(LinkageParameterCatalog.NeutralMissingEvidenceScoring) != 1m
            || p.SharedSurnameMode != V9SharedSurnameModeV9.Bilateral
            || p.UnpublishedTermPolicy != V9TfUnpublishedTermPolicyV9.AbstainCandidate
            || p.UnitTfCalibrations is not { Count: 4 })
            throw new InvalidDataException("RUNNER_V9_E2E_SCALAR_UNSUPPORTED_WINNER");
        var person = CompositeNominalComparisonV9.Classify(a.NomeCompleto, b.NomeCompleto);
        var mother = CompositeNominalComparisonV9.Classify(a.NomeMae, b.NomeMae);
        var cell = p.JointCells.Single(x => x.Person == person && x.Mother == mother);
        var prior = Math.Clamp((double)p.Parameters[LinkageParameterCatalog.PriorMatchProbability],
            .0000001d, .9999999d);
        // SCORING_DECISION_EVIDENCE_V6 selects the fixed model prior even
        // when a complete record-count argument is supplied to the kernel.
        // Do not infer an alternative prior from the number of people.
        var odds = prior / (1d - prior) * ((double)cell.M / (double)cell.U);
        if (a.DataNascimento is { } left && b.DataNascimento is { } right)
        {
            var suffix = left == right ? "EXACT" : "DIFF";
            var m = Math.Clamp((double)p.Parameters["M_DATA_NASCIMENTO_" + suffix], 1e-9, 1d - 1e-9);
            var u = Math.Clamp((double)p.Parameters["U_DATA_NASCIMENTO_" + suffix], 1e-9, 1d - 1e-9);
            odds *= m / u;
        }
        var personTf = Factor(CompositeNominalTfSideV9.Person, person, a.NomeCompleto, b.NomeCompleto);
        var motherTf = Factor(CompositeNominalTfSideV9.Mother, mother, a.NomeMae, b.NomeMae);
        return personTf is null || motherTf is null ? null : EvaluateOdds(odds * personTf.Value * motherTf.Value);

        double? Factor(CompositeNominalTfSideV9 side, CompositeNominalLevelV9 level, string? leftName, string? rightName)
        {
            NominalFrequencyUnitV9? unit = level switch
            {
                CompositeNominalLevelV9.ExactFirst or CompositeNominalLevelV9.ExactFull => NominalFrequencyUnitV9.FirstName,
                CompositeNominalLevelV9.ExactLast => NominalFrequencyUnitV9.LastContentSurname,
                _ => null
            };
            if (unit is null) return 1d;
            var fit = p.UnitTfCalibrations.Single(x => x.Side == side && x.Unit == unit);
            if (fit.State != V9SnapshotTfFitState.Active)
                throw new InvalidDataException("RUNNER_V9_E2E_SCALAR_UNSUPPORTED_TF_STATE");
            var tf = fit.Calibration ?? throw new InvalidDataException("RUNNER_V9_E2E_SCALAR_TF_MISSING");
            var term = Token(leftName, unit.Value);
            if (term is null || term != Token(rightName, unit.Value))
                throw new InvalidDataException("RUNNER_V9_E2E_SCALAR_TF_TERM_MISMATCH");
            if (!tf.Frequencies.TryGetValue(term, out var frequency) || frequency <= 0m || frequency > 1m)
                return null;
            var reference = tf.NominalUReference ?? throw new InvalidDataException("RUNNER_V9_E2E_SCALAR_U_MISSING");
            var u = (reference.MatchingNominalPairs + reference.SmoothingAlpha) /
                (reference.UnmatchedCandidatePairs + 2m * reference.SmoothingAlpha);
            if (u != reference.Probability)
                throw new InvalidDataException("RUNNER_V9_E2E_SCALAR_U_MISMATCH");
            var factor = Math.Pow((double)(u / Math.Max(frequency, tf.MinimumFrequency)),
                (double)(tf.Weight ?? p.TfWeight));
            return tf.Mode == CompositeNominalTfAdjustmentModeV9.AsymmetricPenaltyOnly
                ? Math.Min(1d, factor) : factor;
        }
    }

    private static string? Token(string? name, NominalFrequencyUnitV9 unit)
    {
        var components = BrazilianNameComponents.Project(name);
        if (components is null) return null;
        if (unit == NominalFrequencyUnitV9.LastContentSurname) return components.LastContentSurname;
        var tokens = components.NormalizedFull.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var index = components.TitlePrefix is null ? 0 : 1;
        if (index >= tokens.Length) return null;
        var term = tokens[index].Trim('.', ',');
        return term.Length == 0 ? null : term;
    }

    public static Rank ComputeRank(V9FittedSnapshotPayload winner,
        V9DevE2EFirstFullMembershipOracle.Source source, IReadOnlyList<Guid> people,
        IReadOnlyDictionary<Guid, List<V9DevE2EFirstFullMembershipOracle.Source>> records)
    {
        var rankedPeople = new List<(Guid Person, Pair Score)>();
        var scoreable = 0;
        foreach (var person in people)
        {
            var perRecord = records[person].Select(x =>
                (x.Id, Score: Score(winner, source.Observation, x.Observation)))
                .Where(x => x.Score is not null).OrderByDescending(x => x.Score!.LogOdds)
                .ThenBy(x => x.Id).ToArray();
            scoreable += perRecord.Length;
            if (perRecord.Length > 0) rankedPeople.Add((person, perRecord[0].Score!));
        }
        var ranked = rankedPeople.OrderByDescending(x => x.Score.LogOdds).ThenBy(x => x.Person).ToArray();
        return ranked.Length == 0
            ? new(source.Id,null,0m,null,null,null,scoreable,people.Count)
            : new(source.Id,ranked[0].Person,ranked[0].Score.Posterior,
                ranked.Length > 1 ? ranked[1].Person : null,
                ranked.Length > 1 ? ranked[1].Score.Posterior : null,
                ranked.Length > 1 ? ranked[0].Score.LogOdds - ranked[1].Score.LogOdds : null,
                scoreable,people.Count-ranked.Length);
    }

    // r1 may veto a decision but never changes its raw ranking/score.
    // Compare all persisted rows, not samples, counts or a replay of RankPreview.
    public static async Task<LedgerReceipt> VerifyLedgerAsync(SqlConnection connection, Guid runId,
        IReadOnlyList<Rank> expected, CancellationToken ct)
    {
        var remaining = expected.ToDictionary(x => x.Id);
        var count = 0;
        await using var command = new SqlCommand("""
            SELECT pessoa_observacao_id,melhor_candidato_uuid,score_melhor,
                   segundo_candidato_uuid,score_segundo,margem
            FROM identidade.linkage_resultado WHERE linkage_run_id=@run
            ORDER BY pessoa_observacao_id;
            """, connection);
        command.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = runId;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (!remaining.Remove(reader.GetInt64(0), out var row))
                throw new InvalidDataException("RUNNER_V9_E2E_SCALAR_LEDGER_CENSUS_MISMATCH");
            var best = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1);
            var second = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3);
            var secondScore = reader.IsDBNull(4) ? (decimal?)null : reader.GetDecimal(4);
            var margin = reader.IsDBNull(5) ? (decimal?)null : reader.GetDecimal(5);
            if (best != row.Best || reader.GetDecimal(2) != row.BestScore ||
                second != row.Second || secondScore != row.SecondScore || margin != row.Margin)
                throw new InvalidDataException("RUNNER_V9_E2E_SCALAR_LEDGER_RANK_DIVERGENCE");
            count++;
        }
        if (remaining.Count != 0 || count != expected.Count)
            throw new InvalidDataException("RUNNER_V9_E2E_SCALAR_LEDGER_CENSUS_MISMATCH");
        return new(count,expected.Sum(x => (long)x.ScoreableRecords),
            expected.Sum(x => (long)x.PeopleWithoutScoreableEvidence),count,true);
    }
}

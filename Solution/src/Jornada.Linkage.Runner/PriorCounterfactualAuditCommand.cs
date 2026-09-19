using System.Data;
using System.Globalization;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jornada.Linkage.Runner;

internal static class PriorCounterfactualAuditCommand
{
    internal const string RunOption = "--prior-counterfactual-run";
    internal const string OutputOption = "--prior-counterfactual-output";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static bool IsRequested(string[] args) =>
        args.Any(static arg =>
            arg.Equals(RunOption, StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith(RunOption + "=", StringComparison.OrdinalIgnoreCase));

    internal static async Task ExecuteAsync(
        string[] args,
        IConfiguration configuration,
        IOperationalSqlAdapter operationalSql,
        CancellationToken ct)
    {
        var rawRunId = ReadRequiredOption(args, RunOption);
        if (!Guid.TryParse(rawRunId, out var runId))
            throw new ArgumentException($"{RunOption} deve ser UUID válido.");

        var outputPath = Path.GetFullPath(ReadRequiredOption(args, OutputOption));
        var linkage = new SqlProbabilisticIdentityLinkage(
            configuration,
            operationalSql,
            NullLogger<SqlProbabilisticIdentityLinkage>.Instance);
        var activeModel = await linkage.GetActiveModelAsync(ct);
        var frozenModel = await linkage.GetModelForDiagnosticsAsync(activeModel.ModelId, ct);
        if (!LinkageParameterCatalog.UsesDecisionEvidence(frozenModel.AlgorithmVersion))
            throw new InvalidOperationException(
                "Contrafactual congelado de prior exige modelo DECISION_EVIDENCE, pois contratos legados podem usar prior condicionado ao tamanho do bloco.");

        var counterfactualPrior = await ReadParameterAsync(
            operationalSql,
            configuration,
            activeModel.ModelId,
            "DIAG_CANDIDATE_PRIOR_MATCH_PROBABILITY",
            ct);
        var priorSourceMeanCandidateCount = await ReadParameterAsync(
            operationalSql,
            configuration,
            activeModel.ModelId,
            "DIAG_CANDIDATE_PRIOR_MEAN_CANDIDATES_PER_OBSERVATION",
            ct);
        var activePrior = await ReadParameterAsync(
            operationalSql,
            configuration,
            activeModel.ModelId,
            LinkageParameterCatalog.PriorMatchProbability,
            ct);

        var rows = await ReadValidationRowsAsync(
            operationalSql,
            configuration,
            runId,
            activeModel.ModelId,
            ct);
        if (rows.Count == 0)
            throw new InvalidOperationException($"Run {runId} não contém linhas SCALE-VAL do modelo ativo.");

        var audits = new List<AuditRow>(rows.Count);
        foreach (var row in rows)
        {
            // O run publicado já pode ter criado novas REFERENCIA/blocking_chave.
            // O replay autoritativo usa o top-2 e os scores persistidos no próprio run;
            // a reexecução contra o corpus corrente fica separada como diagnóstico de deriva.
            var currentCorpusAudit = await linkage.DiagnosePriorCounterfactualAsync(
                row.ObservationId,
                activeModel.ModelId,
                counterfactualPrior,
                ct);
            var frozen = RecalculateFrozen(
                frozenModel,
                row,
                activePrior,
                counterfactualPrior);
            audits.Add(new AuditRow(
                row,
                currentCorpusAudit,
                frozen.ActiveDecision,
                frozen.CounterfactualDecision));
        }

        var replayMismatchCount = audits.Count(row =>
            row.Source.PersistedStatus != row.FrozenActiveDecision.Status ||
            row.Source.PersistedResolvedUuid != row.FrozenActiveDecision.PessoaUuidResolvido);
        var currentCorpusDecisionMismatchCount = audits.Count(row =>
            row.Source.PersistedStatus != row.CurrentCorpusAudit.ActiveDecision.Status ||
            row.Source.PersistedResolvedUuid != row.CurrentCorpusAudit.ActiveDecision.PessoaUuidResolvido);
        var currentCorpusTopCandidateMismatchCount = audits.Count(row =>
            row.Source.PersistedBestCandidateUuid != row.CurrentCorpusAudit.ActiveDecision.MelhorCandidatoUuid ||
            row.Source.PersistedSecondCandidateUuid != row.CurrentCorpusAudit.ActiveDecision.SegundoCandidatoUuid);
        var rankingTopChanged = audits.Count(static row => row.CurrentCorpusAudit.RankingTopChanged);
        if (rankingTopChanged != 0)
            throw new InvalidOperationException(
                $"Contrafactual de prior alterou o candidato top em {rankingTopChanged} observações; isso viola a invariável aditiva do prior.");

        var positive = audits.Where(static row => row.Source.Kind == "POS").ToArray();
        var negative = audits.Where(static row => row.Source.Kind == "NEG").ToArray();
        if (positive.Length == 0 || negative.Length == 0)
            throw new InvalidOperationException(
                $"Run {runId} precisa conter positivos e negativos. POS={positive.Length}; NEG={negative.Length}.");

        var activePositive = SummarizePositive(positive, counterfactual: false);
        var counterfactualPositive = SummarizePositive(positive, counterfactual: true);
        var activeNegative = SummarizeNegative(negative, counterfactual: false);
        var counterfactualNegative = SummarizeNegative(negative, counterfactual: true);
        var candidateCounts = audits.Select(static row => row.CurrentCorpusAudit.CandidateCount).Order().ToArray();
        var validationMeanCandidateCount = audits.Average(static row => (decimal)row.CurrentCorpusAudit.CandidateCount);

        var transitions = audits
            .GroupBy(row =>
                $"{Decision(row, false).Status}->{Decision(row, true).Status}",
                StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        var changedRows = audits
            .Where(static row =>
                row.FrozenActiveDecision.Status != row.FrozenCounterfactualDecision.Status ||
                row.FrozenActiveDecision.PessoaUuidResolvido != row.FrozenCounterfactualDecision.PessoaUuidResolvido ||
                row.FrozenActiveDecision.Motivo != row.FrozenCounterfactualDecision.Motivo)
            .Select(row => new
            {
                kind = row.Source.Kind,
                scenario = row.Source.Scenario,
                observationId = row.Source.ObservationId,
                activeStatus = row.FrozenActiveDecision.Status.ToString(),
                counterfactualStatus = row.FrozenCounterfactualDecision.Status.ToString(),
                activeBestPosterior = row.FrozenActiveDecision.MelhorScore,
                counterfactualBestPosterior = row.FrozenCounterfactualDecision.MelhorScore,
                activeSecondPosterior = row.FrozenActiveDecision.SegundoScore,
                counterfactualSecondPosterior = row.FrozenCounterfactualDecision.SegundoScore,
                activeMarginLogOdds = row.FrozenActiveDecision.Margem,
                counterfactualMarginLogOdds = row.FrozenCounterfactualDecision.Margem,
                activeReason = row.FrozenActiveDecision.Motivo,
                counterfactualReason = row.FrozenCounterfactualDecision.Motivo,
                bestCandidateUuid = row.Source.PersistedBestCandidateUuid
            })
            .ToArray();

        var report = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            purpose = "DEV_READ_ONLY_CANDIDATE_PAIR_PRIOR_COUNTERFACTUAL",
            runId,
            model = new
            {
                activeModel.ModelId,
                activeModel.Version,
                activeModel.AlgorithmVersion,
                activeModel.Threshold,
                activeModel.ConflictMargin
            },
            prior = new
            {
                active = activePrior,
                counterfactual = counterfactualPrior,
                deltaLogOdds = DeltaLogOdds(activePrior, counterfactualPrior),
                source = "DIAG_CANDIDATE_PRIOR_MATCH_PROBABILITY",
                changesPersistedModel = false,
                changesScoringParametersOtherThanPrior = false
            },
            replay = new
            {
                sampleSize = audits.Count,
                source = "identidade.linkage_resultado:top2+scores+margin",
                persistedDecisionMismatchCount = replayMismatchCount,
                rankingTopChangedCount = rankingTopChanged,
                rankingInvariantPreserved = rankingTopChanged == 0,
                currentCorpusDecisionMismatchCount,
                currentCorpusTopCandidateMismatchCount
            },
            candidateFanout = new
            {
                measurement = "POST_PUBLICATION_CURRENT_CORPUS_DIAGNOSTIC",
                priorCpfLabeledMeanCandidateCount = priorSourceMeanCandidateCount,
                validationNoCpfMeanCandidateCount = decimal.Round(validationMeanCandidateCount, 6),
                validationNoCpfP95CandidateCount = Percentile(candidateCounts, 0.95),
                validationNoCpfMaxCandidateCount = candidateCounts.Length == 0 ? 0 : candidateCounts[^1],
                meanRatioValidationToCpfLabeled =
                    priorSourceMeanCandidateCount == 0m
                        ? (decimal?)null
                        : decimal.Round(validationMeanCandidateCount / priorSourceMeanCandidateCount, 6)
            },
            active = new
            {
                positive = activePositive,
                negative = activeNegative
            },
            counterfactual = new
            {
                positive = counterfactualPositive,
                negative = counterfactualNegative
            },
            delta = new
            {
                positiveCorrectResolved =
                    counterfactualPositive.CorrectResolved - activePositive.CorrectResolved,
                positiveWrongResolved =
                    counterfactualPositive.WrongResolved - activePositive.WrongResolved,
                positiveConflicts =
                    counterfactualPositive.Conflicts - activePositive.Conflicts,
                positiveUnresolved =
                    counterfactualPositive.Unresolved - activePositive.Unresolved,
                negativeFalseResolved =
                    counterfactualNegative.FalseResolved - activeNegative.FalseResolved,
                negativeConflicts =
                    counterfactualNegative.Conflicts - activeNegative.Conflicts,
                negativeUnresolved =
                    counterfactualNegative.Unresolved - activeNegative.Unresolved,
                decisionChangedRows = changedRows.Length
            },
            transitions,
            changedRows,
            transportability = new
            {
                established = false,
                evidence = "Candidate fan-out is compared between the CPF-labeled prior sample and the no-CPF synthetic validation fixture.",
                limitation = "The no-CPF sample is synthetic/adversarial and therefore cannot establish municipal transportability of a prior estimated from CPF-labeled observations."
            },
            interpretation = new
            {
                ranking = "Changing only a global prior adds the same log-odds constant to every frozen candidate, so candidate ordering and log-odds margins must remain unchanged.",
                decisions = "Replay and threshold crossings use the top-2, rounded posterior scores and log-odds margin persisted by the run, then reapply the shared runtime decision policy. This prevents publication-created references from rewriting the historical candidate set.",
                currentCorpus = "Candidate generation is also reexecuted after publication only to expose corpus drift; those mismatches do not replace the frozen replay.",
                scope = "DEV synthetic read-only counterfactual. It does not alter the active model, linkage_run, identity links or Gold."
            }
        };

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            outputPath,
            JsonSerializer.Serialize(report, JsonOptions),
            ct);

        Console.WriteLine(
            $"Contrafactual read-only de prior: ativo={activePrior.ToString(CultureInfo.InvariantCulture)}; " +
            $"alternativo={counterfactualPrior.ToString(CultureInfo.InvariantCulture)}; " +
            $"mudanças={changedRows.Length}; ranking_top_changed={rankingTopChanged}; " +
            $"POS corretos {activePositive.CorrectResolved}->{counterfactualPositive.CorrectResolved}; " +
            $"NEG falsos {activeNegative.FalseResolved}->{counterfactualNegative.FalseResolved}");
        Console.WriteLine($"Auditoria gravada em {outputPath}");
    }

    private static DecisionSummary SummarizePositive(IReadOnlyList<AuditRow> rows, bool counterfactual)
    {
        var decisions = rows.Select(row => (Row: row, Decision: Decision(row, counterfactual))).ToArray();
        var resolved = decisions.Where(static item => item.Decision.Status == ResolutionStatus.RESOLVIDO).ToArray();
        var correct = resolved.Count(item =>
            item.Row.Source.TruthUuid is { } truth &&
            item.Decision.PessoaUuidResolvido == truth);
        var wrong = resolved.Length - correct;
        return new DecisionSummary(
            rows.Count,
            resolved.Length,
            correct,
            wrong,
            decisions.Count(static item => item.Decision.Status == ResolutionStatus.CONFLITO),
            decisions.Count(static item => item.Decision.Status == ResolutionStatus.NAO_RESOLVIDO));
    }

    private static NegativeDecisionSummary SummarizeNegative(IReadOnlyList<AuditRow> rows, bool counterfactual)
    {
        var decisions = rows.Select(row => Decision(row, counterfactual)).ToArray();
        return new NegativeDecisionSummary(
            rows.Count,
            decisions.Count(static decision => decision.Status == ResolutionStatus.RESOLVIDO),
            decisions.Count(static decision => decision.Status == ResolutionStatus.CONFLITO),
            decisions.Count(static decision => decision.Status == ResolutionStatus.NAO_RESOLVIDO));
    }

    private static ProbabilisticLinkageDecision Decision(AuditRow row, bool counterfactual) =>
        counterfactual ? row.FrozenCounterfactualDecision : row.FrozenActiveDecision;

    private static FrozenPriorDecisions RecalculateFrozen(
        LinkageModel activeModel,
        ValidationRow row,
        decimal activePrior,
        decimal counterfactualPrior)
    {
        var delta = DeltaLogOdds(activePrior, counterfactualPrior);
        var activeRanking = BuildFrozenRanking(row, priorDeltaLogOdds: 0m);
        var counterfactualRanking = BuildFrozenRanking(row, delta);

        var counterfactualParameters = new Dictionary<string, decimal>(
            activeModel.Parameters,
            StringComparer.OrdinalIgnoreCase)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = counterfactualPrior
        };
        var counterfactualModel = LinkageModelPolicy.Create(
            activeModel.ModelId,
            activeModel.Version,
            activeModel.AlgorithmVersion,
            counterfactualParameters);

        var noCandidateReason = string.IsNullOrWhiteSpace(row.PersistedReason)
            ? "SEM_CANDIDATO_NO_RULESET_BLOCKING"
            : row.PersistedReason!;

        return new FrozenPriorDecisions(
            ProbabilisticLinkageDecisions.ResolveRanked(activeModel, activeRanking, noCandidateReason),
            ProbabilisticLinkageDecisions.ResolveRanked(counterfactualModel, counterfactualRanking, noCandidateReason));
    }

    private static IReadOnlyList<CandidateScore> BuildFrozenRanking(
        ValidationRow row,
        decimal priorDeltaLogOdds)
    {
        if (row.PersistedBestCandidateUuid is not { } bestUuid)
            return Array.Empty<CandidateScore>();

        if (row.PersistedSecondCandidateUuid is not null && row.PersistedSecondScore is null)
            throw new InvalidOperationException(
                $"Observação {row.ObservationId} possui segundo candidato sem score persistido.");
        if (row.PersistedSecondCandidateUuid is not null && row.PersistedMargin is null)
            throw new InvalidOperationException(
                $"Observação {row.ObservationId} possui segundo candidato sem margem persistida.");

        var bestLogOdds = priorDeltaLogOdds;
        var best = new CandidateScore(
            bestUuid,
            ShiftPosterior(row.PersistedBestScore, priorDeltaLogOdds),
            bestLogOdds);

        if (row.PersistedSecondCandidateUuid is not { } secondUuid)
            return new[] { best };

        var second = new CandidateScore(
            secondUuid,
            ShiftPosterior(row.PersistedSecondScore!.Value, priorDeltaLogOdds),
            bestLogOdds - row.PersistedMargin!.Value);
        return new[] { best, second };
    }

    private static decimal ShiftPosterior(decimal persistedPosterior, decimal deltaLogOdds)
    {
        if (deltaLogOdds == 0m)
            return persistedPosterior;

        var p = Math.Clamp(
            Convert.ToDouble(persistedPosterior, CultureInfo.InvariantCulture),
            0.0000001d,
            0.9999999d);
        var shiftedLogOdds =
            Math.Log(p / (1d - p)) +
            Convert.ToDouble(deltaLogOdds, CultureInfo.InvariantCulture);
        var posterior = 1d / (1d + Math.Exp(-Math.Clamp(shiftedLogOdds, -40d, 40d)));
        return Math.Round(
            Convert.ToDecimal(posterior, CultureInfo.InvariantCulture),
            8,
            MidpointRounding.AwayFromZero);
    }

    private static decimal DeltaLogOdds(decimal activePrior, decimal counterfactualPrior)
    {
        static double Logit(decimal probability)
        {
            var value = Math.Clamp(
                Convert.ToDouble(probability, CultureInfo.InvariantCulture),
                0.0000001d,
                0.9999999d);
            return Math.Log(value / (1d - value));
        }

        return Convert.ToDecimal(
            Logit(counterfactualPrior) - Logit(activePrior),
            CultureInfo.InvariantCulture);
    }

    private static async Task<decimal> ReadParameterAsync(
        IOperationalSqlAdapter operationalSql,
        IConfiguration configuration,
        Guid modelId,
        string name,
        CancellationToken ct)
    {
        await using var connection = await operationalSql.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = Math.Max(
            1,
            configuration.GetValue("ProbabilisticLinkage:CommandTimeoutSeconds", 900));
        command.CommandText = """
            SELECT valor
            FROM identidade.parametro_linkage
            WHERE modelo_id=@modelo_id AND nome=@nome;
            """;
        AddParameter(command, "@modelo_id", DbType.Guid, modelId);
        AddParameter(command, "@nome", DbType.String, name);
        var raw = await command.ExecuteScalarAsync(ct)
            ?? throw new InvalidOperationException(
                $"Parâmetro {name} ausente no modelo {modelId}.");
        return Convert.ToDecimal(raw, CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<ValidationRow>> ReadValidationRowsAsync(
        IOperationalSqlAdapter operationalSql,
        IConfiguration configuration,
        Guid runId,
        Guid activeModelId,
        CancellationToken ct)
    {
        await using var connection = await operationalSql.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = Math.Max(
            1,
            configuration.GetValue("ProbabilisticLinkage:CommandTimeoutSeconds", 900));
        command.CommandText = """
            SELECT
                r.pessoa_observacao_id,
                po.codigo_pessoa_origem,
                r.status,
                r.pessoa_uuid_resolvido,
                r.melhor_candidato_uuid,
                r.score_melhor,
                r.segundo_candidato_uuid,
                r.score_segundo,
                r.margem,
                r.motivo,
                CASE
                  WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-%-POS-%' THEN N'POS'
                  WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-%-NEG-%' THEN N'NEG'
                END,
                CASE
                  WHEN po.codigo_pessoa_origem LIKE N'%POS-EXACT-%' THEN N'EXACT'
                  WHEN po.codigo_pessoa_origem LIKE N'%POS-NAME_ABBREV-%' THEN N'NAME_ABBREV'
                  WHEN po.codigo_pessoa_origem LIKE N'%POS-MOTHER_ABBREV-%' THEN N'MOTHER_ABBREV'
                  WHEN po.codigo_pessoa_origem LIKE N'%POS-BIRTH_SHIFT-%' THEN N'BIRTH_SHIFT'
                  WHEN po.codigo_pessoa_origem LIKE N'%POS-COMBINED-%' THEN N'COMBINED'
                  WHEN po.codigo_pessoa_origem LIKE N'%NEG-EASY-%' THEN N'EASY'
                  WHEN po.codigo_pessoa_origem LIKE N'%NEG-NAME_COLLISION-%' THEN N'NAME_COLLISION'
                  WHEN po.codigo_pessoa_origem LIKE N'%NEG-MOTHER_COLLISION-%' THEN N'MOTHER_COLLISION'
                  WHEN po.codigo_pessoa_origem LIKE N'%NEG-HARD_HOMONYM-%' THEN N'HARD_HOMONYM'
                  ELSE N'UNKNOWN'
                END,
                CASE
                  WHEN po.codigo_pessoa_origem LIKE N'SCALE-VAL-%-POS-%'
                  THEN vc.pessoa_uuid
                  ELSE NULL
                END
            FROM identidade.linkage_resultado r
            JOIN identidade.linkage_run lr
              ON lr.linkage_run_id=r.linkage_run_id
            JOIN silver.pessoa_observacao po
              ON po.pessoa_observacao_id=r.pessoa_observacao_id
            OUTER APPLY (
                SELECT TOP(1) vc0.pessoa_uuid
                FROM silver.pessoa_observacao tpo
                JOIN identidade.v_vinculo_corrente vc0
                  ON vc0.pessoa_observacao_id=tpo.pessoa_observacao_id
                 AND vc0.status=N'RESOLVIDO'
                 AND vc0.pessoa_uuid IS NOT NULL
                WHERE tpo.codigo_pessoa_origem=
                    CONCAT(
                      N'SCALE-SEHAB-',
                      RIGHT(
                        REPLICATE('0',10)+
                        CONVERT(varchar(10),TRY_CONVERT(int,RIGHT(po.codigo_pessoa_origem,6))),
                        10))
            ) vc
            WHERE r.linkage_run_id=@run_id
              AND lr.modelo_id=@modelo_id
              AND (
                po.codigo_pessoa_origem LIKE N'SCALE-VAL-%-POS-%'
                OR po.codigo_pessoa_origem LIKE N'SCALE-VAL-%-NEG-%'
              )
            ORDER BY r.pessoa_observacao_id;
            """;
        AddParameter(command, "@run_id", DbType.Guid, runId);
        AddParameter(command, "@modelo_id", DbType.Guid, activeModelId);

        var rows = new List<ValidationRow>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var kind = reader.GetString(10);
            var truth = reader.IsDBNull(12) ? (Guid?)null : reader.GetGuid(12);
            if (kind == "POS" && truth is null)
                throw new InvalidOperationException(
                    $"Observação positiva {reader.GetInt64(0)} sem truth UUID.");

            rows.Add(new ValidationRow(
                reader.GetInt64(0),
                reader.GetString(1),
                kind,
                reader.GetString(11),
                Enum.Parse<ResolutionStatus>(reader.GetString(2), ignoreCase: false),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                truth));
        }

        return rows;
    }

    private static void AddParameter(
        System.Data.Common.DbCommand command,
        string name,
        DbType type,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string ReadRequiredOption(string[] args, string option)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var raw = args[i];
            if (raw.StartsWith(option + "=", StringComparison.OrdinalIgnoreCase))
            {
                var value = raw[(option.Length + 1)..];
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            if (!raw.Equals(option, StringComparison.OrdinalIgnoreCase))
                continue;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return args[i + 1];
            break;
        }

        throw new ArgumentException($"{option} é obrigatório na auditoria contrafactual de prior.");
    }

    private static int Percentile(int[] sorted, double probability)
    {
        if (sorted.Length == 0)
            return 0;
        var index = (int)Math.Ceiling(probability * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private sealed record ValidationRow(
        long ObservationId,
        string SourceCode,
        string Kind,
        string Scenario,
        ResolutionStatus PersistedStatus,
        Guid? PersistedResolvedUuid,
        Guid? PersistedBestCandidateUuid,
        decimal PersistedBestScore,
        Guid? PersistedSecondCandidateUuid,
        decimal? PersistedSecondScore,
        decimal? PersistedMargin,
        string? PersistedReason,
        Guid? TruthUuid);

    private sealed record AuditRow(
        ValidationRow Source,
        ProbabilisticPriorCounterfactualAudit CurrentCorpusAudit,
        ProbabilisticLinkageDecision FrozenActiveDecision,
        ProbabilisticLinkageDecision FrozenCounterfactualDecision);

    private sealed record FrozenPriorDecisions(
        ProbabilisticLinkageDecision ActiveDecision,
        ProbabilisticLinkageDecision CounterfactualDecision);

    private sealed record DecisionSummary(
        int Total,
        int Resolved,
        int CorrectResolved,
        int WrongResolved,
        int Conflicts,
        int Unresolved);

    private sealed record NegativeDecisionSummary(
        int Total,
        int FalseResolved,
        int Conflicts,
        int Unresolved);
}

using System.Data;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Evaluation;
using Jornada.Linkage.Runner;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Conference;

internal static class GovernedImplementationConferenceCommand
{
    private static readonly JsonSerializerOptions HashJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static async Task<ConferenceExecutionSummary> ExecuteAsync(
        string connectionString,
        Guid modelId,
        ImplementationConferenceToleranceContract tolerance,
        int commandTimeoutSeconds,
        string? sourceRevision = null,
        CancellationToken cancellationToken = default)
    {
        if (modelId == Guid.Empty)
            throw new ArgumentException("model-id inválido.", nameof(modelId));
        if (commandTimeoutSeconds is < 1 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(commandTimeoutSeconds));

        if (!tolerance.TryGetFrozen(out var frozenTolerance, out var toleranceReason))
            throw new ConferencePreconditionException(toleranceReason);

        var operationalSql = new OperationalSqlAdapter(connectionString);
        await using var connection = await operationalSql.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            await SetSourceRevisionAsync(
                connection,
                transaction,
                sourceRevision ?? ResolveSourceRevision(),
                commandTimeoutSeconds,
                cancellationToken);

            var snapshotBefore = await CalculateSnapshotFingerprintAsync(
                connection,
                transaction,
                modelId,
                commandTimeoutSeconds,
                cancellationToken);

            var model = await LoadModelAsync(
                connection,
                transaction,
                modelId,
                commandTimeoutSeconds,
                cancellationToken);

            var scenarios = ImplementationConferenceCorpus.Build(model, tolerance);
            var evaluated = scenarios
                .Select(static scenario => new NamedConferenceReport(
                    scenario.Name,
                    scenario.Request,
                    IndependentImplementationConference.Evaluate(scenario.Request)))
                .ToArray();

            var aggregate = Aggregate(evaluated, frozenTolerance);

            var snapshotAfter = await CalculateSnapshotFingerprintAsync(
                connection,
                transaction,
                modelId,
                commandTimeoutSeconds,
                cancellationToken);
            if (!snapshotBefore.AsSpan().SequenceEqual(snapshotAfter))
                throw new InvalidOperationException(
                    "MODEL_SNAPSHOT_CHANGED_DURING_CONFERENCE");

            var requestSha256 = SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(
                    new ConferenceRequestEnvelope(
                        IndependentImplementationConference.MethodVersion,
                        IndependentImplementationConference.Scope,
                        model.ModelId,
                        model.Version,
                        model.AlgorithmVersion,
                        Convert.ToHexString(snapshotBefore).ToLowerInvariant(),
                        tolerance,
                        scenarios),
                    HashJson));

            var reportSha256 = SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(
                    new ConferenceReportEnvelope(
                        IndependentImplementationConference.MethodVersion,
                        IndependentImplementationConference.Scope,
                        model.ModelId,
                        model.Version,
                        model.AlgorithmVersion,
                        Convert.ToHexString(snapshotBefore).ToLowerInvariant(),
                        tolerance.Version,
                        aggregate,
                        evaluated.Select(static x => new NamedReport(x.Name, x.Report)).ToArray()),
                    HashJson));

            var evidenceId = await RegisterAsync(
                connection,
                transaction,
                model,
                tolerance,
                aggregate,
                requestSha256,
                reportSha256,
                commandTimeoutSeconds,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new ConferenceExecutionSummary(
                evidenceId,
                aggregate.Status,
                model.ModelId,
                model.Version,
                evaluated.Length,
                aggregate.CandidatesEvaluated,
                aggregate.MaxObservedPairLlrDifference,
                aggregate.MaxObservedLogOddsDifference,
                aggregate.SameFinalDecision,
                aggregate.SameTop1,
                aggregate.Spearman,
                aggregate.Reason,
                Convert.ToHexString(requestSha256).ToLowerInvariant(),
                Convert.ToHexString(reportSha256).ToLowerInvariant());
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<LinkageModel> LoadModelAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid modelId,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        int version;
        string status;
        string algorithm;

        await using (var command = new SqlCommand(
            """
            SELECT versao,status,algoritmo_versao
            FROM identidade.modelo_linkage WITH(HOLDLOCK)
            WHERE modelo_id=@modelo_id;
            """,
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        })
        {
            command.Parameters.Add("@modelo_id", SqlDbType.UniqueIdentifier).Value = modelId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ConferencePreconditionException("MODEL_NOT_FOUND");

            version = reader.GetInt32(0);
            status = reader.GetString(1);
            algorithm = reader.GetString(2);
        }

        if (!string.Equals(status, "RASCUNHO", StringComparison.Ordinal))
            throw new ConferencePreconditionException("MODEL_NOT_DRAFT");
        if (!LinkageParameterCatalog.UsesDecisionEvidence(algorithm))
            throw new ConferencePreconditionException("ALGORITHM_OUTSIDE_DECISION_EVIDENCE_SCOPE");

        var parameters = new SortedDictionary<string, decimal>(StringComparer.Ordinal);
        await using (var command = new SqlCommand(
            """
            SELECT nome,valor
            FROM identidade.parametro_linkage WITH(HOLDLOCK)
            WHERE modelo_id=@modelo_id
            ORDER BY nome;
            """,
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        })
        {
            command.Parameters.Add("@modelo_id", SqlDbType.UniqueIdentifier).Value = modelId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                parameters.Add(reader.GetString(0), reader.GetDecimal(1));
        }

        try
        {
            return LinkageModelPolicy.Create(modelId, version, algorithm, parameters);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConferencePreconditionException(
                "MODEL_CONTRACT_INVALID",
                ex);
        }
    }

    private static AggregateConferenceReport Aggregate(
        IReadOnlyList<NamedConferenceReport> reports,
        decimal frozenTolerance)
    {
        if (reports.Count == 0)
            throw new InvalidOperationException("CONFERENCE_SCENARIOS_EMPTY");

        var divergent = reports.FirstOrDefault(static x =>
            x.Report.Status == ImplementationConferenceStatus.DIVERGENTE);
        var notExecuted = reports.FirstOrDefault(static x =>
            x.Report.Status == ImplementationConferenceStatus.NAO_EXECUTADA);

        var status = divergent is not null
            ? ImplementationConferenceStatus.DIVERGENTE
            : notExecuted is not null
                ? ImplementationConferenceStatus.NAO_EXECUTADA
                : ImplementationConferenceStatus.CONFORME;

        var failed = divergent ?? notExecuted;
        var reason = failed is null || failed.Report.Reason is null
            ? null
            : Limit($"{failed.Name}:{failed.Report.Reason}", 120);

        var llrValues = reports
            .Where(static x => x.Report.MaxObservedPairLlrDifference is not null)
            .Select(static x => x.Report.MaxObservedPairLlrDifference!.Value)
            .ToArray();
        var logOddsValues = reports
            .Where(static x => x.Report.MaxObservedLogOddsDifference is not null)
            .Select(static x => x.Report.MaxObservedLogOddsDifference!.Value)
            .ToArray();
        var spearmanValues = reports
            .Where(static x => x.Report.SpearmanRankCorrelation is not null)
            .Select(static x => x.Report.SpearmanRankCorrelation!.Value)
            .ToArray();

        var aggregate = new AggregateConferenceReport(
            status,
            reports.Sum(static x => x.Request.Candidates.Count),
            llrValues.Length == 0 ? null : llrValues.Max(),
            logOddsValues.Length == 0 ? null : logOddsValues.Max(),
            reports.All(static x => x.Report.SameFinalDecision),
            reports.All(static x => x.Report.SameTop1),
            spearmanValues.Length == 0 ? null : spearmanValues.Min(),
            reason,
            "NOT_ASSESSED_ISSUE_31");

        if (status == ImplementationConferenceStatus.CONFORME
            && (aggregate.MaxObservedPairLlrDifference is null
                || aggregate.MaxObservedPairLlrDifference > frozenTolerance
                || !aggregate.SameFinalDecision))
            throw new InvalidOperationException(
                "AGGREGATE_CONFORME_VIOLATES_PRIMARY_GATES");

        return aggregate;
    }

    private static async Task<byte[]> CalculateSnapshotFingerprintAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid modelId,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            DECLARE @fingerprint BINARY(32);
            EXEC auditoria.sp_calcular_fingerprint_modelo_linkage
                @modelo_id=@modelo_id,
                @fingerprint=@fingerprint OUTPUT;
            SELECT @fingerprint;
            """,
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.Add("@modelo_id", SqlDbType.UniqueIdentifier).Value = modelId;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value as byte[]
            ?? throw new InvalidOperationException("MODEL_SNAPSHOT_FINGERPRINT_MISSING");
    }

    private static async Task<Guid> RegisterAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        LinkageModel model,
        ImplementationConferenceToleranceContract tolerance,
        AggregateConferenceReport aggregate,
        byte[] requestSha256,
        byte[] reportSha256,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (tolerance.MaxAbsolutePairLlrDifference is not { } maxAllowed)
            throw new InvalidOperationException("FROZEN_TOLERANCE_WITHOUT_VALUE");

        await using var command = new SqlCommand(
            """
            DECLARE @evidencia_id UNIQUEIDENTIFIER;
            EXEC auditoria.sp_registrar_conferencia_linkage
                @modelo_id=@modelo_id,
                @modelo_versao=@modelo_versao,
                @metodo_versao=@metodo_versao,
                @escopo=@escopo,
                @tolerancia_versao=@tolerancia_versao,
                @max_llr_par_permitido=@max_llr_par_permitido,
                @status=@status,
                @candidatos_avaliados=@candidatos_avaliados,
                @max_llr_par_observado=@max_llr_par_observado,
                @max_log_odds_observado=@max_log_odds_observado,
                @mesma_decisao_final=@mesma_decisao_final,
                @mesmo_top1=@mesmo_top1,
                @spearman=@spearman,
                @motivo=@motivo,
                @validacao_estatistica=N'NOT_ASSESSED_ISSUE_31',
                @request_sha256=@request_sha256,
                @report_sha256=@report_sha256,
                @evidencia_id=@evidencia_id OUTPUT;
            SELECT @evidencia_id;
            """,
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };

        command.Parameters.Add("@modelo_id", SqlDbType.UniqueIdentifier).Value = model.ModelId;
        command.Parameters.Add("@modelo_versao", SqlDbType.Int).Value = model.Version;
        command.Parameters.Add("@metodo_versao", SqlDbType.NVarChar, 120).Value =
            IndependentImplementationConference.MethodVersion;
        command.Parameters.Add("@escopo", SqlDbType.NVarChar, 220).Value =
            IndependentImplementationConference.Scope;
        command.Parameters.Add("@tolerancia_versao", SqlDbType.NVarChar, 120).Value =
            tolerance.Version;

        var allowed = command.Parameters.Add("@max_llr_par_permitido", SqlDbType.Decimal);
        allowed.Precision = 28;
        allowed.Scale = 16;
        allowed.Value = maxAllowed;

        command.Parameters.Add("@status", SqlDbType.NVarChar, 20).Value =
            aggregate.Status.ToString();
        command.Parameters.Add("@candidatos_avaliados", SqlDbType.Int).Value =
            aggregate.CandidatesEvaluated;

        AddNullableDecimal(
            command,
            "@max_llr_par_observado",
            aggregate.MaxObservedPairLlrDifference,
            28,
            16);
        AddNullableDecimal(
            command,
            "@max_log_odds_observado",
            aggregate.MaxObservedLogOddsDifference,
            28,
            16);

        command.Parameters.Add("@mesma_decisao_final", SqlDbType.Bit).Value =
            aggregate.SameFinalDecision;
        command.Parameters.Add("@mesmo_top1", SqlDbType.Bit).Value =
            aggregate.SameTop1;
        AddNullableDecimal(command, "@spearman", aggregate.Spearman, 18, 12);
        command.Parameters.Add("@motivo", SqlDbType.NVarChar, 120).Value =
            (object?)aggregate.Reason ?? DBNull.Value;
        command.Parameters.Add("@request_sha256", SqlDbType.Binary, 32).Value =
            requestSha256;
        command.Parameters.Add("@report_sha256", SqlDbType.Binary, 32).Value =
            reportSha256;

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid id
            ? id
            : throw new InvalidOperationException("CONFERENCE_EVIDENCE_ID_MISSING");
    }

    private static async Task SetSourceRevisionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string? sourceRevision,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceRevision))
            return;

        await using var command = new SqlCommand(
            """
            EXEC sys.sp_set_session_context
                @key=N'Jornada.SourceRevision',
                @value=@source_revision,
                @read_only=0;
            """,
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.Add("@source_revision", SqlDbType.NVarChar, 80).Value =
            Limit(sourceRevision, 80);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddNullableDecimal(
        SqlCommand command,
        string name,
        decimal? value,
        byte precision,
        byte scale)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = (object?)value ?? DBNull.Value;
    }

    private static string? ResolveSourceRevision()
    {
        var environment = Environment.GetEnvironmentVariable("GITHUB_SHA");
        if (!string.IsNullOrWhiteSpace(environment))
            return environment;

        return Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
    }

    private static string Limit(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed record ConferenceRequestEnvelope(
        string MethodVersion,
        string Scope,
        Guid ModelId,
        int ModelVersion,
        string AlgorithmVersion,
        string ModelSnapshotSha256,
        ImplementationConferenceToleranceContract Tolerance,
        IReadOnlyList<NamedConferenceRequest> Scenarios);

    private sealed record ConferenceReportEnvelope(
        string MethodVersion,
        string Scope,
        Guid ModelId,
        int ModelVersion,
        string AlgorithmVersion,
        string ModelSnapshotSha256,
        string ToleranceVersion,
        AggregateConferenceReport Aggregate,
        IReadOnlyList<NamedReport> Scenarios);
}

internal static class ImplementationConferenceCorpus
{
    private static readonly NameComparisonState?[] NameStates =
    [
        null,
        NameComparisonState.EXACT,
        NameComparisonState.HIGH,
        NameComparisonState.MEDIUM,
        NameComparisonState.LOW
    ];

    private static readonly BirthCase[] BirthCases =
    [
        new("MISSING_NEUTRAL", null, null),
        new(BirthDateSemanticEvidence.Exact,
            new DateOnly(1980, 5, 6), new DateOnly(1980, 5, 6)),
        new(BirthDateSemanticEvidence.DayMonthSwap,
            new DateOnly(1980, 5, 6), new DateOnly(1980, 6, 5)),
        new(BirthDateSemanticEvidence.CenturyShift,
            new DateOnly(1980, 5, 6), new DateOnly(1880, 5, 6)),
        new(BirthDateSemanticEvidence.OneDigitError,
            new DateOnly(1980, 5, 6), new DateOnly(1980, 5, 7)),
        new(BirthDateSemanticEvidence.TwoDigitError,
            new DateOnly(1980, 5, 6), new DateOnly(1980, 7, 8)),
        new(BirthDateSemanticEvidence.PartialComponentAgreement,
            new DateOnly(1980, 5, 6), new DateOnly(1979, 5, 6)),
        new(BirthDateSemanticEvidence.OtherDisagreement,
            new DateOnly(1980, 5, 6), new DateOnly(1991, 7, 8))
    ];

    internal static IReadOnlyList<NamedConferenceRequest> Build(
        LinkageModel model,
        ImplementationConferenceToleranceContract tolerance)
    {
        var scenarios = new List<NamedConferenceRequest>();

        var matrix = new List<CandidateVectorSpec>();
        foreach (var name in NameStates)
        foreach (var mother in NameStates)
        foreach (var birth in BirthCases)
            matrix.Add(new CandidateVectorSpec(name, mother, birth, false));

        scenarios.Add(BuildScenario("STATE_MATRIX", model, matrix, tolerance));
        scenarios.Add(BuildScenario(
            "STRONG_SINGLE_NO_GUARD",
            model,
            [Spec(NameComparisonState.EXACT, NameComparisonState.EXACT, BirthDateSemanticEvidence.Exact, false)],
            tolerance));
        scenarios.Add(BuildScenario(
            "STRONG_SINGLE_GUARD_INPUT",
            model,
            [Spec(NameComparisonState.EXACT, NameComparisonState.EXACT, BirthDateSemanticEvidence.Exact, true)],
            tolerance));
        scenarios.Add(BuildScenario(
            "WEAK_SINGLE",
            model,
            [Spec(NameComparisonState.LOW, NameComparisonState.LOW, BirthDateSemanticEvidence.OtherDisagreement, false)],
            tolerance));
        scenarios.Add(BuildScenario(
            "MISSING_SINGLE",
            model,
            [Spec(null, null, "MISSING_NEUTRAL", false)],
            tolerance));
        scenarios.Add(BuildScenario(
            "TIE_STRONG",
            model,
            [
                Spec(NameComparisonState.EXACT, NameComparisonState.EXACT, BirthDateSemanticEvidence.Exact, false),
                Spec(NameComparisonState.EXACT, NameComparisonState.EXACT, BirthDateSemanticEvidence.Exact, false)
            ],
            tolerance));
        scenarios.Add(BuildScenario(
            "STRONG_VS_WEAK",
            model,
            [
                Spec(NameComparisonState.EXACT, NameComparisonState.EXACT, BirthDateSemanticEvidence.Exact, false),
                Spec(NameComparisonState.LOW, NameComparisonState.LOW, BirthDateSemanticEvidence.OtherDisagreement, false)
            ],
            tolerance));

        return scenarios;
    }

    private static NamedConferenceRequest BuildScenario(
        string name,
        LinkageModel model,
        IReadOnlyList<CandidateVectorSpec> specs,
        ImplementationConferenceToleranceContract tolerance)
    {
        var canonical = specs.Select((spec, index) =>
        {
            var id = DeterministicCandidateId(name, index);
            var breakdown = FellegiSunterScoring.CalculateWithBreakdown(
                model.Parameters,
                spec.NameState,
                spec.MotherNameState,
                null,
                spec.Birth.Left,
                spec.Birth.Right);

            var birthContribution = breakdown.Contributions.Single(static x =>
                string.Equals(x.Evidence, "NASCIMENTO_SEMANTICO", StringComparison.Ordinal)
                || string.Equals(x.Evidence, "NASCIMENTO", StringComparison.Ordinal));
            if (!string.Equals(
                    birthContribution.State,
                    spec.Birth.ExpectedState,
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"BIRTH_CORPUS_STATE_MISMATCH:{spec.Birth.ExpectedState}:{birthContribution.State}");

            return new CanonicalCandidateVector(
                id,
                breakdown,
                spec.DemographicExactCollisionRisk);
        }).ToArray();

        var ranked = canonical
            .Select(static x => new CandidateScore(
                x.CandidateId,
                x.Breakdown.Score.Posterior,
                x.Breakdown.Score.LogOdds,
                x.DemographicExactCollisionRisk))
            .OrderByDescending(static x => x.LogOdds)
            .ThenBy(static x => x.PessoaUuid)
            .ToArray();

        var decision = ProbabilisticLinkageDecisions.ResolveRanked(
            model,
            ranked,
            "SEM_CANDIDATO_NO_RULESET_BLOCKING");

        var ranks = ranked
            .Select(static (x, index) => new { x.PessoaUuid, Rank = index + 1 })
            .ToDictionary(static x => x.PessoaUuid, static x => x.Rank);

        var candidates = canonical.Select(x =>
        {
            var contributions = x.Breakdown.Contributions
                .Select(static c => new ImplementationConferenceEvidence(
                    c.Evidence,
                    c.State))
                .ToArray();

            return new ImplementationConferenceCandidate(
                x.CandidateId,
                ranks[x.CandidateId],
                contributions,
                x.DemographicExactCollisionRisk,
                x.Breakdown.Contributions.Sum(static c => c.LogLikelihoodRatio),
                x.Breakdown.Score.LogOdds,
                x.Breakdown.Score.Posterior);
        }).ToArray();

        return new NamedConferenceRequest(
            name,
            new ImplementationConferenceRequest(
                model.ModelId,
                model.Version,
                model.AlgorithmVersion,
                model.Parameters,
                candidates,
                new ImplementationConferenceDecision(
                    decision.Status,
                    decision.PessoaUuidResolvido,
                    decision.MelhorCandidatoUuid,
                    decision.SegundoCandidatoUuid,
                    decision.Motivo),
                tolerance));
    }

    private static CandidateVectorSpec Spec(
        NameComparisonState? name,
        NameComparisonState? mother,
        string birthState,
        bool demographicExactCollisionRisk) =>
        new(
            name,
            mother,
            BirthCases.Single(x =>
                string.Equals(x.ExpectedState, birthState, StringComparison.Ordinal)),
            demographicExactCollisionRisk);

    private static Guid DeterministicCandidateId(string scenario, int index)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                FormattableString.Invariant($"{scenario}:{index}")));
        var guidBytes = bytes[..16];
        guidBytes[6] = (byte)((guidBytes[6] & 0x0f) | 0x40);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3f) | 0x80);
        return new Guid(guidBytes);
    }

    private sealed record BirthCase(
        string ExpectedState,
        DateOnly? Left,
        DateOnly? Right);

    private sealed record CandidateVectorSpec(
        NameComparisonState? NameState,
        NameComparisonState? MotherNameState,
        BirthCase Birth,
        bool DemographicExactCollisionRisk);

    private sealed record CanonicalCandidateVector(
        Guid CandidateId,
        FellegiSunterScoreBreakdown Breakdown,
        bool DemographicExactCollisionRisk);
}

internal sealed record NamedConferenceRequest(
    string Name,
    ImplementationConferenceRequest Request);

internal sealed record NamedConferenceReport(
    string Name,
    ImplementationConferenceRequest Request,
    ImplementationConferenceReport Report);

internal sealed record NamedReport(
    string Name,
    ImplementationConferenceReport Report);

internal sealed record AggregateConferenceReport(
    ImplementationConferenceStatus Status,
    int CandidatesEvaluated,
    decimal? MaxObservedPairLlrDifference,
    decimal? MaxObservedLogOddsDifference,
    bool SameFinalDecision,
    bool SameTop1,
    decimal? Spearman,
    string? Reason,
    string StatisticalValidation);

internal sealed record ConferenceExecutionSummary(
    Guid EvidenceId,
    ImplementationConferenceStatus Status,
    Guid ModelId,
    int ModelVersion,
    int ScenarioCount,
    int CandidatesEvaluated,
    decimal? MaxObservedPairLlrDifference,
    decimal? MaxObservedLogOddsDifference,
    bool SameFinalDecision,
    bool SameTop1,
    decimal? Spearman,
    string? Reason,
    string RequestSha256,
    string ReportSha256);

internal sealed class ConferencePreconditionException : InvalidOperationException
{
    internal ConferencePreconditionException(string code)
        : base(code)
    {
        Code = code;
    }

    internal ConferencePreconditionException(string code, Exception inner)
        : base(code, inner)
    {
        Code = code;
    }

    internal string Code { get; }
}

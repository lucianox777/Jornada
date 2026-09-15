using System.Collections.Concurrent;
using System.Data;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Diagnóstico read-only do mesmo candidate set e do mesmo ranking usados pelo Runner.
/// O rank determinístico inclui UUID apenas como desempate de representação; EvidenceRank
/// conta somente candidatos com evidência estritamente superior à verdade.
/// </summary>
public sealed record ProbabilisticCandidateRankingAudit(
    long ObservationId,
    Guid TruthPersonUuid,
    Guid ModelId,
    int ModelVersion,
    string AlgorithmVersion,
    string RankingSpace,
    int CandidateCount,
    bool TruthInCandidateSet,
    bool TruthInTop2,
    int? TruthDeterministicRank,
    int? TruthEvidenceRank,
    int TruthTieCount,
    decimal? TruthPosterior,
    decimal? TruthLogOdds,
    Guid? TopCandidateUuid,
    decimal? TopPosterior,
    decimal? TopLogOdds,
    decimal? RankingGapToTop);

/// <summary>
/// Score probabilístico Fellegi-Sunter operacional para registros sem CPF.
/// Modelos com ruleset persistido usam a projeção indexada identidade.blocking_chave.
/// Modelos legados preservam integralmente o blocking histórico por data de nascimento.
/// O modelo e seu ruleset são congelados juntos no primeiro carregamento do modelo no processo.
/// </summary>
public sealed class SqlProbabilisticIdentityLinkage(
    IConfiguration configuration,
    IOperationalSqlAdapter operationalSql,
    ILogger<SqlProbabilisticIdentityLinkage> logger) : IProbabilisticIdentityLinkage
{
    private readonly ConcurrentDictionary<Guid, LinkageRuntimeSnapshot> runtimeCache = new();

    public async Task<ProbabilisticLinkageModelRef> GetActiveModelAsync(CancellationToken ct)
    {
        await using var connection = await operationalSql.OpenAsync(ct);
        var idCommand = new SqlCommand(
            "SELECT modelo_id FROM identidade.modelo_linkage WHERE status='ATIVO'", connection);
        var modelId = await idCommand.ExecuteScalarAsync(ct);
        if (modelId is not Guid id)
            throw new InvalidOperationException("Não existe modelo probabilístico ATIVO.");

        var snapshot = await GetOrLoadRuntimeSnapshotAsync(id, ct);
        return snapshot.Reference;
    }

    public async Task<ProbabilisticLinkageModelRef> GetModelByVersionAsync(int version, CancellationToken ct)
    {
        await using var connection = await operationalSql.OpenAsync(ct);
        var command = new SqlCommand(
            "SELECT modelo_id FROM identidade.modelo_linkage WHERE versao=@versao AND status IN('VALIDADO','ATIVO','INATIVO')",
            connection);
        command.Parameters.Add("@versao", SqlDbType.Int).Value = version;
        var id = await command.ExecuteScalarAsync(ct);
        if (id is not Guid modelId)
            throw new InvalidOperationException($"Modelo probabilístico v{version} não encontrado ou ainda está em RASCUNHO.");

        var snapshot = await GetOrLoadRuntimeSnapshotAsync(modelId, ct);
        return snapshot.Reference;
    }

    public async Task<ProbabilisticLinkageDecision> ResolveWithoutCpfAsync(
        IdentityObservation observation, Guid modeloId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(observation.Cpf))
            throw new InvalidOperationException("O score probabilístico é exclusivo para observação sem CPF.");

        var snapshot = await GetOrLoadRuntimeSnapshotAsync(modeloId, ct);
        var model = snapshot.Model;
        var candidates = await LoadCandidatesAsync(observation, snapshot, ct);
        return ProbabilisticLinkageDecisions.Resolve(model, observation, candidates);
    }

    /// <summary>
    /// Reexecuta, somente em memória e sem publicação, o candidate generation e o ranking do Runner
    /// para uma observação existente. Destina-se a ferramentas DEV/HML de avaliação rotulada.
    /// </summary>
    public async Task<ProbabilisticCandidateRankingAudit> DiagnoseCandidateRankingAsync(
        long observationId,
        Guid truthPersonUuid,
        Guid modelId,
        CancellationToken ct)
    {
        var observation = await LoadObservationForDiagnosticsAsync(observationId, ct);
        if (!string.IsNullOrWhiteSpace(observation.Cpf))
            throw new InvalidOperationException($"Observação {observationId} possui CPF; auditoria probabilística exige SEM_CPF.");

        var snapshot = await GetOrLoadRuntimeSnapshotAsync(modelId, ct);
        var ranked = ProbabilisticLinkageDecisions.Rank(
            snapshot.Model,
            observation,
            await LoadCandidatesAsync(observation, snapshot, ct));
        var decisionV6 = string.Equals(
            snapshot.Model.AlgorithmVersion,
            LinkageParameterCatalog.DecisionEvidenceAlgorithmVersion,
            StringComparison.Ordinal);

        CandidateScore? truth = null;
        var deterministicRank = 0;
        for (var index = 0; index < ranked.Count; index++)
        {
            if (ranked[index].PessoaUuid != truthPersonUuid) continue;
            truth = ranked[index];
            deterministicRank = index + 1;
            break;
        }

        var top = ranked.Count > 0 ? ranked[0] : null;
        int? evidenceRank = null;
        var tieCount = 0;
        decimal? rankingGap = null;
        if (truth is not null)
        {
            var truthMetric = decisionV6 ? truth.LogOdds : truth.Score;
            evidenceRank = 1 + ranked.Count(candidate =>
                (decisionV6 ? candidate.LogOdds : candidate.Score) > truthMetric);
            tieCount = ranked.Count(candidate =>
                (decisionV6 ? candidate.LogOdds : candidate.Score) == truthMetric);
            if (top is not null)
                rankingGap = (decisionV6 ? top.LogOdds : top.Score) - truthMetric;
        }

        return new ProbabilisticCandidateRankingAudit(
            observationId,
            truthPersonUuid,
            snapshot.Model.ModelId,
            snapshot.Model.Version,
            snapshot.Model.AlgorithmVersion,
            decisionV6 ? "LOG_ODDS" : "POSTERIOR",
            ranked.Count,
            truth is not null,
            deterministicRank is > 0 and <= 2,
            truth is null ? null : deterministicRank,
            evidenceRank,
            tieCount,
            truth?.Score,
            truth?.LogOdds,
            top?.PessoaUuid,
            top?.Score,
            top?.LogOdds,
            rankingGap);
    }

    private async Task<IdentityObservation> LoadObservationForDiagnosticsAsync(long observationId, CancellationToken ct)
    {
        await using var connection = await operationalSql.OpenAsync(ct);
        await using var command = new SqlCommand(
            """
            SELECT cpf,cpf_ausente_motivo,nome_completo,data_nascimento,nome_mae
            FROM silver.pessoa_observacao
            WHERE pessoa_observacao_id=@observation_id;
            """,
            connection)
        {
            CommandTimeout = Math.Max(1, configuration.GetValue("ProbabilisticLinkage:CommandTimeoutSeconds", 900))
        };
        command.Parameters.Add("@observation_id", SqlDbType.BigInt).Value = observationId;

        string? cpf;
        string? cpfAbsentReason;
        string name;
        DateOnly birthDate;
        string? motherName;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException($"Observação {observationId} não encontrada.");
            cpf = reader.IsDBNull(0) ? null : reader.GetString(0);
            cpfAbsentReason = reader.IsDBNull(1) ? null : reader.GetString(1);
            name = reader.GetString(2);
            birthDate = DateOnly.FromDateTime(reader.GetDateTime(3));
            motherName = reader.IsDBNull(4) ? null : reader.GetString(4);
        }

        var eligible = PersonResolutionContractCatalog.EligibleTransversal
            .Select(static field => field.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var attributes = new List<IdentityResolutionAttributeValue>();
        await using var attributeCommand = new SqlCommand(
            """
            SELECT atributo_codigo,valor
            FROM silver.pessoa_atributo_observacao
            WHERE pessoa_observacao_id=@observation_id
            ORDER BY atributo_codigo,atributo_instancia_chave,pessoa_atributo_observacao_id;
            """,
            connection)
        {
            CommandTimeout = Math.Max(1, configuration.GetValue("ProbabilisticLinkage:CommandTimeoutSeconds", 900))
        };
        attributeCommand.Parameters.Add("@observation_id", SqlDbType.BigInt).Value = observationId;
        await using var attributeReader = await attributeCommand.ExecuteReaderAsync(ct);
        while (await attributeReader.ReadAsync(ct))
        {
            var code = attributeReader.GetString(0);
            if (eligible.Contains(code))
                attributes.Add(new IdentityResolutionAttributeValue(code, attributeReader.GetString(1)));
        }

        return new IdentityObservation(cpf, cpfAbsentReason, name, birthDate, motherName, attributes);
    }

    private async Task<LinkageRuntimeSnapshot> GetOrLoadRuntimeSnapshotAsync(Guid modelId, CancellationToken ct)
    {
        if (runtimeCache.TryGetValue(modelId, out var cached))
            return cached;

        var model = await LoadModelByIdAsync(modelId, ct);
        await using var connection = await operationalSql.OpenAsync(ct);
        var ruleSet = await LinkageRuleSetReader.TryLoadAsync(connection, modelId, ct);
        var loaded = new LinkageRuntimeSnapshot(model, ruleSet);
        var snapshot = runtimeCache.GetOrAdd(modelId, loaded);

        logger.LogInformation(
            "Snapshot probabilístico congelado. ModeloId={ModelId}; Versão={Version}; RuleSet={RuleSet}; RuleSetFingerprint={RuleSetFingerprint}; Projection={Projection}; ProjectionFingerprint={ProjectionFingerprint}",
            snapshot.Model.ModelId,
            snapshot.Model.Version,
            snapshot.RuleSet?.RuleSetVersion ?? "LEGACY",
            snapshot.RuleSet?.FingerprintSha256 ?? "NONE",
            snapshot.RuleSet?.ProjectionSchemaVersion ?? "NONE",
            snapshot.RuleSet?.ProjectionFingerprintSha256 ?? "NONE");

        return snapshot;
    }

    private async Task<LinkageModel> LoadModelByIdAsync(Guid modelId, CancellationToken ct)
    {
        await using var connection = await operationalSql.OpenAsync(ct);

        var command = new SqlCommand(
            """
            SELECT m.modelo_id, m.versao, m.algoritmo_versao, p.nome, p.valor
            FROM identidade.modelo_linkage m
            JOIN identidade.parametro_linkage p ON p.modelo_id=m.modelo_id
            WHERE m.modelo_id=@modelo_id
            ORDER BY p.nome;
            """,
            connection);
        command.Parameters.Add("@modelo_id", SqlDbType.UniqueIdentifier).Value = modelId;

        int? version = null;
        string? algorithm = null;
        var parameters = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            version ??= reader.GetInt32(1);
            algorithm ??= reader.GetString(2);
            parameters[reader.GetString(3)] = reader.GetDecimal(4);
        }

        if (version is null)
            throw new InvalidOperationException($"Modelo probabilístico {modelId} não encontrado.");

        var model = LinkageModelPolicy.Create(modelId, version.Value, algorithm ?? "UNKNOWN", parameters);

        logger.LogInformation(
            "Modelo probabilístico carregado. ModeloId={ModelId}; Versão={Version}; Algoritmo={Algorithm}",
            model.ModelId,
            model.Version,
            model.AlgorithmVersion);

        return model;
    }

    private async Task<IReadOnlyList<LinkageCandidate>> LoadCandidatesAsync(
        IdentityObservation observation,
        LinkageRuntimeSnapshot snapshot,
        CancellationToken ct)
    {
        var model = snapshot.Model;
        var maxCandidates = Math.Clamp(
            configuration.GetValue("ProbabilisticLinkage:MaxCandidatesPerBlock", 100000),
            1000, 1000000);
        var commandTimeoutSeconds = Math.Max(
            1,
            configuration.GetValue("ProbabilisticLinkage:CommandTimeoutSeconds", 900));

        await using var connection = await operationalSql.OpenAsync(ct);
        if (snapshot.RuleSet is { } ruleSet)
        {
            logger.LogDebug(
                "Blocking dinâmico congelado selecionado. ModeloId={ModelId}; RuleSet={RuleSet}; Fingerprint={Fingerprint}",
                model.ModelId,
                ruleSet.RuleSetVersion,
                ruleSet.FingerprintSha256);

            return await BlockingProjectionCandidateLoader.LoadAsync(
                connection,
                ruleSet,
                observation,
                maxCandidates,
                commandTimeoutSeconds,
                BlockingQueryDialect.SqlServer,
                ct);
        }

        return await LoadLegacyCandidatesAsync(
            connection,
            observation,
            LinkageModelPolicy.SupportsBirthComponentScoring(model),
            maxCandidates,
            commandTimeoutSeconds,
            ct);
    }

    private async Task<IReadOnlyList<LinkageCandidate>> LoadLegacyCandidatesAsync(
        SqlConnection connection,
        IdentityObservation observation,
        bool birthComponentScoring,
        int maxCandidates,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var birthDate = observation.DataNascimento;
        await using var command = new SqlCommand
        {
            Connection = connection,
            CommandTimeout = commandTimeoutSeconds
        };

        var unionParts = new List<string>
        {
            "SELECT pessoa_uuid, nome_completo, data_nascimento, nome_mae FROM gold.pessoa WHERE data_nascimento=@exact_date"
        };
        command.Parameters.Add("@exact_date", SqlDbType.Date).Value = birthDate.ToDateTime(TimeOnly.MinValue);

        if (birthComponentScoring)
        {
            var monthStart = new DateOnly(birthDate.Year, birthDate.Month, 1);
            var monthEnd = monthStart.AddMonths(1);
            command.Parameters.Add("@month_start", SqlDbType.Date).Value = monthStart.ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@month_end", SqlDbType.Date).Value = monthEnd.ToDateTime(TimeOnly.MinValue);

            var initialPredicates = new List<string>();
            if (TryInitial(observation.NomeCompleto, out var nameInitial))
            {
                command.Parameters.Add("@nome_inicial", SqlDbType.NVarChar, 1).Value = nameInitial;
                initialPredicates.Add("LEFT(LTRIM(nome_completo),1)=@nome_inicial");
            }
            if (TryInitial(observation.NomeMae, out var motherInitial))
            {
                command.Parameters.Add("@mae_inicial", SqlDbType.NVarChar, 1).Value = motherInitial;
                initialPredicates.Add("LEFT(LTRIM(nome_mae),1)=@mae_inicial");
            }

            if (initialPredicates.Count > 0)
            {
                var initialFilter = $"({string.Join(" OR ", initialPredicates)})";
                unionParts.Add(
                    $"SELECT pessoa_uuid, nome_completo, data_nascimento, nome_mae FROM gold.pessoa " +
                    $"WHERE data_nascimento>=@month_start AND data_nascimento<@month_end AND {initialFilter}");

                var sameDayDates = Enumerable.Range(1, 12)
                    .Select(month => TryDate(birthDate.Year, month, birthDate.Day))
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .Distinct()
                    .ToArray();
                var sameDayNames = AddDateParameters(command, "same_day", sameDayDates);
                if (sameDayNames.Count > 0)
                {
                    unionParts.Add(
                        $"SELECT pessoa_uuid, nome_completo, data_nascimento, nome_mae FROM gold.pessoa " +
                        $"WHERE data_nascimento IN ({string.Join(",", sameDayNames)}) AND {initialFilter}");
                }
            }

            var swapped = TryDate(birthDate.Year, birthDate.Day, birthDate.Month);
            if (swapped is { } swappedDate && swappedDate != birthDate)
            {
                command.Parameters.Add("@swapped_date", SqlDbType.Date).Value = swappedDate.ToDateTime(TimeOnly.MinValue);
                unionParts.Add(
                    "SELECT pessoa_uuid, nome_completo, data_nascimento, nome_mae FROM gold.pessoa WHERE data_nascimento=@swapped_date");
            }

            var yearTolerance = Math.Clamp(
                configuration.GetValue("ProbabilisticLinkage:BirthYearTolerance", 1),
                0, 2);
            var neighborYearDates = Enumerable.Range(-yearTolerance, yearTolerance * 2 + 1)
                .Where(offset => offset != 0)
                .Select(offset => TryDate(birthDate.Year + offset, birthDate.Month, birthDate.Day))
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToArray();
            var neighborYearNames = AddDateParameters(command, "neighbor_year", neighborYearDates);
            if (neighborYearNames.Count > 0)
            {
                unionParts.Add(
                    $"SELECT pessoa_uuid, nome_completo, data_nascimento, nome_mae FROM gold.pessoa " +
                    $"WHERE data_nascimento IN ({string.Join(",", neighborYearNames)})");
            }
        }

        command.Parameters.Add("@max_plus_one", SqlDbType.Int).Value = maxCandidates + 1;
        command.CommandText = $"""
            WITH candidate AS (
                {string.Join("\nUNION\n", unionParts)}
            )
            SELECT TOP (@max_plus_one) pessoa_uuid, nome_completo, data_nascimento, nome_mae
            FROM candidate
            ORDER BY pessoa_uuid;
            """;

        var result = new List<LinkageCandidate>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new LinkageCandidate(
                reader.GetGuid(0),
                reader.GetString(1),
                DateOnly.FromDateTime(reader.GetDateTime(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
            if (result.Count > maxCandidates)
                throw new InvalidOperationException(
                    $"Candidate generation de nascimento {birthDate:yyyy-MM-dd} excede MaxCandidatesPerBlock={maxCandidates}; " +
                    "o run foi interrompido para evitar truncamento silencioso de candidatos.");
        }

        return result;
    }

    private static IReadOnlyList<string> AddDateParameters(
        SqlCommand command,
        string prefix,
        IReadOnlyList<DateOnly> dates)
    {
        var names = new List<string>(dates.Count);
        for (var i = 0; i < dates.Count; i++)
        {
            var name = $"@{prefix}_{i}";
            command.Parameters.Add(name, SqlDbType.Date).Value = dates[i].ToDateTime(TimeOnly.MinValue);
            names.Add(name);
        }
        return names;
    }

    private static bool TryInitial(string? value, out string initial)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            initial = string.Empty;
            return false;
        }

        initial = normalized[..1].ToUpperInvariant();
        return true;
    }

    private static DateOnly? TryDate(int year, int month, int day)
    {
        if (year is < 1 or > 9999 || month is < 1 or > 12 || day < 1)
            return null;
        if (day > DateTime.DaysInMonth(year, month))
            return null;
        return new DateOnly(year, month, day);
    }
}

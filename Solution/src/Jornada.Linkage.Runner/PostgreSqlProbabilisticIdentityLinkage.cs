using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Jornada.Contracts;
using Jornada.Operational.Sql;

namespace Jornada.Linkage.Runner;

/// <summary>
/// PostgreSQL model/scoring slice. Modelos com ruleset persistido usam a projeção
/// indexada identidade.blocking_chave; modelos legados preservam o blocking histórico.
/// </summary>
public sealed class PostgreSqlProbabilisticIdentityLinkage : IProbabilisticIdentityLinkage
{
    private readonly IConfiguration configuration;
    private readonly IOperationalDatabaseAdapter database;
    private readonly ILogger<PostgreSqlProbabilisticIdentityLinkage> logger;
    private readonly ConcurrentDictionary<Guid, LinkageModel> modelCache = new();

    public PostgreSqlProbabilisticIdentityLinkage(
        IConfiguration configuration, IOperationalDatabaseAdapter database,
        ILogger<PostgreSqlProbabilisticIdentityLinkage> logger)
    {
        this.configuration = configuration;
        this.database = database;
        this.logger = logger;
        if (!string.Equals(database.Provider, OperationalDatabaseProviders.PostgreSql, StringComparison.Ordinal))
            throw new ArgumentException("O scorer PostgreSQL exige provider PostgreSql.", nameof(database));
    }

    public async Task<ProbabilisticLinkageModelRef> GetActiveModelAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = Command(connection,
            "SELECT modelo_id FROM identidade.modelo_linkage WHERE status='ATIVO';");
        var id = await command.ExecuteScalarAsync(ct);
        if (id is not Guid modelId)
            throw new InvalidOperationException("Não existe modelo probabilístico ATIVO.");
        var model = await LoadModelByIdAsync(modelId, ct);
        modelCache[modelId] = model;
        return model.Reference;
    }

    public async Task<ProbabilisticLinkageModelRef> GetModelByVersionAsync(int version, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = Command(connection,
            "SELECT modelo_id FROM identidade.modelo_linkage WHERE versao=@versao AND status IN('VALIDADO','ATIVO','INATIVO');");
        Add(command, "@versao", DbType.Int32, version);
        var id = await command.ExecuteScalarAsync(ct);
        if (id is not Guid modelId)
            throw new InvalidOperationException($"Modelo probabilístico v{version} não encontrado ou ainda está em RASCUNHO.");
        var model = await LoadModelByIdAsync(modelId, ct);
        modelCache[modelId] = model;
        return model.Reference;
    }

    public async Task<ProbabilisticLinkageDecision> ResolveWithoutCpfAsync(
        IdentityObservation observation, Guid modeloId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(observation.Cpf))
            throw new InvalidOperationException("O score probabilístico é exclusivo para observação sem CPF.");

        var model = modelCache.TryGetValue(modeloId, out var cached)
            ? cached
            : await LoadModelByIdAsync(modeloId, ct);
        modelCache[modeloId] = model;

        var candidates = await LoadCandidatesAsync(observation, model, ct);
        return ProbabilisticLinkageDecisions.Resolve(model, observation, candidates);
    }

    private async Task<LinkageModel> LoadModelByIdAsync(Guid modelId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using var command = Command(connection, """
            SELECT m.versao,m.algoritmo_versao,m.normalizacao_versao,m.status,p.nome,p.valor
            FROM identidade.modelo_linkage m
            LEFT JOIN identidade.parametro_linkage p ON p.modelo_id=m.modelo_id
            WHERE m.modelo_id=@modelo_id
            ORDER BY p.nome;
            """, tx);
        Add(command, "@modelo_id", DbType.Guid, modelId);
        int? version = null;
        string? algorithm = null;
        string? normalization = null;
        string? status = null;
        var parameters = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                version ??= reader.GetInt32(0);
                algorithm ??= reader.GetString(1);
                normalization ??= reader.GetString(2);
                status ??= reader.GetString(3);
                if (!reader.IsDBNull(4) && !parameters.TryAdd(reader.GetString(4), reader.GetDecimal(5)))
                    throw new InvalidOperationException("Modelo possui parâmetros duplicados por nome.");
            }
        }
        if (version is null || status is not ("VALIDADO" or "ATIVO" or "INATIVO"))
            throw new InvalidOperationException($"Modelo probabilístico {modelId} não encontrado ou não validado.");
        if (!string.Equals(normalization, IdentityComparison.NormalizationVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Normalização {normalization} não suportada pelo scorer PostgreSQL.");
        var model = LinkageModelPolicy.Create(modelId, version.Value, algorithm ?? "UNKNOWN", parameters);
        await tx.CommitAsync(ct);
        logger.LogInformation("Modelo probabilístico PostgreSQL carregado. ModeloId={ModelId}; Versão={Version}; Algoritmo={Algorithm}",
            model.ModelId, model.Version, model.AlgorithmVersion);
        return model;
    }

    private async Task<IReadOnlyList<LinkageCandidate>> LoadCandidatesAsync(
        IdentityObservation observation,
        LinkageModel model,
        CancellationToken ct)
    {
        var maxCandidates = Math.Clamp(
            configuration.GetValue("ProbabilisticLinkage:MaxCandidatesPerBlock", 100000),
            1000,
            1000000);
        var commandTimeoutSeconds = Math.Max(
            1,
            configuration.GetValue("ProbabilisticLinkage:CommandTimeoutSeconds", 900));

        await using var connection = await database.OpenAsync(ct);
        var ruleSet = await LinkageRuleSetReader.TryLoadAsync(connection, model.ModelId, ct);
        if (ruleSet is not null)
        {
            logger.LogDebug(
                "Blocking dinâmico PostgreSQL selecionado. ModeloId={ModelId}; RuleSet={RuleSet}; Fingerprint={Fingerprint}",
                model.ModelId,
                ruleSet.RuleSetVersion,
                ruleSet.FingerprintSha256);

            return await BlockingProjectionCandidateLoader.LoadAsync(
                connection,
                ruleSet,
                observation,
                maxCandidates,
                commandTimeoutSeconds,
                BlockingQueryDialect.PostgreSql,
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
        DbConnection connection,
        IdentityObservation observation,
        bool birthComponentScoring,
        int maxCandidates,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var birthDate = observation.DataNascimento;
        var yearTolerance = Math.Clamp(configuration.GetValue("ProbabilisticLinkage:BirthYearTolerance", 1), 0, 2);
        var plan = BirthBlockingPlan.Create(
            birthDate,
            observation.NomeCompleto,
            observation.NomeMae,
            birthComponentScoring,
            yearTolerance);

        await using var command = Command(connection, string.Empty);
        command.CommandTimeout = commandTimeoutSeconds;
        var query = PostgreSqlBirthBlockingQuery.Build(command, plan);
        Add(command, "@max_plus_one", DbType.Int32, maxCandidates + 1);
        command.CommandText = $"""
            SELECT g.pessoa_uuid,g.nome_completo,g.data_nascimento,g.nome_mae
            FROM gold.pessoa g WHERE {query.Predicate}
            ORDER BY g.pessoa_uuid LIMIT @max_plus_one;
            """;
        var result = new List<LinkageCandidate>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new LinkageCandidate(
                reader.GetGuid(0),
                reader.GetString(1),
                DateOnly.FromDateTime(reader.GetDateTime(2)),
                reader.GetString(3)));
            if (result.Count > maxCandidates)
                throw new InvalidOperationException(
                    $"Candidate generation de nascimento {birthDate:yyyy-MM-dd} excede MaxCandidatesPerBlock={maxCandidates}; " +
                    "o run foi interrompido para evitar truncamento silencioso de candidatos.");
        }
        return result;
    }

    private static DbCommand Command(DbConnection connection, string sql, DbTransaction? tx = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = tx;
        return command;
    }

    private static void Add(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

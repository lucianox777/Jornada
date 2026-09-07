using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Jornada.Contracts;
using Jornada.Operational.Sql;

namespace Jornada.Linkage.Runner;

/// <summary>
/// PostgreSQL model/scoring slice. Deliberately read-only: the batch runner,
/// publication and calibration lifecycle require separate parity gates.
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
        var candidates = await LoadCandidatesAsync(observation,
            LinkageModelPolicy.SupportsBirthComponentScoring(model), ct);
        return ProbabilisticLinkageDecisions.Resolve(model, observation, candidates);
    }

    private async Task<LinkageModel> LoadModelByIdAsync(Guid modelId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        // A model and its parameters must be read from one consistent snapshot.
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
        IdentityObservation observation, bool birthComponentScoring, CancellationToken ct)
    {
        var maxCandidates = Math.Clamp(configuration.GetValue("ProbabilisticLinkage:MaxCandidatesPerBlock", 100000), 1000, 1000000);
        var birthDate = observation.DataNascimento;
        await using var connection = await database.OpenAsync(ct);
        await using var command = Command(connection, string.Empty);
        command.CommandTimeout = Math.Max(1, configuration.GetValue("ProbabilisticLinkage:CommandTimeoutSeconds", 900));
        var unionParts = new List<string>
        {
            "SELECT pessoa_uuid,nome_completo,data_nascimento,nome_mae FROM gold.pessoa WHERE data_nascimento=@exact_date"
        };
        AddDate(command, "@exact_date", birthDate);
        if (birthComponentScoring)
        {
            var monthStart = new DateOnly(birthDate.Year, birthDate.Month, 1);
            var monthEnd = monthStart.AddMonths(1);
            AddDate(command, "@month_start", monthStart);
            AddDate(command, "@month_end", monthEnd);
            var initialPredicates = new List<string>();
            if (TryInitial(observation.NomeCompleto, out var nameInitial))
            {
                Add(command, "@nome_inicial", DbType.String, nameInitial);
                initialPredicates.Add("UPPER(LEFT(LTRIM(nome_completo),1))=@nome_inicial");
            }
            if (TryInitial(observation.NomeMae, out var motherInitial))
            {
                Add(command, "@mae_inicial", DbType.String, motherInitial);
                initialPredicates.Add("UPPER(LEFT(LTRIM(nome_mae),1))=@mae_inicial");
            }
            if (initialPredicates.Count > 0)
            {
                var initialFilter = $"({string.Join(" OR ", initialPredicates)})";
                unionParts.Add($"SELECT pessoa_uuid,nome_completo,data_nascimento,nome_mae FROM gold.pessoa WHERE data_nascimento>=@month_start AND data_nascimento<@month_end AND {initialFilter}");
                var sameDayDates = Enumerable.Range(1, 12)
                    .Select(month => TryDate(birthDate.Year, month, birthDate.Day))
                    .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
                var sameDayNames = AddDateParameters(command, "same_day", sameDayDates);
                if (sameDayNames.Count > 0)
                    unionParts.Add($"SELECT pessoa_uuid,nome_completo,data_nascimento,nome_mae FROM gold.pessoa WHERE data_nascimento IN ({string.Join(",", sameDayNames)}) AND {initialFilter}");
            }
            var swapped = TryDate(birthDate.Year, birthDate.Day, birthDate.Month);
            if (swapped is { } swappedDate && swappedDate != birthDate)
            {
                AddDate(command, "@swapped_date", swappedDate);
                unionParts.Add("SELECT pessoa_uuid,nome_completo,data_nascimento,nome_mae FROM gold.pessoa WHERE data_nascimento=@swapped_date");
            }
            var yearTolerance = Math.Clamp(configuration.GetValue("ProbabilisticLinkage:BirthYearTolerance", 1), 0, 2);
            var neighborYearDates = Enumerable.Range(-yearTolerance, yearTolerance * 2 + 1)
                .Where(offset => offset != 0)
                .Select(offset => TryDate(birthDate.Year + offset, birthDate.Month, birthDate.Day))
                .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
            var neighborYearNames = AddDateParameters(command, "neighbor_year", neighborYearDates);
            if (neighborYearNames.Count > 0)
                unionParts.Add($"SELECT pessoa_uuid,nome_completo,data_nascimento,nome_mae FROM gold.pessoa WHERE data_nascimento IN ({string.Join(",", neighborYearNames)})");
        }
        Add(command, "@max_plus_one", DbType.Int32, maxCandidates + 1);
        command.CommandText = $"""
            WITH candidate AS (
                {string.Join("\nUNION\n", unionParts)}
            )
            SELECT pessoa_uuid,nome_completo,data_nascimento,nome_mae
            FROM candidate ORDER BY pessoa_uuid LIMIT @max_plus_one;
            """;
        var result = new List<LinkageCandidate>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new LinkageCandidate(reader.GetGuid(0), reader.GetString(1),
                DateOnly.FromDateTime(reader.GetDateTime(2)), reader.GetString(3)));
            if (result.Count > maxCandidates)
                throw new InvalidOperationException($"Candidate generation de nascimento {birthDate:yyyy-MM-dd} excede MaxCandidatesPerBlock={maxCandidates}; o run foi interrompido para evitar truncamento silencioso de candidatos.");
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

    private static void AddDate(DbCommand command, string name, DateOnly date) =>
        Add(command, name, DbType.Date, date.ToDateTime(TimeOnly.MinValue));

    private static IReadOnlyList<string> AddDateParameters(DbCommand command, string prefix, IReadOnlyList<DateOnly> dates)
    {
        var names = new List<string>(dates.Count);
        for (var i = 0; i < dates.Count; i++)
        {
            var name = $"@{prefix}_{i}";
            AddDate(command, name, dates[i]);
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

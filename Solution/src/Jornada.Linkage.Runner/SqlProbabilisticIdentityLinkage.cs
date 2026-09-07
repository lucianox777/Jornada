using System.Collections.Concurrent;
using System.Data;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Score probabilístico Fellegi-Sunter operacional para registros sem CPF.
/// É chamado exclusivamente pelo Jornada.Linkage.Runner, sob demanda ou por agendamento.
///
/// A V2 trata nascimento por componentes: dia, mês e ano são evidências separadas no score.
/// Candidate generation usa múltiplos passes limitados e indexáveis sobre data_nascimento:
/// data exata, mesmo mês/ano com variação de dia, mesmo dia/ano com variação de mês,
/// transposição dia/mês e pequena tolerância configurável de ano. Nome/nome da mãe
/// apenas estreitam os passes mais amplos; não decidem identidade.
///
/// Modelos V1 permanecem compatíveis e continuam usando exclusivamente a data completa
/// exata como bloco. Os passes ampliados só são habilitados quando o modelo publicado
/// contém os parâmetros V2 de dia/mês/ano.
/// </summary>
public sealed class SqlProbabilisticIdentityLinkage(
    IConfiguration configuration,
    IOperationalSqlAdapter operationalSql,
    ILogger<SqlProbabilisticIdentityLinkage> logger) : IProbabilisticIdentityLinkage
{
    private readonly ConcurrentDictionary<Guid, LinkageModel> modelCache = new();

    public async Task<ProbabilisticLinkageModelRef> GetActiveModelAsync(CancellationToken ct)
    {
        var model = await LoadActiveModelAsync(ct);
        modelCache[model.ModelId] = model;
        return new ProbabilisticLinkageModelRef(
            model.ModelId,
            model.Version,
            model.AlgorithmVersion,
            model.Threshold,
            model.ConflictMargin);
    }

    public async Task<ProbabilisticLinkageModelRef> GetModelByVersionAsync(int version, CancellationToken ct)
    {
        var model = await LoadModelByVersionAsync(version, ct);
        modelCache[model.ModelId] = model;
        return new ProbabilisticLinkageModelRef(
            model.ModelId,
            model.Version,
            model.AlgorithmVersion,
            model.Threshold,
            model.ConflictMargin);
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

    private async Task<LinkageModel> LoadActiveModelAsync(CancellationToken ct)
    {
        await using var connection = await operationalSql.OpenAsync(ct);
        var idCommand = new SqlCommand(
            "SELECT modelo_id FROM identidade.modelo_linkage WHERE status='ATIVO'", connection);
        var modelId = await idCommand.ExecuteScalarAsync(ct);
        if (modelId is not Guid id)
            throw new InvalidOperationException("Não existe modelo probabilístico ATIVO.");
        return await LoadModelByIdAsync(id, ct);
    }

    private async Task<LinkageModel> LoadModelByVersionAsync(int version, CancellationToken ct)
    {
        await using var connection = await operationalSql.OpenAsync(ct);
        var command = new SqlCommand(
            "SELECT modelo_id FROM identidade.modelo_linkage WHERE versao=@versao AND status IN('VALIDADO','ATIVO','INATIVO')",
            connection);
        command.Parameters.Add("@versao", SqlDbType.Int).Value = version;
        var id = await command.ExecuteScalarAsync(ct);
        if (id is not Guid modelId)
            throw new InvalidOperationException($"Modelo probabilístico v{version} não encontrado ou ainda está em RASCUNHO.");
        return await LoadModelByIdAsync(modelId, ct);
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
        bool birthComponentScoring,
        CancellationToken ct)
    {
        // Nunca truncamos silenciosamente um bloco: isso poderia excluir o verdadeiro match.
        // O limite é apenas um guard rail operacional; excedê-lo falha o run e exige
        // revisão explícita do blocking/parametrização.
        var maxCandidates = Math.Clamp(
            configuration.GetValue("ProbabilisticLinkage:MaxCandidatesPerBlock", 100000),
            1000, 1000000);

        var birthDate = observation.DataNascimento;

        await using var connection = await operationalSql.OpenAsync(ct);
        var command = new SqlCommand
        {
            Connection = connection,
            CommandTimeout = Math.Max(1, configuration.GetValue("ProbabilisticLinkage:CommandTimeoutSeconds", 900))
        };

        var unionParts = new List<string>
        {
            // V1 e V2: data completa exata permanece o primeiro passe.
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

                // Passo 2: ano+mês iguais, permitindo erro no dia.
                unionParts.Add(
                    $"SELECT pessoa_uuid, nome_completo, data_nascimento, nome_mae FROM gold.pessoa " +
                    $"WHERE data_nascimento>=@month_start AND data_nascimento<@month_end AND {initialFilter}");

                // Passo 3: ano+dia iguais em qualquer mês válido, permitindo erro no mês.
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

            // Passo 4: troca dia/mês (ex.: 05/06 <-> 06/05), quando forma data válida.
            var swapped = TryDate(birthDate.Year, birthDate.Day, birthDate.Month);
            if (swapped is { } swappedDate && swappedDate != birthDate)
            {
                command.Parameters.Add("@swapped_date", SqlDbType.Date).Value = swappedDate.ToDateTime(TimeOnly.MinValue);
                unionParts.Add(
                    "SELECT pessoa_uuid, nome_completo, data_nascimento, nome_mae FROM gold.pessoa WHERE data_nascimento=@swapped_date");
            }

            // Passo 5: tolerância pequena de ano para erros comuns de digitação/registro.
            // O ano permanece uma evidência separada no score e, portanto, discordância
            // não é tratada como identidade automática.
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
                reader.GetString(3)));
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

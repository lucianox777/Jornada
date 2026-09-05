using System.Collections.Concurrent;
using System.Data;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Score probabilístico Fellegi-Sunter operacional para registros sem CPF.
/// É chamado exclusivamente pelo Jornada.Linkage.Runner, sob demanda ou por agendamento. A versão V1 usa data de
/// nascimento exata como blocking e calcula a razão de verossimilhança sobre
/// os estados de similaridade de NOME e NOME_MAE.
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
        IdentityObservation observation,
        Guid modeloId,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(observation.Cpf))
            throw new InvalidOperationException("O score probabilístico V1 é exclusivo para observação sem CPF.");

        var model = modelCache.TryGetValue(modeloId, out var cached)
            ? cached
            : await LoadModelByIdAsync(modeloId, ct);

        modelCache[modeloId] = model;
        var candidates = await LoadCandidatesAsync(observation.DataNascimento, ct);

        if (candidates.Count == 0)
        {
            return new ProbabilisticLinkageDecision(
                ResolutionStatus.NAO_RESOLVIDO,
                null,
                null,
                0m,
                null,
                null,
                null,
                model.ModelId,
                "SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO");
        }

        var scored = candidates
            .Select(candidate => new CandidateScore(candidate.PessoaUuid, Score(model, observation, candidate, candidates.Count)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.PessoaUuid)
            .ToArray();

        var best = scored[0];
        var second = scored.Length > 1 ? scored[1] : null;
        var secondScore = second?.Score;
        decimal? margin = secondScore is null ? null : best.Score - secondScore.Value;

        if (best.Score < model.Threshold)
        {
            return new ProbabilisticLinkageDecision(
                ResolutionStatus.NAO_RESOLVIDO,
                null,
                best.PessoaUuid,
                best.Score,
                second?.PessoaUuid,
                secondScore,
                margin,
                model.ModelId,
                "ABAIXO_T_LINKAGE");
        }

        if (second is not null && margin!.Value < model.ConflictMargin)
        {
            return new ProbabilisticLinkageDecision(
                ResolutionStatus.CONFLITO,
                null,
                best.PessoaUuid,
                best.Score,
                second.PessoaUuid,
                second.Score,
                margin,
                model.ModelId,
                "MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE");
        }

        return new ProbabilisticLinkageDecision(
            ResolutionStatus.RESOLVIDO,
            best.PessoaUuid,
            best.PessoaUuid,
            best.Score,
            second?.PessoaUuid,
            secondScore,
            margin,
            model.ModelId);
    }

    private decimal Score(LinkageModel model, IdentityObservation observation, GoldCandidate candidate, int blockCandidateCount)
    {
        var nameState = IdentityComparison.CompareName(observation.NomeCompleto, candidate.NomeCompleto);
        var motherState = IdentityComparison.CompareName(observation.NomeMae, candidate.NomeMae);

        // DATA_NASCIMENTO é o blocking exato da versão V1 e, portanto, não é
        // somada novamente ao peso para evitar dupla contagem da evidência.
        return FellegiSunterScoring.CalculatePosterior(model.Parameters, nameState, motherState, blockCandidateCount);
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

        var required = new[]
        {
            "PRIOR_MATCH_PROBABILITY", "PRIOR_BLOCK_MIN", "PRIOR_BLOCK_MAX", "T_LINKAGE", "CONFLICT_MARGIN",
            "M_NOME_EXACT", "M_NOME_HIGH", "M_NOME_MEDIUM", "M_NOME_LOW",
            "U_NOME_EXACT", "U_NOME_HIGH", "U_NOME_MEDIUM", "U_NOME_LOW",
            "M_NOME_MAE_EXACT", "M_NOME_MAE_HIGH", "M_NOME_MAE_MEDIUM", "M_NOME_MAE_LOW",
            "U_NOME_MAE_EXACT", "U_NOME_MAE_HIGH", "U_NOME_MAE_MEDIUM", "U_NOME_MAE_LOW"
        };

        var missing = required.Where(x => !parameters.ContainsKey(x)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Modelo incompleto. Parâmetros ausentes: {string.Join(", ", missing)}");

        var model = new LinkageModel(
            modelId,
            version.Value,
            algorithm ?? "UNKNOWN",
            parameters,
            parameters["T_LINKAGE"],
            parameters["CONFLICT_MARGIN"]);

        logger.LogInformation(
            "Modelo probabilístico carregado. ModeloId={ModelId}; Versão={Version}; Algoritmo={Algorithm}",
            model.ModelId,
            model.Version,
            model.AlgorithmVersion);

        return model;
    }

    private async Task<IReadOnlyList<GoldCandidate>> LoadCandidatesAsync(DateOnly birthDate, CancellationToken ct)
    {
        // Nunca truncamos silenciosamente um bloco: isso poderia excluir o verdadeiro match.
        // O limite é apenas um guard rail operacional; excedê-lo falha o run e exige
        // revisão explícita do blocking/parametrização.
        var maxCandidates = Math.Clamp(
            configuration.GetValue("ProbabilisticLinkage:MaxCandidatesPerBlock", 100000),
            1000, 1000000);

        await using var connection = await operationalSql.OpenAsync(ct);
        var command = new SqlCommand(
            """
            SELECT TOP (@max_plus_one) pessoa_uuid, nome_completo, data_nascimento, nome_mae
            FROM gold.pessoa
            WHERE data_nascimento=@data_nascimento
            ORDER BY pessoa_uuid;
            """,
            connection)
        {
            CommandTimeout = Math.Max(1, configuration.GetValue("ProbabilisticLinkage:CommandTimeoutSeconds", 900))
        };
        command.Parameters.Add("@max_plus_one", SqlDbType.Int).Value = maxCandidates + 1;
        command.Parameters.Add("@data_nascimento", SqlDbType.Date).Value = birthDate.ToDateTime(TimeOnly.MinValue);

        var result = new List<GoldCandidate>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new GoldCandidate(
                reader.GetGuid(0),
                reader.GetString(1),
                DateOnly.FromDateTime(reader.GetDateTime(2)),
                reader.GetString(3)));
            if (result.Count > maxCandidates)
                throw new InvalidOperationException(
                    $"Bloco de data de nascimento {birthDate:yyyy-MM-dd} excede MaxCandidatesPerBlock={maxCandidates}; " +
                    "o run foi interrompido para evitar truncamento silencioso de candidatos.");
        }

        return result;
    }

    private sealed record GoldCandidate(Guid PessoaUuid, string NomeCompleto, DateOnly DataNascimento, string NomeMae);
    private sealed record CandidateScore(Guid PessoaUuid, decimal Score);

    private sealed record LinkageModel(
        Guid ModelId,
        int Version,
        string AlgorithmVersion,
        IReadOnlyDictionary<string, decimal> Parameters,
        decimal Threshold,
        decimal ConflictMargin);
}

using System.Data;
using System.Globalization;
using System.Text.Json;
using Jornada.Operational.Sql;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Operação read-only que estima u nominal populacional sobre a referência IBGE já
/// internalizada e compara a estimativa com U_NOME_* do modelo ATIVO, quando existir.
/// Não cria rascunho, não valida/ativa modelo e não altera thresholds.
/// </summary>
public sealed class IbgeNominalUBootstrapReporter(
    ILogger<IbgeNominalUBootstrapReporter> logger,
    IConfiguration configuration,
    IOperationalSqlAdapter operationalSql,
    IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    public const string Operation = "REPORT_IBGE_U_BOOTSTRAP";
    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var seed = configuration.GetValue("LinkageParameters:IbgeUBootstrap:Seed", 20260917);
            var pairCount = Math.Clamp(
                configuration.GetValue("LinkageParameters:IbgeUBootstrap:PairCount", 250_000),
                10_000,
                5_000_000);
            var outputPath = configuration.GetValue(
                "LinkageParameters:IbgeUBootstrap:OutputPath",
                "/tmp/jornada-ibge-u-bootstrap.json")!;

            await using var connection = await operationalSql.OpenAsync(stoppingToken);
            var reference = await ReadActiveReferenceAsync(connection, stoppingToken);
            var entries = await ReadBrazilPublishedMarginalsAsync(connection, reference.Id, stoppingToken);

            var estimate = IbgeNominalUBootstrapEstimator.Estimate(
                entries,
                new IbgeNominalUBootstrapOptions(seed, pairCount));

            var activeModel = await ReadActiveModelAsync(connection, stoppingToken);
            var currentU = activeModel is null
                ? new Dictionary<string, decimal>(StringComparer.Ordinal)
                : await ReadCurrentNominalUAsync(connection, activeModel.ModelId, stoppingToken);
            var currentSupport = activeModel is null
                ? new Dictionary<string, decimal>(StringComparer.Ordinal)
                : await ReadCurrentNominalSupportAsync(connection, activeModel.ModelId, stoppingToken);

            var comparison = estimate.States.ToDictionary(
                state => state.State,
                state =>
                {
                    currentU.TryGetValue($"U_NOME_{state.State}", out var observed);
                    currentSupport.TryGetValue($"SUPPORT_U_NOME_{state.State}", out var support);
                    return new
                    {
                        ibgeBootstrapProbability = Round12(state.Probability),
                        ibgeBootstrapSupport = state.Support,
                        ibgeBootstrapStandardError = Round12(state.StandardError),
                        activeModelProbability = currentU.ContainsKey($"U_NOME_{state.State}") ? Round12(observed) : (decimal?)null,
                        activeModelSupport = currentSupport.ContainsKey($"SUPPORT_U_NOME_{state.State}") ? support : (decimal?)null,
                        deltaBootstrapMinusActive = currentU.ContainsKey($"U_NOME_{state.State}")
                            ? Round12(state.Probability - observed)
                            : (decimal?)null
                    };
                },
                StringComparer.Ordinal);

            var report = new
            {
                schemaVersion = "JORNADA_IBGE_U_BOOTSTRAP_REPORT_V1",
                purpose = "DEV_REFERENCE_ONLY_NO_AUTOMATIC_MODEL_PROMOTION",
                generatedAtUtc = DateTimeOffset.UtcNow,
                reference = new
                {
                    reference.Id,
                    reference.Code,
                    reference.Source,
                    reference.ContentSha256
                },
                methodology = new
                {
                    estimate.MethodVersion,
                    estimate.JointConstructionVersion,
                    estimate.ObservationChannelVersion,
                    note = "Prenome e sobrenome são marginais IBGE compostas independentemente. Não representa distribuição conjunta oficial de nome completo nem taxa real de erro administrativo.",
                    universe = "BRASIL/TODOS/TODOS; somente valores publicados com frequência positiva",
                    relationshipToOperationalU = "Referência populacional não condicionada ao blocking; U operacional atual é condicionado ao ruleset e não é substituído por este relatório."
                },
                sampling = new
                {
                    estimate.Seed,
                    estimate.PairCount,
                    estimate.FirstNamePublishedOccurrences,
                    estimate.SurnamePublishedOccurrences,
                    estimate.FirstNameVocabularySize,
                    estimate.SurnameVocabularySize
                },
                analytic = new
                {
                    exactFirstNameProbability = Round12(estimate.AnalyticExactFirstNameProbability),
                    exactSurnameProbability = Round12(estimate.AnalyticExactSurnameProbability),
                    exactSyntheticFullNameProbability = Round12(estimate.AnalyticExactSyntheticFullNameProbability)
                },
                activeModel = activeModel is null
                    ? null
                    : new
                    {
                        activeModel.ModelId,
                        activeModel.Version,
                        activeModel.AlgorithmVersion,
                        activeModel.SampleMethod
                    },
                states = comparison,
                safeguards = new[]
                {
                    "read-only: nenhum modelo é criado, validado ou ativado",
                    "nenhum threshold ou margem é alterado",
                    "não há canal de ruído inventado; ruído futuro exige versão e evidência independente",
                    "o resultado não constitui homologação HML/produção nem estimativa de acurácia municipal"
                }
            };

            var fullPath = Path.GetFullPath(outputPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(
                fullPath,
                JsonSerializer.Serialize(report, ReportJsonOptions),
                stoppingToken);

            logger.LogInformation(
                "Bootstrap nominal IBGE u concluído. Referência={Reference}; pares={Pairs}; saída={Output}; modelo_ativo={Model}.",
                reference.Code,
                pairCount,
                fullPath,
                activeModel?.Version.ToString(CultureInfo.InvariantCulture) ?? "nenhum");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha no bootstrap nominal IBGE de u.");
            Environment.ExitCode = 1;
        }
        finally
        {
            applicationLifetime.StopApplication();
        }
    }

    private static async Task<ReferenceInfo> ReadActiveReferenceAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT frequencia_nome_versao_id,codigo,fonte,conteudo_sha256
            FROM ref.frequencia_nome_versao
            WHERE status='ATIVA'
            ORDER BY frequencia_nome_versao_id DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Nenhuma referência IBGE de frequências está ATIVA.");

        var info = new ReferenceInfo(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            Convert.ToHexString((byte[])reader.GetValue(3)).ToLowerInvariant());

        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Mais de uma referência de frequências está ATIVA; bootstrap recusado fail-closed.");

        return info;
    }

    private static async Task<IReadOnlyList<IbgeTypedNameFrequencyEntry>> ReadBrazilPublishedMarginalsAsync(
        System.Data.Common.DbConnection connection,
        long referenceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT tipo,valor_normalizado,SUM(CONVERT(bigint,frequencia)) AS ocorrencias
            FROM ref.frequencia_nome
            WHERE frequencia_nome_versao_id=@id
              AND escopo_geografico='BRASIL'
              AND sexo='TODOS'
              AND periodo_nascimento='TODOS'
              AND uf_codigo='00'
              AND municipio_codigo='0000000'
            GROUP BY tipo,valor_normalizado
            ORDER BY tipo,valor_normalizado;
            """;
        var id = command.CreateParameter();
        id.ParameterName = "@id";
        id.DbType = DbType.Int64;
        id.Value = referenceId;
        command.Parameters.Add(id);

        var result = new List<IbgeTypedNameFrequencyEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var type = reader.GetString(0);
            var kind = type switch
            {
                "NOME" => IbgeNameStatisticKind.FirstName,
                "SOBRENOME" => IbgeNameStatisticKind.Surname,
                _ => throw new InvalidOperationException($"Tipo IBGE inesperado no recorte nacional: {type}.")
            };
            var value = reader.GetString(1);
            var occurrences = reader.GetInt64(2);
            if (occurrences > 0)
                result.Add(new IbgeTypedNameFrequencyEntry(kind, value, occurrences));
        }

        if (!result.Any(entry => entry.StatisticKind == IbgeNameStatisticKind.FirstName) ||
            !result.Any(entry => entry.StatisticKind == IbgeNameStatisticKind.Surname))
            throw new InvalidOperationException("Recorte IBGE nacional publicado não contém simultaneamente NOME e SOBRENOME.");

        return result;
    }

    private static async Task<ActiveModelInfo?> ReadActiveModelAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT TOP(1) modelo_id,versao,algoritmo_versao,amostra_metodo
            FROM identidade.modelo_linkage
            WHERE status='ATIVO'
              AND ISNULL(amostra_metodo,'')<>'SEED_DEV_FIXO_NAO_TREINADO'
            ORDER BY versao DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new ActiveModelInfo(
            reader.GetGuid(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static Task<Dictionary<string, decimal>> ReadCurrentNominalUAsync(
        System.Data.Common.DbConnection connection,
        Guid modelId,
        CancellationToken cancellationToken) =>
        ReadParametersAsync(connection, modelId, "U_NOME_", cancellationToken);

    private static Task<Dictionary<string, decimal>> ReadCurrentNominalSupportAsync(
        System.Data.Common.DbConnection connection,
        Guid modelId,
        CancellationToken cancellationToken) =>
        ReadParametersAsync(connection, modelId, "SUPPORT_U_NOME_", cancellationToken);

    private static async Task<Dictionary<string, decimal>> ReadParametersAsync(
        System.Data.Common.DbConnection connection,
        Guid modelId,
        string prefix,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT nome,valor
            FROM identidade.parametro_linkage
            WHERE modelo_id=@modelo_id AND nome LIKE @prefix + '%'
            ORDER BY nome;
            """;

        var model = command.CreateParameter();
        model.ParameterName = "@modelo_id";
        model.DbType = DbType.Guid;
        model.Value = modelId;
        command.Parameters.Add(model);

        var prefixParameter = command.CreateParameter();
        prefixParameter.ParameterName = "@prefix";
        prefixParameter.DbType = DbType.String;
        prefixParameter.Value = prefix;
        command.Parameters.Add(prefixParameter);

        var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result[reader.GetString(0)] = reader.GetDecimal(1);
        return result;
    }

    private static decimal Round12(decimal value) =>
        decimal.Round(value, 12, MidpointRounding.AwayFromZero);

    private sealed record ReferenceInfo(long Id, string Code, string Source, string ContentSha256);
    private sealed record ActiveModelInfo(Guid ModelId, int Version, string AlgorithmVersion, string? SampleMethod);
}

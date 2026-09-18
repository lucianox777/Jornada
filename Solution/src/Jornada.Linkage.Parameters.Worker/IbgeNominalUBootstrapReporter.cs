using System.Data;
using System.Globalization;
using System.Text.Json;
using Jornada.Operational.Sql;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Operação read-only que reproduz o Monte Carlo nominal IBGE usado para u de NOME e
/// NOME_MAE e compara com o modelo ATIVO, quando existir. Não cria, valida ou ativa modelo.
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
            var seed = configuration.GetValue(
                "LinkageParameters:IbgeUBootstrap:Seed",
                configuration.GetValue("LinkageParameters:IbgeNominalU:Seed", 20260917));
            var pairCount = Math.Clamp(
                configuration.GetValue(
                    "LinkageParameters:IbgeUBootstrap:PairCount",
                    configuration.GetValue("LinkageParameters:IbgeNominalU:PairCount", 1_000_000)),
                10_000,
                5_000_000);
            var outputPath = configuration.GetValue(
                "LinkageParameters:IbgeUBootstrap:OutputPath",
                "/tmp/jornada-ibge-u-bootstrap.json")!;

            await using var connection = await operationalSql.OpenAsync(stoppingToken);
            var reference = await IbgeNominalUReferenceReader.ReadActiveReferenceAsync(connection, stoppingToken);

            var personEntries = await IbgeNominalUReferenceReader.ReadBrazilPublishedMarginalsAsync(
                connection, reference.Id, "TODOS", stoppingToken);
            var motherEntries = await IbgeNominalUReferenceReader.ReadBrazilPublishedMarginalsAsync(
                connection, reference.Id, "FEMININO", stoppingToken);

            var personEstimate = IbgeNominalUBootstrapEstimator.Estimate(
                personEntries,
                new IbgeNominalUBootstrapOptions(seed, pairCount));
            var motherEstimate = IbgeNominalUBootstrapEstimator.Estimate(
                motherEntries,
                new IbgeNominalUBootstrapOptions(unchecked(seed + 1), pairCount));

            var activeModel = await ReadActiveModelAsync(connection, stoppingToken);
            var personU = activeModel is null
                ? new Dictionary<string, decimal>(StringComparer.Ordinal)
                : await ReadParametersAsync(connection, activeModel.ModelId, "U_NOME_", stoppingToken);
            var personSupport = activeModel is null
                ? new Dictionary<string, decimal>(StringComparer.Ordinal)
                : await ReadParametersAsync(connection, activeModel.ModelId, "IBGE_MC_SUPPORT_U_NOME_", stoppingToken);
            var motherU = activeModel is null
                ? new Dictionary<string, decimal>(StringComparer.Ordinal)
                : await ReadParametersAsync(connection, activeModel.ModelId, "U_NOME_MAE_", stoppingToken);
            var motherSupport = activeModel is null
                ? new Dictionary<string, decimal>(StringComparer.Ordinal)
                : await ReadParametersAsync(connection, activeModel.ModelId, "IBGE_MC_SUPPORT_U_NOME_MAE_", stoppingToken);

            var personComparison = personEstimate.States.ToDictionary(
                state => state.State,
                state =>
                {
                    personU.TryGetValue($"U_NOME_{state.State}", out var activeProbability);
                    personSupport.TryGetValue($"IBGE_MC_SUPPORT_U_NOME_{state.State}", out var activeSupport);
                    return (object)new
                    {
                        monteCarloProbability = Round12(state.Probability),
                        monteCarloSupport = state.Support,
                        monteCarloStandardError = Round12(state.StandardError),
                        activeModelProbability = personU.ContainsKey($"U_NOME_{state.State}") ? Round12(activeProbability) : (decimal?)null,
                        activeModelMonteCarloSupport = personSupport.ContainsKey($"IBGE_MC_SUPPORT_U_NOME_{state.State}") ? activeSupport : (decimal?)null
                    };
                },
                StringComparer.Ordinal);

            decimal? motherPresentMass = null;
            if (motherU.TryGetValue("U_NOME_MAE_MISSING", out var missingProbability))
                motherPresentMass = 1m - missingProbability;

            var motherComparison = motherEstimate.States.ToDictionary(
                state => state.State,
                state =>
                {
                    motherU.TryGetValue($"U_NOME_MAE_{state.State}", out var activeProbability);
                    motherSupport.TryGetValue($"IBGE_MC_SUPPORT_U_NOME_MAE_{state.State}", out var activeSupport);
                    var conditional = motherPresentMass is > 0m && motherU.ContainsKey($"U_NOME_MAE_{state.State}")
                        ? Round12(activeProbability / motherPresentMass.Value)
                        : (decimal?)null;
                    return (object)new
                    {
                        monteCarloProbabilityGivenPresent = Round12(state.Probability),
                        monteCarloSupport = state.Support,
                        monteCarloStandardError = Round12(state.StandardError),
                        activeModelJointProbability = motherU.ContainsKey($"U_NOME_MAE_{state.State}") ? Round12(activeProbability) : (decimal?)null,
                        activeModelProbabilityGivenPresent = conditional,
                        activeModelMonteCarloSupport = motherSupport.ContainsKey($"IBGE_MC_SUPPORT_U_NOME_MAE_{state.State}") ? activeSupport : (decimal?)null
                    };
                },
                StringComparer.Ordinal);

            var report = new
            {
                schemaVersion = "JORNADA_IBGE_U_BOOTSTRAP_REPORT_V2",
                purpose = "DEV_MONTE_CARLO_NOMINAL_U_DIAGNOSTIC_NO_AUTOMATIC_PROMOTION",
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
                    personFirstNameSex = "TODOS",
                    motherFirstNameSex = "FEMININO",
                    surnameSex = "TODOS",
                    personEstimate.MethodVersion,
                    personEstimate.JointConstructionVersion,
                    personEstimate.ObservationChannelVersion,
                    note = "Prenome e sobrenome são marginais IBGE compostas independentemente; não constituem distribuição conjunta oficial de nome completo.",
                    operationalUse = "U nominal de NOME usa Monte Carlo IBGE. U nominal presente de NOME_MAE usa Monte Carlo IBGE feminino e é multiplicado pela massa de presença observada no u condicionado ao blocking. Datas de nascimento permanecem condicionadas ao blocking."
                },
                personName = new
                {
                    sampling = new
                    {
                        personEstimate.Seed,
                        personEstimate.PairCount,
                        personEstimate.FirstNamePublishedOccurrences,
                        personEstimate.SurnamePublishedOccurrences,
                        personEstimate.FirstNameVocabularySize,
                        personEstimate.SurnameVocabularySize
                    },
                    analytic = new
                    {
                        exactFirstNameProbability = Round12(personEstimate.AnalyticExactFirstNameProbability),
                        exactSurnameProbability = Round12(personEstimate.AnalyticExactSurnameProbability),
                        exactSyntheticFullNameProbability = Round12(personEstimate.AnalyticExactSyntheticFullNameProbability)
                    },
                    states = personComparison
                },
                motherName = new
                {
                    sampling = new
                    {
                        motherEstimate.Seed,
                        motherEstimate.PairCount,
                        motherEstimate.FirstNamePublishedOccurrences,
                        motherEstimate.SurnamePublishedOccurrences,
                        motherEstimate.FirstNameVocabularySize,
                        motherEstimate.SurnameVocabularySize
                    },
                    analytic = new
                    {
                        exactFirstNameProbability = Round12(motherEstimate.AnalyticExactFirstNameProbability),
                        exactSurnameProbability = Round12(motherEstimate.AnalyticExactSurnameProbability),
                        exactSyntheticFullNameProbability = Round12(motherEstimate.AnalyticExactSyntheticFullNameProbability)
                    },
                    activeModelPresentMass = motherPresentMass is null ? null : Round12(motherPresentMass.Value),
                    states = motherComparison
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
                safeguards = new[]
                {
                    "read-only: nenhum modelo é criado, validado ou ativado",
                    "nenhum threshold ou margem é alterado",
                    "datas de nascimento não são substituídas pela referência nominal",
                    "resultado DEV; não constitui homologação nem estimativa de acurácia municipal"
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
                "Monte Carlo nominal IBGE concluído. Referência={Reference}; pares_por_campo={Pairs}; saída={Output}; modelo_ativo={Model}.",
                reference.Code,
                pairCount,
                fullPath,
                activeModel?.Version.ToString(CultureInfo.InvariantCulture) ?? "nenhum");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha no Monte Carlo nominal IBGE de u.");
            Environment.ExitCode = 1;
        }
        finally
        {
            applicationLifetime.StopApplication();
        }
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

    private sealed record ActiveModelInfo(Guid ModelId, int Version, string AlgorithmVersion, string? SampleMethod);
}

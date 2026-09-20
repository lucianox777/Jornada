using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Jornada.Ensaio;

/// <summary>
/// Produz evidência de escala sobre dados já existentes em HML.
/// Não reseta/seed, não ativa modelo e executa o Runner somente em MODEL_VALIDATION/publish=false.
/// A execução cria apenas um modelo RASCUNHO->VALIDADO e o histórico do run de validação.
/// </summary>
public sealed class HmlScaleEvidenceRunner(
    IConfiguration configuration,
    EnsaioRuntimeOptions options,
    Func<DbConnection> openConnection)
{
    private const string CurrentSqlServerSampleMethod = "M_INTERGESTOR_U_BLOCKING_CONDITIONED_IBGE_BOOTSTRAP_V5";
    private static readonly Regex GitSha = new("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex SafeLabel = new("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true };

    private static readonly string[] ForwardedSettings =
    [
        "AlgorithmVersion", "NormalizationVersion", "TrainingSampleSize", "TrainingSamplePoolSize",
        "SmoothingAlpha", "ReadCommandTimeoutSeconds", "MinimumIndependentMatchedPairs"
    ];

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var environmentProfile = configuration["Ensaio:HmlScale:EnvironmentProfile"]?.Trim();
        if (!string.Equals(environmentProfile, "HML", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("HML_SCALE_EVIDENCE exige Ensaio:HmlScale:EnvironmentProfile=HML.");

        if (!configuration.GetValue("Ensaio:HmlScale:AllowNonProductionWrites", false))
            throw new InvalidOperationException(
                "HML_SCALE_EVIDENCE cria somente metadados de modelo/run, mas exige Ensaio:HmlScale:AllowNonProductionWrites=true.");

        var gitCommitSha = options.BaselineSha.Trim();
        if (!GitSha.IsMatch(gitCommitSha))
            throw new InvalidOperationException("Ensaio:BaselineSha deve ser o SHA Git exato de 40 caracteres da build exercitada.");

        var profile = configuration["Ensaio:HmlScale:Profile"]?.Trim() ?? "hml-representative";
        if (!SafeLabel.IsMatch(profile))
            throw new InvalidOperationException("Ensaio:HmlScale:Profile aceita somente letras, números, ponto, hífen e sublinhado.");

        var maxRecords = Positive("Ensaio:HmlScale:MaxRecords", 100_000);
        var batchSize = Positive("Ensaio:HmlScale:BatchSize", 10_000);
        var maxParallelism = Positive("Ensaio:HmlScale:MaxParallelism", 4);
        var since = configuration["Ensaio:HmlScale:Since"]?.Trim();
        if (!string.IsNullOrWhiteSpace(since)
            && !DateTimeOffset.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
            throw new InvalidOperationException("Ensaio:HmlScale:Since deve ser ISO-8601 válido quando informado.");

        await using var preflightConnection = openConnection();
        await preflightConnection.OpenAsync(cancellationToken);
        var databaseName = preflightConnection.Database;
        if (databaseName.Contains("prod", StringComparison.OrdinalIgnoreCase)
            || databaseName.Contains("production", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"HML_SCALE_EVIDENCE recusado para banco com nome de Produção: {databaseName}.");

        var solutionSchema = await ScalarStringAsync(
            preflightConnection,
            "SELECT CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema'));",
            cancellationToken);
        if (!string.Equals(solutionSchema, "3.70", StringComparison.Ordinal))
            throw new InvalidOperationException($"HML_SCALE_EVIDENCE exige Jornada.SolutionSchema=3.70; atual={solutionSchema ?? "(ausente)"}.");

        var activeFrequencyReferences = await ScalarInt64Async(
            preflightConnection,
            "SELECT COUNT_BIG(*) FROM ref.frequencia_nome_versao WHERE status=N'ATIVA' AND conteudo_sha256 IS NOT NULL;",
            cancellationToken);
        if (activeFrequencyReferences != 1)
            throw new InvalidOperationException(
                $"HML_SCALE_EVIDENCE exige exatamente uma referência nominal ATIVA com fingerprint; encontradas={activeFrequencyReferences}.");

        var activeModelBefore = await ScalarStringAsync(
            preflightConnection,
            "SELECT TOP(1) CONVERT(nvarchar(36),modelo_id) FROM identidade.modelo_linkage WHERE status=N'ATIVO' ORDER BY versao DESC;",
            cancellationToken);
        var maxVersionBefore = await ScalarInt32Async(
            preflightConnection,
            "SELECT ISNULL(MAX(versao),0) FROM identidade.modelo_linkage;",
            cancellationToken);
        await preflightConnection.CloseAsync();

        var generate = NewCalibrator("GENERATE_DRAFT", null);
        var parameterStopwatch = Stopwatch.StartNew();
        var generateExitCode = await ExecuteProcessAsync(generate, cancellationToken);
        parameterStopwatch.Stop();
        if (generateExitCode != 0)
            throw new InvalidOperationException($"GENERATE_DRAFT terminou com exit code {generateExitCode}.");

        var model = await ReadSingleNewDraftAsync(maxVersionBefore, cancellationToken);

        var validate = NewCalibrator("VALIDATE", model.Version);
        var validateExitCode = await ExecuteProcessAsync(validate, cancellationToken);
        if (validateExitCode != 0)
            throw new InvalidOperationException($"VALIDATE v{model.Version} terminou com exit code {validateExitCode}.");

        await AssertModelStatusAsync(model.ModelId, "VALIDADO", cancellationToken);

        var correlationId = Guid.NewGuid();
        var runner = NewRunner(model.Version, correlationId, profile, maxRecords, batchSize, maxParallelism, since);
        var runnerStopwatch = Stopwatch.StartNew();
        var runnerExitCode = await ExecuteProcessAsync(runner, cancellationToken);
        runnerStopwatch.Stop();
        if (runnerExitCode != 0)
            throw new InvalidOperationException($"MODEL_VALIDATION v{model.Version} terminou com exit code {runnerExitCode}.");

        var evidence = await ReadEvidenceAsync(correlationId, cancellationToken);
        if (!string.Equals(evidence.RunStatus, "CONCLUIDO_SEM_PUBLICACAO", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Run de evidência deve terminar CONCLUIDO_SEM_PUBLICACAO; atual={evidence.RunStatus}.");
        if (evidence.PublicadoEm is not null)
            throw new InvalidOperationException("Run MODEL_VALIDATION de evidência não pode possuir publicado_em.");
        if (evidence.ModelId != model.ModelId || evidence.ModelVersion != model.Version)
            throw new InvalidOperationException("Run de evidência não preservou o modelo VALIDADO selecionado.");

        var activeModelAfter = await ReadActiveModelAsync(cancellationToken);
        if (!string.Equals(activeModelBefore, activeModelAfter, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"HML_SCALE_EVIDENCE alterou o modelo ATIVO ({activeModelBefore ?? "(nenhum)"} -> {activeModelAfter ?? "(nenhum)"}).");

        if (evidence.GoldPeople <= 0)
            throw new InvalidOperationException("Evidência HML exige ao menos uma Pessoa REFERENCIA em gold.pessoa.");
        if (evidence.Eligible <= 0)
            throw new InvalidOperationException("Evidência HML exige ao menos um registro elegível no MODEL_VALIDATION.");
        if (string.IsNullOrWhiteSpace(evidence.RuleSetVersion)
            || string.IsNullOrWhiteSpace(evidence.RuleSetFingerprintSha256)
            || string.IsNullOrWhiteSpace(evidence.ProjectionSchemaVersion)
            || string.IsNullOrWhiteSpace(evidence.ProjectionFingerprintSha256))
            throw new InvalidOperationException("Evidência HML exige proveniência completa de RULESET/projeção.");

        var report = new
        {
            reportVersion = "LINKAGE_SCALE_EVIDENCE_V1",
            gitCommitSha = gitCommitSha.ToLowerInvariant(),
            profile,
            environmentProfile = "HML",
            capturedAtUtc = DateTimeOffset.UtcNow,
            databaseName,
            goldPeople = evidence.GoldPeople,
            pairedPeople = model.MatchedPairs,
            pendingWithoutCpf = evidence.Eligible,
            trainingSampleSize = model.TrainingSampleSize,
            trainingPoolSize = model.TrainingPoolSize,
            modelVersion = model.Version,
            runtimeScope = new
            {
                mode = "MODEL_VALIDATION",
                selectedModel = new
                {
                    modelId = model.ModelId,
                    version = model.Version,
                    algorithmVersion = evidence.AlgorithmVersion
                },
                blocking = new
                {
                    mode = "RULESET",
                    ruleSetVersion = evidence.RuleSetVersion,
                    ruleSetFingerprintSha256 = evidence.RuleSetFingerprintSha256,
                    projectionSchemaVersion = evidence.ProjectionSchemaVersion,
                    projectionFingerprintSha256 = evidence.ProjectionFingerprintSha256
                }
            },
            parametersGenerateMilliseconds = parameterStopwatch.ElapsedMilliseconds,
            runnerMilliseconds = runnerStopwatch.ElapsedMilliseconds,
            correlationId,
            runner = new
            {
                status = evidence.RunStatus,
                eligible = evidence.Eligible,
                evaluated = evidence.Evaluated,
                resolved = evidence.Resolved,
                unresolved = evidence.Unresolved,
                conflicts = evidence.Conflicts,
                noCandidateInBirthDateBlock = evidence.NoCandidate
            },
            capturePolicy = new
            {
                reset = false,
                seed = false,
                activateModel = false,
                publishLinkage = false,
                expectedModelStatus = "VALIDADO",
                expectedRunStatus = "CONCLUIDO_SEM_PUBLICACAO",
                activeModelPreserved = true
            }
        };

        Directory.CreateDirectory(options.OutputDirectory);
        var configuredOutput = configuration["Ensaio:HmlScale:Output"]?.Trim();
        var outputPath = string.IsNullOrWhiteSpace(configuredOutput)
            ? Path.Combine(
                options.OutputDirectory,
                $"hml-scale-{profile}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}.json")
            : Path.GetFullPath(configuredOutput);

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        var json = JsonSerializer.Serialize(report, ReportJsonOptions) + Environment.NewLine;
        await File.WriteAllTextAsync(outputPath, json, new UTF8Encoding(false), cancellationToken);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        await File.WriteAllTextAsync(outputPath + ".sha256", hash + "  " + Path.GetFileName(outputPath) + Environment.NewLine,
            new UTF8Encoding(false), cancellationToken);

        Console.WriteLine($"HML SCALE EVIDENCE: OK report={outputPath}");
        Console.WriteLine($"HML SCALE EVIDENCE SHA256: {hash}");
        Console.WriteLine(
            $"modelo=v{model.Version} permanece VALIDADO; activePreserved={activeModelAfter ?? "(nenhum)"}; " +
            $"eligible={evidence.Eligible}; evaluated={evidence.Evaluated}; parametersMs={parameterStopwatch.ElapsedMilliseconds}; runnerMs={runnerStopwatch.ElapsedMilliseconds}");
        return 0;
    }

    private int Positive(string key, int fallback)
    {
        var value = configuration.GetValue(key, fallback);
        if (value <= 0)
            throw new InvalidOperationException($"{key} deve ser inteiro > 0.");
        return value;
    }

    private ProcessStartInfo NewCalibrator(string operation, int? targetVersion)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = configuration["Ensaio:Calibrador:FileName"] ?? "dotnet",
            Arguments = configuration["Ensaio:Calibrador:Arguments"]
                ?? "run --project ../Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.csproj --configuration Release",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        AddDatabaseEnvironment(startInfo);
        startInfo.Environment["LinkageParameters__Operation"] = operation;
        startInfo.Environment["LinkageParameters__RunOnce"] = "true";
        if (targetVersion is not null)
            startInfo.Environment["LinkageParameters__TargetVersion"] = targetVersion.Value.ToString(CultureInfo.InvariantCulture);

        if (operation == "GENERATE_DRAFT")
        {
            foreach (var key in ForwardedSettings)
            {
                var value = configuration[$"LinkageParameters:{key}"];
                if (!string.IsNullOrWhiteSpace(value))
                    startInfo.Environment[$"LinkageParameters__{key}"] = value;
            }

            foreach (var key in new[] { "Seed", "ValidationBasisPoints", "TestBasisPoints" })
            {
                var value = configuration[$"LinkageParameters:DecisionCalibration:{key}"];
                if (!string.IsNullOrWhiteSpace(value))
                    startInfo.Environment[$"LinkageParameters__DecisionCalibration__{key}"] = value;
            }

            foreach (var key in new[] { "MinimumConditionedPairs", "MinimumConditionedPairsPerPass" })
            {
                var value = configuration[$"LinkageParameters:NominalUConvergence:{key}"];
                if (!string.IsNullOrWhiteSpace(value))
                    startInfo.Environment[$"LinkageParameters__NominalUConvergence__{key}"] = value;
            }
        }

        return startInfo;
    }

    private ProcessStartInfo NewRunner(
        int modelVersion,
        Guid correlationId,
        string profile,
        int maxRecords,
        int batchSize,
        int maxParallelism,
        string? since)
    {
        var fileName = configuration["Ensaio:Runner:FileName"] ?? "dotnet";
        var prefix = configuration["Ensaio:Runner:Arguments"]
            ?? "run --project ../Jornada.Linkage.Runner/Jornada.Linkage.Runner.csproj --configuration Release --";
        var sinceArg = string.IsNullOrWhiteSpace(since) ? string.Empty : $" --since {since}";
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments =
                $"{prefix} --mode MODEL_VALIDATION --model-version {modelVersion}" +
                $"{sinceArg} --max-records {maxRecords} --batch-size {batchSize} --max-parallelism {maxParallelism}" +
                $" --publish false --requested-by HML_SCALE_EVIDENCE --reason {profile} --correlation-id {correlationId:D}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        AddDatabaseEnvironment(startInfo);
        return startInfo;
    }

    private void AddDatabaseEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment["ConnectionStrings__Jornada"] = options.ConnectionString;
        startInfo.Environment["Database__Provider"] = "SqlServer";
    }

    private static async Task<int> ExecuteProcessAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Não foi possível iniciar {startInfo.FileName}.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = (await stdout).TrimEnd();
        if (!string.IsNullOrWhiteSpace(output))
            Console.WriteLine(output);
        var error = (await stderr).TrimEnd();
        if (!string.IsNullOrWhiteSpace(error))
            Console.Error.WriteLine(error);
        return process.ExitCode;
    }

    private async Task<ModelEvidence> ReadSingleNewDraftAsync(int maxVersionBefore, CancellationToken cancellationToken)
    {
        await using var connection = openConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT modelo_id,versao,status,
                   ISNULL(amostra_m_tamanho,0),ISNULL(amostra_pool_tamanho,0),ISNULL(amostra_u_tamanho,0)
            FROM identidade.modelo_linkage
            WHERE versao>@before
              AND amostra_metodo=@sample_method
            ORDER BY versao;
            """;
        AddParameter(command, "@before", maxVersionBefore);
        AddParameter(command, "@sample_method", CurrentSqlServerSampleMethod);

        var rows = new List<ModelEvidence>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ModelEvidence(
                reader.GetGuid(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5)));
        }

        if (rows.Count != 1)
            throw new InvalidOperationException(
                $"HML_SCALE_EVIDENCE exige exatamente um novo modelo da calibração corrente; encontrados={rows.Count}. " +
                "Calibração concorrente ou ausência de RASCUNHO torna a evidência ambígua.");
        if (!string.Equals(rows[0].Status, "RASCUNHO", StringComparison.Ordinal))
            throw new InvalidOperationException($"Novo modelo deveria estar RASCUNHO; atual={rows[0].Status}.");
        return rows[0];
    }

    private async Task AssertModelStatusAsync(Guid modelId, string expected, CancellationToken cancellationToken)
    {
        await using var connection = openConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM identidade.modelo_linkage WHERE modelo_id=@id;";
        AddParameter(command, "@id", modelId);
        var status = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (!string.Equals(status, expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Modelo {modelId} deveria estar {expected}; atual={status ?? "(ausente)"}.");
    }

    private async Task<string?> ReadActiveModelAsync(CancellationToken cancellationToken)
    {
        await using var connection = openConnection();
        await connection.OpenAsync(cancellationToken);
        return await ScalarStringAsync(
            connection,
            "SELECT TOP(1) CONVERT(nvarchar(36),modelo_id) FROM identidade.modelo_linkage WHERE status=N'ATIVO' ORDER BY versao DESC;",
            cancellationToken);
    }

    private async Task<RunEvidence> ReadEvidenceAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        await using var connection = openConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT lr.status,lr.modelo_id,lr.modelo_versao,
                   lr.registros_elegiveis,lr.avaliados,lr.resolvidos,lr.nao_resolvidos,lr.conflitos,lr.sem_candidato_no_bloco,
                   lr.publicado_em,m.algoritmo_versao,
                   rs.ruleset_versao,rs.fingerprint_sha256,rs.projection_schema_version,rs.projection_fingerprint_sha256,
                   (SELECT COUNT_BIG(*) FROM gold.pessoa WHERE estado_identidade=N'REFERENCIA') AS gold_referencias
            FROM identidade.linkage_run lr
            JOIN identidade.modelo_linkage m ON m.modelo_id=lr.modelo_id
            LEFT JOIN identidade.linkage_ruleset rs ON rs.modelo_id=lr.modelo_id
            WHERE lr.correlation_id=@correlation;
            """;
        AddParameter(command, "@correlation", correlationId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException($"Run de evidência não encontrado para correlation_id={correlationId:D}.");

        var result = new RunEvidence(
            reader.GetString(0),
            reader.GetGuid(1),
            reader.GetInt32(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetInt64(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.GetInt64(15));

        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException($"Mais de um run encontrado para correlation_id={correlationId:D}.");
        return result;
    }

    private static async Task<string?> ScalarStringAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static async Task<long> ScalarInt64Async(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Consulta escalar retornou NULL.");
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<int> ScalarInt32Async(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Consulta escalar retornou NULL.");
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record ModelEvidence(
        Guid ModelId,
        int Version,
        string Status,
        int MatchedPairs,
        int TrainingPoolSize,
        int TrainingSampleSize);

    private sealed record RunEvidence(
        string RunStatus,
        Guid ModelId,
        int ModelVersion,
        long Eligible,
        long Evaluated,
        long Resolved,
        long Unresolved,
        long Conflicts,
        long NoCandidate,
        DateTimeOffset? PublicadoEm,
        string AlgorithmVersion,
        string? RuleSetVersion,
        string? RuleSetFingerprintSha256,
        string? ProjectionSchemaVersion,
        string? ProjectionFingerprintSha256,
        long GoldPeople);
}

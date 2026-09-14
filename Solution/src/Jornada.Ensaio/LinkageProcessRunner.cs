using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using Jornada.Operational.Sql;
using Microsoft.Extensions.Configuration;

namespace Jornada.Ensaio;

public sealed class LinkageProcessRunner(
    IConfiguration configuration,
    EnsaioRuntimeOptions options,
    Func<DbConnection> openConnection,
    ICollection<string> log)
{
    private static readonly string[] ForwardedSettings =
    [
        "AlgorithmVersion", "NormalizationVersion", "TrainingSampleSize", "TrainingSamplePoolSize",
        "SmoothingAlpha", "TLinkage", "ConflictMargin", "ReadCommandTimeoutSeconds",
        "MinimumIndependentMatchedPairs"
    ];

    public async Task GenerateDraftAsync(EnsaioEtapa stage, CancellationToken cancellationToken)
    {
        var startInfo = NewCalibrator();
        startInfo.Environment["LinkageParameters__Operation"] = "GENERATE_DRAFT";
        startInfo.Environment["LinkageParameters__RunOnce"] = "true";
        foreach (var key in ForwardedSettings)
        {
            var value = configuration[$"LinkageParameters:{key}"];
            if (!string.IsNullOrWhiteSpace(value))
                startInfo.Environment[$"LinkageParameters__{key}"] = value;
        }

        var exitCode = await ExecuteProcessAsync(startInfo, cancellationToken);
        log.Add($"{stage.Codigo}: Linkage.Parameters.Worker GENERATE_DRAFT exit={exitCode}");
        if (exitCode != 0)
            throw new InvalidOperationException($"GENERATE_DRAFT terminou com exit code {exitCode}.");
    }

    public async Task ValidateModelAsync(EnsaioEtapa stage, CancellationToken cancellationToken)
    {
        var version = await LatestVersionAsync("RASCUNHO", cancellationToken)
            ?? throw new InvalidOperationException("Nenhum modelo RASCUNHO disponível para validação; o calibrador final pode ter falhado por insuficiência de evidência.");
        var startInfo = NewCalibrator();
        startInfo.Environment["LinkageParameters__Operation"] = "VALIDATE";
        startInfo.Environment["LinkageParameters__TargetVersion"] = version.ToString(CultureInfo.InvariantCulture);
        startInfo.Environment["LinkageParameters__RunOnce"] = "true";
        var exitCode = await ExecuteProcessAsync(startInfo, cancellationToken);
        log.Add($"{stage.Codigo}: Linkage.Parameters.Worker VALIDATE v{version} exit={exitCode}");
        if (exitCode != 0)
            throw new InvalidOperationException($"VALIDATE v{version} terminou com exit code {exitCode}.");
    }

    public async Task RunModelValidationAsync(EnsaioEtapa stage, CancellationToken cancellationToken)
    {
        var version = await LatestVersionAsync("VALIDADO", cancellationToken)
            ?? throw new InvalidOperationException("Nenhum modelo VALIDADO disponível para MODEL_VALIDATION.");
        var fileName = configuration["Ensaio:Runner:FileName"] ?? "dotnet";
        var prefix = configuration["Ensaio:Runner:Arguments"]
            ?? "run --project ../Jornada.Linkage.Runner/Jornada.Linkage.Runner.csproj --configuration Release --";
        var maxRecords = Math.Max(1, configuration.GetValue("Ensaio:Runner:MaxRecords", 100000));
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = $"{prefix} --mode MODEL_VALIDATION --model-version {version} --max-records {maxRecords} --publish false --requested-by Jornada.Ensaio --reason ensaio-realista",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        AddDatabaseEnvironment(startInfo);
        var exitCode = await ExecuteProcessAsync(startInfo, cancellationToken);
        log.Add($"{stage.Codigo}: Jornada.Linkage.Runner MODEL_VALIDATION v{version} exit={exitCode}");
        if (exitCode != 0)
            throw new InvalidOperationException($"MODEL_VALIDATION v{version} terminou com exit code {exitCode}.");
    }

    private ProcessStartInfo NewCalibrator()
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
        return startInfo;
    }

    private void AddDatabaseEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment["ConnectionStrings__Jornada"] = options.ConnectionString;
        startInfo.Environment["Database__Provider"] = OperationalDatabaseProviders.SqlServer;
    }

    private static async Task<int> ExecuteProcessAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Não foi possível iniciar {startInfo.FileName}.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await stdout).TrimEnd();
        if (!string.IsNullOrWhiteSpace(output)) Console.WriteLine(output);
        var error = (await stderr).TrimEnd();
        if (!string.IsNullOrWhiteSpace(error)) Console.Error.WriteLine(error);
        return process.ExitCode;
    }

    private async Task<int?> LatestVersionAsync(string status, CancellationToken cancellationToken)
    {
        await using var connection = openConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP(1) versao FROM identidade.modelo_linkage WHERE status=@status AND amostra_metodo='M_INTERGESTOR_U_GOLD_SERIALIZED' ORDER BY gerado_em DESC,versao DESC;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@status";
        parameter.Value = status;
        command.Parameters.Add(parameter);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }
}

using System.Diagnostics;
using Jornada.Operational.Sql;
using Microsoft.Extensions.Configuration;

namespace Jornada.Ensaio;

public sealed class CalibrationProcessRunner(
    IConfiguration configuration,
    EnsaioRuntimeOptions options,
    ICollection<string> log)
{
    private static readonly string[] ForwardedSettings =
    [
        "AlgorithmVersion",
        "NormalizationVersion",
        "TrainingSampleSize",
        "TrainingSamplePoolSize",
        "SmoothingAlpha",
        "TLinkage",
        "ConflictMargin",
        "ReadCommandTimeoutSeconds",
        "MinimumIndependentMatchedPairs"
    ];

    public async Task RunAsync(EnsaioEtapa stage, CancellationToken cancellationToken)
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

        startInfo.Environment["ConnectionStrings__Jornada"] = options.ConnectionString;
        startInfo.Environment["Database__Provider"] = OperationalDatabaseProviders.SqlServer;
        startInfo.Environment["LinkageParameters__Operation"] = "GENERATE_DRAFT";
        startInfo.Environment["LinkageParameters__RunOnce"] = "true";

        foreach (var key in ForwardedSettings)
        {
            var value = configuration[$"LinkageParameters:{key}"];
            if (!string.IsNullOrWhiteSpace(value))
                startInfo.Environment[$"LinkageParameters__{key}"] = value;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar Jornada.Linkage.Parameters.Worker.");

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = (await stdout).TrimEnd();
        if (!string.IsNullOrWhiteSpace(output))
            Console.WriteLine(output);

        var error = (await stderr).TrimEnd();
        if (!string.IsNullOrWhiteSpace(error))
            Console.Error.WriteLine(error);

        log.Add($"{stage.Codigo}: Linkage.Parameters.Worker exit={process.ExitCode}");
    }
}

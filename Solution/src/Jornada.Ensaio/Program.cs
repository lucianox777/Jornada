using System.Data.Common;
using Jornada.Ensaio;
using Jornada.Operational.Sql;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var options = EnsaioRuntimeOptions.FromConfiguration(configuration);
var provider = configuration["Database:Provider"] ?? OperationalDatabaseProviders.SqlServer;
var adapter = OperationalDatabaseAdapterFactory.Create(provider, options.ConnectionString);

if (adapter.Provider != OperationalDatabaseProviders.SqlServer)
    throw new InvalidOperationException("Jornada.Ensaio usa SQL Server como provider operacional padrão.");

DbConnection OpenConnection() => adapter.CreateConnection();

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var mode = configuration["Ensaio:Mode"]?.Trim() ?? "FULL_REHEARSAL";
if (string.Equals(mode, "HML_SCALE_EVIDENCE", StringComparison.OrdinalIgnoreCase))
{
    var evidenceRunner = new HmlScaleEvidenceRunner(configuration, options, OpenConnection);
    return await evidenceRunner.RunAsync(cancellation.Token);
}
if (string.Equals(mode, SyntheticCalibrationDevRunner.Mode, StringComparison.OrdinalIgnoreCase))
{
    var syntheticRunner = new SyntheticCalibrationDevRunner(configuration, options, OpenConnection);
    return await syntheticRunner.RunAsync(cancellation.Token);
}
if (!string.Equals(mode, "FULL_REHEARSAL", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        $"Ensaio:Mode inválido: {mode}. Use FULL_REHEARSAL, HML_SCALE_EVIDENCE ou {SyntheticCalibrationDevRunner.Mode}.");
}

var log = new List<string>();
var checkpointCollector = new CheckpointCollector(OpenConnection);
var drainProbe = new SqlServerPipelineDrainProbe(OpenConnection);
var linkageRunner = new LinkageProcessRunner(configuration, options, OpenConnection, log);
var ingestionRunner = new IngestionStageRunner(options, drainProbe, log);
var runner = new EnsaioRunner(options, checkpointCollector, linkageRunner, ingestionRunner, log);

return await runner.RunAsync(cancellation.Token);

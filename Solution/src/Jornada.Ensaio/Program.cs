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

var log = new List<string>();
var checkpointCollector = new CheckpointCollector(OpenConnection);
var drainProbe = new SqlServerPipelineDrainProbe(OpenConnection);
var linkageRunner = new LinkageProcessRunner(configuration, options, OpenConnection, log);
var ingestionRunner = new IngestionStageRunner(options, drainProbe, log);
var runner = new EnsaioRunner(options, checkpointCollector, linkageRunner, ingestionRunner, log);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

return await runner.RunAsync(cancellation.Token);

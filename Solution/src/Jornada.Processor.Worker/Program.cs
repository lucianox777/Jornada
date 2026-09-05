using Jornada.Operational.Sql;
using Jornada.Bronze.Storage;
using Jornada.Pipeline.Coordination;
using Jornada.Contracts;
using Jornada.Processor.Worker;

var builder = Host.CreateApplicationBuilder(args);

var options = new ProcessorOptions
{
    PollingMilliseconds = builder.Configuration.GetValue<int?>("Processor:PollingMilliseconds") ?? 1000,
    MaxPessoasPorEntrega = builder.Configuration.GetValue<int?>("Processor:MaxPessoasPorEntrega") ?? 10_000,
    MaxRegistrosPorEntrega = builder.Configuration.GetValue<int?>("Processor:MaxRegistrosPorEntrega") ?? 10_000,
    LeaseDurationSeconds = builder.Configuration.GetValue<int?>("Processor:LeaseDurationSeconds") ?? 120,
    HeartbeatSeconds = builder.Configuration.GetValue<int?>("Processor:HeartbeatSeconds") ?? 30,
    RecoveryScanSeconds = builder.Configuration.GetValue<int?>("Processor:RecoveryScanSeconds") ?? 30,
    MaxProcessingAttempts = builder.Configuration.GetValue<int?>("Processor:MaxProcessingAttempts") ?? 5,
    RetryBaseSeconds = builder.Configuration.GetValue<int?>("Processor:RetryBaseSeconds") ?? 10,
    RetryMaxSeconds = builder.Configuration.GetValue<int?>("Processor:RetryMaxSeconds") ?? 300
};

var repositoryRoot = FindRepositoryRoot(builder.Environment.ContentRootPath);
var jornadaConnectionString = builder.Configuration.GetConnectionString("Jornada")
    ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IBronzeObjectStore>(_ =>
{
    var provider = builder.Configuration["BronzeStorage:Provider"] ?? "FileSystem";
    if (!string.Equals(provider, "FileSystem", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"BronzeStorage:Provider não suportado nesta distribuição: {provider}.");
    var configuredRoot = builder.Configuration["BronzeStorage:RootPath"];
    if (!builder.Environment.IsDevelopment() && (string.IsNullOrWhiteSpace(configuredRoot) || !Path.IsPathRooted(configuredRoot)))
        throw new InvalidOperationException("BronzeStorage:RootPath deve ser um caminho absoluto para armazenamento compartilhado/durável em HML/Produção.");

    var root = string.IsNullOrWhiteSpace(configuredRoot)
        ? Path.Combine(repositoryRoot, "data", "bronze")
        : Path.GetFullPath(Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(repositoryRoot, configuredRoot));
    return new FileSystemBronzeObjectStore(root);
});

var operationalSql = new OperationalSqlAdapter(jornadaConnectionString);
builder.Services.AddSingleton<IOperationalSqlAdapter>(operationalSql);
builder.Services.AddSingleton(new SqlPipelineCoordinator(
    operationalSql,
    TimeSpan.FromSeconds(Math.Max(5, builder.Configuration.GetValue("PipelineCoordination:HeartbeatSeconds", 5))),
    TimeSpan.FromSeconds(Math.Max(1, builder.Configuration.GetValue("PipelineCoordination:ExclusiveIntentTimeoutSeconds", 5)))));
builder.Services.AddSingleton(new ProcessorRuntimeIdentity(
    $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"));
builder.Services.AddSingleton<IIdentityMapRepository, SqlIdentityMapRepository>();
builder.Services.AddSingleton<SqlProcessorRepository>();
builder.Services.AddSingleton<RegistryQualityEngine>();
builder.Services.AddSingleton<IRegistryQualityEvaluator>(_ => new PositiveGrantedValueRegistryQcEvaluator("AA01", 1));
builder.Services.AddSingleton<IRegistryQualityEvaluator>(_ => new PositiveGrantedValueRegistryQcEvaluator("POT1", 1));
builder.Services.AddSingleton(_ => new IngestionPackageParser(repositoryRoot, options));
builder.Services.AddSingleton<IngestionProcessor>();
builder.Services.AddHostedService<ProcessorWorker>();

await builder.Build().RunAsync();

static string FindRepositoryRoot(string contentRoot)
{
    var dir = new DirectoryInfo(contentRoot);
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "config", "contracts"))) return dir.FullName;
        dir = dir.Parent;
    }
    throw new DirectoryNotFoundException("Não foi possível localizar a raiz da Jornada para os contratos do Processor.");
}

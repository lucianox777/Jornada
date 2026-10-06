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

var jornadaConnectionString = builder.Configuration.GetConnectionString("Jornada")
    ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
var databaseProvider = builder.Configuration["Database:Provider"] ?? OperationalDatabaseProviders.SqlServer;
var processorOperation = builder.Configuration["Processor:Operation"]?.Trim().ToUpperInvariant();

if (string.Equals(processorOperation, "REBUILD_LOCAL_BLOCKING", StringComparison.Ordinal)
    || string.Equals(processorOperation, "REFRESH_LOCAL_BLOCKING", StringComparison.Ordinal))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException($"{processorOperation} só pode executar em Development/Test.");
    if (!string.Equals(databaseProvider, OperationalDatabaseProviders.SqlServer, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"{processorOperation} requer o provider SQL Server.");

    var refreshAll = string.Equals(processorOperation, "REFRESH_LOCAL_BLOCKING", StringComparison.Ordinal);
    var result = refreshAll
        ? await LocalBlockingProjectionBootstrap.RefreshAllSqlServerAsync(
            jornadaConnectionString,
            CancellationToken.None)
        : await LocalBlockingProjectionBootstrap.RebuildMissingSqlServerAsync(
            jornadaConnectionString,
            CancellationToken.None);
    Console.WriteLine(
        refreshAll
            ? $"Blocking local reconciliado: {result.SyntheticPersons} Pessoas SCALE; " +
              $"{result.RebuiltPersons} atualizadas; {result.ProjectedKeys} chaves correntes."
            : $"Blocking local pronto: {result.SyntheticPersons} Pessoas SCALE; " +
              $"{result.RebuiltPersons} reconstruídas; {result.ProjectedKeys} chaves materializadas.");
    return;
}

var repositoryRoot = FindRepositoryRoot(builder.Environment.ContentRootPath);
var operationalDatabase = OperationalDatabaseAdapterFactory.Create(databaseProvider, jornadaConnectionString);
OperationalRuntimeHeartbeat? runtimeHeartbeat = null;

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IOperationalDatabaseAdapter>(operationalDatabase);
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

var coordinationHeartbeat = TimeSpan.FromSeconds(Math.Max(5,
    builder.Configuration.GetValue("PipelineCoordination:HeartbeatSeconds", 5)));
var exclusiveIntentTimeout = TimeSpan.FromSeconds(Math.Max(1,
    builder.Configuration.GetValue("PipelineCoordination:ExclusiveIntentTimeoutSeconds", 5)));

builder.Services.AddSingleton(new ProcessorRuntimeIdentity(
    $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"));
builder.Services.AddSingleton<RegistryQualityEngine>();
builder.Services.AddSingleton<IRegistryQualityEvaluator>(_ => new PositiveGrantedValueRegistryQcEvaluator("AA01", 1));
builder.Services.AddSingleton<IRegistryQualityEvaluator>(_ => new PositiveGrantedValueRegistryQcEvaluator("POT1", 1));

if (string.Equals(operationalDatabase.Provider, OperationalDatabaseProviders.SqlServer, StringComparison.Ordinal))
{
    var operationalSql = operationalDatabase as IOperationalSqlAdapter
        ?? throw new InvalidOperationException("Provider SqlServer não expôs IOperationalSqlAdapter.");
    builder.Services.AddSingleton<IOperationalSqlAdapter>(operationalSql);
    runtimeHeartbeat = new OperationalRuntimeHeartbeat(
        operationalSql,
        builder.Configuration["JORNADA_NODE_ID"] ?? Environment.MachineName,
        "Processor",
        TimeSpan.FromSeconds(Math.Max(5, builder.Configuration.GetValue("Monitoring:HeartbeatSeconds", 10))));

    var pipelineCoordinator = new SqlPipelineCoordinator(
        operationalSql, coordinationHeartbeat, exclusiveIntentTimeout);
    builder.Services.AddSingleton(pipelineCoordinator);
    builder.Services.AddSingleton<IProcessorPipelineCoordinator>(
        new SqlProcessorPipelineCoordinatorAdapter(pipelineCoordinator));
    builder.Services.AddSingleton<IIdentityMapRepository, SqlIdentityMapRepository>();
    builder.Services.AddSingleton<SqlProcessorRepository>();
    builder.Services.AddSingleton<IProcessorRepository>(sp =>
        new SqlProcessorRepositoryAdapter(sp.GetRequiredService<SqlProcessorRepository>()));
}

builder.Services.AddSingleton(_ => new IngestionPackageParser(repositoryRoot, options));
builder.Services.AddSingleton<IngestionProcessor>();
builder.Services.AddHostedService<ProcessorWorker>();

var host = builder.Build();

if (string.Equals(processorOperation, "PROCESS_UNTIL_IDLE", StringComparison.Ordinal))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("PROCESS_UNTIL_IDLE só pode executar em Development/Test.");

    Guid? targetEntregaId = null;
    var configuredTarget = builder.Configuration["Processor:TargetEntregaId"]?.Trim();
    if (!string.IsNullOrWhiteSpace(configuredTarget))
    {
        if (!Guid.TryParse(configuredTarget, out var parsedTarget))
            throw new InvalidOperationException("Processor:TargetEntregaId deve ser um GUID válido.");
        targetEntregaId = parsedTarget;
        Console.WriteLine($"Processor one-shot restrito à Entrega {targetEntregaId}.");
    }

    var repository = host.Services.GetRequiredService<IProcessorRepository>();
    var processor = host.Services.GetRequiredService<IngestionProcessor>();
    var recovered = await repository.RecoverExpiredLeasesAsync(options.MaxProcessingAttempts, CancellationToken.None);
    var processed = 0;
    while (await processor.ProcessNextAsync(targetEntregaId, CancellationToken.None))
    {
        processed++;
        if (processed >= 10_000)
            throw new InvalidOperationException("PROCESS_UNTIL_IDLE excedeu 10.000 ciclos; execução interrompida por segurança.");
    }

    Console.WriteLine($"Processor one-shot concluído: lotes_processados={processed}; leases_recuperados={recovered}.");
    return;
}

if (runtimeHeartbeat is not null)
    _ = runtimeHeartbeat.RunAsync(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
await host.RunAsync();

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

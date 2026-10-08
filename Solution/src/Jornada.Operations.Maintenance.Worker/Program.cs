using Jornada.Contracts;
using Jornada.Operational.Sql;
using Jornada.Bronze.Storage;
using Jornada.Operations.Maintenance.Worker;

var builder = Host.CreateApplicationBuilder(args);
var jornadaConnectionString = builder.Configuration.GetConnectionString("Jornada")
    ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
var operationalSql = new OperationalSqlAdapter(jornadaConnectionString);
var runOnce = builder.Configuration.GetValue("MaintenanceExecution:RunOnce", false);
builder.Services.AddSingleton<IOperationalSqlAdapter>(operationalSql);
builder.Services.AddOptions<ItemProcessedRetentionOptions>()
    .Bind(builder.Configuration.GetSection("ItemProcessedRetention"))
    .Validate(o => !o.Enabled || o.DetailRetentionDays > 0,
        "ItemProcessedRetention:DetailRetentionDays deve ser explicitamente > 0 quando Enabled=true.")
    .Validate(o => o.MaxRowsPerCycle > 0, "ItemProcessedRetention:MaxRowsPerCycle deve ser > 0.")
    .Validate(o => o.IntervalMinutes > 0, "ItemProcessedRetention:IntervalMinutes deve ser > 0.")
    .ValidateOnStart();

builder.Services.AddOptions<PipelineWatchdogOptions>()
    .Bind(builder.Configuration.GetSection("PipelineWatchdog"))
    .Validate(o => o.IntervalMinutes > 0, "PipelineWatchdog:IntervalMinutes deve ser > 0.")
    .Validate(o => o.LinkageRunMaxMinutes > 0, "PipelineWatchdog:LinkageRunMaxMinutes deve ser > 0.")
    .Validate(o => o.ModelGenerationMaxMinutes > 0, "PipelineWatchdog:ModelGenerationMaxMinutes deve ser > 0.")
    .Validate(o => o.ExpiredLeaseGraceMinutes >= 0, "PipelineWatchdog:ExpiredLeaseGraceMinutes deve ser >= 0.")
    .Validate(o => o.PendingBacklogMaxAgeMinutes > 0, "PipelineWatchdog:PendingBacklogMaxAgeMinutes deve ser > 0.")
    .ValidateOnStart();

builder.Services.AddOptions<DeliveryBronzeRetentionOptions>()
    .Bind(builder.Configuration.GetSection("DeliveryBronzeRetention"))
    .Validate(o => !o.Enabled || o.RetentionDays > 0,
        "DeliveryBronzeRetention:RetentionDays deve ser explicitamente > 0 quando Enabled=true.")
    .Validate(o => o.MaxRowsPerCycle > 0, "DeliveryBronzeRetention:MaxRowsPerCycle deve ser > 0.")
    .Validate(o => o.IntervalMinutes > 0, "DeliveryBronzeRetention:IntervalMinutes deve ser > 0.")
    .Validate(o => o.ObjectLockTimeoutSeconds >= 0, "DeliveryBronzeRetention:ObjectLockTimeoutSeconds deve ser >= 0.")
    .ValidateOnStart();

builder.Services.AddSingleton<IBronzeObjectStore>(_ =>
{
    var provider = builder.Configuration["BronzeStorage:Provider"] ?? "FileSystem";
    if (!string.Equals(provider, "FileSystem", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"BronzeStorage:Provider não suportado nesta distribuição: {provider}.");
    var configuredRoot = builder.Configuration["BronzeStorage:RootPath"];
    if (!builder.Environment.IsDevelopment() && (string.IsNullOrWhiteSpace(configuredRoot) || !Path.IsPathRooted(configuredRoot)))
        throw new InvalidOperationException("BronzeStorage:RootPath deve ser absoluto em HML/Produção.");
    var root = string.IsNullOrWhiteSpace(configuredRoot)
        ? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data", "bronze"))
        : Path.GetFullPath(Path.IsPathRooted(configuredRoot) ? configuredRoot : Path.Combine(builder.Environment.ContentRootPath, configuredRoot));
    return new FileSystemBronzeObjectStore(root);
});
builder.Services.AddSingleton<IBronzeObjectMaintenanceStore>(sp => (IBronzeObjectMaintenanceStore)sp.GetRequiredService<IBronzeObjectStore>());

if (runOnce)
{
    builder.Services.AddSingleton<ItemProcessedRetentionWorker>();
    builder.Services.AddSingleton<DeliveryBronzeRetentionWorker>();
    builder.Services.AddSingleton<PipelineWatchdogWorker>();
}
else
{
    builder.Services.AddHostedService<ItemProcessedRetentionWorker>();
    builder.Services.AddHostedService<DeliveryBronzeRetentionWorker>();
    builder.Services.AddHostedService<PipelineWatchdogWorker>();
}

var host = builder.Build();

if (runOnce)
{
    var failures = new List<string>();
    var itemOptions = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ItemProcessedRetentionOptions>>().Value;
    var deliveryOptions = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DeliveryBronzeRetentionOptions>>().Value;
    var watchdogOptions = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PipelineWatchdogOptions>>().Value;

    if (itemOptions.Enabled)
        await RunOnceStepAsync("ITEM_PROCESSED_RETENTION",
            () => host.Services.GetRequiredService<ItemProcessedRetentionWorker>().RunCycleAsync(itemOptions, CancellationToken.None),
            failures);
    if (deliveryOptions.Enabled)
        await RunOnceStepAsync("DELIVERY_BRONZE_RETENTION",
            () => host.Services.GetRequiredService<DeliveryBronzeRetentionWorker>().RunCycleAsync(deliveryOptions, CancellationToken.None),
            failures);
    if (watchdogOptions.Enabled)
        await RunOnceStepAsync("PIPELINE_WATCHDOG",
            async () => { _ = await host.Services.GetRequiredService<PipelineWatchdogWorker>().RunCycleAsync(watchdogOptions, CancellationToken.None); },
            failures);

    Console.WriteLine($"Operations Maintenance RunOnce concluído: falhas={failures.Count}.");
    if (failures.Count > 0)
    {
        Console.Error.WriteLine("Falhas: " + string.Join("; ", failures));
        Environment.ExitCode = JornadaExitCodes.FAILURE;
    }
    return;
}

var heartbeat = new OperationalRuntimeHeartbeat(
    operationalSql,
    builder.Configuration["JORNADA_NODE_ID"] ?? Environment.MachineName,
    "OperationsMaintenance",
    TimeSpan.FromSeconds(Math.Max(5, builder.Configuration.GetValue("Monitoring:HeartbeatSeconds", 10))));
_ = heartbeat.RunAsync(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
await host.RunAsync();

static async Task RunOnceStepAsync(string name, Func<Task> action, ICollection<string> failures)
{
    try
    {
        await action();
        Console.WriteLine($"{name}: OK");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}={ex.GetType().Name}:{ex.Message}");
    }
}

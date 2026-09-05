using Jornada.Operational.Sql;
using Jornada.Bronze.Storage;
using Jornada.Operations.Maintenance.Worker;

var builder = Host.CreateApplicationBuilder(args);
var jornadaConnectionString = builder.Configuration.GetConnectionString("Jornada")
    ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
builder.Services.AddSingleton<IOperationalSqlAdapter>(new OperationalSqlAdapter(jornadaConnectionString));
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
    .Validate(o => o.InitialLoadMaxHours > 0, "PipelineWatchdog:InitialLoadMaxHours deve ser > 0.")
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

builder.Services.AddHostedService<ItemProcessedRetentionWorker>();
builder.Services.AddHostedService<DeliveryBronzeRetentionWorker>();
builder.Services.AddHostedService<PipelineWatchdogWorker>();
await builder.Build().RunAsync();

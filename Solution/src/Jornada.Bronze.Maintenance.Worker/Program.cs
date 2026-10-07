using Jornada.Operational.Sql;
using Jornada.Bronze.Maintenance.Worker;
using Jornada.Bronze.Storage;

var builder = Host.CreateApplicationBuilder(args);
var jornadaConnectionString = builder.Configuration.GetConnectionString("Jornada")
    ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
var operationalSql = new OperationalSqlAdapter(jornadaConnectionString);
var runOnce = builder.Configuration.GetValue("BronzeMaintenance:RunOnce", false);
builder.Services.AddSingleton<IOperationalSqlAdapter>(operationalSql);
builder.Services.Configure<BronzeMaintenanceOptions>(builder.Configuration.GetSection("BronzeMaintenance"));

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
        : Path.GetFullPath(Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(builder.Environment.ContentRootPath, configuredRoot));
    return new FileSystemBronzeObjectStore(root);
});
builder.Services.AddSingleton<IBronzeObjectMaintenanceStore>(sp => (IBronzeObjectMaintenanceStore)sp.GetRequiredService<IBronzeObjectStore>());
builder.Services.AddSingleton<BronzeMaintenanceRepository>();
if (runOnce)
    builder.Services.AddSingleton<BronzeMaintenanceWorker>();
else
    builder.Services.AddHostedService<BronzeMaintenanceWorker>();

var host = builder.Build();

if (runOnce)
{
    var options = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BronzeMaintenanceOptions>>().Value;
    if (options.Enabled)
        await host.Services.GetRequiredService<BronzeMaintenanceWorker>().RunCycleAsync(CancellationToken.None);
    Console.WriteLine($"Bronze Maintenance RunOnce concluído: enabled={options.Enabled}.");
    return;
}

var heartbeat = new OperationalRuntimeHeartbeat(
    operationalSql,
    builder.Configuration["JORNADA_NODE_ID"] ?? Environment.MachineName,
    "BronzeMaintenance",
    TimeSpan.FromSeconds(Math.Max(5, builder.Configuration.GetValue("Monitoring:HeartbeatSeconds", 10))));
_ = heartbeat.RunAsync(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
await host.RunAsync();

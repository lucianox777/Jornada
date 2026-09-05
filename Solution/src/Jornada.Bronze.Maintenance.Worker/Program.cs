using Jornada.Operational.Sql;
using Jornada.Bronze.Maintenance.Worker;
using Jornada.Bronze.Storage;

var builder = Host.CreateApplicationBuilder(args);
var jornadaConnectionString = builder.Configuration.GetConnectionString("Jornada")
    ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
builder.Services.AddSingleton<IOperationalSqlAdapter>(new OperationalSqlAdapter(jornadaConnectionString));
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
builder.Services.AddHostedService<BronzeMaintenanceWorker>();

await builder.Build().RunAsync();

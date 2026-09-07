using Jornada.Operational.Sql;
using Jornada.Pipeline.Coordination;
using Jornada.Linkage.Parameters.Worker;

var builder = Host.CreateApplicationBuilder(args);
var jornadaConnectionString = builder.Configuration.GetConnectionString("Jornada")
    ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
var provider = builder.Configuration.GetValue("Database:Provider", OperationalDatabaseProviders.SqlServer);
var database = OperationalDatabaseAdapterFactory.Create(provider, jornadaConnectionString);

if (database.Provider == OperationalDatabaseProviders.PostgreSql)
{
    builder.Services.AddSingleton(database);
    builder.Services.AddHostedService<PostgreSqlLinkageParametersWorker>();
}
else
{
    // O worker canônico SQL Server e seu coordenador permanecem inalterados.
    var operationalSql = new OperationalSqlAdapter(jornadaConnectionString);
    builder.Services.AddSingleton<IOperationalSqlAdapter>(operationalSql);
    builder.Services.AddSingleton(new SqlPipelineCoordinator(
        operationalSql,
        TimeSpan.FromSeconds(Math.Max(5, builder.Configuration.GetValue("PipelineCoordination:HeartbeatSeconds", 5))),
        TimeSpan.FromSeconds(Math.Max(1, builder.Configuration.GetValue("PipelineCoordination:ExclusiveIntentTimeoutSeconds", 5)))));
    builder.Services.AddHostedService<LinkageParametersWorker>();
}

await builder.Build().RunAsync();

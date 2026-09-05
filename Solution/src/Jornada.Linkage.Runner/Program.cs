using Jornada.Operational.Sql;
using Jornada.Contracts;
using Jornada.Pipeline.Coordination;
using Jornada.Linkage.Runner;

if (args.Any(a => a.Equals("--help", StringComparison.OrdinalIgnoreCase) || a.Equals("-h", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine(LinkageRunOptions.Usage);
    return;
}

var options = LinkageRunOptions.Parse(args);
var builder = Host.CreateApplicationBuilder(args);
var jornadaConnectionString = builder.Configuration.GetConnectionString("Jornada")
    ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
builder.Services.AddSingleton(options);
var operationalSql = new OperationalSqlAdapter(jornadaConnectionString);
builder.Services.AddSingleton<IOperationalSqlAdapter>(operationalSql);
builder.Services.AddSingleton(new SqlPipelineCoordinator(
    operationalSql,
    TimeSpan.FromSeconds(Math.Max(5, builder.Configuration.GetValue("PipelineCoordination:HeartbeatSeconds", 5))),
    TimeSpan.FromSeconds(Math.Max(1, builder.Configuration.GetValue("PipelineCoordination:ExclusiveIntentTimeoutSeconds", 5)))));
builder.Services.AddSingleton<IProbabilisticIdentityLinkage, SqlProbabilisticIdentityLinkage>();
builder.Services.AddSingleton<IProbabilisticLinkageBatchRunner, ProbabilisticLinkageBatchRunner>();
builder.Services.AddHostedService<LinkageRunnerWorker>();

await builder.Build().RunAsync();

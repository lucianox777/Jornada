using Jornada.Operational.Sql;
using Jornada.Contracts;
using Jornada.Pipeline.Coordination;
using Jornada.Linkage.Runner;

if (BlockingPassAuditCommand.IsRequested(args))
{
    var auditBuilder = Host.CreateApplicationBuilder(args);
    var auditConnectionString = auditBuilder.Configuration.GetConnectionString("Jornada")
        ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
    var auditOperationalSql = new OperationalSqlAdapter(auditConnectionString);
    await BlockingPassAuditCommand.ExecuteAsync(
        args,
        auditBuilder.Configuration,
        auditOperationalSql,
        CancellationToken.None);
    return;
}

if (CandidateRankingAuditCommand.IsRequested(args))
{
    var auditBuilder = Host.CreateApplicationBuilder(args);
    var auditConnectionString = auditBuilder.Configuration.GetConnectionString("Jornada")
        ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
    var auditOperationalSql = new OperationalSqlAdapter(auditConnectionString);
    await CandidateRankingAuditCommand.ExecuteAsync(
        args,
        auditBuilder.Configuration,
        auditOperationalSql,
        CancellationToken.None);
    return;
}

if (args.Any(a => a.Equals("--help", StringComparison.OrdinalIgnoreCase) || a.Equals("-h", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine(LinkageRunOptions.Usage);
    Console.WriteLine();
    Console.WriteLine("Auditoria DEV/HML read-only de candidate ranking:");
    Console.WriteLine("  --candidate-ranking-audit-labels <arquivo.csv>");
    Console.WriteLine("  --candidate-ranking-audit-output <arquivo.json>");
    Console.WriteLine();
    Console.WriteLine("Auditoria DEV/HML read-only de blocking por passe:");
    Console.WriteLine("  --blocking-pass-audit-labels <arquivo.csv>");
    Console.WriteLine("  --blocking-pass-audit-output <arquivo.json>");
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

using Jornada.Operational.Sql;
using Jornada.Pipeline.Coordination;
using Jornada.Linkage.Parameters.Worker;

var builder = Host.CreateApplicationBuilder(args);
var jornadaConnectionString = builder.Configuration.GetConnectionString("Jornada")
    ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
var provider = builder.Configuration.GetValue("Database:Provider", OperationalDatabaseProviders.SqlServer);
var database = OperationalDatabaseAdapterFactory.Create(provider, jornadaConnectionString);
var operation = builder.Configuration.GetValue("LinkageParameters:Operation", "GENERATE_DRAFT")!
    .Trim()
    .ToUpperInvariant();

if (operation == NameFrequencySourceChecker.Operation)
{
    builder.Services.AddHostedService<NameFrequencySourceChecker>();
}
else if (database.Provider == OperationalDatabaseProviders.PostgreSql)
{
    if (operation is NameFrequencyReferenceImporter.Operation or NameFrequencySnapshotLoader.Operation)
        throw new InvalidOperationException($"{operation} ainda possui implementação canônica apenas para SQL Server.");

    builder.Services.AddSingleton(database);
    builder.Services.AddHostedService<PostgreSqlLinkageParametersWorker>();
}
else
{
    var operationalSql = new OperationalSqlAdapter(jornadaConnectionString);

    // A geração de modelo congela a referência ATIVA de frequências. Em um banco novo
    // o schema existe, mas os dados de referência ainda não foram materializados; nesse
    // caso executamos uma única vez o loader offline do snapshot versionado. Se já há
    // referência ATIVA (inclusive uma revisão futura governada), ela é preservada.
    if (operation == "GENERATE_DRAFT" && !await HasActiveNameFrequencyReferenceAsync(operationalSql))
    {
        Console.WriteLine("Referência de frequências ausente; carregando snapshot local canônico antes de GENERATE_DRAFT.");
        var bootstrapBuilder = Host.CreateApplicationBuilder(args);
        bootstrapBuilder.Services.AddSingleton<IOperationalSqlAdapter>(operationalSql);
        bootstrapBuilder.Services.AddHostedService<NameFrequencySnapshotLoader>();
        await bootstrapBuilder.Build().RunAsync();

        if (Environment.ExitCode != 0 || !await HasActiveNameFrequencyReferenceAsync(operationalSql))
            throw new InvalidOperationException("GENERATE_DRAFT exige uma referência de frequências ATIVA; carga do snapshot local não foi concluída.");
    }

    builder.Services.AddSingleton<IOperationalSqlAdapter>(operationalSql);

    if (operation == NameFrequencyReferenceImporter.Operation)
    {
        builder.Services.AddHostedService<NameFrequencyReferenceImporter>();
    }
    else if (operation == NameFrequencySnapshotLoader.Operation)
    {
        builder.Services.AddHostedService<NameFrequencySnapshotLoader>();
    }
    else
    {
        builder.Services.AddSingleton(new SqlPipelineCoordinator(
            operationalSql,
            TimeSpan.FromSeconds(Math.Max(5, builder.Configuration.GetValue("PipelineCoordination:HeartbeatSeconds", 5))),
            TimeSpan.FromSeconds(Math.Max(1, builder.Configuration.GetValue("PipelineCoordination:ExclusiveIntentTimeoutSeconds", 5)))));
        builder.Services.AddHostedService<LinkageParametersWorker>();
    }
}

await builder.Build().RunAsync();

static async Task<bool> HasActiveNameFrequencyReferenceAsync(IOperationalSqlAdapter operationalSql)
{
    await using var connection = await operationalSql.OpenAsync(CancellationToken.None);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM ref.frequencia_nome_versao WHERE status='ATIVA' AND conteudo_sha256 IS NOT NULL;";
    var value = await command.ExecuteScalarAsync(CancellationToken.None);
    return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) == 1;
}

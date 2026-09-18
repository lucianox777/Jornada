using Jornada.Operational.Sql;
using Jornada.Pipeline.Coordination;
using Jornada.Linkage.Parameters.Worker;

const string EnsureNameFrequencySnapshotOperation = "ENSURE_NAME_FREQUENCY_SNAPSHOT";
const string CanonicalNameFrequencyReferenceCode = "CENSO2022_NOMES_BRASIL_V1";

var builder = Host.CreateApplicationBuilder(args);
var jornadaConnectionString = builder.Configuration.GetConnectionString("Jornada")
    ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
var provider = builder.Configuration.GetValue("Database:Provider", OperationalDatabaseProviders.SqlServer);
_ = OperationalDatabaseAdapterFactory.Create(provider, jornadaConnectionString);
var operation = builder.Configuration.GetValue("LinkageParameters:Operation", "GENERATE_DRAFT")!
    .Trim()
    .ToUpperInvariant();

if (operation == NameFrequencySourceChecker.Operation)
{
    builder.Services.AddHostedService<NameFrequencySourceChecker>();
}
else
{
    var operationalSql = new OperationalSqlAdapter(jornadaConnectionString);

    if (operation == EnsureNameFrequencySnapshotOperation)
    {
        var referenceState = await NameFrequencyReferenceState.EnsureCanonicalActiveAsync(
            operationalSql,
            CanonicalNameFrequencyReferenceCode,
            CancellationToken.None);

        if (referenceState == CanonicalNameFrequencyReferenceState.AlreadyActive)
        {
            Console.WriteLine($"Referencia IBGE canonica {CanonicalNameFrequencyReferenceCode} ja esta ATIVA; nenhuma recarga necessaria.");
            return;
        }

        if (referenceState == CanonicalNameFrequencyReferenceState.Reactivated)
        {
            Console.WriteLine($"Referencia IBGE canonica {CanonicalNameFrequencyReferenceCode} ja estava materializada e foi reativada sem recarga.");
            return;
        }

        Console.WriteLine($"Referencia IBGE canonica {CanonicalNameFrequencyReferenceCode} ausente ou ainda CARREGANDO; materializando snapshot local antes de liberar o ambiente.");
        Console.WriteLine("A carga canonica contem milhoes de linhas e pode levar alguns minutos. O processo imprimira um heartbeat a cada 15 segundos.");
        var ensureBuilder = Host.CreateApplicationBuilder(args);
        ensureBuilder.Services.AddSingleton<IOperationalSqlAdapter>(operationalSql);
        ensureBuilder.Services.AddHostedService<NameFrequencySnapshotLoader>();
        await RunHostWithHeartbeatAsync(
            ensureBuilder.Build(),
            "Carga da referencia de frequencias",
            TimeSpan.FromSeconds(15));

        var finalState = await NameFrequencyReferenceState.EnsureCanonicalActiveAsync(
            operationalSql,
            CanonicalNameFrequencyReferenceCode,
            CancellationToken.None);

        if (Environment.ExitCode != 0 || finalState == CanonicalNameFrequencyReferenceState.MissingOrLoading)
            throw new InvalidOperationException($"Nao foi possivel materializar e ativar a referencia IBGE canonica {CanonicalNameFrequencyReferenceCode}.");

        Console.WriteLine($"Referencia IBGE canonica {CanonicalNameFrequencyReferenceCode} materializada e ATIVA.");
        return;
    }

    // A geração de modelo congela a referência ATIVA de frequências. O bootstrap normal
    // do ambiente já materializa o Censo 2022; este fallback permanece fail-safe para
    // execuções diretas do calibrador contra um banco criado fora dos entrypoints oficiais.
    if (operation == "GENERATE_DRAFT" && !await HasActiveNameFrequencyReferenceAsync(operationalSql))
    {
        var canonicalState = await NameFrequencyReferenceState.EnsureCanonicalActiveAsync(
            operationalSql,
            CanonicalNameFrequencyReferenceCode,
            CancellationToken.None);

        if (canonicalState == CanonicalNameFrequencyReferenceState.Reactivated)
            Console.WriteLine($"Referencia IBGE canonica {CanonicalNameFrequencyReferenceCode} reativada sem recarga antes de GENERATE_DRAFT.");

        if (canonicalState == CanonicalNameFrequencyReferenceState.MissingOrLoading)
        {
            Console.WriteLine("Referencia de frequencias ausente; carregando snapshot local canonico antes de GENERATE_DRAFT.");
            Console.WriteLine("A carga canonica contem milhoes de linhas e pode levar alguns minutos. O processo imprimira um heartbeat a cada 15 segundos.");
            var bootstrapBuilder = Host.CreateApplicationBuilder(args);
            bootstrapBuilder.Services.AddSingleton<IOperationalSqlAdapter>(operationalSql);
            bootstrapBuilder.Services.AddHostedService<NameFrequencySnapshotLoader>();
            await RunHostWithHeartbeatAsync(
                bootstrapBuilder.Build(),
                "Carga da referencia de frequencias",
                TimeSpan.FromSeconds(15));
        }

        if (Environment.ExitCode != 0 || !await HasActiveNameFrequencyReferenceAsync(operationalSql))
            throw new InvalidOperationException("GENERATE_DRAFT exige uma referencia de frequencias ATIVA; carga/reativacao da referencia canonica nao foi concluida.");
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
    else if (operation == IbgeNominalUBootstrapReporter.Operation)
    {
        builder.Services.AddHostedService<IbgeNominalUBootstrapReporter>();
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

var host = builder.Build();
if (operation == NameFrequencySnapshotLoader.Operation)
{
    Console.WriteLine("Carga explícita da referência de frequências iniciada. O processo imprimirá um heartbeat a cada 15 segundos.");
    await RunHostWithHeartbeatAsync(host, "Carga da referência de frequências", TimeSpan.FromSeconds(15));
}
else
{
    await host.RunAsync();
}

static async Task RunHostWithHeartbeatAsync(IHost host, string activity, TimeSpan interval)
{
    var startedAt = DateTimeOffset.UtcNow;
    var runTask = host.RunAsync();

    while (!runTask.IsCompleted)
    {
        var completed = await Task.WhenAny(runTask, Task.Delay(interval));
        if (completed == runTask)
            break;

        var elapsed = DateTimeOffset.UtcNow - startedAt;
        Console.WriteLine($"{activity} em andamento há {elapsed.TotalSeconds:N0}s; processo ativo, aguarde...");
    }

    await runTask;
}

static async Task<bool> HasActiveNameFrequencyReferenceAsync(IOperationalSqlAdapter operationalSql)
{
    await using var connection = await operationalSql.OpenAsync(CancellationToken.None);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM ref.frequencia_nome_versao WHERE status='ATIVA' AND conteudo_sha256 IS NOT NULL;";
    var value = await command.ExecuteScalarAsync(CancellationToken.None);
    return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) == 1;
}


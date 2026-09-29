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

    // Derivado IBGE nominal versionado. Explicitamente requerido na preparacao
    // do ambiente; nao e tarefa residente, nem reexecuta MC em cache hit.
    if (operation == IbgeNominalUReferenceStore.EnsureOperation)
    {
        await using var referenceConnection =
            await operationalSql.OpenAsync(CancellationToken.None);
        var reference = await IbgeNominalUReferenceReader.ReadActiveReferenceAsync(
            referenceConnection, CancellationToken.None);
        var seed = builder.Configuration.GetValue("LinkageParameters:IbgeNominalU:Seed", 20260917);
        var pairs = Math.Clamp(
            builder.Configuration.GetValue("LinkageParameters:IbgeNominalU:PairCount", 1_000_000),
            10_000, 5_000_000);
        var person = await IbgeNominalUReferenceStore.EnsureAsync(
            referenceConnection, reference, "TODOS",
            new IbgeNominalUBootstrapOptions(seed, pairs), CancellationToken.None);
        var mother = await IbgeNominalUReferenceStore.EnsureAsync(
            referenceConnection, reference, "FEMININO",
            new IbgeNominalUBootstrapOptions(unchecked(seed + 1), pairs), CancellationToken.None);
        Console.WriteLine(
            $"Referencias u nominais prontas: origem={reference.Code}; " +
            $"NOME={person.Id}/{person.ResultSha256}; " +
            $"NOME_MAE={mother.Id}/{mother.ResultSha256}; seed={seed}; pares={pairs}.");
        return;
    }

    // A referência IBGE é pré-condição carregada explicitamente na preparação.
    // GENERATE_DRAFT faz somente a verificação leve; nunca carrega ou reativa.
    if (operation == "GENERATE_DRAFT")
        await GenerateDraftIbgePrecondition.RequireActiveAsync(
            () => HasActiveNameFrequencyReferenceAsync(operationalSql));

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
    command.CommandText =
        """
        SELECT COUNT(*)
        FROM ref.frequencia_nome_versao v
        WHERE v.status='ATIVA'
          AND DATALENGTH(v.conteudo_sha256)=32
          AND EXISTS (
              SELECT 1 FROM ref.frequencia_nome n
              WHERE n.frequencia_nome_versao_id=v.frequencia_nome_versao_id
                AND n.tipo='NOME')
          AND EXISTS (
              SELECT 1 FROM ref.frequencia_nome s
              WHERE s.frequencia_nome_versao_id=v.frequencia_nome_versao_id
                AND s.tipo='SOBRENOME');
        """;
    var value = await command.ExecuteScalarAsync(CancellationToken.None);
    return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) == 1;
}


using Jornada.Operational.Sql;
using Jornada.Pipeline.Coordination;
using Jornada.Linkage.Parameters.Worker;

// Explicitly read the freshly published SQL IBGE reference for the 30k preflight.
// This entry point never fits, certifies, or activates a model.
// A2: One fail-closed exception boundary for *every* CLI and fallback host
// operation. No unhandled .NET exception/message/stack is allowed on public
// stderr when an entry-point call fails. The nested calibrator retains its
// own 10 KiB reserved last-error protocol and is not double-logged.
var entryStatus = await V9WorkerEntryBoundary.ExecuteAsync(args, async guardedArgs =>
{
if (guardedArgs.Length > 0 && guardedArgs[0] == "--v9-tf-export-ibge-pin")
{
    await V9TfPinnedIbgePreflightCli.RunAsync(guardedArgs);
    return;
}

if (guardedArgs.Length > 0 && guardedArgs[0] == "--v9-prepare-gold-runner")
{
    await V9GoldRunnerVariantPreparation.RunAsync(guardedArgs);
    return;
}

// Read-only TRAIN pre-SQL capacity reproduction for the frozen 100k corpus.
// No SQL credential, worker, TF query, fitting, TEST labels or Gold.
if (guardedArgs.Length > 0 && guardedArgs[0] == "--v9-offline-train-capacity")
{
    await V9OfflineCalibratorCapacityCli.RunAsync(guardedArgs);
    return;
}

if (guardedArgs.Length > 0 && guardedArgs[0] is "--v9-calibrate" or "--v9-validate-finalists")
{
    try
    {
        await V9NestedCalibrationCli.RunAsync(guardedArgs);
    }
    catch (Exception)
    {
        // RunAsync has already written a safe failure code and reserved-log
        // record; never allow .NET's unhandled-exception handler to print
        // full exception.Message/stack/connection strings to public stderr.
        Environment.ExitCode = 1;
    }
    return;
}

// Read-only parity: the source V2 plan and its explicit-default configuration
// must deserialize to exactly the same effective CSharp calibration parameters.
if (guardedArgs.Length > 0 && guardedArgs[0] == "--v9-compare-calibration-plans")
{
    await V9CalibrationPlanParityCli.RunAsync(guardedArgs);
    return;
}

if (guardedArgs.Length > 0 && guardedArgs[0] is "--v9-adaptive-study" or "--v9-adaptive-census")
{
    await V9AdaptiveCalibrationStudyCli.RunAsync(guardedArgs);
    return;
}

// Frozen seed42 -> seed43 transfer: physical replay of pinned snapshots, never a fit.
if (guardedArgs.Length > 0 && guardedArgs[0] == "--v9-frozen-transfer")
{
    await V9FrozenTransferCli.RunAsync(guardedArgs);
    return;
}

// Find the exact, possibly unpublished calibration record before choosing
// a portable fit. SQL SELECT only; never launches a worker or runs training.
if (guardedArgs.Length > 0 && guardedArgs[0] == "--v9-audit-ref-inventory")
{
    await V9RefInventoryCli.RunAsync(guardedArgs);
    return;
}

// Opt-in, read-only SQL REF calibration inventory matched against a pinned V9
// portable fit. No host, fit, publication, or operational model activation.
if (guardedArgs.Length > 0 && guardedArgs[0] == "--v9-audit-ref-frozen-snapshot")
{
    await V9RefFittedSnapshotAuditCli.RunAsync(guardedArgs);
    return;
}

// Explicit new TRAIN-only V3 candidate, using physical recorded 81-cell
// counts from a SHA-pinned study. No TF re-fit, SQL, TEST, publication or DT-15.
if (guardedArgs.Length > 0 && guardedArgs[0] == V9FrozenJointTrainingDerivationCli.Flag)
{
    await V9FrozenJointTrainingDerivationCli.RunAsync(guardedArgs);
    return;
}

// Verify archived ORIGINAL V2 and derived V3 together without SQL/30k.
// The same fail-closed frozen-pair gate is used by the manual physical CLI.
if (guardedArgs.Length > 0 && guardedArgs[0] == V9FrozenJointIndependentComparisonCli.PreflightFlag)
{
    await V9FrozenJointIndependentComparisonCli.RunPreflightAsync(guardedArgs);
    return;
}

// Manual independent SQL validation of the NEW V3 against the original V2.
// Genuine frozen model bytes; no calibrator or operational activation.
if (guardedArgs.Length > 0 && guardedArgs[0] == V9FrozenJointIndependentComparisonCli.Flag)
{
    await V9FrozenJointIndependentComparisonCli.RunAsync(guardedArgs);
    return;
}

// Check SHA-verified TF fit suitability for a diagnostic Gold variant only.
// Does not write another fit, access SQL, alter a threshold or start a host.
if (guardedArgs.Length > 0 && guardedArgs[0] == "--v9-gold-frozen-tf-preflight")
{
    await V9FittedSnapshotArtifact.GoldTfDiagnosticPreflightAsync(guardedArgs);
    return;
}

// Portable artifact inspection never starts the host or connects to SQL.
if (guardedArgs.Length > 0 && guardedArgs[0] == "--v9-inspect-fitted-snapshot")
{
    await V9FittedSnapshotArtifact.InspectAsync(guardedArgs);
    return;
}

// Read-only opt-in physical diagnostic; never starts the operational worker.
if (guardedArgs.Length > 0 && guardedArgs[0] == "--v9-physical-probe-seed42")
{
    await V9PhysicalCalibrationEvidenceCli.RunAsync(guardedArgs);
    return;
}

const string EnsureNameFrequencySnapshotOperation = "ENSURE_NAME_FREQUENCY_SNAPSHOT";
const string CanonicalNameFrequencyReferenceCode = "CENSO2022_NOMES_BRASIL_V1";

var builder = Host.CreateApplicationBuilder(guardedArgs);
 // Host may swallow a BackgroundService exception after logging it. Redirect
 // all warning/error logs away from stock Console/EventSource providers to a
 // fixed-code logger, and preserve a nonzero exit code for fatal host errors.
builder.Logging.AddProvider(new V9WorkerSafeHostLoggerProvider());
builder.Logging.AddFilter((provider, _, level) =>
    V9WorkerSafeHostLoggerProvider.AllowProviderLog(provider, level));
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
        var ensureBuilder = Host.CreateApplicationBuilder(guardedArgs);
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

    // Derivado IBGE nominal versionado. Primeiro tenta importar o artefato portátil
    // pré-calculado da mesma fonte/método/seed/PairCount. Somente builds sem esse
    // artefato recaem no Monte Carlo completo; bancos DEV novos não repetem milhões
    // de comparações para uma referência pública congelada.
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
        var artifactPath = builder.Configuration.GetValue<string?>(
            "LinkageParameters:IbgeNominalU:PortableArtifactPath");

        var imported = await IbgeNominalUPortableArtifactStore.TryImportAsync(
            referenceConnection, reference, seed, pairs, artifactPath, CancellationToken.None);
        if (imported)
        {
            Console.WriteLine(
                $"Bootstrap nominal IBGE restaurado do artefato portátil validado; " +
                $"origem={reference.Code}; seed={seed}; pares={pairs}; sem Monte Carlo.");
        }
        else
        {
            Console.WriteLine(
                $"Artefato portátil IBGE nominal u não encontrado; executando Monte Carlo determinístico completo " +
                $"uma vez (seed={seed}; pares={pairs}).");
            _ = await AwaitWithHeartbeatAsync(
                IbgeNominalUReferenceStore.EnsureAsync(
                    referenceConnection, reference, "TODOS",
                    new IbgeNominalUBootstrapOptions(seed, pairs), CancellationToken.None),
                "Monte Carlo IBGE NOME",
                TimeSpan.FromSeconds(15));
            _ = await AwaitWithHeartbeatAsync(
                IbgeNominalUReferenceStore.EnsureAsync(
                    referenceConnection, reference, "FEMININO",
                    new IbgeNominalUBootstrapOptions(unchecked(seed + 1), pairs), CancellationToken.None),
                "Monte Carlo IBGE NOME_MAE",
                TimeSpan.FromSeconds(15));
        }

        var person = await IbgeNominalUReferenceStore.RequireAsync(
            referenceConnection, reference, "TODOS",
            new IbgeNominalUBootstrapOptions(seed, pairs), CancellationToken.None);
        var mother = await IbgeNominalUReferenceStore.RequireAsync(
            referenceConnection, reference, "FEMININO",
            new IbgeNominalUBootstrapOptions(unchecked(seed + 1), pairs), CancellationToken.None);
        Console.WriteLine(
            $"Referencias u nominais prontas: origem={reference.Code}; " +
            $"NOME={person.Id}/{person.ResultSha256}; " +
            $"NOME_MAE={mother.Id}/{mother.ResultSha256}; seed={seed}; pares={pairs}.");
        return;
    }

    if (operation == IbgeNominalUPortableArtifactStore.ExportOperation)
    {
        await using var referenceConnection =
            await operationalSql.OpenAsync(CancellationToken.None);
        var reference = await IbgeNominalUReferenceReader.ReadActiveReferenceAsync(
            referenceConnection, CancellationToken.None);
        var seed = builder.Configuration.GetValue("LinkageParameters:IbgeNominalU:Seed", 20260917);
        var pairs = Math.Clamp(
            builder.Configuration.GetValue("LinkageParameters:IbgeNominalU:PairCount", 1_000_000),
            10_000, 5_000_000);
        var output = builder.Configuration.GetValue<string?>(
            "LinkageParameters:IbgeNominalU:PortableArtifactOutputPath")
            ?? throw new InvalidOperationException(
                "LinkageParameters:IbgeNominalU:PortableArtifactOutputPath é obrigatório para exportar o artefato.");
        var exported = await IbgeNominalUPortableArtifactStore.ExportPersistedAsync(
            referenceConnection, reference, seed, pairs, output, CancellationToken.None);
        Console.WriteLine($"Artefato portátil IBGE nominal u exportado: {exported}");
        return;
    }

    // A primeira carga IBGE e seus derivados são pré-condição explícita do ambiente.
    // GENERATE_DRAFT apenas comprova o bootstrap persistido; nunca carrega, reativa
    // ou exige que a fonte original continue com status ATIVA.
    if (operation == "GENERATE_DRAFT")
    {
        var seed = builder.Configuration.GetValue("LinkageParameters:IbgeNominalU:Seed", 20260917);
        var pairs = Math.Clamp(
            builder.Configuration.GetValue("LinkageParameters:IbgeNominalU:PairCount", 1_000_000),
            10_000, 5_000_000);
        await using var bootstrapConnection = await operationalSql.OpenAsync(CancellationToken.None);
        _ = await PersistedIbgeBootstrapReferenceQuery.RequireAsync(
            bootstrapConnection, seed, pairs, CancellationToken.None);
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

});
if (entryStatus != 0)
    Environment.ExitCode = entryStatus;

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

static async Task<T> AwaitWithHeartbeatAsync<T>(Task<T> task, string activity, TimeSpan interval)
{
    var startedAt = DateTimeOffset.UtcNow;
    while (!task.IsCompleted)
    {
        var completed = await Task.WhenAny(task, Task.Delay(interval));
        if (completed == task)
            break;

        var elapsed = DateTimeOffset.UtcNow - startedAt;
        Console.WriteLine($"{activity} em andamento há {elapsed.TotalSeconds:N0}s; processo ativo, aguarde...");
    }

    return await task;
}

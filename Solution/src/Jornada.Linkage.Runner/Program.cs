using Jornada.Linkage.Parameters.Worker;
using Jornada.Operational.Sql;
using Jornada.Contracts;
using Jornada.Pipeline.Coordination;
using Jornada.Linkage.Runner;
using Jornada.Bronze.Storage;

// Explicit disposable 1k DEV harness only; productive V9 dispatch remains denied.
if (args.FirstOrDefault() == V9DevE2EFullCommand.Flag)
{
    try
    {
        await V9DevE2EFullCommand.RunAsync(args);
    }
    catch (Exception error)
    {
        V9DevE2EFullCommand.ReportFailure(error);
        Environment.ExitCode = 1;
    }
    return;
}

// Read-only proof that the real derived V3 is executable by the actual V9
// multipass SQL query builder and frozen TF+joint scorer; NO DB or publication.
if (args.FirstOrDefault() == V9FrozenGoldRuntimeProbeCli.Flag)
{
    await V9FrozenGoldRuntimeProbeCli.RunAsync(args);
    return;
}

if (args.FirstOrDefault() == V9LowerNonMatchStudyCli.Flag)
{
    await V9LowerNonMatchStudyCli.RunAsync(args);
    return;
}

if (args.FirstOrDefault() == V9GoldJointTrainMatrixCommand.PreflightFlag)
{
    await V9GoldJointTrainMatrixCommand.RunPlanPreflightAsync(args);
    return;
}

if (args.FirstOrDefault() == V9GoldJointTrainMatrixCommand.Flag)
{
    await V9GoldJointTrainMatrixCommand.RunAsync(args);
    return;
}

if (args.FirstOrDefault() == GoldTrainLowerNonMatchMatrixCommand.Flag)
{
    await GoldTrainLowerNonMatchMatrixCommand.RunAsync(args);
    return;
}

if (args.FirstOrDefault() == GoldTrainThreeOrderAggregateCommand.Flag)
{
    await GoldTrainThreeOrderAggregateCommand.RunAsync(args);
    return;
}

if (args.FirstOrDefault() == V9GoldHarnessDatabasePreparation.Flag)
{
    await V9GoldHarnessDatabasePreparation.RunAsync(args);
    return;
}

if (args.FirstOrDefault() == V9GoldIndependentHoldoutProvenance.Flag)
{
    var holdoutConnection = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
        ?? throw new InvalidOperationException("Conexão LocalDB descartável ausente.");
    await V9GoldIndependentHoldoutProvenance.RunAsync(args, holdoutConnection);
    return;
}

if (args.FirstOrDefault() == "--v9-gold-holdout-harness")
{
    var holdoutConnection = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
        ?? throw new InvalidOperationException("Holdout exige LocalDB descartável.");
    await V9GoldHarnessCommand.RunIndependentHoldoutAsync(args, holdoutConnection);
    return;
}

if (args.FirstOrDefault() == V9GoldHarnessCommand.JointTrainTrialFlag)
{
    var jointConnection = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
        ?? throw new InvalidOperationException("Joint TRAIN exige LocalDB descartável.");
    _ = await V9GoldHarnessCommand.RunJointTrainTrialAsync(args, jointConnection);
    return;
}

if (args.FirstOrDefault() == V9GoldHarnessCommand.Flag)
{
    var harnessConnection = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
        ?? throw new InvalidOperationException("Conexão de JornadaHarness descartável ausente.");
    await V9GoldHarnessCommand.RunAsync(args, harnessConnection);
    return;
}

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

if (NameAbbreviationAuditCommand.IsRequested(args))
{
    var auditBuilder = Host.CreateApplicationBuilder(args);
    var auditConnectionString = auditBuilder.Configuration.GetConnectionString("Jornada")
        ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
    var auditOperationalSql = new OperationalSqlAdapter(auditConnectionString);
    await NameAbbreviationAuditCommand.ExecuteAsync(
        args,
        auditBuilder.Configuration,
        auditOperationalSql,
        CancellationToken.None);
    return;
}

if (PriorCounterfactualAuditCommand.IsRequested(args))
{
    var auditBuilder = Host.CreateApplicationBuilder(args);
    var auditConnectionString = auditBuilder.Configuration.GetConnectionString("Jornada")
        ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
    var auditOperationalSql = new OperationalSqlAdapter(auditConnectionString);
    await PriorCounterfactualAuditCommand.ExecuteAsync(
        args,
        auditBuilder.Configuration,
        auditOperationalSql,
        CancellationToken.None);
    return;
}

if (Dt05SnapshotMaintenanceCommand.IsRequested(args))
{
    var maintenanceBuilder = Host.CreateApplicationBuilder(args);
    await Dt05SnapshotMaintenanceCommand.ExecuteAsync(
        args,
        maintenanceBuilder.Configuration,
        maintenanceBuilder.Environment,
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
    Console.WriteLine("Auditoria DEV read-only de abreviação compatível:");
    Console.WriteLine("  --name-abbreviation-audit-run <linkage_run_id>");
    Console.WriteLine("  --name-abbreviation-audit-output <arquivo.json>");
    Console.WriteLine();
    Console.WriteLine("Contrafactual DEV read-only do prior candidato-par:");
    Console.WriteLine("  --prior-counterfactual-run <linkage_run_id>");
    Console.WriteLine("  --prior-counterfactual-output <arquivo.json>");
    Console.WriteLine();
    Console.WriteLine("Auditoria DEV/HML read-only de blocking por passe:");
    Console.WriteLine("  --blocking-pass-audit-labels <arquivo.csv>");
    Console.WriteLine("  --blocking-pass-audit-output <arquivo.json>");
    Console.WriteLine("  [--blocking-pass-audit-compare-combined true]  # D, C e D∪C; SQL real, somente leitura");
    Console.WriteLine();
    Console.WriteLine("Manutenção DT-05 de snapshots (audit read-only por padrão):");
    Console.WriteLine("  --dt05-snapshot-maintenance-report <arquivo.json>");
    Console.WriteLine("  [--dt05-snapshot-min-age-hours 24]");
    Console.WriteLine("  [--dt05-snapshot-gc]  # remove somente órfãos/temp antigos; Development/Test");
    return;
}

var options = LinkageRunOptions.Parse(args);
var builder = Host.CreateApplicationBuilder(args);
var jornadaConnectionString = builder.Configuration.GetConnectionString("Jornada")
    ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
 
// Snapshot the effective host configuration once: no reload during this run.
// Do not log or persist the full configuration (it can contain secrets).
var frozenConfiguration = new ConfigurationBuilder()
    .AddInMemoryCollection(builder.Configuration.AsEnumerable()
        .Where(entry => entry.Value is not null)
        .ToDictionary(entry => entry.Key, entry => entry.Value,
            StringComparer.OrdinalIgnoreCase))
    .Build();
SqlProbabilisticIdentityLinkage? pinnedLinkage = null;
// The Operator Master changes configuration only while the run-once Runner is stopped.
if (options.Mode != LinkageRunType.REPLAY)
{
    // V9 file-only selection is intentionally fail-closed until the productive
    // cohort/batch adapter is certified. Never silently execute the legacy SQL model.
    var v9SelectionFile = frozenConfiguration["Linkage:V9SelectionFile"];
    if (!string.IsNullOrWhiteSpace(v9SelectionFile))
    {
        // A V9 package must never silently override a different operator
        // selection, or an explicit --model-version. Reject before file/SQL IO.
        if (frozenConfiguration.GetValue<int?>("Linkage:SelectedModelVersion") != 9
            || options.ModelVersion is int requestedV9Version && requestedV9Version != 9)
            throw new InvalidOperationException(
                "RUNNER_V9_SELECTION_VERSION_MISMATCH: pacote V9 exige versão 9 na configuração e no argumento opcional.");
        // Validate the same immutable snapshot AND construct the actual V9 cohort
        // scorer before failing closed. Never resolve the legacy SQL model here.
        var frozenV9 = await V9ConfiguredArtifactLoader.LoadCohortRuntimeAsync(v9SelectionFile);
        var v9Boundary = new V9OperationalRunBoundary(frozenV9);
        Console.WriteLine($"RUNNER_V9_ARTIFACT_VERIFIED sha256={v9Boundary.Runtime.WinnerSha256}");
        // RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED remains mandatory here.
        v9Boundary.EnsureProductiveCertified();
    }

    // V9 must never fall through to the legacy SQL-model loader merely because
    // the Operator Master omitted its file selector. Reject before opening SQL.
    if (frozenConfiguration.GetValue<int?>("Linkage:SelectedModelVersion") == 9
        || options.ModelVersion == 9)
        throw new InvalidOperationException(
            "RUNNER_V9_SELECTION_MISSING: modelo V9 exige Linkage:V9SelectionFile; fallback SQL proibido.");

    var configuredVersion = frozenConfiguration.GetValue<int?>("Linkage:SelectedModelVersion");
    if (configuredVersion is not > 0)
        throw new InvalidOperationException(
            "RUNNER_MODEL_CONFIGURATION_MISSING: Linkage:SelectedModelVersion deve ser definido pelo Operador Master.");
    if (options.ModelVersion is int requestedVersion && requestedVersion != configuredVersion)
        throw new InvalidOperationException(
            "RUNNER_MODEL_CONFIGURATION_MISMATCH: argumento --model-version diverge da configuração do Operador Master.");

    var preflightSql = new OperationalSqlAdapter(jornadaConnectionString);
    pinnedLinkage = new SqlProbabilisticIdentityLinkage(
        frozenConfiguration, preflightSql,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<SqlProbabilisticIdentityLinkage>.Instance);
    // Load the model AND its mandatory ruleset into the same cache later used by the worker.
    var selectedModel = await pinnedLinkage.GetModelByVersionAsync(configuredVersion.Value, CancellationToken.None);
    if (selectedModel.Version != configuredVersion.Value || selectedModel.BlockingContract is null)
        throw new InvalidOperationException("RUNNER_MODEL_CONTRACT_INCOMPATIBLE: modelo sem contrato operacional compatível.");

    pinnedLinkage.PinStartupModel(selectedModel);

    var fingerprintInput = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
    {
        selectedModel.ModelId, selectedModel.Version, selectedModel.AlgorithmVersion,
        selectedModel.Threshold, selectedModel.ConflictMargin,
        selectedModel.BlockingContract.RuleSetVersion,
        selectedModel.BlockingContract.RuleSetFingerprintSha256,
        selectedModel.BlockingContract.ProjectionSchemaVersion,
        selectedModel.BlockingContract.ProjectionFingerprintSha256
    });
    var fingerprint = Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(fingerprintInput)).ToLowerInvariant();
    Console.WriteLine($"RUNNER_MODEL_PINNED modelId={selectedModel.ModelId} version={selectedModel.Version} contractSha256={fingerprint}");
    options = options with { ModelVersion = configuredVersion.Value };
}
builder.Services.AddSingleton<IConfiguration>(frozenConfiguration);
builder.Services.AddSingleton(options);
var operationalSql = new OperationalSqlAdapter(jornadaConnectionString);
builder.Services.AddSingleton<IOperationalSqlAdapter>(operationalSql);
builder.Services.AddSingleton(new SqlPipelineCoordinator(
    operationalSql,
    TimeSpan.FromSeconds(Math.Max(5, builder.Configuration.GetValue("PipelineCoordination:HeartbeatSeconds", 5))),
    TimeSpan.FromSeconds(Math.Max(1, builder.Configuration.GetValue("PipelineCoordination:ExclusiveIntentTimeoutSeconds", 5)))));
var bronzeProvider = builder.Configuration["BronzeStorage:Provider"] ?? "FileSystem";
if (!string.Equals(bronzeProvider, "FileSystem", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException($"BronzeStorage:Provider não suportado pelo Runner: {bronzeProvider}.");
var configuredBronzeRoot = builder.Configuration["BronzeStorage:RootPath"];
var bronzeRoot = string.IsNullOrWhiteSpace(configuredBronzeRoot)
    ? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "data", "bronze"))
    : Path.GetFullPath(configuredBronzeRoot);
builder.Services.AddSingleton<IBronzeObjectStore>(_ => new FileSystemBronzeObjectStore(bronzeRoot));
builder.Services.AddSingleton(sp => new Dt05ReplayManifestPublisher(
    sp.GetRequiredService<IBronzeObjectStore>(), bronzeRoot));
builder.Services.AddSingleton(sp => new Dt05CandidateStateSnapshotPublisher(
    sp.GetRequiredService<IOperationalSqlAdapter>(), bronzeRoot));
builder.Services.AddSingleton(sp => new Dt05BlockingProjectionSnapshotPublisher(
    sp.GetRequiredService<IOperationalSqlAdapter>(), bronzeRoot));
builder.Services.AddSingleton<Dt05ReplaySql>();
builder.Services.AddSingleton(_ => new Dt05HistoricalCandidateStateVerifier(bronzeRoot));
builder.Services.AddSingleton(_ => new Dt05HistoricalBlockingProjectionVerifier(bronzeRoot));
if (pinnedLinkage is not null)
    builder.Services.AddSingleton<IProbabilisticIdentityLinkage>(pinnedLinkage);
else
    builder.Services.AddSingleton<IProbabilisticIdentityLinkage, SqlProbabilisticIdentityLinkage>();
builder.Services.AddSingleton<IProbabilisticLinkageBatchRunner, ProbabilisticLinkageBatchRunner>();
builder.Services.AddHostedService<LinkageRunnerWorker>();

await builder.Build().RunAsync();

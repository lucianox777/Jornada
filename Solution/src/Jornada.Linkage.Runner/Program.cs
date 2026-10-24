using Jornada.Linkage.Parameters.Worker;
using Jornada.Operational.Sql;
using Jornada.Contracts;
using Jornada.Pipeline.Coordination;
using Jornada.Linkage.Runner;
using Jornada.Bronze.Storage;

// One-shot diagnostic for the owned, empty Windows LocalDB harness only.
// Read-only: never changes the canonical procedure, SQL data or TEST.
if (args.FirstOrDefault() == "--v9-gold-dt10-patch-preflight")
{
    if (args.Length != 1 || !OperatingSystem.IsWindows())
        throw new ArgumentException("Preflight exige Windows e sem argumentos adicionais.");
    var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
        ?? throw new InvalidOperationException("Conexão de LocalDB descartável ausente.");
    V9GoldHarnessCommand.ValidateDisposableConnection(cs);
    await using var db = new Microsoft.Data.SqlClient.SqlConnection(cs);
    await db.OpenAsync();
    await using var check = new Microsoft.Data.SqlClient.SqlCommand("""
        IF DB_NAME()<>N'JornadaHarness'
           OR COALESCE(TRY_CONVERT(int,SERVERPROPERTY(N'IsLocalDB')),0)<>1
           OR NOT EXISTS(SELECT 1 FROM sys.extended_properties
                WHERE class=0 AND name=N'Jornada.EnvironmentProfile'
                  AND CONVERT(nvarchar(200),value)=N'Development')
           OR NOT EXISTS(SELECT 1 FROM sys.extended_properties
                WHERE class=0 AND name=N'Jornada.V9.ExperimentalHarness'
                  AND CONVERT(nvarchar(200),value)=N'PINNED_FIT_PREVIEW_NO_ACTIVATION')
          THROW 51900, 'Proibido consultar módulo fora do LocalDB marcado.',1;
        SELECT OBJECT_DEFINITION(
            OBJECT_ID(N'identidade.sp_publicar_resolucao_progressiva_linkage_lote',N'P'));
        """, db);
    var definition = await check.ExecuteScalarAsync() as string
        ?? throw new InvalidDataException("Procedure DT-10 indisponível no LocalDB descartável.");
    var oldGuard = "l.raw_motivo NOT LIKE N'SEM_CANDIDATO_%'";
    var expectedHeader = "CREATE OR ALTER PROCEDURE identidade.sp_publicar_resolucao_progressiva_linkage_lote";
    var declarationMatch = definition.Contains(expectedHeader, StringComparison.OrdinalIgnoreCase);
    var guardOccurrences = definition.Split(oldGuard, StringSplitOptions.None).Length - 1;
    var alreadyPatched = definition.Contains(
        V9GoldLowerNonMatchExperiment.PublicationReason, StringComparison.Ordinal);
    var normalizedHeader = System.Text.RegularExpressions.Regex.IsMatch(
        definition, @"(?:CREATE|ALTER)(?:\s+OR\s+ALTER)?\s+(?:PROC|PROCEDURE)\s+(?:\[?identidade\]?)\s*\.\s*\[?sp_publicar_resolucao_progressiva_linkage_lote\]?",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase |
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(definition))).ToLowerInvariant();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {
        check = "OWNED_LOCALDB_ONLY_DT10_MODULE_READ_ONLY",
        moduleSha256 = sha, moduleLength = definition.Length,
        exactDeclarationMatch = declarationMatch,
        recognizedProcedureDeclaration = normalizedHeader,
        exactOldGuardOccurrences = guardOccurrences,
        experimentalReasonAlreadyPresent = alreadyPatched,
        hasThrow51810 = definition.Contains("THROW 51810", StringComparison.OrdinalIgnoreCase)
    }));
    // Running the same pure fail-closed logic as the physical trial,
    // without modifying the SQL procedure or executing Gold waves.
    _ = V9GoldDisposableLowerProcedurePatch.Apply(definition);
    Console.WriteLine("DT10_PATCH_DRY_RUN_ACCEPTED");
    return;
}

if (args.FirstOrDefault() == V9LowerNonMatchStudyCli.Flag)
{
    await V9LowerNonMatchStudyCli.RunAsync(args);
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
builder.Services.AddSingleton<IProbabilisticIdentityLinkage, SqlProbabilisticIdentityLinkage>();
builder.Services.AddSingleton<IProbabilisticLinkageBatchRunner, ProbabilisticLinkageBatchRunner>();
builder.Services.AddHostedService<LinkageRunnerWorker>();

await builder.Build().RunAsync();

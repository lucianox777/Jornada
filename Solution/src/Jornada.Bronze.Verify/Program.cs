using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Bronze.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

if (args.Any(a => a.Equals("--help", StringComparison.OrdinalIgnoreCase) || a.Equals("-h", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine("""
        Jornada.Bronze.Verify

          --entrega-id <UUID>          opcional; verifica somente a Entrega indicada
          --minimum-count <N>          padrão 0; falha se menos referências forem verificadas
          --deep                       inventaria também objetos físicos e órfãos content-addressed
          --report <arquivo.json>      grava relatório machine-readable
          --gc-plan <arquivo.json>     gera plano DRY-RUN de órfãos elegíveis; NUNCA exclui
          --orphan-grace-hours <N>     padrão 24 para o plano de GC

        Sem --entrega-id, verifica todas as referências Bronze persistidas.
        --deep/--gc-plan podem ser custosos: percorrem os 65.536 buckets físicos.
        """);
    return;
}

Guid? entregaFilter = null;
var minimumCount = 0;
var deep = false;
string? reportPath = null;
string? gcPlanPath = null;
var orphanGraceHours = 24;
for (var i = 0; i < args.Length; i++)
{
    if (args[i].Equals("--entrega-id", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
    {
        if (!Guid.TryParse(args[++i], out var parsed)) throw new ArgumentException("--entrega-id deve ser UUID válido.");
        entregaFilter = parsed;
    }
    else if (args[i].Equals("--minimum-count", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
    {
        if (!int.TryParse(args[++i], out minimumCount) || minimumCount < 0) throw new ArgumentException("--minimum-count deve ser inteiro >= 0.");
    }
    else if (args[i].Equals("--deep", StringComparison.OrdinalIgnoreCase)) deep = true;
    else if (args[i].Equals("--report", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) reportPath = args[++i];
    else if (args[i].Equals("--gc-plan", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) { gcPlanPath = args[++i]; deep = true; }
    else if (args[i].Equals("--orphan-grace-hours", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
    {
        if (!int.TryParse(args[++i], out orphanGraceHours) || orphanGraceHours < 1) throw new ArgumentException("--orphan-grace-hours deve ser inteiro >= 1.");
    }
}

var configuration = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json", optional: true).AddEnvironmentVariables().Build();
var connectionString = configuration.GetConnectionString("Jornada") ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
var root = configuration["BronzeStorage:RootPath"];
if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root)) throw new InvalidOperationException("BronzeStorage:RootPath absoluto é obrigatório para verificação de restore.");
var store = new FileSystemBronzeObjectStore(root);

var checkedCount = 0; var missing = 0; var divergent = 0; var unavailable = 0; var keyHashMismatch = 0; var metadataConflicts = 0;
var referenced = new HashSet<string>(StringComparer.Ordinal);
var metadata = new Dictionary<string, (string Sha, long Length)>(StringComparer.Ordinal);
await using (var connection = new SqlConnection(connectionString))
{
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT objeto_chave,payload_sha256,tamanho_bytes,entrega_id
        FROM bronze.entrega_arquivo
        WHERE estado_armazenamento='DISPONIVEL'
          AND (@entrega_id IS NULL OR entrega_id=@entrega_id)
        ORDER BY recebido_em,entrega_id;
        """;
    command.Parameters.Add("@entrega_id", System.Data.SqlDbType.UniqueIdentifier).Value = (object?)entregaFilter ?? DBNull.Value;
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var key = reader.GetString(0); var sha = reader.GetString(1).Trim().ToLowerInvariant(); var length = reader.GetInt64(2); var entrega = reader.GetGuid(3);
        checkedCount++; referenced.Add(key);
        try
        {
            var keyHash = BronzeObjectCoordination.Sha256FromObjectKey(key);
            if (!string.Equals(keyHash, sha, StringComparison.Ordinal)) { keyHashMismatch++; Console.Error.WriteLine($"KEY_HASH_MISMATCH entrega={entrega} key={key}"); }
        }
        catch (InvalidDataException ex) { keyHashMismatch++; Console.Error.WriteLine($"KEY_INVALID entrega={entrega} key={key}: {ex.Message}"); }
        if (metadata.TryGetValue(key, out var prior) && (prior.Sha != sha || prior.Length != length)) metadataConflicts++;
        else metadata[key] = (sha, length);
        try { await store.VerifyAsync(key, sha, length, CancellationToken.None); }
        catch (BronzeObjectNotFoundException ex) { missing++; Console.Error.WriteLine($"MISSING entrega={entrega} key={key}: {ex.Message}"); }
        catch (BronzeObjectIntegrityException ex) { divergent++; Console.Error.WriteLine($"DIVERGENT entrega={entrega} key={key}: {ex.Message}"); }
        catch (BronzeStorageUnavailableException ex) { unavailable++; Console.Error.WriteLine($"UNAVAILABLE entrega={entrega} key={key}: {ex.Message}"); }
    }
}

var physicalCount = 0; var orphanCount = 0; var physicalDivergent = 0; var physicalUnavailable = 0;
var cutoff = DateTimeOffset.UtcNow.AddHours(-orphanGraceHours);
var plan = new List<GcCandidate>();
if (deep)
{
    var maintenance = (IBronzeObjectMaintenanceStore)store;
    for (var bucket = 0; bucket <= 0xffff; bucket++)
    {
        await foreach (var candidate in maintenance.EnumerateCanonicalObjectsInBucketAsync(bucket, null, DateTimeOffset.MaxValue, CancellationToken.None))
        {
            physicalCount++;
            string hash;
            try
            {
                hash = BronzeObjectCoordination.Sha256FromObjectKey(candidate.ObjectKey);
                await store.VerifyAsync(candidate.ObjectKey, hash, candidate.Length, CancellationToken.None);
            }
            catch (BronzeObjectIntegrityException ex) { physicalDivergent++; Console.Error.WriteLine($"PHYSICAL_DIVERGENT key={candidate.ObjectKey}: {ex.Message}"); continue; }
            catch (BronzeStorageUnavailableException ex) { physicalUnavailable++; Console.Error.WriteLine($"PHYSICAL_UNAVAILABLE key={candidate.ObjectKey}: {ex.Message}"); continue; }
            catch (InvalidDataException ex) { physicalDivergent++; Console.Error.WriteLine($"PHYSICAL_INVALID key={candidate.ObjectKey}: {ex.Message}"); continue; }
            if (referenced.Contains(candidate.ObjectKey)) continue;
            orphanCount++;
            if (candidate.LastWriteUtc <= cutoff)
                plan.Add(new GcCandidate(candidate.ObjectKey, hash, candidate.Length, candidate.LastWriteUtc, "UNREFERENCED_AND_OLDER_THAN_GRACE"));
        }
    }
}

plan.Sort((a,b) => string.CompareOrdinal(a.ObjectKey,b.ObjectKey));
var insufficient = checkedCount < minimumCount;
var status = missing == 0 && divergent == 0 && unavailable == 0 && keyHashMismatch == 0 && metadataConflicts == 0 && physicalDivergent == 0 && physicalUnavailable == 0 && !insufficient ? "PASS" : "FAIL";
var report = new
{
    schemaVersion = 1, status, checkedReferences = checkedCount, minimumCount, missing, divergent, unavailable, keyHashMismatch, metadataConflicts,
    deep, physicalCount, orphanCount, physicalDivergent, physicalUnavailable, gcEligibleCount = plan.Count,
    entregaFilter = entregaFilter?.ToString(), generatedAtUtc = DateTimeOffset.UtcNow
};
Console.WriteLine($"Bronze verify: status={status}; checked={checkedCount}; missing={missing}; divergent={divergent}; unavailable={unavailable}; keyMismatch={keyHashMismatch}; metadataConflicts={metadataConflicts}; physical={physicalCount}; orphans={orphanCount}; gcEligible={plan.Count}");
if (reportPath is not null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, BronzeVerifyJson.Options) + Environment.NewLine);
}
if (gcPlanPath is not null)
{
    var candidatesJson = JsonSerializer.Serialize(plan, BronzeVerifyJson.Options);
    var planSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(candidatesJson))).ToLowerInvariant();
    var gcPlan = new { schemaVersion = 1, mode = "DRY_RUN_ONLY", generatedAtUtc = DateTimeOffset.UtcNow, orphanGraceHours, cutoffUtc = cutoff, candidateCount = plan.Count, candidatesSha256 = planSha, candidates = plan,
        note = "Plano não executa deleção. Cada candidato deve ser revalidado sob applock imediatamente antes de qualquer expurgo pelo worker." };
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(gcPlanPath))!);
    await File.WriteAllTextAsync(gcPlanPath, JsonSerializer.Serialize(gcPlan, BronzeVerifyJson.Options) + Environment.NewLine);
}
if (insufficient) Console.Error.WriteLine($"INSUFFICIENT: checked={checkedCount}; minimum={minimumCount}.");
Environment.ExitCode = status == "PASS" ? 0 : 2;

internal static class BronzeVerifyJson { public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }; }
internal sealed record GcCandidate(string ObjectKey, string Sha256, long Length, DateTimeOffset LastWriteUtc, string Reason);

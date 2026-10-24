using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>Create-only LocalDB provisioning; never upgrades, resets or deletes an existing database.</summary>
public static class V9GoldHarnessDatabasePreparation
{
    public const string Flag = "--v9-gold-harness-create";
    public const string VerifyLowerPatchFlag = "--verify-disposable-lower-patch";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static async Task RunAsync(string[] args)
    {
        var verifyLowerPatch = args.Length == 4 && args[3] == VerifyLowerPatchFlag;
        if ((args.Length != 3 && !verifyLowerPatch) || args[0] != Flag || !OperatingSystem.IsWindows())
            throw new ArgumentException("Windows: --v9-gold-harness-create <diretório novo> <Solution/database> [--verify-disposable-lower-patch]");
        var output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Preparação exige diretório novo; reuso/limpeza proibidos.");
        var schema = Path.GetFullPath(args[2]);
        var files = new[] { "Jornada_Fase1.sql" }.Concat(File.ReadAllLines(Path.Combine(schema, "migrations", "manifest.txt"))
            .Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith('#'))).ToArray();
        var batches = new List<(string File, string Sql)>();
        foreach (var relative in files)
        {
            var file = Path.GetFullPath(Path.Combine(schema, relative));
            if (!file.StartsWith(schema + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Manifesto aponta para fora do schema canônico.");
            var content = await File.ReadAllTextAsync(file);
            if (Regex.IsMatch(content, @"^\s*(USE\s|:connect\b|:r\s)", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new InvalidDataException("Schema de ensaio não pode trocar banco nem incluir scripts externos.");
            batches.AddRange(Regex.Split(content, @"^\s*GO\s*(?:--[^\r\n]*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => (relative, x)));
        }
        Directory.CreateDirectory(output);
        var instance = "JornadaV9Gold" + DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        await WriteNewAsync(Path.Combine(output, "owned-instance.json"), new { instance, database = "JornadaHarness", createOnly = true, canonicalFiles = files.Length });
        try
        {
            await LocalDbAsync("create", instance);
            await LocalDbAsync("start", instance);
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = "(localdb)\\" + instance, InitialCatalog = "master", IntegratedSecurity = true,
                Encrypt = SqlConnectionEncryptOption.Optional
            };
            var data = Path.Combine(output, "sql-data");
            Directory.CreateDirectory(data);
            // Explicit new absolute file names prevent collision with another instance's default directory.
            var mdf = Path.Combine(data, "JornadaHarness.mdf").Replace("'", "''", StringComparison.Ordinal);
            var ldf = Path.Combine(data, "JornadaHarness_log.ldf").Replace("'", "''", StringComparison.Ordinal);
            await using (var master = new SqlConnection(builder.ConnectionString))
            {
                await master.OpenAsync();
                await using var create = new SqlCommand($"""
                    IF DB_ID(N'JornadaHarness') IS NOT NULL THROW 51900,'Banco já existe; criação recusada.',1;
                    CREATE DATABASE JornadaHarness ON PRIMARY(NAME=N'JornadaHarness',FILENAME=N'{mdf}')
                        LOG ON(NAME=N'JornadaHarness_log',FILENAME=N'{ldf}');
                    """, master) { CommandTimeout = 120 };
                await create.ExecuteNonQueryAsync();
            }
            builder.InitialCatalog = "JornadaHarness";
            V9GoldHarnessCommand.ValidateDisposableConnection(builder.ConnectionString);
            await using var sql = new SqlConnection(builder.ConnectionString);
            await sql.OpenAsync();
            foreach (var batch in batches)
            {
                await using (var check = new SqlCommand("IF DB_NAME()<>N'JornadaHarness' THROW 51900,'Schema fora do banco de ensaio.',1;", sql))
                    await check.ExecuteNonQueryAsync();
                // CREATE VIEW/PROCEDURE must remain the first statement of its batch.
                await using var command = new SqlCommand(batch.Sql, sql)
                { CommandTimeout = 900 };
                await command.ExecuteNonQueryAsync();
            }
            await using (var mark = new SqlCommand("""
                EXEC sys.sp_addextendedproperty @name=N'Jornada.EnvironmentProfile',@value=N'Development';
                EXEC sys.sp_addextendedproperty @name=N'Jornada.V9.ExperimentalHarness',@value=N'PINNED_FIT_PREVIEW_NO_ACTIVATION';
                """, sql))
                await mark.ExecuteNonQueryAsync();
            // Explicit opt-in physical preflight: exercise the SAME SQL
            // OBJECT_DEFINITION and guarded temporary DT-10 patch that the
            // lower-band trial uses. No TRAIN, TEST or operational database;
            // this empty harness is a uniquely owned disposable LocalDB.
            // Never verify by changing or weakening a canonical migration.
            if (verifyLowerPatch)
            {
                await using var read = new SqlCommand(
                    "SELECT OBJECT_DEFINITION(OBJECT_ID(N'identidade.sp_publicar_resolucao_progressiva_linkage_lote'));",
                    sql);
                var installedDefinition = await read.ExecuteScalarAsync() as string
                    ?? throw new InvalidDataException("LocalDB disposable DT-10 definition missing.");
                var experimentalDefinition = V9GoldDisposableLowerProcedurePatch.Apply(installedDefinition);
                await using var execute = new SqlCommand(experimentalDefinition, sql) { CommandTimeout = 900 };
                await execute.ExecuteNonQueryAsync();
                Console.WriteLine("Disposable DT-10 lower-band patch compiled in isolated LocalDB.");
            }
            await WriteNewAsync(Path.Combine(output, "schema-ready.json"), new
            {
                instance, database = "JornadaHarness", canonicalFiles = files.Length,
                dataRoot = data, completed = true, syntheticObservationsInserted = 0, certified = false, activated = false
            });
            Console.WriteLine(JsonSerializer.Serialize(new { instance, database = "JornadaHarness", completed = true }));
        }
        catch (Exception error)
        {
            await WriteNewAsync(Path.Combine(output, "preparation-invalidated.json"), new { instance, exceptionType = error.GetType().Name, error.Message, usable = false });
            throw;
        }
    }

    private static async Task LocalDbAsync(string verb, string instance)
    {
        using var process = new Process { StartInfo = new("SqlLocalDB.exe") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true } };
        process.StartInfo.ArgumentList.Add(verb); process.StartInfo.ArgumentList.Add(instance);
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await output;
        if (process.ExitCode != 0) throw new InvalidOperationException("SqlLocalDB " + verb + ": " + await error);
        await error;
    }

    private static async Task WriteNewAsync<T>(string file, T value)
    {
        await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, value, Json);
    }
}

using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Offline ref-only archive of the canonical synthetic V9 corpus.
/// This command never fits, selects, publishes or activates a model.
/// </summary>
internal static class V9RefSyntheticCorpusStoreCli
{
    private const string Code = "V9_SEED42_30000_CSHARP_V1";
    private static readonly string[] Names =
    [
        "pessoas_verdade.csv",
        "observacoes.csv",
        "gabarito.json",
        "generation-manifest.json"
    ];

    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 3 || (args[1] != "import" && args[1] != "export"))
            throw new ArgumentException(
                "Usage: --v9-ref-corpus-store <import|export> <corpus-directory>");

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings__Jornada is required for explicit ref archival.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var expected = await ReadExpectedShaAsync(connection);
        if (args[1] == "import")
        {
            await ImportAsync(connection, expected, Path.GetFullPath(args[2]));
            Console.WriteLine("PASS: four immutable synthetic files verified and archived under ref; no model operations.");
        }
        else
        {
            await ExportAsync(connection, expected, Path.GetFullPath(args[2]));
            Console.WriteLine("PASS: four ref files recovered with exact SHA256; no synthetic regeneration.");
        }
    }

    private static async Task<Dictionary<string, string>> ReadExpectedShaAsync(SqlConnection connection)
    {
        const string sql = """
            SELECT corpus_sha256, observacoes_sha256, gabarito_sha256, manifesto_sha256
            FROM ref.corpus_sintetico_versao_v9
            WHERE codigo = @codigo AND origem = N'SINTETICO_IBGE'
              AND status = N'EVIDENCIA_NAO_OPERACIONAL'
            """;
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.Add("@codigo", SqlDbType.NVarChar, 120).Value = Code;
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidDataException("Approved immutable synthetic ref ledger not found.");
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < Names.Length; i++)
            hashes.Add(Names[i], reader.GetString(i));
        if (await reader.ReadAsync())
            throw new InvalidDataException("Duplicate synthetic provenance version.");
        return hashes;
    }

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void CheckHash(string file, string expected, string actual)
    {
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Synthetic ref SHA256 mismatch: " + file);
    }

    private static async Task CheckCompleteAsync(SqlConnection connection)
    {
        await using var cmd = new SqlCommand(
            "EXEC ref.sp_exigir_corpus_sintetico_v9_completo @codigo", connection);
        cmd.Parameters.Add("@codigo", SqlDbType.NVarChar, 120).Value = Code;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ImportAsync(
        SqlConnection connection, IReadOnlyDictionary<string, string> hashes, string sourceDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException("Synthetic source directory does not exist.");
        // All four files are preverified BEFORE the first SQL write. Their content
        // remains fixed by the approved metadata registry and SQL SHA2_256 gates.
        foreach (var name in Names)
        {
            var path = Path.Combine(sourceDirectory, name);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > 536870912)
                throw new InvalidDataException("Missing/invalid synthetic source: " + name);
            CheckHash(name, hashes[name], HashFile(path));
        }

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            foreach (var name in Names)
            {
                await using var stream = new FileStream(
                    Path.Combine(sourceDirectory, name), FileMode.Open, FileAccess.Read,
                    FileShare.Read);
                await using var command = new SqlCommand(
                    "ref.sp_registrar_corpus_sintetico_arquivo_v9", connection, transaction)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 180
                };
                command.Parameters.Add("@codigo", SqlDbType.NVarChar, 120).Value = Code;
                command.Parameters.Add("@arquivo", SqlDbType.NVarChar, 64).Value = name;
                command.Parameters.Add("@conteudo", SqlDbType.VarBinary, -1).Value = stream;
                await command.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
        await CheckCompleteAsync(connection);
    }

    private static async Task ExportAsync(
        SqlConnection connection, IReadOnlyDictionary<string, string> hashes, string destinationDirectory)
    {
        if (Directory.Exists(destinationDirectory) || File.Exists(destinationDirectory))
            throw new IOException("Synthetic export destination must not already exist.");
        // Reject incomplete records before opening a local output file.
        await CheckCompleteAsync(connection);
        var parent = Path.GetDirectoryName(destinationDirectory)
            ?? throw new InvalidOperationException("Missing export parent directory.");
        Directory.CreateDirectory(parent);
        var stagingDirectory = Path.Combine(parent, ".v9-ref-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            const string sql = """
                SELECT arquivo, sha256, tamanho_bytes, conteudo
                FROM ref.corpus_sintetico_arquivo_v9
                WHERE codigo = @codigo ORDER BY arquivo
                """;
            await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 180 };
            cmd.Parameters.Add("@codigo", SqlDbType.NVarChar, 120).Value = Code;
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(0);
                if (!hashes.TryGetValue(name, out var expected) || !seen.Add(name))
                    throw new InvalidDataException("Unexpected or duplicate synthetic ref file.");
                var storedSha = reader.GetString(1);
                var storedSize = reader.GetInt64(2);
                CheckHash(name, expected, storedSha);
                var output = Path.Combine(stagingDirectory, name);
                await using (var source = reader.GetStream(3))
                await using (var target = new FileStream(
                    output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await source.CopyToAsync(target);
                }
                if (new FileInfo(output).Length != storedSize)
                    throw new InvalidDataException("Synthetic ref byte size mismatch: " + name);
                CheckHash(name, expected, HashFile(output));
            }
            if (seen.Count != Names.Length)
                throw new InvalidDataException("Synthetic ref archive is incomplete.");
            Directory.Move(stagingDirectory, destinationDirectory);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }
}

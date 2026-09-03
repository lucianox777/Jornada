using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace Jornada.Tests.Integration;

internal static class SqlBatchRunner
{
    private static readonly Regex GoLine = new(@"^\s*GO\s*(?:--.*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static async Task ExecuteFileAsync(SqlConnection connection, string path, CancellationToken cancellationToken = default)
    {
        var script = await File.ReadAllTextAsync(path, cancellationToken);
        foreach (var batch in GoLine.Split(script))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            using var command = connection.CreateCommand();
            command.CommandText = batch;
            command.CommandTimeout = 120;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}

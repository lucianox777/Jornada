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

        // Os scripts de bootstrap usam NOCOUNT/XACT_ABORT para execução fail-closed.
        // Essas opções são de sessão e não devem vazar para a lógica dos testes que reutiliza a conexão:
        // NOCOUNT ON altera ExecuteNonQuery para -1 e XACT_ABORT ON invalida transações de cenários negativos.
        using var resetSession = connection.CreateCommand();
        resetSession.CommandText = "SET NOCOUNT OFF; SET XACT_ABORT OFF;";
        await resetSession.ExecuteNonQueryAsync(cancellationToken);
    }
}

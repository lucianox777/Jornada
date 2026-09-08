using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace Jornada.Tests.Integration;

internal static class SqlBatchRunner
{
    private static readonly Regex GoLine = new(@"^\s*GO\s*(?:--.*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static async Task ExecuteFileAsync(SqlConnection connection, string path, CancellationToken cancellationToken = default)
    {
        await ExecuteScriptAsync(connection, path, cancellationToken);

        // A V1 operacional exige a âncora CPF antes de qualquer writer. As fixtures históricas
        // chamam Jornada_Fase1.sql diretamente; centralizamos aqui o complemento obrigatório
        // para que todo banco isolado de Integration possua o mesmo contrato do instalador.
        if (string.Equals(Path.GetFileName(path), "Jornada_Fase1.sql", StringComparison.OrdinalIgnoreCase))
        {
            var databaseDirectory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("Diretório do baseline SQL indisponível.");
            var anchorPath = Path.Combine(databaseDirectory, "migrations", "20260907_Cpf_Ancora.sql");
            if (!File.Exists(anchorPath))
                throw new FileNotFoundException("Migração obrigatória da âncora CPF não encontrada no output de Integration.", anchorPath);
            await ExecuteScriptAsync(connection, anchorPath, cancellationToken);
        }

        // Os scripts de bootstrap usam NOCOUNT/XACT_ABORT para execução fail-closed.
        // Essas opções são de sessão e não devem vazar para a lógica dos testes que reutiliza a conexão:
        // NOCOUNT ON altera ExecuteNonQuery para -1 e XACT_ABORT ON invalida transações de cenários negativos.
        using var resetSession = connection.CreateCommand();
        resetSession.CommandText = "SET NOCOUNT OFF; SET XACT_ABORT OFF;";
        await resetSession.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteScriptAsync(SqlConnection connection, string path, CancellationToken cancellationToken)
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

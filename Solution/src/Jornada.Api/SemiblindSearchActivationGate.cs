using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Api;

/// <summary>
/// Gate técnico independente de credencial/escopo: enquanto #539 e #378 estiverem
/// pendentes, a busca só pode executar com flag explicitamente ligada e
/// marcador residente de Development. O nome físico do banco não concede
/// capacidade: HML/Produção permanecem fail-closed pelo perfil residente.
/// </summary>
internal interface ISemiblindSearchActivationGate
{
    Task<bool> IsEnabledAsync(CancellationToken ct);
}

internal sealed class SqlSyntheticDevelopmentSemiblindSearchActivationGate(
    IHostEnvironment environment,
    IConfiguration configuration,
    IOperationalSqlAdapter sql) : ISemiblindSearchActivationGate
{
    internal const string EnabledSetting = "SemiblindIdentitySearch:Enabled";

    public async Task<bool> IsEnabledAsync(CancellationToken ct)
    {
        // Não consultar SQL e jamais permitir ativação em HML/Produção.
        if (!environment.IsDevelopment() || !configuration.GetValue<bool>(EnabledSetting))
            return false;

        try
        {
            await using var connection = await sql.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 5;
            command.CommandText = """
                SELECT DB_NAME(),
                       (SELECT TOP (1) CONVERT(nvarchar(64), value)
                          FROM sys.extended_properties
                         WHERE class=0 AND name=N'Jornada.EnvironmentProfile');
                """;
            await using var reader = await command.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct)
                && IsEligible(
                    environment.EnvironmentName,
                    configuration.GetValue<bool>(EnabledSetting),
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1));
        }
        catch (SqlException)
        {
            // Falhar fechado se o banco ou o marcador residente não puder ser comprovado.
            return false;
        }
    }

    internal static bool IsEligible(
        string environmentName, bool enabled, string? databaseName, string? profile) =>
        enabled
        && string.Equals(environmentName, "Development", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(databaseName)
        && string.Equals(profile, "Development", StringComparison.Ordinal);
}

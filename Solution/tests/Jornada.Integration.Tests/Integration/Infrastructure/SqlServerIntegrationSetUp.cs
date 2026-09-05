using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Data.SqlClient;
using NUnit.Framework;
using Testcontainers.MsSql;

namespace Jornada.Tests.Integration;

/// <summary>
/// Fornece um SQL Server real para toda a árvore Jornada.Tests.Integration.
///
/// Regra de seleção:
/// 1. Se JORNADA_TEST_SQL_CONNECTION não existir, sobe SQL Server 2022 descartável via
///    Testcontainers, cria banco exclusivo e publica sua connection string.
/// 2. Se JORNADA_TEST_SQL_CONNECTION existir, ela é tratada como conexão-base do servidor e,
///    por padrão, também recebe um banco JornadaIntegration_Test_{Guid} exclusivo por execução.
/// 3. Somente quando JORNADA_TEST_SQL_USE_EXISTING_DATABASE=true a suíte usa literalmente o
///    banco informado externamente. Esse modo pressupõe que CI/HML já tenha provisionado um
///    banco dedicado à execução e assume explicitamente a responsabilidade pelo isolamento.
///
/// O schema não é aplicado aqui de propósito: as fixtures de domínio existentes continuam
/// responsáveis por Jornada_Fase1.sql / seed, evitando duplicar a política de bootstrap.
/// </summary>
[SetUpFixture]
public sealed class SqlServerIntegrationSetUp
{
    private MsSqlContainer? _container;
    private string? _previousConnectionString;
    private string? _databaseAdministrationConnectionString;
    private string? _isolatedDatabaseName;
    private bool _ownsPublishedConnectionString;

    [OneTimeSetUp]
    public async Task StartSqlServerAsync()
    {
        _previousConnectionString = Environment.GetEnvironmentVariable(
            Infrastructure.SqlIntegrationEnvironment.ConnectionStringVariable);

        if (Infrastructure.SqlIntegrationEnvironment.IsFabricSqlDatabase
            && string.IsNullOrWhiteSpace(_previousConnectionString))
        {
            throw new InvalidOperationException(
                "JORNADA_TEST_SQL_TARGET=FABRIC_SQL_DATABASE exige JORNADA_TEST_SQL_CONNECTION " +
                "apontando para um banco SQL no Fabric previamente provisionado.");
        }

        if (Infrastructure.SqlIntegrationEnvironment.IsFabricSqlDatabase
            && !Infrastructure.SqlIntegrationEnvironment.UseExistingExternalDatabase)
        {
            throw new InvalidOperationException(
                "JORNADA_TEST_SQL_TARGET=FABRIC_SQL_DATABASE exige " +
                "JORNADA_TEST_SQL_USE_EXISTING_DATABASE=true. O harness de Integration não cria nem remove " +
                "itens de banco do workspace Fabric.");
        }

        if (!string.IsNullOrWhiteSpace(_previousConnectionString)
            && Infrastructure.SqlIntegrationEnvironment.UseExistingExternalDatabase)
        {
            if (Infrastructure.SqlIntegrationEnvironment.IsFabricSqlDatabase)
            {
                ConfigureFabricDeviceCodeAuthentication();
            }

            var externalBuilder = new SqlConnectionStringBuilder(_previousConnectionString)
            {
                Pooling = false,
            };

            if (Infrastructure.SqlIntegrationEnvironment.IsFabricSqlDatabase)
            {
                externalBuilder.Authentication = SqlAuthenticationMethod.ActiveDirectoryDeviceCodeFlow;
                externalBuilder.ConnectTimeout = Math.Max(externalBuilder.ConnectTimeout, 300);
            }

            EnsureIntegrationDatabaseNameIsSafe(externalBuilder.InitialCatalog);

            if (string.Equals(
                    Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_RESET_EXISTING_DATABASE"),
                    "true",
                    StringComparison.OrdinalIgnoreCase))
            {
                await ResetExternalIntegrationDatabaseAsync(externalBuilder.ConnectionString)
                    .ConfigureAwait(false);
                TestContext.Progress.WriteLine(
                    "Integration database reset: banco externo Test/Dev/Local limpo antes da suíte.");
            }

            Environment.SetEnvironmentVariable(
                Infrastructure.SqlIntegrationEnvironment.ConnectionStringVariable,
                externalBuilder.ConnectionString);
            _ownsPublishedConnectionString = true;

            await AssertConnectionWorksAsync(externalBuilder.ConnectionString).ConfigureAwait(false);
            TestContext.Progress.WriteLine(
                $"Integration SQL target: {Infrastructure.SqlIntegrationEnvironment.Target}");
            TestContext.Progress.WriteLine(
                "Integration SQL: usando banco externo pré-provisionado por opção explícita " +
                "(JORNADA_TEST_SQL_USE_EXISTING_DATABASE=true). O pipeline é responsável pelo isolamento.");
            TestContext.Progress.WriteLine("Integration connection pooling: disabled");
            return;
        }

        string serverConnectionString;
        string source;

        if (!string.IsNullOrWhiteSpace(_previousConnectionString))
        {
            serverConnectionString = _previousConnectionString;
            source = "SQL externo";
        }
        else
        {
            ConfigureDockerApiVersionForCompatibility();
            var image = Infrastructure.SqlIntegrationEnvironment.SqlServerImage;
            _container = new MsSqlBuilder(image).Build();
            await _container.StartAsync().ConfigureAwait(false);

            serverConnectionString = _container.GetConnectionString();
            source = $"Testcontainers / {image}";
        }

        _isolatedDatabaseName = $"JornadaIntegration_Test_{Guid.NewGuid():N}";
        _databaseAdministrationConnectionString = BuildMasterConnectionString(serverConnectionString);

        try
        {
            await CreateDatabaseAsync(
                    _databaseAdministrationConnectionString,
                    _isolatedDatabaseName)
                .ConfigureAwait(false);
        }
        catch (SqlException ex) when (!string.IsNullOrWhiteSpace(_previousConnectionString))
        {
            throw new InvalidOperationException(
                "A suíte Integration exige banco isolado por execução. A conexão externa fornecida em " +
                "JORNADA_TEST_SQL_CONNECTION não conseguiu criar o banco temporário. Conceda permissão " +
                "adequada à identidade do CI/HML ou forneça um banco já exclusivo para esta execução e " +
                "defina JORNADA_TEST_SQL_USE_EXISTING_DATABASE=true.",
                ex);
        }

        var builder = new SqlConnectionStringBuilder(serverConnectionString)
        {
            InitialCatalog = _isolatedDatabaseName,
            Pooling = false,
        };

        Environment.SetEnvironmentVariable(
            Infrastructure.SqlIntegrationEnvironment.ConnectionStringVariable,
            builder.ConnectionString);
        _ownsPublishedConnectionString = true;

        await AssertConnectionWorksAsync(builder.ConnectionString).ConfigureAwait(false);

        TestContext.Progress.WriteLine($"Integration SQL target: {Infrastructure.SqlIntegrationEnvironment.Target}");
        TestContext.Progress.WriteLine($"Integration SQL: {source}");
        TestContext.Progress.WriteLine($"Integration database: {_isolatedDatabaseName}");
        TestContext.Progress.WriteLine("Integration connection pooling: disabled");
    }

    [OneTimeTearDown]
    public async Task StopSqlServerAsync()
    {
        try
        {
            if (_isolatedDatabaseName is not null
                && _databaseAdministrationConnectionString is not null)
            {
                await DropDatabaseAsync(
                        _databaseAdministrationConnectionString,
                        _isolatedDatabaseName)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            if (_ownsPublishedConnectionString)
            {
                Environment.SetEnvironmentVariable(
                    Infrastructure.SqlIntegrationEnvironment.ConnectionStringVariable,
                    _previousConnectionString);
            }

            if (_container is not null)
            {
                await _container.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static void ConfigureDockerApiVersionForCompatibility()
    {
        // Testcontainers 4.14 usa Docker API 1.44 por padrão. Docker Desktop/Engine
        // antigos (por exemplo API máxima 1.41) rejeitam essa versão antes de qualquer teste.
        // Se o operador já configurou DOCKER_API_VERSION, respeitamos a escolha explícita.
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_API_VERSION")))
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo("docker")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("version");
            startInfo.ArgumentList.Add("--format");
            startInfo.ArgumentList.Add("{{.Server.APIVersion}}");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return;
            }

            if (!process.WaitForExit(5000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Processo já encerrou entre o timeout e o Kill; nada a fazer.
                }
                catch (Win32Exception)
                {
                    // Falha de plataforma/permissão ao encerrar o probe; não mascara a descoberta normal.
                }
                return;
            }

            if (process.ExitCode != 0)
            {
                return;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            if (Version.TryParse(output, out var serverApi)
                && serverApi < new Version(1, 44))
            {
                Environment.SetEnvironmentVariable("DOCKER_API_VERSION", output);
                TestContext.Progress.WriteLine(
                    $"Integration Docker API: servidor anuncia {output}; " +
                    "DOCKER_API_VERSION ajustado para compatibilidade do Testcontainers.");
            }
        }
        catch (InvalidOperationException ex)
        {
            TestContext.Progress.WriteLine(
                $"Integration Docker API: não foi possível consultar 'docker version' ({ex.Message}); " +
                "Testcontainers fará a descoberta normal do ambiente.");
        }
        catch (Win32Exception ex)
        {
            TestContext.Progress.WriteLine(
                $"Integration Docker API: não foi possível iniciar 'docker version' ({ex.Message}); " +
                "Testcontainers fará a descoberta normal do ambiente.");
        }
    }

    private static string BuildMasterConnectionString(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master",
            Pooling = false,
        };
        return builder.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string masterConnectionString, string databaseName)
    {
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}];";
        command.CommandTimeout = 60;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task DropDatabaseAsync(string masterConnectionString, string databaseName)
    {
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END;
            """;
        command.CommandTimeout = 60;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static void EnsureIntegrationDatabaseNameIsSafe(string? databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException(
                "A conexão externa de Integration deve informar explicitamente o banco de dados.");
        }

        if (!databaseName.Contains("test", StringComparison.OrdinalIgnoreCase)
            && !databaseName.Contains("dev", StringComparison.OrdinalIgnoreCase)
            && !databaseName.Contains("local", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Por segurança, o banco externo de Integration deve conter 'Test', 'Dev' ou 'Local' no nome.");
        }
    }

    private static void ConfigureFabricDeviceCodeAuthentication()
    {
        var provider = new ActiveDirectoryAuthenticationProvider(result =>
        {
            // VSTest/NUnit pode capturar a saída do testhost e só mostrá-la no TRX ao final.
            // Para que o operador veja o código enquanto a autenticação ainda está ativa,
            // publicamos a mensagem em um arquivo local monitorado pelo script PowerShell pai.
            var deviceCodeFile = Environment.GetEnvironmentVariable(
                "JORNADA_FABRIC_DEVICE_CODE_FILE");

            if (!string.IsNullOrWhiteSpace(deviceCodeFile))
            {
                var directory = Path.GetDirectoryName(deviceCodeFile);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(
                    deviceCodeFile,
                    result.Message,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            // Mantemos também o Progress para evidência no TRX.
            TestContext.Progress.WriteLine(string.Empty);
            TestContext.Progress.WriteLine("===================================================");
            TestContext.Progress.WriteLine("MICROSOFT ENTRA - DEVICE CODE");
            TestContext.Progress.WriteLine(result.Message);
            TestContext.Progress.WriteLine("Conclua o login com a identidade autorizada no banco Fabric de compatibilidade.");
            TestContext.Progress.WriteLine("===================================================");

            return Task.CompletedTask;
        });

        if (!SqlAuthenticationProvider.SetProvider(
                SqlAuthenticationMethod.ActiveDirectoryDeviceCodeFlow,
                provider))
        {
            throw new InvalidOperationException(
                "Não foi possível registrar o provider Active Directory Device Code Flow do SqlClient.");
        }

        TestContext.Progress.WriteLine(
            "Fabric authentication provider: Active Directory Device Code Flow " +
            "(relay de código para o PowerShell; Connect Timeout >= 300s)");
    }

    private static async Task ResetExternalIntegrationDatabaseAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using (var acquire = connection.CreateCommand())
        {
            acquire.CommandText = """
                DECLARE @rc int;
                EXEC @rc = sys.sp_getapplock
                    @Resource = N'JORNADA_INTEGRATION_DATABASE_RESET',
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Session',
                    @LockTimeout = 60000;
                IF @rc < 0
                    THROW 51000, 'Não foi possível obter lock exclusivo para reset do banco de Integration.', 1;
                """;
            acquire.CommandTimeout = 65;
            await acquire.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var tables = new List<string>();
        await using (var discover = connection.CreateCommand())
        {
            discover.CommandText = """
                SELECT QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name)
                FROM sys.tables
                WHERE is_ms_shipped = 0
                ORDER BY object_id;
                """;

            await using var reader = await discover.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                tables.Add(reader.GetString(0));
            }
        }

        try
        {
            foreach (var table in tables)
            {
                await ExecuteResetSqlAsync(
                        connection,
                        $"DISABLE TRIGGER ALL ON {table}; ALTER TABLE {table} NOCHECK CONSTRAINT ALL;")
                    .ConfigureAwait(false);
            }

            foreach (var table in tables)
            {
                await ExecuteResetSqlAsync(connection, $"DELETE FROM {table};")
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                foreach (var table in tables)
                {
                    await ExecuteResetSqlAsync(
                            connection,
                            $"ALTER TABLE {table} WITH CHECK CHECK CONSTRAINT ALL; ENABLE TRIGGER ALL ON {table};")
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                await using var release = connection.CreateCommand();
                release.CommandText = """
                    DECLARE @rc int;
                    EXEC @rc = sys.sp_releaseapplock
                        @Resource = N'JORNADA_INTEGRATION_DATABASE_RESET',
                        @LockOwner = 'Session';
                    """;
                release.CommandTimeout = 15;
                await release.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task ExecuteResetSqlAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task AssertConnectionWorksAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DB_NAME();";
        command.CommandTimeout = 15;

        var databaseName = Convert.ToString(
            await command.ExecuteScalarAsync().ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);

        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("Microsoft SQL respondeu sem DB_NAME().");
        }
    }
}

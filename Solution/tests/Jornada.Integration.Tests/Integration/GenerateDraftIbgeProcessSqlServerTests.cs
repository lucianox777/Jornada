using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

/// <summary>
/// Prova SQL/processo real do gate IBGE de GENERATE_DRAFT.
/// Cada fixture cria um banco próprio, com nome aprovado pela guarda da suíte;
/// nunca desativa a referência de outros testes nem acessa JornadaLocal.
/// Depende do contrato de compatibilidade RF-052 da PR #602.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class GenerateDraftIbgeProcessSqlServerTests
{
    private const string GuardMessage =
        "GENERATE_DRAFT exige referência IBGE ATIVA carregada por ENSURE_NAME_FREQUENCY_SNAPSHOT explícito.";
    private string? _connectionString;
    private string? _masterConnectionString;
    private string? _databaseName;

    [OneTimeSetUp]
    public async Task CreateIsolatedDatabaseAsync()
    {
        var provided = RequireGuardedIntegrationConnection();
        var master = new SqlConnectionStringBuilder(provided)
        {
            InitialCatalog = "master",
            Pooling = false,
        };
        _masterConnectionString = master.ConnectionString;
        _databaseName = $"JornadaIntegration_Test_{Guid.NewGuid():N}";
        AssertIsOwnDisposableDatabase(_databaseName);

        try
        {
            await using (var connection = new SqlConnection(_masterConnectionString))
            {
                await connection.OpenAsync();
                await using var create = connection.CreateCommand();
                create.CommandText = $"CREATE DATABASE [{_databaseName}];";
                create.CommandTimeout = 60;
                await create.ExecuteNonQueryAsync();
            }

            _connectionString = new SqlConnectionStringBuilder(provided)
            {
                InitialCatalog = _databaseName,
                Pooling = false,
            }.ConnectionString;

            await using var isolated = new SqlConnection(_connectionString);
            await isolated.OpenAsync();
            // Instala o schema canônico e TODAS as migrations, inclusive a RF-052.
            // Não faz seed, ENSURE, LOAD nem qualquer bootstrap implícito da referência.
            await SqlBatchRunner.ExecuteCanonicalSchemaAsync(
                isolated, Path.Combine(AppContext.BaseDirectory, "database"));
        }
        catch
        {
            if (_databaseName is not null)
                await DropOwnDatabaseAsync();
            throw;
        }
    }

    [OneTimeTearDown]
    public async Task DropIsolatedDatabaseAsync()
    {
        if (_databaseName is not null)
            await DropOwnDatabaseAsync();
    }

    [Test]
    [Order(1)]
    public async Task Sem_referencia_ativa_worker_falha_sem_modelo_rascunho_ou_carga_implicita()
    {
        var connectionString = RequireOwnConnection();
        var before = await SnapshotAsync(connectionString);
        Assert.Multiple(() =>
        {
            Assert.That(before.ActiveReferences, Is.Zero);
            Assert.That(before.ReferenceVersions, Is.Zero);
            Assert.That(before.FrequencyRows, Is.Zero);
            Assert.That(before.ModelRows, Is.Zero);
        });

        var run = await RunRealWorkerAsync(connectionString);
        Assert.Multiple(() =>
        {
            Assert.That(run.ExitCode, Is.Not.Zero,
                "O processo real deve encerrar com erro quando falta referência ATIVA.");
            Assert.That(run.Output, Does.Contain(GuardMessage),
                "A falha deve ocorrer no gate IBGE, não em outra configuração ou dependência.");
        });

        var after = await SnapshotAsync(connectionString);
        Assert.That(after, Is.EqualTo(before),
            "O processo não pode criar modelo, RASCUNHO, parâmetro, ruleset, evento " +
            "de promoção ou versão/linha de referência por carga implícita.");
    }

    [Test]
    [Order(2)]
    public async Task Com_referencia_compativel_worker_ultrapassa_o_gate_e_chega_ao_gold_vazio()
    {
        var connectionString = RequireOwnConnection();
        var before = await SnapshotAsync(connectionString);
        Assert.That(before.ActiveReferences, Is.Zero,
            "O caso positivo começa no mesmo banco sem referência ATIVA.");

        await PublishMinimalCompatibleReferenceAsync(connectionString);
        var activated = await SnapshotAsync(connectionString);
        Assert.Multiple(() =>
        {
            Assert.That(activated.ActiveReferences, Is.EqualTo(1));
            Assert.That(activated.ReferenceVersions, Is.EqualTo(before.ReferenceVersions + 1));
            Assert.That(activated.FrequencyRows, Is.EqualTo(before.FrequencyRows + 2));
            Assert.That(activated.ModelRows, Is.EqualTo(before.ModelRows));
        });

        // A fixture deliberadamente NÃO povoa a Gold: a falha posterior por corpus vazio
        // demonstra que o processo passou pela pré-condição sem fingir calibração completa.
        var run = await RunRealWorkerAsync(connectionString);
        Assert.Multiple(() =>
        {
            Assert.That(run.Output, Does.Not.Contain(GuardMessage));
            Assert.That(run.Output, Does.Contain("Gold Pessoas vazia."),
                "O worker real precisa alcançar a leitura da Gold, após o gate IBGE.");
            Assert.That(run.ExitCode, Is.Not.Zero,
                "Sem corpus de treino, o worker deve falhar explicitamente após o gate.");
        });

        var after = await SnapshotAsync(connectionString);
        Assert.Multiple(() =>
        {
            Assert.That(after.ActiveReferences, Is.EqualTo(1));
            Assert.That(after.ReferenceVersions, Is.EqualTo(activated.ReferenceVersions),
                "GENERATE_DRAFT não deve disparar nova versão de referência.");
            Assert.That(after.FrequencyRows, Is.EqualTo(activated.FrequencyRows));
            Assert.That(after.ModelRows, Is.EqualTo(activated.ModelRows + 1),
                "Modelo GERANDO confirma a passagem pelo gate, antes da falha por Gold vazia.");
            Assert.That(after.DraftRows, Is.EqualTo(activated.DraftRows));
            Assert.That(after.FailedModels, Is.EqualTo(activated.FailedModels + 1));
        });
    }

    private static string RequireGuardedIntegrationConnection()
    {
        // Guarda idêntica à da prova de referência SQL: não aceitar nomes genéricos
        // com 'Dev'/'Local' nem reutilizar banco original de operação.
        var value = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                "JORNADA_TEST_SQL_CONNECTION não foi inicializada pela fixture Integration.");

        var database = new SqlConnectionStringBuilder(value).InitialCatalog ?? string.Empty;
        const string prefix = "JornadaIntegration_Test_";
        var generated = database.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParseExact(database[prefix.Length..], "N", out _);
        if (!database.Equals("JornadaTest", StringComparison.OrdinalIgnoreCase)
            && !database.StartsWith("JornadaTest_", StringComparison.OrdinalIgnoreCase)
            && !generated)
        {
            throw new InvalidOperationException(
                "Banco permitido: JornadaTest, JornadaTest_* ou JornadaIntegration_Test_<GUID>; " +
                "JornadaLocal e demais bancos não são permitidos.");
        }
        return value;
    }

    private static void AssertIsOwnDisposableDatabase(string name)
    {
        const string prefix = "JornadaIntegration_Test_";
        if (!name.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(name[prefix.Length..], "N", out _))
            throw new InvalidOperationException("Recusado: nome do banco temporário fora da guarda.");
    }

    private string RequireOwnConnection() =>
        _connectionString ?? throw new InvalidOperationException("Banco temporário não inicializado.");

    private async Task DropOwnDatabaseAsync()
    {
        var name = _databaseName ?? throw new InvalidOperationException("Banco temporário ausente.");
        AssertIsOwnDisposableDatabase(name);
        var master = _masterConnectionString
            ?? throw new InvalidOperationException("Conexão administrativa temporária ausente.");

        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var drop = connection.CreateCommand();
        drop.CommandText = $"""
            IF DB_ID(N'{name}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{name}];
            END;
            """;
        drop.CommandTimeout = 90;
        await drop.ExecuteNonQueryAsync();
        _databaseName = null;
    }

    private static async Task<DatabaseSnapshot> SnapshotAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              (SELECT COUNT_BIG(*) FROM ref.frequencia_nome_versao WHERE status=N'ATIVA'),
              (SELECT COUNT_BIG(*) FROM ref.frequencia_nome_versao),
              (SELECT COUNT_BIG(*) FROM ref.frequencia_nome),
              (SELECT COUNT_BIG(*) FROM identidade.modelo_linkage),
              (SELECT COUNT_BIG(*) FROM identidade.modelo_linkage WHERE status=N'RASCUNHO'),
              (SELECT COUNT_BIG(*) FROM identidade.modelo_linkage WHERE status=N'FALHOU'),
              (SELECT COUNT_BIG(*) FROM identidade.parametro_linkage),
              (SELECT COUNT_BIG(*) FROM identidade.estatistica_linkage),
              (SELECT COUNT_BIG(*) FROM identidade.linkage_ruleset),
              (SELECT COUNT_BIG(*) FROM auditoria.modelo_linkage_estado_evento);
            """;
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("SQL não retornou o snapshot de evidência.");

        return new DatabaseSnapshot(
            reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5),
            reader.GetInt64(6), reader.GetInt64(7), reader.GetInt64(8),
            reader.GetInt64(9));
    }

    private static async Task PublishMinimalCompatibleReferenceAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT ref.frequencia_nome_versao(
                codigo, fonte, edicao, data_referencia, publicado_em,
                status, normalizacao_versao, manifest_schema_version)
            VALUES (
                @code, N'FIXTURE SINTÉTICA DE TESTE', N'Teste de pré-condição',
                '2022-08-01', '2025-11-04',
                N'CARREGANDO', @normalization, 1);
            DECLARE @id BIGINT = SCOPE_IDENTITY();
            INSERT ref.frequencia_nome(
                frequencia_nome_versao_id, tipo, valor, valor_normalizado,
                sexo, periodo_nascimento, escopo_geografico,
                uf_codigo, municipio_codigo, frequencia)
            VALUES
                (@id, N'NOME', N'Maria', N'MARIA',
                    N'TODOS', N'TODOS', N'BRASIL', '00', '0000000', 1000),
                (@id, N'SOBRENOME', N'Silva', N'SILVA',
                    N'TODOS', N'TODOS', N'BRASIL', '00', '0000000', 400);
            EXEC ref.sp_publicar_frequencia_nome_versao
                @frequencia_nome_versao_id=@id,
                @conteudo_sha256=@hash;
            """;
        command.Parameters.Add("@code", SqlDbType.NVarChar, 80).Value =
            "TEST-IBGE-GATE-" + Guid.NewGuid().ToString("N")[..12];
        command.Parameters.Add("@normalization", SqlDbType.NVarChar, 80).Value =
            Jornada.Contracts.IdentityComparison.NormalizationVersion;
        command.Parameters.Add("@hash", SqlDbType.Binary, 32).Value =
            Enumerable.Repeat((byte)0x42, 32).ToArray();
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<WorkerResult> RunRealWorkerAsync(string connectionString)
    {
        var workerDll = LocateBuiltWorker();
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(workerDll)
                ?? throw new InvalidOperationException("Diretório do Parameters.Worker indisponível."),
        };
        info.ArgumentList.Add(workerDll);
        info.Environment["ConnectionStrings__Jornada"] = connectionString;
        info.Environment["Database__Provider"] = "SqlServer";
        info.Environment["LinkageParameters__Operation"] = "GENERATE_DRAFT";
        info.Environment["LinkageParameters__RunOnce"] = "true";
        info.Environment["LinkageParameters__NormalizationVersion"] =
            Jornada.Contracts.IdentityComparison.NormalizationVersion;

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("Falha ao iniciar o processo real Parameters.Worker.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException(
                "Parameters.Worker não concluiu em 90s; processo interrompido para não deixar gravações residuais.");
        }
        return new WorkerResult(
            process.ExitCode, (await stdout) + Environment.NewLine + (await stderr));
    }

    private static string LocateBuiltWorker()
    {
        var testOutput = new DirectoryInfo(Path.GetFullPath(AppContext.BaseDirectory));
        var tfm = testOutput.Name;
        var configuration = testOutput.Parent?.Name;
        if (!tfm.StartsWith("net", StringComparison.Ordinal)
            || configuration is not ("Debug" or "Release"))
            throw new InvalidOperationException(
                $"Layout de build inesperado para testes Integration: {testOutput.FullName}");

        for (DirectoryInfo? directory = testOutput; directory is not null; directory = directory.Parent)
        {
            var sourceDir = Path.Combine(directory.FullName, "src", "Jornada.Linkage.Parameters.Worker");
            if (!File.Exists(Path.Combine(sourceDir, "Jornada.Linkage.Parameters.Worker.csproj")))
                continue;

            var output = Path.Combine(sourceDir, "bin", configuration, tfm);
            var dll = Path.Combine(output, "Jornada.Linkage.Parameters.Worker.dll");
            var runtime = Path.Combine(output, "Jornada.Linkage.Parameters.Worker.runtimeconfig.json");
            if (!File.Exists(dll) || !File.Exists(runtime))
                throw new FileNotFoundException(
                    $"Build do processo real Parameters.Worker não encontrado em {output}.");
            return dll;
        }

        throw new DirectoryNotFoundException(
            "Solution/src/Jornada.Linkage.Parameters.Worker não encontrada a partir do output Integration.");
    }

    private sealed record WorkerResult(int ExitCode, string Output);
    private sealed record DatabaseSnapshot(
        long ActiveReferences,
        long ReferenceVersions,
        long FrequencyRows,
        long ModelRows,
        long DraftRows,
        long FailedModels,
        long ParameterRows,
        long StatisticsRows,
        long RulesetRows,
        long PromotionEvents);
}

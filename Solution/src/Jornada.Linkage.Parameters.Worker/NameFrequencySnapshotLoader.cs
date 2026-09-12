using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Carrega no SQL Server a referência de nomes a partir do snapshot bruto versionado
/// junto ao projeto. Não acessa rede. O snapshot é a fonte operacional canônica para
/// calibração e replay; a API externa não participa da execução normal.
/// </summary>
public sealed class NameFrequencySnapshotLoader(
    ILogger<NameFrequencySnapshotLoader> logger,
    IConfiguration configuration,
    IOperationalSqlAdapter operationalSql,
    IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    public const string Operation = "LOAD_NAME_FREQUENCY_SNAPSHOT";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var manifestPath = ResolveManifestPath();
            var manifest = await ReadManifestAsync(manifestPath, stoppingToken);
            var baseDirectory = Path.GetDirectoryName(manifestPath)
                ?? throw new InvalidOperationException("Diretório do manifesto de frequências inválido.");

            var rows = new List<SnapshotRow>(350_000);
            foreach (var file in manifest.Snapshot.Files)
            {
                var path = Path.GetFullPath(Path.Combine(baseDirectory, file.Path));
                if (!File.Exists(path))
                {
                    if (file.Required)
                        throw new FileNotFoundException($"Snapshot obrigatório ausente: {file.Path}", path);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(file.Sha256))
                    throw new InvalidOperationException($"Snapshot {file.Path} existe, mas não possui sha256 fixado no manifesto.");

                await VerifyFileHashAsync(path, file.Sha256, stoppingToken);
                await ReadRowsAsync(path, rows, stoppingToken);
            }

            ValidateRows(rows);
            var canonicalHash = NameFrequencyReferenceImporter.ComputeCanonicalHash(
                rows.Select(x => new NameFrequencyReferenceImporter.NameFrequencyImportRow(
                    x.Type,
                    x.Value,
                    x.NormalizedValue,
                    x.Frequency)));

            await PersistAsync(manifest, rows, canonicalHash, stoppingToken);
            logger.LogInformation(
                "Snapshot local de frequências carregado. Versão={Version}; linhas={Rows}; arquivos={Files}; SHA256={Hash}.",
                manifest.ReferenceCode,
                rows.Count,
                manifest.Snapshot.Files.Count,
                Convert.ToHexString(canonicalHash));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao carregar snapshot local de frequências de nomes/sobrenomes.");
            Environment.ExitCode = 1;
        }
        finally
        {
            applicationLifetime.StopApplication();
        }
    }

    private string ResolveManifestPath()
    {
        var configured = configuration.GetValue<string?>("NameFrequencySnapshot:ManifestPath");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "reference",
            "ibge-nomes-2022",
            "manifest.json"));
    }

    internal static async Task<SnapshotManifest> ReadManifestAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Manifesto do snapshot de frequências não encontrado.", path);

        await using var stream = File.OpenRead(path);
        var manifest = await JsonSerializer.DeserializeAsync<SnapshotManifest>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);

        if (manifest is null || manifest.SchemaVersion != 1)
            throw new InvalidDataException("Manifesto de frequências ausente ou schemaVersion incompatível.");
        if (string.IsNullOrWhiteSpace(manifest.ReferenceCode) || manifest.ReferenceCode.Length > 80)
            throw new InvalidDataException("referenceCode inválido no manifesto.");
        if (manifest.Snapshot.Files.Count == 0)
            throw new InvalidDataException("Manifesto não declara arquivos de snapshot.");
        if (manifest.Policy.RemoteApiRequiredAtRuntime)
            throw new InvalidDataException("Snapshot operacional não pode exigir API remota em runtime.");
        if (manifest.Policy.RemoteCheckMayMutateData)
            throw new InvalidDataException("Light check remoto não pode alterar os dados do snapshot.");

        return manifest;
    }

    private static async Task VerifyFileHashAsync(string path, string expectedHex, CancellationToken cancellationToken)
    {
        if (expectedHex.Length != 64 || expectedHex.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException($"SHA-256 inválido no manifesto para {Path.GetFileName(path)}.");

        await using var stream = File.OpenRead(path);
        var actual = await SHA256.HashDataAsync(stream, cancellationToken);
        if (!string.Equals(Convert.ToHexString(actual), expectedHex, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SHA-256 do snapshot diverge do manifesto: {Path.GetFileName(path)}.");
    }

    internal static async Task ReadRowsAsync(string path, ICollection<SnapshotRow> destination, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress, leaveOpen: false);
        using var reader = new StreamReader(gzip);

        var lineNumber = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            SnapshotRawRow? raw;
            try
            {
                raw = JsonSerializer.Deserialize<SnapshotRawRow>(line, SnapshotJsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"JSON inválido em {Path.GetFileName(path)}:{lineNumber}.", ex);
            }

            if (raw is null)
                throw new InvalidDataException($"Linha vazia semanticamente em {Path.GetFileName(path)}:{lineNumber}.");

            destination.Add(ToValidatedRow(raw, path, lineNumber));
        }
    }

    private static SnapshotRow ToValidatedRow(SnapshotRawRow raw, string path, int lineNumber)
    {
        var type = raw.Tipo?.Trim().ToUpperInvariant();
        if (type is not ("NOME" or "SOBRENOME"))
            throw new InvalidDataException($"tipo inválido em {Path.GetFileName(path)}:{lineNumber}.");

        var value = raw.Valor?.Trim();
        var normalized = IdentityComparison.NormalizeText(value);
        if (string.IsNullOrWhiteSpace(value) || normalized is null || value.Length > 200 || normalized.Length > 200)
            throw new InvalidDataException($"valor inválido em {Path.GetFileName(path)}:{lineNumber}.");
        if (raw.Frequencia <= 0)
            throw new InvalidDataException($"frequencia deve ser positiva em {Path.GetFileName(path)}:{lineNumber}.");

        var sex = string.IsNullOrWhiteSpace(raw.Sexo) ? "TODOS" : raw.Sexo.Trim().ToUpperInvariant();
        if (sex is not ("TODOS" or "MASCULINO" or "FEMININO"))
            throw new InvalidDataException($"sexo inválido em {Path.GetFileName(path)}:{lineNumber}.");

        var period = string.IsNullOrWhiteSpace(raw.PeriodoNascimento) ? "TODOS" : raw.PeriodoNascimento.Trim().ToUpperInvariant();
        if (period.Length > 40)
            throw new InvalidDataException($"periodoNascimento excede 40 caracteres em {Path.GetFileName(path)}:{lineNumber}.");

        var scope = string.IsNullOrWhiteSpace(raw.EscopoGeografico) ? "BRASIL" : raw.EscopoGeografico.Trim().ToUpperInvariant();
        if (scope is not ("BRASIL" or "UF" or "MUNICIPIO"))
            throw new InvalidDataException($"escopoGeografico inválido em {Path.GetFileName(path)}:{lineNumber}.");

        var uf = string.IsNullOrWhiteSpace(raw.UfCodigo) ? "00" : raw.UfCodigo.Trim();
        var municipality = string.IsNullOrWhiteSpace(raw.MunicipioCodigo) ? "0000000" : raw.MunicipioCodigo.Trim();
        if (uf.Length != 2 || municipality.Length != 7)
            throw new InvalidDataException($"código geográfico inválido em {Path.GetFileName(path)}:{lineNumber}.");
        if (scope == "BRASIL" && (uf != "00" || municipality != "0000000"))
            throw new InvalidDataException($"BRASIL deve usar códigos geográficos neutros em {Path.GetFileName(path)}:{lineNumber}.");
        if (scope == "UF" && (uf == "00" || municipality != "0000000"))
            throw new InvalidDataException($"UF deve informar UF e município neutro em {Path.GetFileName(path)}:{lineNumber}.");
        if (scope == "MUNICIPIO" && (uf == "00" || municipality == "0000000"))
            throw new InvalidDataException($"MUNICIPIO deve informar UF e município em {Path.GetFileName(path)}:{lineNumber}.");

        return new SnapshotRow(type, value, normalized, sex, period, scope, uf, municipality, raw.Frequencia);
    }

    private static void ValidateRows(IReadOnlyCollection<SnapshotRow> rows)
    {
        if (rows.Count == 0)
            throw new InvalidOperationException("Snapshot local está vazio.");
        if (!rows.Any(x => x.Type == "NOME"))
            throw new InvalidOperationException("Snapshot não contém NOME.");
        if (!rows.Any(x => x.Type == "SOBRENOME"))
            throw new InvalidOperationException("Snapshot não contém SOBRENOME.");

        var duplicate = rows.GroupBy(x => new
        {
            x.Type,
            x.Value,
            x.Sex,
            x.BirthPeriod,
            x.GeographicScope,
            x.UfCode,
            x.MunicipalityCode
        }).FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
            throw new InvalidOperationException($"Snapshot contém chave dimensional duplicada: {duplicate.Key.Type}/{duplicate.Key.Value}.");
    }

    private async Task PersistAsync(
        SnapshotManifest manifest,
        IReadOnlyCollection<SnapshotRow> rows,
        byte[] canonicalHash,
        CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(manifest.ReferenceDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var referenceDate))
            throw new InvalidDataException("referenceDate inválida no manifesto.");
        DateOnly? publicationDate = null;
        if (!string.IsNullOrWhiteSpace(manifest.PublicationDate))
        {
            if (!DateOnly.TryParseExact(manifest.PublicationDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                throw new InvalidDataException("publicationDate inválida no manifesto.");
            publicationDate = parsed;
        }

        await using var connection = await operationalSql.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            long versionId;
            string? status;
            byte[]? existingHash;
            await using (var read = new SqlCommand(
                "SELECT frequencia_nome_versao_id,status,conteudo_sha256 FROM ref.frequencia_nome_versao WITH(UPDLOCK,HOLDLOCK) WHERE codigo=@codigo;",
                connection,
                transaction))
            {
                read.Parameters.Add("@codigo", SqlDbType.NVarChar, 80).Value = manifest.ReferenceCode;
                await using var reader = await read.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    versionId = reader.GetInt64(0);
                    status = reader.GetString(1);
                    existingHash = reader.IsDBNull(2) ? null : (byte[])reader.GetValue(2);
                }
                else
                {
                    versionId = 0;
                    status = null;
                    existingHash = null;
                }
            }

            if (versionId != 0 && status != "CARREGANDO")
            {
                if (existingHash is not null && existingHash.AsSpan().SequenceEqual(canonicalHash))
                {
                    await transaction.CommitAsync(cancellationToken);
                    logger.LogInformation("Snapshot {Version} já está publicado com o mesmo conteúdo; carga idempotente.", manifest.ReferenceCode);
                    return;
                }
                throw new InvalidOperationException($"Versão {manifest.ReferenceCode} já publicada com conteúdo diferente. Snapshot imutável não pode ser substituído.");
            }

            if (versionId == 0)
            {
                await using var create = new SqlCommand(
                    """
                    INSERT ref.frequencia_nome_versao(codigo,fonte,edicao,data_referencia,publicado_em,status)
                    VALUES(@codigo,@fonte,@edicao,@data,@publicado,'CARREGANDO');
                    SELECT CONVERT(BIGINT,SCOPE_IDENTITY());
                    """,
                    connection,
                    transaction);
                create.Parameters.Add("@codigo", SqlDbType.NVarChar, 80).Value = manifest.ReferenceCode;
                create.Parameters.Add("@fonte", SqlDbType.NVarChar, 200).Value = manifest.Source;
                create.Parameters.Add("@edicao", SqlDbType.NVarChar, 120).Value = "Censo 2022 - Nomes no Brasil";
                create.Parameters.Add("@data", SqlDbType.Date).Value = referenceDate.ToDateTime(TimeOnly.MinValue);
                create.Parameters.Add("@publicado", SqlDbType.Date).Value = publicationDate is null ? DBNull.Value : publicationDate.Value.ToDateTime(TimeOnly.MinValue);
                versionId = Convert.ToInt64(await create.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            }
            else
            {
                await using var clearCoverage = new SqlCommand("DELETE FROM ref.frequencia_nome_cobertura WHERE frequencia_nome_versao_id=@id;", connection, transaction);
                clearCoverage.Parameters.Add("@id", SqlDbType.BigInt).Value = versionId;
                await clearCoverage.ExecuteNonQueryAsync(cancellationToken);
                await using var clear = new SqlCommand("DELETE FROM ref.frequencia_nome WHERE frequencia_nome_versao_id=@id;", connection, transaction);
                clear.Parameters.Add("@id", SqlDbType.BigInt).Value = versionId;
                await clear.ExecuteNonQueryAsync(cancellationToken);
            }

            var table = new DataTable();
            table.Columns.Add("frequencia_nome_versao_id", typeof(long));
            table.Columns.Add("tipo", typeof(string));
            table.Columns.Add("valor", typeof(string));
            table.Columns.Add("valor_normalizado", typeof(string));
            table.Columns.Add("sexo", typeof(string));
            table.Columns.Add("periodo_nascimento", typeof(string));
            table.Columns.Add("escopo_geografico", typeof(string));
            table.Columns.Add("uf_codigo", typeof(string));
            table.Columns.Add("municipio_codigo", typeof(string));
            table.Columns.Add("frequencia", typeof(long));
            foreach (var row in rows)
                table.Rows.Add(versionId, row.Type, row.Value, row.NormalizedValue, row.Sex, row.BirthPeriod, row.GeographicScope, row.UfCode, row.MunicipalityCode, row.Frequency);

            using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, transaction)
            {
                DestinationTableName = "ref.frequencia_nome",
                BatchSize = 10_000,
                BulkCopyTimeout = Math.Max(60, configuration.GetValue("NameFrequencySnapshot:SqlBulkTimeoutSeconds", 900))
            })
            {
                foreach (DataColumn column in table.Columns)
                    bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                await bulk.WriteToServerAsync(table, cancellationToken);
            }

            foreach (var coverage in rows
                .GroupBy(x => new { x.Type, x.GeographicScope })
                .Select(g => new
                {
                    g.Key.Type,
                    g.Key.GeographicScope,
                    IncludesSex = g.Any(x => x.Sex != "TODOS"),
                    IncludesPeriod = g.Any(x => x.BirthPeriod != "TODOS")
                }))
            {
                await using var insertCoverage = new SqlCommand(
                    """
                    INSERT ref.frequencia_nome_cobertura(
                        frequencia_nome_versao_id,tipo,escopo_geografico,inclui_sexo,inclui_periodo_nascimento,
                        cobertura,ausencia_significa,origem)
                    VALUES(@id,@tipo,@escopo,@sexo,@periodo,@cobertura,'NAO_PUBLICADA_OU_SUPRIMIDA',@origem);
                    """,
                    connection,
                    transaction);
                insertCoverage.Parameters.Add("@id", SqlDbType.BigInt).Value = versionId;
                insertCoverage.Parameters.Add("@tipo", SqlDbType.NVarChar, 20).Value = coverage.Type;
                insertCoverage.Parameters.Add("@escopo", SqlDbType.NVarChar, 20).Value = coverage.GeographicScope;
                insertCoverage.Parameters.Add("@sexo", SqlDbType.Bit).Value = coverage.IncludesSex;
                insertCoverage.Parameters.Add("@periodo", SqlDbType.Bit).Value = coverage.IncludesPeriod;
                insertCoverage.Parameters.Add("@cobertura", SqlDbType.NVarChar, 20).Value = coverage.GeographicScope == "BRASIL" ? "COMPLETA" : "PARCIAL";
                insertCoverage.Parameters.Add("@origem", SqlDbType.NVarChar, 300).Value = $"snapshot:{manifest.ReferenceCode}";
                await insertCoverage.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var publish = new SqlCommand(
                "EXEC ref.sp_publicar_frequencia_nome_versao @frequencia_nome_versao_id=@id,@conteudo_sha256=@sha;",
                connection,
                transaction))
            {
                publish.Parameters.Add("@id", SqlDbType.BigInt).Value = versionId;
                publish.Parameters.Add("@sha", SqlDbType.Binary, 32).Value = canonicalHash;
                await publish.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public sealed record SnapshotManifest(
        int SchemaVersion,
        string ReferenceCode,
        string Source,
        string SourceUrl,
        string ApiBaseUrl,
        string ReferenceDate,
        string? PublicationDate,
        string NormalizationVersion,
        SnapshotPolicy Policy,
        SnapshotFiles Snapshot,
        LightCheckManifest LightCheck);

    public sealed record SnapshotPolicy(
        string OperationalSource,
        bool RemoteApiRequiredAtRuntime,
        bool RemoteCheckMayMutateData,
        string MissingDetailedCellMeaning,
        bool ReplaceInPlace);

    public sealed record SnapshotFiles(string Format, string Directory, IReadOnlyList<SnapshotFile> Files);
    public sealed record SnapshotFile(string Path, string Kind, bool Required, string? Sha256 = null);
    public sealed record LightCheckManifest(bool Enabled, bool Authoritative, string Purpose, IReadOnlyList<string> Signals);

    public sealed record SnapshotRawRow(
        string? Tipo,
        string? Valor,
        long Frequencia,
        string? Sexo = null,
        string? PeriodoNascimento = null,
        string? EscopoGeografico = null,
        string? UfCodigo = null,
        string? MunicipioCodigo = null);

    public sealed record SnapshotRow(
        string Type,
        string Value,
        string NormalizedValue,
        string Sex,
        string BirthPeriod,
        string GeographicScope,
        string UfCode,
        string MunicipalityCode,
        long Frequency);
}

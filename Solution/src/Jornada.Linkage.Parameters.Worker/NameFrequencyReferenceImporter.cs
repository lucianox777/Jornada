using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Importa a referência nacional de frequências de nomes e sobrenomes publicada pelo
/// Censo Demográfico 2022. O importador é deliberadamente separado da estimação m/u:
/// ele apenas internaliza evidência observada e versionada; não calcula raridade nem peso.
///
/// O primeiro incremento consome os rankings nacionais completos. As distribuições por
/// período/UF/município serão adicionadas como fatos da mesma natureza em versão posterior,
/// preservando células suprimidas como ausência, nunca como frequência zero.
/// </summary>
public sealed class NameFrequencyReferenceImporter(
    ILogger<NameFrequencyReferenceImporter> logger,
    IConfiguration configuration,
    IOperationalSqlAdapter operationalSql,
    IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    public const string Operation = "IMPORT_NAME_FREQUENCY";
    private const string SourceName = "IBGE - Censo Demográfico 2022 - Nomes no Brasil";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var endpoint = configuration.GetValue(
                "NameFrequencyImport:BaseUrl",
                "https://servicodados.ibge.gov.br/api/v3/nomes/2022")!;
            var versionCode = configuration.GetValue(
                "NameFrequencyImport:VersionCode",
                "CENSO2022_NOMES_BRASIL_NACIONAL_V1")!;
            var edition = configuration.GetValue(
                "NameFrequencyImport:Edition",
                "Censo 2022 - Nomes no Brasil")!;
            var referenceDate = configuration.GetValue(
                "NameFrequencyImport:ReferenceDate",
                new DateOnly(2022, 8, 1));
            var publicationDate = configuration.GetValue<DateOnly?>(
                "NameFrequencyImport:PublicationDate");
            var timeoutSeconds = Math.Max(15, configuration.GetValue("NameFrequencyImport:HttpTimeoutSeconds", 120));
            var maxPages = Math.Max(1, configuration.GetValue("NameFrequencyImport:MaxPages", 20_000));

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Jornada-do-Cidadao/1.0 (+referencia-estatistica)");
            http.DefaultRequestHeaders.Referrer = new Uri("https://censo2022.ibge.gov.br/nomes/");

            var rows = new List<NameFrequencyImportRow>(350_000);
            await ReadRankingAsync(http, endpoint, "nome", "NOME", maxPages, rows, stoppingToken);
            await ReadRankingAsync(http, endpoint, "sobrenome", "SOBRENOME", maxPages, rows, stoppingToken);

            Validate(rows);
            rows.Sort(NameFrequencyImportRowComparer.Instance);
            var hash = ComputeCanonicalHash(rows);

            await PersistAsync(versionCode, edition, referenceDate, publicationDate, rows, hash, stoppingToken);

            logger.LogInformation(
                "Referência de frequências importada. Versão={Version}; linhas={Rows}; SHA256={Hash}; escopo=BRASIL.",
                versionCode,
                rows.Count,
                Convert.ToHexString(hash));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao importar referência de frequências de nomes/sobrenomes.");
            Environment.ExitCode = 1;
        }
        finally
        {
            applicationLifetime.StopApplication();
        }
    }

    private static async Task ReadRankingAsync(
        HttpClient http,
        string endpoint,
        string apiType,
        string domainType,
        int maxPages,
        List<NameFrequencyImportRow> destination,
        CancellationToken cancellationToken)
    {
        var page = 1;
        var totalPages = 1;
        do
        {
            if (page > maxPages)
                throw new InvalidOperationException($"Ranking {apiType} excedeu limite de segurança de {maxPages} páginas.");

            var url = $"{endpoint.TrimEnd('/')}/localidade/0/ranking/{apiType}?page={page.ToString(CultureInfo.InvariantCulture)}";
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var parsed = ParseRankingPage(document.RootElement, domainType);
            if (parsed.TotalPages < page)
                throw new InvalidOperationException($"Resposta inconsistente do ranking {apiType}: totalPages={parsed.TotalPages}, page={page}.");

            totalPages = parsed.TotalPages;
            destination.AddRange(parsed.Rows);
            page++;
        }
        while (page <= totalPages);
    }

    public static NameFrequencyRankingPage ParseRankingPage(JsonElement root, string domainType)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Resposta de ranking deve ser um objeto JSON.");
        if (!root.TryGetProperty("totalPages", out var totalPagesNode) ||
            !totalPagesNode.TryGetInt32(out var totalPages) || totalPages <= 0)
            throw new InvalidDataException("Resposta de ranking sem totalPages válido.");
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Resposta de ranking sem items[].");

        var rows = new List<NameFrequencyImportRow>(items.GetArrayLength());
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("nome", out var nameNode) || nameNode.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Item do ranking sem nome.");
            if (!item.TryGetProperty("frequencia", out var frequencyNode) ||
                !frequencyNode.TryGetInt64(out var frequency) || frequency <= 0)
                throw new InvalidDataException("Item do ranking sem frequência positiva.");

            var name = nameNode.GetString()?.Trim();
            var normalized = IdentityComparison.NormalizeText(name);
            if (string.IsNullOrWhiteSpace(name) || normalized is null)
                throw new InvalidDataException("Item do ranking contém nome vazio após normalização.");

            rows.Add(new NameFrequencyImportRow(domainType, name, normalized, frequency));
        }

        return new NameFrequencyRankingPage(totalPages, rows);
    }

    private static void Validate(IReadOnlyCollection<NameFrequencyImportRow> rows)
    {
        if (rows.Count == 0)
            throw new InvalidOperationException("Fonte retornou referência vazia.");
        if (!rows.Any(x => x.Type == "NOME"))
            throw new InvalidOperationException("Fonte não retornou frequências de NOME.");
        if (!rows.Any(x => x.Type == "SOBRENOME"))
            throw new InvalidOperationException("Fonte não retornou frequências de SOBRENOME.");

        var duplicate = rows
            .GroupBy(x => (x.Type, x.Value), StringTupleComparer.Instance)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Fonte retornou chave duplicada: {duplicate.Key.Type}/{duplicate.Key.Value}.");
    }

    public static byte[] ComputeCanonicalHash(IEnumerable<NameFrequencyImportRow> rows)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var row in rows.OrderBy(x => x, NameFrequencyImportRowComparer.Instance))
        {
            var line = $"{row.Type}|{row.Value}|{row.NormalizedValue}|{row.Frequency.ToString(CultureInfo.InvariantCulture)}\n";
            hash.AppendData(Encoding.UTF8.GetBytes(line));
        }
        return hash.GetHashAndReset();
    }

    private async Task PersistAsync(
        string versionCode,
        string edition,
        DateOnly referenceDate,
        DateOnly? publicationDate,
        IReadOnlyCollection<NameFrequencyImportRow> rows,
        byte[] hash,
        CancellationToken cancellationToken)
    {
        await using var connection = await operationalSql.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            long versionId;
            string? status;
            byte[]? existingHash;
            await using (var read = new SqlCommand(
                """
                SELECT frequencia_nome_versao_id,status,conteudo_sha256
                FROM ref.frequencia_nome_versao WITH(UPDLOCK,HOLDLOCK)
                WHERE codigo=@codigo;
                """,
                connection,
                transaction))
            {
                read.Parameters.Add("@codigo", SqlDbType.NVarChar, 80).Value = versionCode;
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
                if (existingHash is not null && existingHash.AsSpan().SequenceEqual(hash))
                {
                    await transaction.CommitAsync(cancellationToken);
                    logger.LogInformation("Importação idempotente: versão {Version} já publicada com o mesmo SHA-256.", versionCode);
                    return;
                }

                throw new InvalidOperationException(
                    $"Versão {versionCode} já foi publicada com conteúdo diferente; use novo VersionCode.");
            }

            if (versionId == 0)
            {
                await using var create = new SqlCommand(
                    """
                    INSERT ref.frequencia_nome_versao(codigo,fonte,edicao,data_referencia,publicado_em,status)
                    VALUES(@codigo,@fonte,@edicao,@data_referencia,@publicado_em,'CARREGANDO');
                    SELECT CONVERT(BIGINT,SCOPE_IDENTITY());
                    """,
                    connection,
                    transaction);
                create.Parameters.Add("@codigo", SqlDbType.NVarChar, 80).Value = versionCode;
                create.Parameters.Add("@fonte", SqlDbType.NVarChar, 200).Value = SourceName;
                create.Parameters.Add("@edicao", SqlDbType.NVarChar, 120).Value = edition;
                create.Parameters.Add("@data_referencia", SqlDbType.Date).Value = referenceDate.ToDateTime(TimeOnly.MinValue);
                create.Parameters.Add("@publicado_em", SqlDbType.Date).Value = publicationDate is null
                    ? DBNull.Value
                    : publicationDate.Value.ToDateTime(TimeOnly.MinValue);
                versionId = Convert.ToInt64(await create.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            }
            else
            {
                await using var clear = new SqlCommand(
                    "DELETE FROM ref.frequencia_nome WHERE frequencia_nome_versao_id=@id;",
                    connection,
                    transaction);
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
                table.Rows.Add(versionId, row.Type, row.Value, row.NormalizedValue, "TODOS", "TODOS", "BRASIL", "00", "0000000", row.Frequency);

            using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, transaction)
            {
                DestinationTableName = "ref.frequencia_nome",
                BatchSize = 10_000,
                BulkCopyTimeout = Math.Max(60, configuration.GetValue("NameFrequencyImport:SqlBulkTimeoutSeconds", 900))
            })
            {
                foreach (DataColumn column in table.Columns)
                    bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                await bulk.WriteToServerAsync(table, cancellationToken);
            }

            await using (var publish = new SqlCommand(
                "EXEC ref.sp_publicar_frequencia_nome_versao @frequencia_nome_versao_id=@id,@conteudo_sha256=@sha;",
                connection,
                transaction))
            {
                publish.Parameters.Add("@id", SqlDbType.BigInt).Value = versionId;
                publish.Parameters.Add("@sha", SqlDbType.Binary, 32).Value = hash;
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

    public sealed record NameFrequencyImportRow(string Type, string Value, string NormalizedValue, long Frequency);
    public sealed record NameFrequencyRankingPage(int TotalPages, IReadOnlyList<NameFrequencyImportRow> Rows);

    private sealed class NameFrequencyImportRowComparer : IComparer<NameFrequencyImportRow>
    {
        public static readonly NameFrequencyImportRowComparer Instance = new();
        public int Compare(NameFrequencyImportRow? x, NameFrequencyImportRow? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            var type = string.CompareOrdinal(x.Type, y.Type);
            return type != 0 ? type : string.CompareOrdinal(x.Value, y.Value);
        }
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string Type, string Value)>
    {
        public static readonly StringTupleComparer Instance = new();
        public bool Equals((string Type, string Value) x, (string Type, string Value) y) =>
            string.Equals(x.Type, y.Type, StringComparison.Ordinal) &&
            string.Equals(x.Value, y.Value, StringComparison.Ordinal);
        public int GetHashCode((string Type, string Value) obj) => HashCode.Combine(obj.Type, obj.Value);
    }
}

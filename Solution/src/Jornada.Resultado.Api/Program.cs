using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Net;
using Jornada.Operational.Sql;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Jornada")
    ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
var jornadaApiBaseUrl = builder.Configuration["JornadaApiBaseUrl"]
    ?? throw new InvalidOperationException("JornadaApiBaseUrl não configurada.");
var databaseProvider = builder.Configuration["Database:Provider"] ?? OperationalDatabaseProviders.SqlServer;
var operationalDatabase = OperationalDatabaseAdapterFactory.Create(databaseProvider, connectionString);

builder.Services.AddSingleton<IOperationalDatabaseAdapter>(operationalDatabase);
builder.Services.AddSingleton(new ResultadoDatabaseDialect(operationalDatabase.Provider));
builder.Services.AddSingleton(new ResultadoOptions(jornadaApiBaseUrl));
builder.Services.AddSingleton<ResultadoRepository>();
builder.Services.AddHttpClient();

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    databaseProvider = operationalDatabase.Provider,
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/v1/ingestao/resultados/{nomeArquivo}", async (
    HttpRequest http,
    string nomeArquivo,
    ResultadoRepository repository,
    ResultadoOptions options,
    IHttpClientFactory httpClientFactory,
    CancellationToken ct) =>
{
    var gestor = http.Headers["X-Jornada-Gestor"].ToString().Trim();
    var accessKey = http.Headers["X-Jornada-Access-Key"].ToString();
    if (string.IsNullOrWhiteSpace(gestor) || string.IsNullOrWhiteSpace(accessKey))
        return Results.Unauthorized();

    string parsedFileName;
    try { parsedFileName = ResultadoNomeArquivo.Parse(nomeArquivo); }
    catch (ArgumentException ex) { return Results.BadRequest(new { erro = ex.Message }); }

    var candidate = await repository.FindLatestAsync(gestor, parsedFileName, ct);
    if (candidate is null) return Results.NotFound();

    using var authRequest = new HttpRequestMessage(
        HttpMethod.Get,
        ResultadoUrl.Combine(options.JornadaApiBaseUrl, $"api/v1/ingestao/entregas/{candidate.EntregaId}"));
    authRequest.Headers.TryAddWithoutValidation("X-Jornada-Gestor", gestor);
    authRequest.Headers.TryAddWithoutValidation("X-Jornada-Access-Key", accessKey);
    using var authResponse = await httpClientFactory.CreateClient().SendAsync(authRequest, HttpCompletionOption.ResponseHeadersRead, ct);
    if (authResponse.StatusCode != HttpStatusCode.OK)
        return Results.StatusCode((int)authResponse.StatusCode);

    return Results.Ok(await repository.GetDetailAsync(candidate, parsedFileName, ct));
});

app.Run();

internal sealed record ResultadoOptions(string JornadaApiBaseUrl);
internal sealed record ResultadoCandidate(Guid EntregaId, long EntregasEncontradas);

internal static class ResultadoNomeArquivo
{
    public static string Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("nomeArquivo deve ser o nome exato do ZIP enviado, sem caminho.");

        var value = raw.Trim();
        if (value.Length > 260
            || !value.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || value.Contains('/')
            || value.Contains('\\')
            || value.Contains(':'))
            throw new ArgumentException("nomeArquivo deve ser o nome exato do ZIP enviado, sem caminho.");

        return value;
    }
}

internal static class ResultadoUrl
{
    public static Uri Combine(string baseUrl, string relative) =>
        new(new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute), relative);
}

internal sealed class ResultadoDatabaseDialect
{
    public ResultadoDatabaseDialect(string provider)
    {
        Provider = provider;
        FindLatestDeliverySql = provider switch
        {
            OperationalDatabaseProviders.SqlServer => """
                SELECT TOP(1) e.entrega_id, COUNT_BIG(*) OVER() AS entregas_encontradas
                FROM ingestao.entrega e
                JOIN ref.gestor g ON g.gestor_id=e.gestor_id
                JOIN bronze.entrega_arquivo b ON b.entrega_id=e.entrega_id
                WHERE g.codigo=@gestor
                  AND b.nome_arquivo=@nomeArquivo
                ORDER BY e.recebido_em DESC,e.entrega_id DESC;
                """,
            OperationalDatabaseProviders.PostgreSql => """
                SELECT e.entrega_id, COUNT(*) OVER() AS entregas_encontradas
                FROM ingestao.entrega e
                JOIN ref.gestor g ON g.gestor_id=e.gestor_id
                JOIN bronze.entrega_arquivo b ON b.entrega_id=e.entrega_id
                WHERE g.codigo=@gestor
                  AND b.nome_arquivo=@nomeArquivo
                ORDER BY e.recebido_em DESC,e.entrega_id DESC
                LIMIT 1;
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Provider de banco não suportado.")
        };

        CountDetailedItemsSql = provider switch
        {
            OperationalDatabaseProviders.SqlServer => """
                SELECT ip.classe_item,ip.resultado,COUNT_BIG(*)
                FROM ingestao.item_processado ip
                JOIN ingestao.lote l ON l.lote_id=ip.lote_id
                WHERE l.entrega_id=@entrega
                GROUP BY ip.classe_item,ip.resultado;
                """,
            OperationalDatabaseProviders.PostgreSql => """
                SELECT ip.classe_item,ip.resultado,COUNT(*)
                FROM ingestao.item_processado ip
                JOIN ingestao.lote l ON l.lote_id=ip.lote_id
                WHERE l.entrega_id=@entrega
                GROUP BY ip.classe_item,ip.resultado;
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Provider de banco não suportado.")
        };
    }

    public string Provider { get; }
    public string FindLatestDeliverySql { get; }
    public string CountDetailedItemsSql { get; }
}

internal sealed class ResultadoRepository(
    IOperationalDatabaseAdapter connections,
    ResultadoDatabaseDialect dialect)
{
    public async Task<ResultadoCandidate?> FindLatestAsync(string gestor, string nomeArquivo, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = dialect.FindLatestDeliverySql;
        AddParameter(command, "@gestor", DbType.String, gestor, 30);
        AddParameter(command, "@nomeArquivo", DbType.String, nomeArquivo, 260);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new ResultadoCandidate(reader.GetGuid(0), reader.GetInt64(1));
    }

    public async Task<object> GetDetailAsync(ResultadoCandidate candidate, string nomeArquivo, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        var entrega = await LoadDeliveryAsync(connection, candidate.EntregaId, ct);
        var lotes = await LoadLotsAsync(connection, candidate.EntregaId, ct);
        var detailed = await LoadDetailedSummaryAsync(connection, candidate.EntregaId, dialect.CountDetailedItemsSql, ct);
        var consolidated = await LoadConsolidatedSummaryAsync(connection, candidate.EntregaId, ct);
        var keys = detailed.Keys.Union(consolidated.Keys)
            .OrderBy(k => k.Classe, StringComparer.Ordinal)
            .ThenBy(k => k.Resultado, StringComparer.Ordinal);
        var resumo = keys.Select(k => new ResultadoContagem(
            k.Classe,
            k.Resultado,
            detailed.GetValueOrDefault(k),
            consolidated.GetValueOrDefault(k),
            detailed.GetValueOrDefault(k) + consolidated.GetValueOrDefault(k))).ToArray();
        var totalDetalhado = detailed.Values.Sum();
        var totalConsolidado = consolidated.Values.Sum();
        var terminal = entrega.Status is "PROCESSADA" or "REJEITADA" or "QUARENTENA";

        return new
        {
            nomeArquivoConsultado = nomeArquivo,
            databaseProvider = dialect.Provider,
            finalizado = terminal,
            entregasEncontradasParaOMesmoNome = candidate.EntregasEncontradas,
            entrega,
            processamento = new
            {
                lotes,
                resumoItens = resumo,
                itensDetalhadosDisponiveis = totalDetalhado,
                itensConsolidados = totalConsolidado,
                detalheItemAItemIntegral = totalConsolidado == 0,
                observacaoRetencao = totalConsolidado == 0
                    ? "A trilha granular ainda está integralmente disponível em ingestao.item_processado."
                    : "Há itens já consolidados pela política de retenção; o resumo permanece íntegro, mas o detalhe item a item histórico não é mais completo."
            }
        };
    }

    private static async Task<EntregaDetalhe> LoadDeliveryAsync(DbConnection connection, Guid entregaId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.entrega_id,e.status,e.payload_sha256,e.bytes_recebidos,e.data_referencia,
                   e.recebido_em,e.ultima_atualizacao,b.nome_arquivo,g.codigo,so.codigo,
                   gpv.versao,e.natureza,tr.codigo,trv.versao
            FROM ingestao.entrega e
            JOIN ref.gestor g ON g.gestor_id=e.gestor_id
            JOIN ref.sistema_origem so ON so.sistema_origem_id=e.sistema_origem_id
            JOIN ref.gestor_pessoa_versao gpv ON gpv.gestor_pessoa_versao_id=e.gestor_pessoa_versao_id
            JOIN bronze.entrega_arquivo b ON b.entrega_id=e.entrega_id
            LEFT JOIN ref.tipo_registro tr ON tr.tipo_registro_id=e.tipo_registro_id
            LEFT JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_versao_id=e.tipo_registro_versao_id
            WHERE e.entrega_id=@entrega;
            """;
        AddParameter(command, "@entrega", DbType.Guid, entregaId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Entrega desapareceu durante a consulta.");
        return new EntregaDetalhe(
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3),
            ReadDateTimeOffset(reader, 4), ReadDateTimeOffset(reader, 5), ReadDateTimeOffset(reader, 6),
            reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetInt32(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetInt32(13));
    }

    private static async Task<List<LoteDetalhe>> LoadLotsAsync(DbConnection connection, Guid entregaId, CancellationToken ct)
    {
        var lotes = new List<LoteDetalhe>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT lote_id,lote_seq,lote_total,qtd_pessoas,qtd_registros,status,erro_codigo,
                   tentativa_count,recuperacao_count,ultima_tentativa_em,proxima_tentativa_em,
                   poison_em,criado_em,atualizado_em
            FROM ingestao.lote
            WHERE entrega_id=@entrega
            ORDER BY lote_seq,lote_id;
            """;
        AddParameter(command, "@entrega", DbType.Guid, entregaId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lotes.Add(new LoteDetalhe(
                reader.GetGuid(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4),
                reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetInt32(7), reader.GetInt32(8),
                ReadNullableDateTimeOffset(reader, 9),
                ReadNullableDateTimeOffset(reader, 10),
                ReadNullableDateTimeOffset(reader, 11),
                ReadDateTimeOffset(reader, 12), ReadDateTimeOffset(reader, 13)));
        }
        return lotes;
    }

    private static async Task<Dictionary<ResultadoKey, long>> LoadDetailedSummaryAsync(
        DbConnection connection,
        Guid entregaId,
        string sql,
        CancellationToken ct)
    {
        var result = new Dictionary<ResultadoKey, long>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@entrega", DbType.Guid, entregaId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result[new ResultadoKey(reader.GetString(0), reader.GetString(1))] = reader.GetInt64(2);
        return result;
    }

    private static async Task<Dictionary<ResultadoKey, long>> LoadConsolidatedSummaryAsync(DbConnection connection, Guid entregaId, CancellationToken ct)
    {
        var result = new Dictionary<ResultadoKey, long>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT classe_item,resultado,SUM(quantidade)
            FROM ingestao.item_processado_resumo
            WHERE entrega_id=@entrega
            GROUP BY classe_item,resultado;
            """;
        AddParameter(command, "@entrega", DbType.Guid, entregaId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result[new ResultadoKey(reader.GetString(0), reader.GetString(1))] = Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture);
        return result;
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value, int? size = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        if (size is int parameterSize) parameter.Size = parameterSize;
        command.Parameters.Add(parameter);
    }

    private static DateTimeOffset ReadDateTimeOffset(DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => throw new InvalidDataException($"Valor temporal inesperado no ordinal {ordinal}: {value.GetType().FullName}.")
        };
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadDateTimeOffset(reader, ordinal);
}

internal sealed record EntregaDetalhe(
    Guid EntregaId, string Status, string PayloadSha256, long BytesRecebidos,
    DateTimeOffset DataReferencia, DateTimeOffset RecebidoEm, DateTimeOffset UltimaAtualizacao,
    string NomeArquivo, string Gestor, string CodigoSistemaOrigem, int PessoaSchemaVersao,
    string? Natureza, string? CodigoTipo, int? TipoVersao);

internal sealed record LoteDetalhe(
    Guid LoteId, int LoteSeq, int LoteTotal, int QtdPessoas, int QtdRegistros,
    string Status, string? ErroCodigo, int TentativaCount, int RecuperacaoCount,
    DateTimeOffset? UltimaTentativaEm, DateTimeOffset? ProximaTentativaEm,
    DateTimeOffset? PoisonEm, DateTimeOffset CriadoEm, DateTimeOffset AtualizadoEm);

internal readonly record struct ResultadoKey(string Classe, string Resultado);
internal sealed record ResultadoContagem(string ClasseItem, string Resultado, long QuantidadeDetalhada, long QuantidadeConsolidada, long TotalObservado);

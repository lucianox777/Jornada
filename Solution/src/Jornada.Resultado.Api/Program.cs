using System.Data;
using System.Net;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Jornada")
    ?? throw new InvalidOperationException("ConnectionStrings:Jornada não configurada.");
var jornadaApiBaseUrl = builder.Configuration["JornadaApiBaseUrl"]
    ?? throw new InvalidOperationException("JornadaApiBaseUrl não configurada.");

builder.Services.AddSingleton(new ResultadoOptions(connectionString, jornadaApiBaseUrl));
builder.Services.AddSingleton<ResultadoRepository>();
builder.Services.AddHttpClient();

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow }));

app.MapGet("/api/v1/ingestao/resultados/{identificador}", async (
    HttpRequest http,
    string identificador,
    ResultadoRepository repository,
    ResultadoOptions options,
    IHttpClientFactory httpClientFactory,
    CancellationToken ct) =>
{
    var gestor = http.Headers["X-Jornada-Gestor"].ToString().Trim();
    var accessKey = http.Headers["X-Jornada-Access-Key"].ToString();
    if (string.IsNullOrWhiteSpace(gestor) || string.IsNullOrWhiteSpace(accessKey))
        return Results.Unauthorized();

    ResultadoIdentificador parsed;
    try { parsed = ResultadoIdentificador.Parse(identificador); }
    catch (ArgumentException ex) { return Results.BadRequest(new { erro = ex.Message }); }

    var candidate = await repository.FindLatestAsync(gestor, parsed, ct);
    if (candidate is null) return Results.NotFound();

    using var authRequest = new HttpRequestMessage(
        HttpMethod.Get,
        ResultadoUrl.Combine(options.JornadaApiBaseUrl, $"api/v1/ingestao/entregas/{candidate.EntregaId}"));
    authRequest.Headers.TryAddWithoutValidation("X-Jornada-Gestor", gestor);
    authRequest.Headers.TryAddWithoutValidation("X-Jornada-Access-Key", accessKey);
    using var authResponse = await httpClientFactory.CreateClient().SendAsync(authRequest, HttpCompletionOption.ResponseHeadersRead, ct);
    if (authResponse.StatusCode != HttpStatusCode.OK)
        return Results.StatusCode((int)authResponse.StatusCode);

    return Results.Ok(await repository.GetDetailAsync(candidate, parsed, ct));
});

app.Run();

internal sealed record ResultadoOptions(string ConnectionString, string JornadaApiBaseUrl);
internal sealed record ResultadoCandidate(Guid EntregaId, long EntregasEncontradas);
internal enum ResultadoIdentificadorTipo { SHA256, NOME_ARQUIVO }

internal sealed record ResultadoIdentificador(ResultadoIdentificadorTipo Tipo, string Valor)
{
    public static ResultadoIdentificador Parse(string raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 64 && value.All(IsLowerHex))
            return new ResultadoIdentificador(ResultadoIdentificadorTipo.SHA256, value);
        if (value.Length > 260 || !value.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal))
            throw new ArgumentException("Identificador deve ser SHA-256 hexadecimal minúsculo ou o nome exato do ZIP enviado.");
        return new ResultadoIdentificador(ResultadoIdentificadorTipo.NOME_ARQUIVO, value);
    }

    private static bool IsLowerHex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f';
}

internal static class ResultadoUrl
{
    public static Uri Combine(string baseUrl, string relative) =>
        new(new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute), relative);
}

internal sealed class ResultadoRepository(ResultadoOptions options)
{
    public async Task<ResultadoCandidate?> FindLatestAsync(string gestor, ResultadoIdentificador identificador, CancellationToken ct)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP(1) e.entrega_id, COUNT_BIG(*) OVER() AS entregas_encontradas
            FROM ingestao.entrega e
            JOIN ref.gestor g ON g.gestor_id=e.gestor_id
            JOIN bronze.entrega_arquivo b ON b.entrega_id=e.entrega_id
            WHERE g.codigo=@gestor
              AND ((@sha IS NOT NULL AND e.payload_sha256=@sha)
                OR (@nome IS NOT NULL AND b.nome_arquivo=@nome))
            ORDER BY e.recebido_em DESC,e.entrega_id DESC;
            """;
        command.Parameters.Add(new SqlParameter("@gestor", SqlDbType.NVarChar, 30) { Value = gestor });
        command.Parameters.Add(new SqlParameter("@sha", SqlDbType.Char, 64)
        {
            Value = identificador.Tipo == ResultadoIdentificadorTipo.SHA256 ? identificador.Valor : DBNull.Value
        });
        command.Parameters.Add(new SqlParameter("@nome", SqlDbType.NVarChar, 260)
        {
            Value = identificador.Tipo == ResultadoIdentificadorTipo.NOME_ARQUIVO ? identificador.Valor : DBNull.Value
        });
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new ResultadoCandidate(reader.GetGuid(0), reader.GetInt64(1));
    }

    public async Task<object> GetDetailAsync(ResultadoCandidate candidate, ResultadoIdentificador identificador, CancellationToken ct)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(ct);
        var entrega = await LoadDeliveryAsync(connection, candidate.EntregaId, ct);
        var lotes = await LoadLotsAsync(connection, candidate.EntregaId, ct);
        var detailed = await LoadDetailedSummaryAsync(connection, candidate.EntregaId, ct);
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
            identificadorConsultado = identificador.Valor,
            tipoIdentificador = identificador.Tipo.ToString(),
            finalizado = terminal,
            entregasEncontradasParaOMesmoArquivo = candidate.EntregasEncontradas,
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

    private static async Task<EntregaDetalhe> LoadDeliveryAsync(SqlConnection connection, Guid entregaId, CancellationToken ct)
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
        command.Parameters.AddWithValue("@entrega", entregaId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Entrega desapareceu durante a consulta.");
        return new EntregaDetalhe(
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3),
            reader.GetDateTimeOffset(4), reader.GetDateTimeOffset(5), reader.GetDateTimeOffset(6),
            reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetInt32(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetInt32(13));
    }

    private static async Task<List<LoteDetalhe>> LoadLotsAsync(SqlConnection connection, Guid entregaId, CancellationToken ct)
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
        command.Parameters.AddWithValue("@entrega", entregaId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lotes.Add(new LoteDetalhe(
                reader.GetGuid(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4),
                reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetInt32(7), reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetDateTimeOffset(9),
                reader.IsDBNull(10) ? null : reader.GetDateTimeOffset(10),
                reader.IsDBNull(11) ? null : reader.GetDateTimeOffset(11),
                reader.GetDateTimeOffset(12), reader.GetDateTimeOffset(13)));
        }
        return lotes;
    }

    private static async Task<Dictionary<ResultadoKey, long>> LoadDetailedSummaryAsync(SqlConnection connection, Guid entregaId, CancellationToken ct)
    {
        var result = new Dictionary<ResultadoKey, long>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ip.classe_item,ip.resultado,COUNT_BIG(*)
            FROM ingestao.item_processado ip
            JOIN ingestao.lote l ON l.lote_id=ip.lote_id
            WHERE l.entrega_id=@entrega
            GROUP BY ip.classe_item,ip.resultado;
            """;
        command.Parameters.AddWithValue("@entrega", entregaId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result[new ResultadoKey(reader.GetString(0), reader.GetString(1))] = reader.GetInt64(2);
        return result;
    }

    private static async Task<Dictionary<ResultadoKey, long>> LoadConsolidatedSummaryAsync(SqlConnection connection, Guid entregaId, CancellationToken ct)
    {
        var result = new Dictionary<ResultadoKey, long>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT classe_item,resultado,SUM(quantidade)
            FROM ingestao.item_processado_resumo
            WHERE entrega_id=@entrega
            GROUP BY classe_item,resultado;
            """;
        command.Parameters.AddWithValue("@entrega", entregaId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result[new ResultadoKey(reader.GetString(0), reader.GetString(1))] = reader.GetInt64(2);
        return result;
    }
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

using System.Data;
using System.Data.Common;

namespace Jornada.Operational.Sql;

public sealed record PostgreSqlIngestionMetadataRequest(
    string GestorCodigo,
    string CodigoSistemaOrigem,
    int PessoaSchemaVersao,
    string? Natureza,
    string? CodigoTipo,
    int? TipoVersao,
    string IdempotencyKey,
    string PayloadSha256,
    long BytesRecebidos,
    DateTimeOffset DataReferencia,
    string NomeArquivo,
    string ObjetoChave,
    string BronzeSha256,
    long BronzeBytes);

public sealed record PostgreSqlIngestionMetadataReceipt(
    Guid EntregaId,
    string Status,
    string PayloadSha256,
    long BytesRecebidos,
    DateTimeOffset RecebidoEm,
    bool RetransmissaoIdempotente);

public sealed record PostgreSqlIngestionMetadataStatus(
    Guid EntregaId,
    string Status,
    DateTimeOffset RecebidoEm,
    DateTimeOffset UltimaAtualizacao,
    string? ErroCodigo);

/// <summary>
/// Persistência relacional da primeira etapa da ingestão PostgreSQL.
/// O objeto Bronze continua sendo gravado pelo IBronzeObjectStore antes desta operação; esta classe
/// registra de forma atômica Entrega + referência Bronze + lote inicial e preserva idempotência por Gestor.
/// </summary>
public sealed class PostgreSqlIngestionMetadataStore
{
    private readonly IOperationalDatabaseAdapter database;

    public PostgreSqlIngestionMetadataStore(IOperationalDatabaseAdapter database)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        if (!string.Equals(database.Provider, OperationalDatabaseProviders.PostgreSql, StringComparison.Ordinal))
            throw new ArgumentException("PostgreSqlIngestionMetadataStore exige provider PostgreSql.", nameof(database));
    }

    public async Task<PostgreSqlIngestionMetadataReceipt> RegisterAsync(
        PostgreSqlIngestionMetadataRequest request,
        CancellationToken ct = default)
    {
        Validate(request);
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var resolved = await ResolveContextAsync(connection, transaction, request, ct);
            var entregaId = Guid.NewGuid();
            var loteId = Guid.NewGuid();

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO ingestao.entrega(
                    entrega_id,gestor_id,sistema_origem_id,gestor_pessoa_versao_id,natureza,
                    tipo_registro_id,tipo_registro_versao_id,idempotency_key,payload_sha256,
                    bytes_recebidos,status,data_referencia,recebido_em,ultima_atualizacao)
                VALUES(
                    @entrega_id,@gestor_id,@sistema_origem_id,@gestor_pessoa_versao_id,@natureza,
                    @tipo_registro_id,@tipo_registro_versao_id,@idempotency_key,@sha,
                    @bytes,'RECEBIDA',@data_referencia,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)
                ON CONFLICT(gestor_id,idempotency_key) DO NOTHING
                RETURNING entrega_id,status,payload_sha256,bytes_recebidos,recebido_em;
                """;
            Add(insert, "@entrega_id", entregaId);
            Add(insert, "@gestor_id", resolved.GestorId);
            Add(insert, "@sistema_origem_id", resolved.SistemaOrigemId);
            Add(insert, "@gestor_pessoa_versao_id", resolved.GestorPessoaVersaoId);
            Add(insert, "@natureza", request.Natureza);
            Add(insert, "@tipo_registro_id", resolved.TipoRegistroId);
            Add(insert, "@tipo_registro_versao_id", resolved.TipoRegistroVersaoId);
            Add(insert, "@idempotency_key", request.IdempotencyKey);
            Add(insert, "@sha", request.PayloadSha256);
            Add(insert, "@bytes", request.BytesRecebidos);
            Add(insert, "@data_referencia", request.DataReferencia.ToUniversalTime());

            await using var insertedReader = await insert.ExecuteReaderAsync(ct);
            if (await insertedReader.ReadAsync(ct))
            {
                var receipt = ReadReceipt(insertedReader, retransmission: false);
                await insertedReader.DisposeAsync();

                await InsertBronzeReferenceAsync(connection, transaction, request, entregaId, ct);
                await InsertInitialLotAsync(connection, transaction, entregaId, loteId, ct);
                await transaction.CommitAsync(ct);
                return receipt;
            }
            await insertedReader.DisposeAsync();

            var existing = await LoadExistingAsync(connection, transaction, resolved.GestorId, request.IdempotencyKey, ct)
                ?? throw new InvalidOperationException("Conflito de idempotência sem Entrega existente.");
            if (!string.Equals(existing.PayloadSha256, request.PayloadSha256, StringComparison.Ordinal)
                || existing.BytesRecebidos != request.BytesRecebidos)
                throw new InvalidOperationException("Idempotency-Key já utilizado para conteúdo diferente.");

            await transaction.CommitAsync(ct);
            return existing with { RetransmissaoIdempotente = true };
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
            throw;
        }
    }

    public async Task<PostgreSqlIngestionMetadataStatus?> GetStatusAsync(
        string gestorCodigo,
        Guid entregaId,
        CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.entrega_id,e.status,e.recebido_em,e.ultima_atualizacao,
                   (SELECT l.erro_codigo
                      FROM ingestao.lote l
                     WHERE l.entrega_id=e.entrega_id AND l.erro_codigo IS NOT NULL
                     ORDER BY l.atualizado_em DESC,l.lote_seq DESC
                     LIMIT 1) AS erro
              FROM ingestao.entrega e
              JOIN ref.gestor g ON g.gestor_id=e.gestor_id
             WHERE e.entrega_id=@entrega_id AND g.codigo=@gestor;
            """;
        Add(command, "@entrega_id", entregaId);
        Add(command, "@gestor", gestorCodigo);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new PostgreSqlIngestionMetadataStatus(
            reader.GetGuid(0),
            reader.GetString(1),
            ReadTimestamp(reader, 2),
            ReadTimestamp(reader, 3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private static async Task<ResolvedContext> ResolveContextAsync(
        DbConnection connection,
        DbTransaction transaction,
        PostgreSqlIngestionMetadataRequest request,
        CancellationToken ct)
    {
        long gestorId;
        long pessoaVersaoId;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT g.gestor_id,gpv.gestor_pessoa_versao_id
                  FROM ref.gestor g
                  JOIN ref.gestor_pessoa_versao gpv
                    ON gpv.gestor_id=g.gestor_id AND gpv.versao=@pessoa_versao
                 WHERE g.codigo=@gestor AND g.ativo=TRUE
                   AND gpv.status IN ('ATIVA','ENCERRADA');
                """;
            Add(command, "@gestor", request.GestorCodigo);
            Add(command, "@pessoa_versao", request.PessoaSchemaVersao);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException("Contrato cadastral do Gestor/versão não encontrado.");
            gestorId = reader.GetInt64(0);
            pessoaVersaoId = reader.GetInt64(1);
        }

        long sistemaOrigemId;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT sistema_origem_id
                  FROM ref.sistema_origem
                 WHERE gestor_id=@gestor_id AND codigo=@codigo AND ativo=TRUE;
                """;
            Add(command, "@gestor_id", gestorId);
            Add(command, "@codigo", request.CodigoSistemaOrigem);
            var value = await command.ExecuteScalarAsync(ct);
            if (value is null || value is DBNull)
                throw new InvalidOperationException("codigoSistemaOrigem não está cadastrado/ativo para o Gestor.");
            sistemaOrigemId = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        if (request.CodigoTipo is null)
            return new ResolvedContext(gestorId, sistemaOrigemId, pessoaVersaoId, null, null);
        if (request.Natureza is null || request.TipoVersao is null)
            throw new InvalidOperationException("Natureza e versão são obrigatórias quando CodigoTipo é informado.");

        await using var typeCommand = connection.CreateCommand();
        typeCommand.Transaction = transaction;
        typeCommand.CommandText = """
            SELECT tr.tipo_registro_id,trv.tipo_registro_versao_id
              FROM ref.tipo_registro tr
              JOIN ref.tipo_registro_versao trv
                ON trv.tipo_registro_id=tr.tipo_registro_id AND trv.versao=@versao
             WHERE tr.gestor_id=@gestor_id AND tr.codigo=@codigo AND tr.natureza=@natureza
               AND tr.ativo=TRUE AND trv.status IN ('ATIVA','ENCERRADA');
            """;
        Add(typeCommand, "@gestor_id", gestorId);
        Add(typeCommand, "@codigo", request.CodigoTipo);
        Add(typeCommand, "@natureza", request.Natureza);
        Add(typeCommand, "@versao", request.TipoVersao.Value);
        await using var typeReader = await typeCommand.ExecuteReaderAsync(ct);
        if (!await typeReader.ReadAsync(ct))
            throw new InvalidOperationException("Tipo/versão não pertence ao Gestor ou não está disponível.");
        return new ResolvedContext(gestorId, sistemaOrigemId, pessoaVersaoId, typeReader.GetInt64(0), typeReader.GetInt64(1));
    }

    private static async Task InsertBronzeReferenceAsync(
        DbConnection connection,
        DbTransaction transaction,
        PostgreSqlIngestionMetadataRequest request,
        Guid entregaId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO bronze.entrega_arquivo(
                entrega_id,nome_arquivo,content_type,objeto_chave,payload_sha256,tamanho_bytes,recebido_em,estado_armazenamento)
            VALUES(@entrega_id,@nome,'application/zip',@objeto_chave,@sha,@bytes,CURRENT_TIMESTAMP,'DISPONIVEL');
            """;
        Add(command, "@entrega_id", entregaId);
        Add(command, "@nome", request.NomeArquivo);
        Add(command, "@objeto_chave", request.ObjetoChave);
        Add(command, "@sha", request.BronzeSha256);
        Add(command, "@bytes", request.BronzeBytes);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertInitialLotAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid entregaId,
        Guid loteId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ingestao.lote(
                lote_id,entrega_id,lote_seq,lote_total,qtd_pessoas,qtd_registros,status,criado_em,atualizado_em)
            VALUES(@lote_id,@entrega_id,1,1,0,0,'PENDENTE',CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);
            """;
        Add(command, "@lote_id", loteId);
        Add(command, "@entrega_id", entregaId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<PostgreSqlIngestionMetadataReceipt?> LoadExistingAsync(
        DbConnection connection,
        DbTransaction transaction,
        long gestorId,
        string idempotencyKey,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT entrega_id,status,payload_sha256,bytes_recebidos,recebido_em
              FROM ingestao.entrega
             WHERE gestor_id=@gestor_id AND idempotency_key=@idempotency_key
             FOR UPDATE;
            """;
        Add(command, "@gestor_id", gestorId);
        Add(command, "@idempotency_key", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadReceipt(reader, retransmission: true) : null;
    }

    private static PostgreSqlIngestionMetadataReceipt ReadReceipt(DbDataReader reader, bool retransmission) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            ReadTimestamp(reader, 4),
            retransmission);

    private static DateTimeOffset ReadTimestamp(DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset dto => dto.ToUniversalTime(),
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => throw new InvalidCastException($"Valor temporal PostgreSQL inesperado: {value.GetType().FullName}.")
        };
    }

    private static void Validate(PostgreSqlIngestionMetadataRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.GestorCodigo)) throw new ArgumentException("GestorCodigo obrigatório.");
        if (string.IsNullOrWhiteSpace(request.CodigoSistemaOrigem)) throw new ArgumentException("CodigoSistemaOrigem obrigatório.");
        if (request.PessoaSchemaVersao <= 0) throw new ArgumentOutOfRangeException(nameof(request.PessoaSchemaVersao));
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 200) throw new ArgumentException("IdempotencyKey inválido.");
        if (request.PayloadSha256.Length != 64 || request.BronzeSha256.Length != 64) throw new ArgumentException("SHA-256 deve ter 64 caracteres hexadecimais.");
        if (request.BytesRecebidos < 0 || request.BronzeBytes < 0) throw new ArgumentOutOfRangeException(nameof(request.BytesRecebidos));
        if (request.CodigoTipo is { Length: not 4 }) throw new ArgumentException("CodigoTipo deve ter quatro caracteres.");
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private sealed record ResolvedContext(
        long GestorId,
        long SistemaOrigemId,
        long GestorPessoaVersaoId,
        long? TipoRegistroId,
        long? TipoRegistroVersaoId);
}

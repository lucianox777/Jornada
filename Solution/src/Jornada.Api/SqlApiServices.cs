using Jornada.Operational.Sql;
using System.Collections.Concurrent;
using System.Data;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Bronze.Storage;
using Jornada.Ingestion;
using Microsoft.Data.SqlClient;

namespace Jornada.Api;

internal sealed class SqlIdentityResolutionService(IOperationalSqlAdapter connections) : IIdentityResolutionService
{
    public async Task<IdentityResolutionResponse> ResolveAsync(AccessContext context, IdentityResolutionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Cpf))
        {
            return new IdentityResolutionResponse(
                ResolutionStatus.NAO_RESOLVIDO,
                null,
                ResolutionMethod.PENDENTE_PROBABILISTICO,
                "CPF_AUSENTE_CONSULTA_NAO_DISPARA_LINKAGE");
        }

        var cpf = CpfRules.NormalizeAndValidate(request.Cpf);
        if (cpf is null)
        {
            return new IdentityResolutionResponse(
                ResolutionStatus.CONFLITO,
                null,
                ResolutionMethod.CPF_DETERMINISTICO,
                "CPF_INVALIDO");
        }

        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP(1) pessoa_uuid,estado,estado_motivo,metodo_resolucao
            FROM identidade.identity_map
            WHERE tipo='CPF' AND identificador=@cpf AND vigencia_fim IS NULL
            ORDER BY vigencia_inicio DESC, identity_map_id DESC;
            """;
        command.Parameters.Add(new SqlParameter("@cpf", SqlDbType.Char, 11) { Value = cpf });
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new IdentityResolutionResponse(ResolutionStatus.NAO_RESOLVIDO, null, ResolutionMethod.CPF_DETERMINISTICO, "CPF_NAO_LOCALIZADO");

        var estado = reader.GetString(1);
        if (string.Equals(estado, "EM_CONFLITO", StringComparison.Ordinal))
            return new IdentityResolutionResponse(ResolutionStatus.CONFLITO, null, ResolutionMethod.CPF_DETERMINISTICO, CpfIdentityConsistency.IdentifierInConflictReason);

        var metodo = Enum.TryParse<ResolutionMethod>(reader.GetString(3), ignoreCase: false, out var parsed) ? parsed : ResolutionMethod.CPF_DETERMINISTICO;
        return new IdentityResolutionResponse(ResolutionStatus.RESOLVIDO, reader.GetGuid(0), metodo);
    }
}

internal sealed class IngestionConflictException(string message) : Exception(message);
internal sealed class IngestionContractException(string message) : Exception(message);

internal static class BronzeStorageHttpFailureMapper
{
    public const int RetryAfterSeconds = 60;

    public static (int StatusCode, string Code, string Message) Map(Exception exception) => exception switch
    {
        BronzeObjectIntegrityException => (StatusCodes.Status503ServiceUnavailable, "BRONZE_INTEGRIDADE_DIVERGENTE", "Armazenamento Bronze temporariamente indisponível por divergência de integridade."),
        BronzeStorageUnavailableException => (StatusCodes.Status503ServiceUnavailable, "BRONZE_STORAGE_INDISPONIVEL", "Armazenamento Bronze temporariamente indisponível."),
        _ => throw new ArgumentOutOfRangeException(nameof(exception), "Exceção não pertence ao contrato de falha da Bronze.")
    };
}

internal sealed class SqlIngestionService(IOperationalSqlAdapter connections, IBronzeObjectStore bronzeStore) : IIngestionService
{
    public async Task<IngestionReceipt> ReceivePackageAsync(
        AccessContext context,
        string idempotencyKey,
        IngestionPackageManifest manifest,
        string packageFileName,
        Stream packageStream,
        long packageBytes,
        string payloadSha256,
        CancellationToken ct)
    {
        if (idempotencyKey.Length > 200)
            throw new IngestionContractException("Idempotency-Key excede 200 caracteres.");

        if (!packageStream.CanRead || !packageStream.CanSeek)
            throw new IngestionContractException("Stream do pacote deve ser legível e seekable.");
        var sha256 = payloadSha256;
        var now = DateTimeOffset.UtcNow;

        // v3.37: o lock Shared por objeto fecha a corrida com o GC. A API o mantém desde antes do
        // PutIfAbsent até o commit da referência SQL. O GC usa o mesmo recurso em modo Exclusive.
        // LockOwner=Session exige sessão física dedicada: sem pooling e sem enlistment automático.
        await using var connection = await connections.OpenDedicatedSessionAsync(ct);
        var objectLockResource = BronzeObjectCoordination.LockResourceForSha256(sha256);
        var objectLockHeld = await AcquireSessionAppLockAsync(connection, objectLockResource, "Shared", 30_000, ct);
        if (!objectLockHeld)
            throw new BronzeStorageUnavailableException(
                "coordenação de referência",
                new TimeoutException("Não foi possível adquirir o lock compartilhado do objeto Bronze em 30 s."));

        try
        {
            // Bronze externa e imutável: objeto primeiro, linha SQL depois. Como a chave é content-addressed,
            // retry/idempotência reutilizam o mesmo objeto. Falha SQL pode deixar somente órfão seguro para GC.
            var bronzeObject = await bronzeStore.PutIfAbsentAsync(sha256, packageStream, packageBytes, ct);

            await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                var resolved = await ResolveContextAsync(connection, tx, context, manifest, ct);

                await using (var existing = connection.CreateCommand())
                {
                    existing.Transaction = tx;
                    existing.CommandText = """
                        SELECT TOP(1) entrega_id,payload_sha256,bytes_recebidos,status,recebido_em
                        FROM ingestao.entrega WITH (UPDLOCK,HOLDLOCK)
                        WHERE gestor_id=@gestor_id AND idempotency_key=@idempotency_key;
                        """;
                    existing.Parameters.AddWithValue("@gestor_id", resolved.GestorId);
                    existing.Parameters.Add(new SqlParameter("@idempotency_key", SqlDbType.NVarChar, 200) { Value = idempotencyKey });
                    await using var reader = await existing.ExecuteReaderAsync(ct);
                    if (await reader.ReadAsync(ct))
                    {
                        var existingId = reader.GetGuid(0);
                        var existingHash = reader.GetString(1);
                        var existingBytes = reader.GetInt64(2);
                        var existingStatus = reader.GetString(3);
                        var existingReceived = reader.GetDateTimeOffset(4);
                        await reader.DisposeAsync();

                        if (!string.Equals(existingHash, sha256, StringComparison.Ordinal) || existingBytes != packageBytes)
                            throw new IngestionConflictException("Idempotency-Key já utilizado para conteúdo diferente.");

                        await tx.CommitAsync(ct);
                        return new IngestionReceipt(existingId, existingStatus, existingHash, existingBytes, existingReceived);
                    }
                }

                var entregaId = Guid.NewGuid();
                var loteId = Guid.NewGuid();

                await using (var insertEntrega = connection.CreateCommand())
                {
                    insertEntrega.Transaction = tx;
                    insertEntrega.CommandText = """
                        INSERT ingestao.entrega(
                            entrega_id,gestor_id,sistema_origem_id,gestor_pessoa_versao_id,natureza,
                            tipo_registro_id,tipo_registro_versao_id,idempotency_key,payload_sha256,
                            bytes_recebidos,status,data_referencia,recebido_em,ultima_atualizacao)
                        VALUES(
                            @entrega_id,@gestor_id,@sistema_origem_id,@gestor_pessoa_versao_id,@natureza,
                            @tipo_registro_id,@tipo_registro_versao_id,@idempotency_key,@sha,
                            @bytes,'RECEBIDA',@data_referencia,@agora,@agora);
                        """;
                    insertEntrega.Parameters.AddWithValue("@entrega_id", entregaId);
                    insertEntrega.Parameters.AddWithValue("@gestor_id", resolved.GestorId);
                    insertEntrega.Parameters.AddWithValue("@sistema_origem_id", resolved.SistemaOrigemId);
                    insertEntrega.Parameters.AddWithValue("@gestor_pessoa_versao_id", resolved.GestorPessoaVersaoId);
                    insertEntrega.Parameters.Add(new SqlParameter("@natureza", SqlDbType.NVarChar, 30) { Value = (object?)manifest.Natureza?.ToString() ?? DBNull.Value });
                    insertEntrega.Parameters.Add(new SqlParameter("@tipo_registro_id", SqlDbType.BigInt) { Value = (object?)resolved.TipoRegistroId ?? DBNull.Value });
                    insertEntrega.Parameters.Add(new SqlParameter("@tipo_registro_versao_id", SqlDbType.BigInt) { Value = (object?)resolved.TipoRegistroVersaoId ?? DBNull.Value });
                    insertEntrega.Parameters.Add(new SqlParameter("@idempotency_key", SqlDbType.NVarChar, 200) { Value = idempotencyKey });
                    insertEntrega.Parameters.Add(new SqlParameter("@sha", SqlDbType.Char, 64) { Value = sha256 });
                    insertEntrega.Parameters.AddWithValue("@bytes", packageBytes);
                    insertEntrega.Parameters.AddWithValue("@data_referencia", manifest.DataReferencia);
                    insertEntrega.Parameters.AddWithValue("@agora", now);
                    await insertEntrega.ExecuteNonQueryAsync(ct);
                }

                await using (var insertBronze = connection.CreateCommand())
                {
                    insertBronze.Transaction = tx;
                    insertBronze.CommandText = """
                        INSERT bronze.entrega_arquivo(
                            entrega_id,nome_arquivo,content_type,objeto_chave,payload_sha256,tamanho_bytes,recebido_em)
                        VALUES(@entrega_id,@nome,'application/zip',@objeto_chave,@sha,@bytes,@agora);
                        """;
                    insertBronze.Parameters.AddWithValue("@entrega_id", entregaId);
                    insertBronze.Parameters.Add(new SqlParameter("@nome", SqlDbType.NVarChar, 260) { Value = packageFileName });
                    insertBronze.Parameters.Add(new SqlParameter("@objeto_chave", SqlDbType.NVarChar, 1024) { Value = bronzeObject.ObjectKey });
                    insertBronze.Parameters.Add(new SqlParameter("@sha", SqlDbType.Char, 64) { Value = bronzeObject.Sha256 });
                    insertBronze.Parameters.AddWithValue("@bytes", bronzeObject.Length);
                    insertBronze.Parameters.AddWithValue("@agora", now);
                    await insertBronze.ExecuteNonQueryAsync(ct);
                }

                await using (var insertLote = connection.CreateCommand())
                {
                    insertLote.Transaction = tx;
                    insertLote.CommandText = """
                        INSERT ingestao.lote(lote_id,entrega_id,lote_seq,lote_total,qtd_pessoas,qtd_registros,status,criado_em,atualizado_em)
                        VALUES(@lote_id,@entrega_id,1,1,0,0,'PENDENTE',@agora,@agora);
                        """;
                    insertLote.Parameters.AddWithValue("@lote_id", loteId);
                    insertLote.Parameters.AddWithValue("@entrega_id", entregaId);
                    insertLote.Parameters.AddWithValue("@agora", now);
                    await insertLote.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
                return new IngestionReceipt(entregaId, "RECEBIDA", sha256, packageBytes, now);
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        finally
        {
            if (objectLockHeld)
                await ReleaseSessionAppLockAsync(connection, objectLockResource);
        }
    }

    private static async Task<bool> AcquireSessionAppLockAsync(
        SqlConnection connection, string resource, string mode, int timeoutMilliseconds, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @rc int;
            EXEC @rc = sys.sp_getapplock
                 @Resource=@resource,
                 @LockMode=@mode,
                 @LockOwner='Session',
                 @LockTimeout=@timeout;
            SELECT @rc;
            """;
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource });
        command.Parameters.Add(new SqlParameter("@mode", SqlDbType.NVarChar, 32) { Value = mode });
        command.Parameters.AddWithValue("@timeout", timeoutMilliseconds);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        return result >= 0;
    }

    private static async Task ReleaseSessionAppLockAsync(SqlConnection connection, string resource)
    {
        if (connection.State != ConnectionState.Open) return;
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "EXEC sys.sp_releaseapplock @Resource=@resource, @LockOwner='Session';";
            command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource });
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch
        {
            // Se a liberação explícita falhar, a sessão não pode voltar ao pool ainda segurando o app lock.
            SqlConnection.ClearPool(connection);
            try { connection.Close(); } catch { }
        }
    }

    public async Task<IngestionStatusResponse?> GetStatusAsync(AccessContext context, Guid entregaId, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.entrega_id,e.status,e.recebido_em,e.ultima_atualizacao,
                   (SELECT TOP(1) l.erro_codigo FROM ingestao.lote l
                    WHERE l.entrega_id=e.entrega_id AND l.erro_codigo IS NOT NULL
                    ORDER BY l.atualizado_em DESC,l.lote_seq DESC) erro
            FROM ingestao.entrega e
            JOIN ref.gestor g ON g.gestor_id=e.gestor_id
            WHERE e.entrega_id=@entrega_id AND g.codigo=@gestor;
            """;
        command.Parameters.AddWithValue("@entrega_id", entregaId);
        command.Parameters.Add(new SqlParameter("@gestor", SqlDbType.NVarChar, 30) { Value = context.GestorCodigo });
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new IngestionStatusResponse(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetDateTimeOffset(2),
            reader.GetDateTimeOffset(3),
            reader.NullableString(4));
    }

    private static async Task<ResolvedIngestionContext> ResolveContextAsync(
        SqlConnection connection,
        SqlTransaction tx,
        AccessContext context,
        IngestionPackageManifest manifest,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT g.gestor_id,gpv.gestor_pessoa_versao_id
            FROM ref.gestor g
            JOIN ref.gestor_pessoa_versao gpv ON gpv.gestor_id=g.gestor_id AND gpv.versao=@pessoa_versao
            WHERE g.codigo=@gestor AND g.ativo=1 AND gpv.status IN('ATIVA','ENCERRADA');
            """;
        command.Parameters.Add(new SqlParameter("@gestor", SqlDbType.NVarChar, 30) { Value = context.GestorCodigo });
        command.Parameters.AddWithValue("@pessoa_versao", manifest.PessoaSchemaVersao);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new IngestionContractException("Contrato cadastral do Gestor/versão não encontrado.");
        var gestorId = reader.GetInt64(0);
        var gestorPessoaVersaoId = reader.GetInt64(1);
        await reader.DisposeAsync();

        long sistemaOrigemId;
        await using (var sourceCommand = connection.CreateCommand())
        {
            sourceCommand.Transaction = tx;
            sourceCommand.CommandText = """
                SELECT sistema_origem_id
                FROM ref.sistema_origem WITH (UPDLOCK,HOLDLOCK)
                WHERE gestor_id=@gestor_id AND codigo=@codigo AND ativo=1;
                """;
            sourceCommand.Parameters.AddWithValue("@gestor_id", gestorId);
            sourceCommand.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 80) { Value = manifest.CodigoSistemaOrigem });
            var sourceValue = await sourceCommand.ExecuteScalarAsync(ct);
            if (sourceValue is null || sourceValue is DBNull)
                throw new IngestionContractException("codigoSistemaOrigem não está cadastrado/ativo para o Gestor autenticado.");
            sistemaOrigemId = Convert.ToInt64(sourceValue, System.Globalization.CultureInfo.InvariantCulture);
        }

        if (manifest.CodigoTipo is null)
            return new ResolvedIngestionContext(gestorId, sistemaOrigemId, gestorPessoaVersaoId, null, null);

        await using var typeCommand = connection.CreateCommand();
        typeCommand.Transaction = tx;
        typeCommand.CommandText = """
            SELECT tr.tipo_registro_id,trv.tipo_registro_versao_id
            FROM ref.tipo_registro tr
            JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_id=tr.tipo_registro_id AND trv.versao=@versao
            WHERE tr.gestor_id=@gestor_id AND tr.codigo=@codigo AND tr.natureza=@natureza
              AND tr.ativo=1 AND trv.status IN('ATIVA','ENCERRADA');
            """;
        typeCommand.Parameters.AddWithValue("@gestor_id", gestorId);
        typeCommand.Parameters.Add(new SqlParameter("@codigo", SqlDbType.Char, 4) { Value = manifest.CodigoTipo! });
        typeCommand.Parameters.Add(new SqlParameter("@natureza", SqlDbType.NVarChar, 30) { Value = manifest.Natureza!.Value.ToString() });
        typeCommand.Parameters.AddWithValue("@versao", manifest.TipoVersao!.Value);
        await using var typeReader = await typeCommand.ExecuteReaderAsync(ct);
        if (!await typeReader.ReadAsync(ct))
            throw new IngestionContractException("Tipo/versão não pertence ao Gestor ou não está disponível.");
        return new ResolvedIngestionContext(gestorId, sistemaOrigemId, gestorPessoaVersaoId, typeReader.GetInt64(0), typeReader.GetInt64(1));
    }

    private sealed record ResolvedIngestionContext(long GestorId, long SistemaOrigemId, long GestorPessoaVersaoId, long? TipoRegistroId, long? TipoRegistroVersaoId);
}

internal sealed class SqlPersonProjectionService(
    IOperationalSqlAdapter connections,
    IContractResolver contracts) : IPersonProjectionService
{
    public async Task<PersonProjectionResponse?> GetAsync(AccessContext context, Guid pessoaUuid, CancellationToken ct)
    {
        var results = await GetManyAsync(context, [pessoaUuid], ct);
        return results.Count == 0 ? null : results[0];
    }

    public async Task<IReadOnlyList<PersonProjectionResponse>> GetManyAsync(
        AccessContext context,
        IReadOnlyList<Guid> pessoaUuids,
        CancellationToken ct)
    {
        if (pessoaUuids.Count == 0) return Array.Empty<PersonProjectionResponse>();
        var schemaPath = await contracts.ResolvePersonSchemaAsync(context, ct);
        var schema = PersonSchemaProjection.GetCached(schemaPath);
        var requestedIdsJson = JsonSerializer.Serialize(pessoaUuids);

        await using var connection = await connections.OpenAsync(ct);
        var canonicalByRequested = new Dictionary<Guid, Guid>();
        await using (var canonical = connection.CreateCommand())
        {
            canonical.CommandText = """
                SELECT j.pessoa_uuid,identidade.fn_pessoa_uuid_canonico(j.pessoa_uuid)
                FROM OPENJSON(@ids) WITH (pessoa_uuid UNIQUEIDENTIFIER '$') j;
                """;
            canonical.Parameters.Add(new SqlParameter("@ids", SqlDbType.NVarChar, -1) { Value = requestedIdsJson });
            await using var reader = await canonical.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                if (!reader.IsDBNull(1)) canonicalByRequested[reader.GetGuid(0)] = reader.GetGuid(1);
        }
        if (canonicalByRequested.Count == 0) return Array.Empty<PersonProjectionResponse>();
        var canonicalIds = canonicalByRequested.Values.Distinct().ToArray();
        var idsJson = JsonSerializer.Serialize(canonicalIds);
        var rows = new Dictionary<Guid, PersonProjectionRow>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                DECLARE @gestor_id BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo=@gestor);
                SELECT gp.pessoa_uuid,gp.cpf,gp.status_cpf,gp.nome_completo,gp.data_nascimento,gp.nome_mae,
                       gp.fontes_distintas,gp.estado_concordancia,gp.atualizado_em,
                       src.codigo_pessoa_origem,src.cpf_ausente_motivo
                FROM OPENJSON(@ids) WITH (pessoa_uuid UNIQUEIDENTIFIER '$') j
                JOIN serving.v_pessoa gp ON gp.pessoa_uuid=j.pessoa_uuid
                OUTER APPLY(
                    SELECT TOP(1) po.codigo_pessoa_origem,po.cpf_ausente_motivo
                    FROM silver.pessoa_observacao po
                    JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
                    WHERE vc.pessoa_uuid=gp.pessoa_uuid AND po.gestor_id=@gestor_id
                    ORDER BY po.source_as_of DESC,po.pessoa_observacao_id DESC
                ) src;
                """;
            command.Parameters.Add(new SqlParameter("@gestor", SqlDbType.NVarChar, 30) { Value = context.GestorCodigo });
            command.Parameters.Add(new SqlParameter("@ids", SqlDbType.NVarChar, -1) { Value = idsJson });
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var uuid = reader.GetGuid(0);
                rows[uuid] = new PersonProjectionRow(
                    uuid,
                    reader.NullableString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    DateOnly.FromDateTime(reader.GetDateTime(4)),
                    reader.GetString(5),
                    reader.GetInt32(6),
                    reader.GetString(7),
                    reader.GetDateTimeOffset(8),
                    reader.NullableString(9),
                    reader.NullableString(10),
                    [],
                    []);
            }
        }

        if (rows.Count == 0) return Array.Empty<PersonProjectionResponse>();

        await using (var attrs = connection.CreateCommand())
        {
            attrs.CommandText = """
                SELECT a.pessoa_uuid,a.atributo_codigo,a.valor,a.source_record_id,a.evidencia_tipo,
                       a.referencia_evidencia,a.verificado_em
                FROM OPENJSON(@ids) WITH (pessoa_uuid UNIQUEIDENTIFIER '$') j
                JOIN gold.v_pessoa_atributo_corrente a ON a.pessoa_uuid=j.pessoa_uuid
                WHERE controle.fn_projecao_jornada_permitida(a.fonte_gestor_id,@gestor_id,'ATRIBUTO_PESSOA',a.atributo_codigo)=1
                ORDER BY a.pessoa_uuid,a.atributo_codigo;
                """;
            attrs.Parameters.Add(new SqlParameter("@ids", SqlDbType.NVarChar, -1) { Value = idsJson });
            attrs.Parameters.Add(new SqlParameter("@gestor_id", SqlDbType.BigInt) { Value = await ResolveGestorIdAsync(connection, context.GestorCodigo, ct) });
            await using var reader = await attrs.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var uuid = reader.GetGuid(0);
                if (!rows.TryGetValue(uuid, out var row)) continue;
                row.Attributes.Add(new PersonAttributeProjection(
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.NullableString(3),
                    reader.NullableString(4),
                    reader.NullableDateTimeOffset(5),
                    reader.GetDateTimeOffset(6)));
            }
        }

        await using (var verifications = connection.CreateCommand())
        {
            verifications.CommandText = """
                DECLARE @gestor_id BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo=@gestor);
                ;WITH ranked AS(
                    SELECT vc.pessoa_uuid,v.campo_codigo,v.evidencia_tipo,v.referencia_evidencia,v.verificado_em,
                           ROW_NUMBER() OVER(PARTITION BY vc.pessoa_uuid,v.campo_codigo ORDER BY v.verificado_em DESC,v.pessoa_campo_verificacao_id DESC) rn
                    FROM OPENJSON(@ids) WITH (pessoa_uuid UNIQUEIDENTIFIER '$') j
                    JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_uuid=j.pessoa_uuid AND vc.status='RESOLVIDO'
                    JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=vc.pessoa_observacao_id AND po.gestor_id=@gestor_id
                    JOIN silver.pessoa_campo_verificacao_observacao v ON v.pessoa_observacao_id=po.pessoa_observacao_id
                )
                SELECT pessoa_uuid,campo_codigo,evidencia_tipo,referencia_evidencia,verificado_em
                FROM ranked WHERE rn=1 ORDER BY pessoa_uuid,campo_codigo;
                """;
            verifications.Parameters.Add(new SqlParameter("@gestor", SqlDbType.NVarChar, 30) { Value = context.GestorCodigo });
            verifications.Parameters.Add(new SqlParameter("@ids", SqlDbType.NVarChar, -1) { Value = idsJson });
            await using var reader = await verifications.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var uuid = reader.GetGuid(0);
                if (!rows.TryGetValue(uuid, out var row)) continue;
                row.DocumentVerifications.Add(new DocumentVerificationProjection(
                    reader.GetString(1), reader.GetString(2), reader.NullableString(3), reader.GetDateTimeOffset(4)));
            }
        }

        var ordered = new List<PersonProjectionResponse>(rows.Count);
        foreach (var requestedUuid in pessoaUuids)
        {
            if (!canonicalByRequested.TryGetValue(requestedUuid, out var canonicalUuid)) continue;
            if (!rows.TryGetValue(canonicalUuid, out var row)) continue;
            ordered.Add(new PersonProjectionResponse(
                canonicalUuid,
                schema.Project(row),
                schema.SchemaRef,
                new PersonProjectionMetadata(row.FontesDistintas, row.EstadoConcordancia, row.AtualizadoEm,
                    requestedUuid, requestedUuid != canonicalUuid)));
        }
        return ordered;
    }

    private static async Task<long> ResolveGestorIdAsync(SqlConnection connection, string gestorCodigo, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT gestor_id FROM ref.gestor WHERE codigo=@gestor;";
        command.Parameters.Add(new SqlParameter("@gestor", SqlDbType.NVarChar, 30) { Value = gestorCodigo });
        var value = await command.ExecuteScalarAsync(ct);
        return value is long id ? id : throw new InvalidOperationException($"Gestor autenticado não localizado: {gestorCodigo}.");
    }

    private sealed record PersonProjectionRow(
        Guid PessoaUuid,
        string? Cpf,
        string StatusCpf,
        string NomeCompleto,
        DateOnly DataNascimento,
        string NomeMae,
        int FontesDistintas,
        string EstadoConcordancia,
        DateTimeOffset AtualizadoEm,
        string? CodigoPessoaOrigem,
        string? CpfAusenteMotivo,
        List<PersonAttributeProjection> Attributes,
        List<DocumentVerificationProjection> DocumentVerifications);

    private sealed record PersonAttributeProjection(
        string Codigo,
        string Valor,
        string? SourceRecordId,
        string? EvidenciaTipo,
        DateTimeOffset? ReferenciaEvidencia,
        DateTimeOffset VerificadoEm);

    private sealed record DocumentVerificationProjection(
        string CampoCodigo,
        string EvidenciaTipo,
        string? ReferenciaEvidencia,
        DateTimeOffset VerificadoEm);

    private sealed class PersonSchemaProjection
    {
        private sealed record CacheEntry(PersonSchemaProjection Projection, long Length, DateTime LastWriteUtc);
        private static readonly ConcurrentDictionary<string, Lazy<CacheEntry>> Cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _properties;
        public string SchemaRef { get; }

        private PersonSchemaProjection(HashSet<string> properties, string schemaRef)
        {
            _properties = properties;
            SchemaRef = schemaRef;
        }

        public static PersonSchemaProjection GetCached(string path)
        {
            var full = Path.GetFullPath(path);
            var entry = Cache.GetOrAdd(full, static p => new Lazy<CacheEntry>(() =>
            {
                var info = new FileInfo(p);
                return new CacheEntry(Load(p), info.Length, info.LastWriteTimeUtc);
            }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
            var current = new FileInfo(full);
            if (current.Length != entry.Length || current.LastWriteTimeUtc != entry.LastWriteUtc)
                throw new InvalidOperationException(
                    $"Schema de projeção versionado foi alterado in-place após entrar no cache: {full}. Publique uma nova versão vN.");
            return entry.Projection;
        }

        private static PersonSchemaProjection Load(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var properties = root.GetProperty("properties")
                .EnumerateObject()
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal);
            var schemaRef = root.TryGetProperty("$id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()!
                : Path.GetFileName(path);
            return new PersonSchemaProjection(properties, schemaRef);
        }

        public JsonElement Project(PersonProjectionRow row)
        {
            var output = new Dictionary<string, object?>(StringComparer.Ordinal);
            Add("codigoPessoaOrigem", row.CodigoPessoaOrigem);
            Add("cpf", row.Cpf);
            Add("cpfAusenteMotivo", row.Cpf is null ? row.CpfAusenteMotivo ?? row.StatusCpf : null);
            Add("nomeCompleto", row.NomeCompleto);
            Add("dataNascimento", row.DataNascimento.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            Add("nomeMae", row.NomeMae);
            if (_properties.Contains("atributosTransversais"))
            {
                output["atributosTransversais"] = row.Attributes.Select(a => new Dictionary<string, object?>
                {
                    ["sourceRecordId"] = a.SourceRecordId,
                    ["atributoCodigo"] = a.Codigo,
                    ["valor"] = a.Valor,
                    ["statusEvidencia"] = "COMPROVADO",
                    ["evidenciaTipo"] = a.EvidenciaTipo,
                    ["referenciaEvidencia"] = a.ReferenciaEvidencia,
                    ["verificadoEm"] = a.VerificadoEm,
                    ["atualizadoEmOrigem"] = null
                }).ToArray();
            }
            if (_properties.Contains("conferenciasDocumentais"))
            {
                output["conferenciasDocumentais"] = row.DocumentVerifications.Select(v => new Dictionary<string, object?>
                {
                    ["campoCodigo"] = v.CampoCodigo,
                    ["evidenciaTipo"] = v.EvidenciaTipo,
                    ["referenciaEvidencia"] = v.ReferenciaEvidencia,
                    ["verificadoEm"] = v.VerificadoEm
                }).ToArray();
            }
            return JsonSerializer.SerializeToElement(output);

            void Add(string name, object? value)
            {
                if (_properties.Contains(name)) output[name] = value;
            }
        }
    }
}

internal sealed class SqlRegistrosQueryService(IOperationalSqlAdapter connections) : IRegistrosQueryService
{
    public async Task<IReadOnlyList<RegistroJornadaDto>> GetRegistrosAsync(
        AccessContext context, Guid pessoaUuid, string? natureza, string? codigo, DateOnly? desde, DateOnly? ate, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.registro_id,r.pessoa_uuid,r.natureza,r.codigo,r.nome,r.gestor,r.data_referencia,r.ocorrido_em,r.situacao
            FROM serving.v_registros_pessoa r
            JOIN ref.gestor gr ON gr.codigo=r.gestor
            WHERE r.pessoa_uuid=@uuid
              AND controle.fn_projecao_jornada_permitida(gr.gestor_id,@gestor_consumidor_id,'REGISTRO',r.codigo)=1
              AND (@natureza IS NULL OR r.natureza=@natureza)
              AND (@codigo IS NULL OR r.codigo=@codigo)
              AND (@desde IS NULL OR CAST(r.ocorrido_em AS date)>=@desde)
              AND (@ate IS NULL OR CAST(r.ocorrido_em AS date)<=@ate)
            ORDER BY r.ocorrido_em DESC,r.registro_id DESC;
            """;
        command.Parameters.AddWithValue("@uuid", pessoaUuid);
        command.Parameters.Add(new SqlParameter("@gestor_consumidor_id", SqlDbType.BigInt) { Value = await ResolveGestorIdAsync(connection, context.GestorCodigo, ct) });
        AddNullable(command, "@natureza", SqlDbType.NVarChar, 30, natureza?.ToUpperInvariant());
        AddNullable(command, "@codigo", SqlDbType.Char, 4, codigo?.ToUpperInvariant());
        AddNullableDate(command, "@desde", desde);
        AddNullableDate(command, "@ate", ate);
        var result = new List<RegistroJornadaDto>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new RegistroJornadaDto(
                reader.GetString(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.GetDateTimeOffset(6), reader.GetDateTimeOffset(7), reader.NullableString(8)));
        }
        return result;
    }

    public async Task<IReadOnlyList<BeneficioConcedidoPessoaDto>> GetBeneficiosConcedidosAsync(
        AccessContext context, Guid pessoaUuid, string? codigo, DateOnly? desde, DateOnly? ate, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.beneficio_concedido_id,b.pessoa_uuid,b.gestor,b.beneficio,b.nome_beneficio,b.versao_tipo,
                   b.versao_interna,b.operacao,b.status_analitico,b.codigo_registro_origem,b.tipo_medida,
                   b.data_inicio_concessao,b.data_fim_concessao,b.data_evento_concessao,b.situacao_vigencia,b.situacao_vigencia_desde,b.motivo_encerramento,b.valor_concedido,b.quantidade,b.unidade_medida,
                   b.data_referencia,b.qc_resultado,b.qc_especifico_implementado
            FROM serving.v_beneficios_concedidos_pessoa b
            JOIN ref.gestor gr ON gr.codigo=b.gestor
            WHERE b.pessoa_uuid=@uuid
              AND controle.fn_projecao_jornada_permitida(gr.gestor_id,@gestor_consumidor_id,'REGISTRO',b.beneficio)=1
              AND (@codigo IS NULL OR b.beneficio=@codigo)
              AND (@desde IS NULL OR COALESCE(b.data_evento_concessao,b.data_inicio_concessao,CAST(b.data_referencia AS date))>=@desde)
              AND (@ate IS NULL OR COALESCE(b.data_evento_concessao,b.data_inicio_concessao,CAST(b.data_referencia AS date))<=@ate)
            ORDER BY COALESCE(b.data_evento_concessao,b.data_inicio_concessao,CAST(b.data_referencia AS date)) DESC,b.beneficio_concedido_id DESC;
            """;
        command.Parameters.AddWithValue("@uuid", pessoaUuid);
        command.Parameters.Add(new SqlParameter("@gestor_consumidor_id", SqlDbType.BigInt) { Value = await ResolveGestorIdAsync(connection, context.GestorCodigo, ct) });
        AddNullable(command, "@codigo", SqlDbType.Char, 4, codigo?.ToUpperInvariant());
        AddNullableDate(command, "@desde", desde);
        AddNullableDate(command, "@ate", ate);
        var result = new List<BeneficioConcedidoPessoaDto>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new BeneficioConcedidoPessoaDto(
                reader.GetInt64(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5),
                reader.GetInt32(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10),
                reader.IsDBNull(11) ? null : DateOnly.FromDateTime(reader.GetDateTime(11)),
                reader.IsDBNull(12) ? null : DateOnly.FromDateTime(reader.GetDateTime(12)),
                reader.IsDBNull(13) ? null : DateOnly.FromDateTime(reader.GetDateTime(13)),
                reader.GetString(14),
                reader.IsDBNull(15) ? null : DateOnly.FromDateTime(reader.GetDateTime(15)),
                reader.NullableString(16), reader.NullableDecimal(17), reader.NullableDecimal(18), reader.NullableString(19),
                reader.GetDateTimeOffset(20), reader.NullableString(21), Convert.ToBoolean(reader.GetValue(22), System.Globalization.CultureInfo.InvariantCulture)));
        }
        return result;
    }

    public async Task<IReadOnlyList<ServicoPrestadoPessoaDto>> GetServicosPrestadosAsync(
        AccessContext context, Guid pessoaUuid, string? codigo, DateOnly? desde, DateOnly? ate, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.servico_prestado_id,s.pessoa_uuid,s.gestor,s.servico,s.nome_servico,s.versao_tipo,
                   s.versao_interna,s.operacao,s.status_analitico,s.codigo_registro_origem,
                   s.data_hora_servico,s.unidade_servico,s.situacao,s.data_referencia
            FROM serving.v_servicos_prestados_pessoa s
            JOIN ref.gestor gr ON gr.codigo=s.gestor
            WHERE s.pessoa_uuid=@uuid
              AND controle.fn_projecao_jornada_permitida(gr.gestor_id,@gestor_consumidor_id,'REGISTRO',s.servico)=1
              AND (@codigo IS NULL OR s.servico=@codigo)
              AND (@desde IS NULL OR CAST(s.data_hora_servico AS date)>=@desde)
              AND (@ate IS NULL OR CAST(s.data_hora_servico AS date)<=@ate)
            ORDER BY s.data_hora_servico DESC,s.servico_prestado_id DESC;
            """;
        command.Parameters.AddWithValue("@uuid", pessoaUuid);
        command.Parameters.Add(new SqlParameter("@gestor_consumidor_id", SqlDbType.BigInt) { Value = await ResolveGestorIdAsync(connection, context.GestorCodigo, ct) });
        AddNullable(command, "@codigo", SqlDbType.Char, 4, codigo?.ToUpperInvariant());
        AddNullableDate(command, "@desde", desde);
        AddNullableDate(command, "@ate", ate);
        var result = new List<ServicoPrestadoPessoaDto>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new ServicoPrestadoPessoaDto(
                reader.GetInt64(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5),
                reader.GetInt32(6), reader.GetString(7), reader.GetString(8), reader.GetString(9),
                reader.GetDateTimeOffset(10), reader.NullableString(11), reader.NullableString(12), reader.GetDateTimeOffset(13)));
        }
        return result;
    }

    private static async Task<long> ResolveGestorIdAsync(SqlConnection connection, string gestorCodigo, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT gestor_id FROM ref.gestor WHERE codigo=@gestor;";
        command.Parameters.Add(new SqlParameter("@gestor", SqlDbType.NVarChar, 30) { Value = gestorCodigo });
        var value = await command.ExecuteScalarAsync(ct);
        return value is long id ? id : throw new InvalidOperationException($"Gestor autenticado não localizado: {gestorCodigo}.");
    }

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, int size, string? value) =>
        command.Parameters.Add(new SqlParameter(name, type, size) { Value = (object?)value ?? DBNull.Value });

    private static void AddNullableDate(SqlCommand command, string name, DateOnly? value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Date) { Value = value.HasValue ? value.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value });
}

internal sealed class SqlPossibilidadesQueryService(IOperationalSqlAdapter connections) : IPossibilidadesQueryService
{
    public async Task<IReadOnlyList<PossibilidadeCompativelDto>> GetCompativeisAsync(
        AccessContext context, Guid pessoaUuid, string? natureza, string? codigo, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.pessoa_uuid,p.natureza,p.codigo,p.nome,p.gestor,p.regra_versao,p.avaliado_em,p.validade_ate,p.motivo
            FROM serving.v_possibilidades_compativeis p
            JOIN ref.gestor gr ON gr.codigo=p.gestor
            WHERE p.pessoa_uuid=@uuid
              AND controle.fn_projecao_jornada_permitida(gr.gestor_id,@gestor_consumidor_id,'POSSIBILIDADE',p.codigo)=1
              AND (@natureza IS NULL OR p.natureza=@natureza)
              AND (@codigo IS NULL OR p.codigo=@codigo)
              AND (p.validade_ate IS NULL OR p.validade_ate>=SYSDATETIMEOFFSET())
            ORDER BY p.avaliado_em DESC,p.codigo;
            """;
        command.Parameters.AddWithValue("@uuid", pessoaUuid);
        command.Parameters.Add(new SqlParameter("@gestor_consumidor_id", SqlDbType.BigInt) { Value = await ResolveGestorIdAsync(connection, context.GestorCodigo, ct) });
        command.Parameters.Add(new SqlParameter("@natureza", SqlDbType.NVarChar, 30) { Value = (object?)natureza?.ToUpperInvariant() ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@codigo", SqlDbType.Char, 4) { Value = (object?)codigo?.ToUpperInvariant() ?? DBNull.Value });
        var result = new List<PossibilidadeCompativelDto>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new PossibilidadeCompativelDto(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.GetDateTimeOffset(6), reader.NullableDateTimeOffset(7), reader.GetString(8)));
        }
        return result;
    }
    private static async Task<long> ResolveGestorIdAsync(SqlConnection connection, string gestorCodigo, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT gestor_id FROM ref.gestor WHERE codigo=@gestor;";
        command.Parameters.Add(new SqlParameter("@gestor", SqlDbType.NVarChar, 30) { Value = gestorCodigo });
        var value = await command.ExecuteScalarAsync(ct);
        return value is long id ? id : throw new InvalidOperationException($"Gestor autenticado não localizado: {gestorCodigo}.");
    }

}

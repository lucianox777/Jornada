using System.Data;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Processor.Worker;

internal sealed partial class SqlProcessorRepository
{
    /// <summary>
    /// Cutover exclusivo da Pessoa v4. As versões v1-v3 continuam usando PersistValidatedAsync
    /// sem mudança de comportamento. Na v4, ausência de codigoPessoaOrigem não cria pessoa_origem
    /// artificial e não estabelece continuidade de fonte entre transmissões.
    /// </summary>
    public async Task PersistValidatedV4Async(ReservedBatch batch, ParsedPackage package, CancellationToken ct)
    {
        if (batch.PessoaSchemaVersao < 4)
            throw new InvalidOperationException("PersistValidatedV4Async só pode processar Pessoa schema v4 ou superior.");

        await using var connection = await connections.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await SetProcessingAsync(connection, tx, batch, ct);
            var peopleBySource = new Dictionary<string, ProcessedPerson>(StringComparer.Ordinal);

            foreach (var person in package.Pessoas)
            {
                var processed = await PersistPersonV4Async(
                    connection,
                    tx,
                    batch,
                    package.Manifest.CodigoBasePessoaOrigem,
                    person,
                    ct);

                if (!string.IsNullOrWhiteSpace(person.CodigoPessoaOrigem))
                {
                    if (processed is null)
                        throw new InvalidOperationException("Pessoa v4 com código de origem não retornou referência processada.");
                    peopleBySource[person.CodigoPessoaOrigem] = processed;
                }
            }

            foreach (var fact in package.Registros)
            {
                if (!peopleBySource.TryGetValue(fact.CodigoPessoaOrigem, out var person))
                    throw new InvalidDataException($"Pessoa de origem não encontrada para registro: {fact.CodigoPessoaOrigem}.");
                await PersistFactAsync(connection, tx, batch, person, fact, ct);
            }

            await using (var finish = connection.CreateCommand())
            {
                finish.Transaction = tx;
                finish.CommandText = """
                    UPDATE ingestao.lote
                       SET qtd_pessoas=@qtd_pessoas,qtd_registros=@qtd_registros,status='PROCESSADO',erro_codigo=NULL,
                           lease_id=NULL,lease_owner=NULL,lease_adquirido_em=NULL,lease_expira_em=NULL,heartbeat_em=NULL,
                           proxima_tentativa_em=NULL,atualizado_em=SYSUTCDATETIME()
                     WHERE lote_id=@lote_id AND lease_id=@lease_id AND lease_owner=@lease_owner;
                    IF @@ROWCOUNT<>1 THROW 51022,'Lease perdido antes da publicação final do lote.',1;
                    EXEC ingestao.sp_recalcular_entrega @entrega_id=@entrega_id;
                    """;
                finish.Parameters.AddWithValue("@qtd_pessoas", package.Pessoas.Count);
                finish.Parameters.AddWithValue("@qtd_registros", package.Registros.Count);
                finish.Parameters.AddWithValue("@lote_id", batch.LoteId);
                finish.Parameters.AddWithValue("@lease_id", batch.LeaseId);
                finish.Parameters.Add(new SqlParameter("@lease_owner", SqlDbType.NVarChar, 200) { Value = batch.LeaseOwner });
                finish.Parameters.AddWithValue("@entrega_id", batch.EntregaId);
                await finish.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            if (tx.Connection is not null)
                await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<ProcessedPerson?> PersistPersonV4Async(
        SqlConnection connection,
        SqlTransaction tx,
        ReservedBatch batch,
        string? manifestBasePessoaOrigem,
        ParsedPerson person,
        CancellationToken ct)
    {
        var origin = await PersonOriginSqlPersistence.ResolveOrCreateAsync(
            connection,
            tx,
            batch.SistemaOrigemId,
            person.CodigoPessoaOrigem,
            manifestBasePessoaOrigem,
            ct);

        PersonVersionState? latest = null;
        if (origin is not null)
        {
            latest = await GetLatestPersonVersionAsync(connection, tx, origin.PessoaOrigemId, ct);
            if (latest is not null && string.Equals(latest.ConteudoHash, person.ConteudoHash, StringComparison.Ordinal))
            {
                await TouchPersonOriginAsync(connection, tx, origin.PessoaOrigemId, batch.DataReferencia, ct);
                await RecordProcessedItemAsync(connection, tx, batch, "PESSOA", origin.PessoaOrigemId, null,
                    origin.CodigoPessoaOrigem, "RETRANSMITIDO", latest.VersaoInterna, person.ConteudoHash, ct);
                var retransmitted = await LoadProcessedPersonAsync(connection, tx, latest.ObservationId, ct);
                if (retransmitted.PessoaUuid is Guid retransmittedUuid)
                    await RefreshGoldPersonAsync(connection, tx, retransmittedUuid, ct);
                return retransmitted;
            }
        }

        // Sem identidade estável de origem não existe versionamento de fonte entre transmissões.
        // Cada nova observação permanece uma ocorrência independente; linkage/retroalimentação
        // pode associá-la à identidade Jornada sem fabricar pessoa_origem.
        var internalVersion = origin is null ? 1 : (latest?.VersaoInterna ?? 0) + 1;
        var nomeCmp = IdentityComparison.NormalizeText(person.NomeCompleto) ?? person.NomeCompleto.ToUpperInvariant();
        var maeCmp = IdentityComparison.NormalizeText(person.NomeMae);

        long observationId;
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT silver.pessoa_observacao(
                    pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
                    cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
                OUTPUT INSERTED.pessoa_observacao_id
                VALUES(@pessoa_origem_id,@lote_id,@gestor_id,@codigo,@versao,@hash,
                       @cpf,@cpf_motivo,@nome,@nome_cmp,@nascimento,@mae,@mae_cmp,@source_as_of);
                """;
            insert.Parameters.Add(new SqlParameter("@pessoa_origem_id", SqlDbType.BigInt) { Value = (object?)origin?.PessoaOrigemId ?? DBNull.Value });
            insert.Parameters.AddWithValue("@lote_id", batch.LoteId);
            insert.Parameters.AddWithValue("@gestor_id", batch.GestorId);
            AddNullable(insert, "@codigo", SqlDbType.NVarChar, 255, origin?.CodigoPessoaOrigem);
            insert.Parameters.AddWithValue("@versao", internalVersion);
            insert.Parameters.Add(new SqlParameter("@hash", SqlDbType.Char, 64) { Value = person.ConteudoHash });
            AddNullable(insert, "@cpf", SqlDbType.Char, 11, person.Cpf);
            AddNullable(insert, "@cpf_motivo", SqlDbType.NVarChar, 30, person.CpfAusenteMotivo);
            insert.Parameters.Add(new SqlParameter("@nome", SqlDbType.NVarChar, 500) { Value = person.NomeCompleto });
            insert.Parameters.Add(new SqlParameter("@nome_cmp", SqlDbType.NVarChar, 500) { Value = nomeCmp });
            insert.Parameters.Add(new SqlParameter("@nascimento", SqlDbType.Date) { Value = person.DataNascimento.ToDateTime(TimeOnly.MinValue) });
            insert.Parameters.Add(new SqlParameter("@mae", SqlDbType.NVarChar, 500) { Value = (object?)person.NomeMae ?? DBNull.Value });
            insert.Parameters.Add(new SqlParameter("@mae_cmp", SqlDbType.NVarChar, 500) { Value = (object?)maeCmp ?? DBNull.Value });
            insert.Parameters.AddWithValue("@source_as_of", batch.DataReferencia);
            observationId = Convert.ToInt64(await insert.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        }

        await PersistPersonIdentifiersAsync(connection, tx, batch, observationId, person.Identificadores, ct);

        var identityDecision = await PersonIdentityResolutionSql.ResolveAsync(
            connection,
            tx,
            person,
            batch.GestorId,
            ct);
        var identity = identityDecision.Resolution;
        var uuid = identity.PessoaUuid;

        await using (var link = connection.CreateCommand())
        {
            link.Transaction = tx;
            link.CommandText = """
                INSERT identidade.vinculo_fonte(
                    pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
                VALUES(@obs,@uuid,@metodo,NULL,@status,NULL,1,@resolvido_em,@motivo);
                """;
            link.Parameters.AddWithValue("@obs", observationId);
            link.Parameters.Add(new SqlParameter("@uuid", SqlDbType.UniqueIdentifier) { Value = (object?)uuid ?? DBNull.Value });
            link.Parameters.Add(new SqlParameter("@metodo", SqlDbType.NVarChar, 40) { Value = identity.MetodoResolucao.ToString() });
            link.Parameters.Add(new SqlParameter("@status", SqlDbType.NVarChar, 30) { Value = identity.Status.ToString() });
            link.Parameters.Add(new SqlParameter("@resolvido_em", SqlDbType.DateTimeOffset) { Value = uuid.HasValue ? DateTimeOffset.UtcNow : DBNull.Value });
            link.Parameters.Add(new SqlParameter("@motivo", SqlDbType.NVarChar, 120) { Value = (object?)identity.Motivo ?? DBNull.Value });
            await link.ExecuteNonQueryAsync(ct);
        }

        if (identity.Status == ResolutionStatus.CONFLITO)
        {
            await RecordIdentityDivergenceAsync(
                connection,
                tx,
                batch.GestorId,
                observationId,
                person.CodigoPessoaOrigem ?? "SEM_CODIGO_ORIGEM",
                identity.Motivo ?? "CONFLITO_IDENTIDADE",
                ct);
        }

        if (identityDecision.InconsistenciaRetroalimentacao)
        {
            await RecordFeedbackInconsistencyV4Async(
                connection,
                tx,
                batch.GestorId,
                observationId,
                person.CodigoPessoaOrigem,
                identityDecision.UuidJornadaRecebido,
                uuid,
                ct);
        }

        foreach (var verification in person.ConferenciasDocumentais)
        {
            await using var verify = connection.CreateCommand();
            verify.Transaction = tx;
            verify.CommandText = """
                INSERT silver.pessoa_campo_verificacao_observacao(
                    pessoa_observacao_id,gestor_id,campo_codigo,evidencia_tipo,referencia_evidencia,verificado_em,source_transaction_id)
                VALUES(@obs,@gestor,@campo,@evidencia,@referencia,@verificado,@source_transaction);
                """;
            verify.Parameters.AddWithValue("@obs", observationId);
            verify.Parameters.AddWithValue("@gestor", batch.GestorId);
            verify.Parameters.Add(new SqlParameter("@campo", SqlDbType.NVarChar, 40) { Value = verification.CampoCodigo });
            verify.Parameters.Add(new SqlParameter("@evidencia", SqlDbType.NVarChar, 80) { Value = verification.EvidenciaTipo });
            AddNullable(verify, "@referencia", SqlDbType.NVarChar, 255, verification.ReferenciaEvidencia);
            verify.Parameters.AddWithValue("@verificado", verification.VerificadoEm);
            AddNullable(verify, "@source_transaction", SqlDbType.NVarChar, 255, person.SourceTransactionId);
            await verify.ExecuteNonQueryAsync(ct);
        }

        var attributes = new List<PersistedAttribute>();
        var attributeInstances = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attribute in person.Atributos)
        {
            if (string.Equals(attribute.StatusEvidencia, "COMPROVADO", StringComparison.OrdinalIgnoreCase)
                && !attribute.VerificadoEm.HasValue)
                throw new InvalidDataException($"Atributo COMPROVADO sem verificadoEm: {attribute.AtributoCodigo}.");

            var identityRule = await ResolveAttributeIdentityRuleAsync(connection, tx, attribute.AtributoCodigo, ct);
            var instanceKey = TransversalAttributeInstanceKey.Compute(identityRule.Cardinality, identityRule.InstanceKeyRule, attribute.Valor);
            if (!attributeInstances.Add(attribute.AtributoCodigo + "\u001f" + instanceKey))
                throw new InvalidDataException($"pessoas.jsonl: atributo/instância duplicado na mesma Pessoa: {attribute.AtributoCodigo} / {instanceKey}.");

            long attributeObservationId;
            await using (var attr = connection.CreateCommand())
            {
                attr.Transaction = tx;
                attr.CommandText = """
                    INSERT silver.pessoa_atributo_observacao(
                        source_record_id,pessoa_observacao_id,fonte_gestor_id,atributo_codigo,atributo_instancia_chave,valor,status_evidencia,evidencia_tipo,
                        referencia_evidencia,verificado_em,atualizado_em_origem,ingested_at)
                    OUTPUT INSERTED.pessoa_atributo_observacao_id
                    VALUES(@source_record_id,@pessoa_observacao_id,@gestor_id,@codigo,@instancia,@valor,@status,@evidencia_tipo,
                           @referencia,@verificado,@atualizado,SYSDATETIMEOFFSET());
                    """;
                AddNullable(attr, "@source_record_id", SqlDbType.NVarChar, 255, attribute.SourceRecordId);
                attr.Parameters.AddWithValue("@pessoa_observacao_id", observationId);
                attr.Parameters.AddWithValue("@gestor_id", batch.GestorId);
                attr.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 80) { Value = attribute.AtributoCodigo });
                attr.Parameters.Add(new SqlParameter("@instancia", SqlDbType.NVarChar, 512) { Value = instanceKey });
                attr.Parameters.Add(new SqlParameter("@valor", SqlDbType.NVarChar, 2000) { Value = attribute.Valor });
                attr.Parameters.Add(new SqlParameter("@status", SqlDbType.NVarChar, 20) { Value = attribute.StatusEvidencia });
                AddNullable(attr, "@evidencia_tipo", SqlDbType.NVarChar, 80, attribute.EvidenciaTipo);
                AddNullableDto(attr, "@referencia", attribute.ReferenciaEvidencia);
                AddNullableDto(attr, "@verificado", attribute.VerificadoEm);
                AddNullableDto(attr, "@atualizado", attribute.AtualizadoEmOrigem);
                attributeObservationId = Convert.ToInt64(await attr.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
            }

            var persisted = new PersistedAttribute(attributeObservationId, attribute, instanceKey, identityRule.Cardinality);
            attributes.Add(persisted);

            long? subprefeituraId = null;
            long? distritoId = null;
            if (attribute.Geografia is not null)
                (subprefeituraId, distritoId) = await EnsureGeographyIdsAsync(connection, tx, attribute.Geografia, ct);

            if (string.Equals(attribute.AtributoCodigo, "ENDERECO_RESIDENCIAL", StringComparison.OrdinalIgnoreCase))
            {
                await InsertTerritorialReferenceAsync(connection, tx, attributeObservationId, TerritorialReferenceNature.DOMICILIAR,
                    "ENDERECO_RESIDENCIAL", subprefeituraId, distritoId, attribute.SituacaoGeografia, attribute.Geografia, batch.DataReferencia, ct);
            }
            else if (string.Equals(attribute.AtributoCodigo, "REFERENCIA_TERRITORIAL", StringComparison.OrdinalIgnoreCase))
            {
                if (!attribute.NaturezaReferenciaTerritorial.HasValue)
                    throw new InvalidDataException("REFERENCIA_TERRITORIAL sem natureza declarada pela fonte.");
                await InsertTerritorialReferenceAsync(connection, tx, attributeObservationId, attribute.NaturezaReferenciaTerritorial.Value,
                    "REFERENCIA_TERRITORIAL", subprefeituraId, distritoId, attribute.SituacaoGeografia, attribute.Geografia, batch.DataReferencia, ct);
            }
        }

        var selectedGeography = await SelectTerritorialReferenceAsync(connection, tx, observationId, ct);

        if (uuid.HasValue)
        {
            await RefreshGoldPersonAsync(connection, tx, uuid.Value, ct);
            foreach (var attribute in attributes.Where(a => string.Equals(a.Value.StatusEvidencia, "COMPROVADO", StringComparison.OrdinalIgnoreCase)))
                await PromoteAttributeAsync(connection, tx, batch.GestorId, uuid.Value, attribute, ct);
        }

        if (origin is null)
            return null;

        await TouchPersonOriginAsync(connection, tx, origin.PessoaOrigemId, batch.DataReferencia, ct);
        await RecordProcessedItemAsync(connection, tx, batch, "PESSOA", origin.PessoaOrigemId, null,
            origin.CodigoPessoaOrigem, latest is null ? "INCLUIDO" : "VERSIONADO", internalVersion, person.ConteudoHash, ct);

        return new ProcessedPerson(
            observationId,
            origin.PessoaOrigemId,
            batch.SistemaOrigemId,
            origin.CodigoPessoaOrigem,
            person.Cpf,
            person.CpfAusenteMotivo,
            uuid,
            ToAssignmentState(identity.Status),
            selectedGeography.ReferenciaTerritorialObservacaoId,
            selectedGeography.NaturezaReferenciaTerritorial,
            selectedGeography.SubprefeituraId,
            selectedGeography.DistritoId);
    }

    private static async Task RecordFeedbackInconsistencyV4Async(
        SqlConnection connection,
        SqlTransaction tx,
        long gestorId,
        long observationId,
        string? codigoPessoaOrigem,
        Guid? uuidRecebido,
        Guid? uuidCpf,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            IF NOT EXISTS(
                SELECT 1
                FROM qualidade.divergencia_gestor
                WHERE gestor_id=@gestor
                  AND pessoa_observacao_id=@obs
                  AND tipo='INCONSISTENCIA_RETROALIMENTACAO'
                  AND status='ABERTA')
            BEGIN
                INSERT qualidade.divergencia_gestor(
                    gestor_id,tipo,motivo,pessoa_observacao_id,codigo_pessoa_origem,status)
                VALUES(
                    @gestor,'INCONSISTENCIA_RETROALIMENTACAO',@motivo,@obs,@codigo,'ABERTA');
            END;
            """;
        command.Parameters.AddWithValue("@gestor", gestorId);
        command.Parameters.AddWithValue("@obs", observationId);
        command.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 255)
        {
            Value = (object?)codigoPessoaOrigem ?? "SEM_CODIGO_ORIGEM"
        });
        // UUIDs permanecem fora da mensagem de log; o motivo persistido identifica a classe do
        // defeito e a própria observação conserva o UUID recebido em identificadores[].
        command.Parameters.Add(new SqlParameter("@motivo", SqlDbType.NVarChar, 120)
        {
            Value = uuidRecebido.HasValue && uuidCpf.HasValue
                ? "UUID_JORNADA_DIVERGE_DA_ANCORA_CPF"
                : "UUID_JORNADA_INCONSISTENTE"
        });
        await command.ExecuteNonQueryAsync(ct);
    }
}

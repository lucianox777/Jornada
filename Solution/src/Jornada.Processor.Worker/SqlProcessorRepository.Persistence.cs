using System.Data;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Processor.Worker;

internal sealed partial class SqlProcessorRepository
{
    public async Task PersistValidatedAsync(ReservedBatch batch, ParsedPackage package, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await SetProcessingAsync(connection, tx, batch, ct);
            var peopleBySource = new Dictionary<string, ProcessedPerson>(StringComparer.Ordinal);

            foreach (var person in package.Pessoas)
            {
                var processed = await PersistPersonAsync(connection, tx, batch, person, ct);
                peopleBySource[person.CodigoPessoaOrigem] = processed;
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
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<ProcessedPerson> PersistPersonAsync(
        SqlConnection connection,
        SqlTransaction tx,
        ReservedBatch batch,
        ParsedPerson person,
        CancellationToken ct)
    {
        var pessoaOrigemId = await EnsurePersonOriginAsync(connection, tx, batch.SistemaOrigemId, person.CodigoPessoaOrigem, ct);
        var latest = await GetLatestPersonVersionAsync(connection, tx, pessoaOrigemId, ct);
        if (latest is not null && string.Equals(latest.ConteudoHash, person.ConteudoHash, StringComparison.Ordinal))
        {
            await TouchPersonOriginAsync(connection, tx, pessoaOrigemId, batch.DataReferencia, ct);
            await RecordProcessedItemAsync(connection, tx, batch, "PESSOA", pessoaOrigemId, null,
                person.CodigoPessoaOrigem, "RETRANSMITIDO", latest.VersaoInterna, person.ConteudoHash, ct);
            var retransmitted = await LoadProcessedPersonAsync(connection, tx, latest.ObservationId, ct);
            if (retransmitted.PessoaUuid is Guid retransmittedUuid)
                await RefreshGoldPersonAsync(connection, tx, retransmittedUuid, ct);
            return retransmitted;
        }

        var internalVersion = (latest?.VersaoInterna ?? 0) + 1;
        var nomeCmp = IdentityComparison.NormalizeText(person.NomeCompleto) ?? person.NomeCompleto.ToUpperInvariant();
        var maeCmp = IdentityComparison.NormalizeText(person.NomeMae) ?? person.NomeMae.ToUpperInvariant();
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
            insert.Parameters.AddWithValue("@pessoa_origem_id", pessoaOrigemId);
            insert.Parameters.AddWithValue("@lote_id", batch.LoteId);
            insert.Parameters.AddWithValue("@gestor_id", batch.GestorId);
            insert.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 255) { Value = person.CodigoPessoaOrigem });
            insert.Parameters.AddWithValue("@versao", internalVersion);
            insert.Parameters.Add(new SqlParameter("@hash", SqlDbType.Char, 64) { Value = person.ConteudoHash });
            AddNullable(insert, "@cpf", SqlDbType.Char, 11, person.Cpf);
            AddNullable(insert, "@cpf_motivo", SqlDbType.NVarChar, 30, person.CpfAusenteMotivo);
            insert.Parameters.Add(new SqlParameter("@nome", SqlDbType.NVarChar, 500) { Value = person.NomeCompleto });
            insert.Parameters.Add(new SqlParameter("@nome_cmp", SqlDbType.NVarChar, 500) { Value = nomeCmp });
            insert.Parameters.Add(new SqlParameter("@nascimento", SqlDbType.Date) { Value = person.DataNascimento.ToDateTime(TimeOnly.MinValue) });
            insert.Parameters.Add(new SqlParameter("@mae", SqlDbType.NVarChar, 500) { Value = person.NomeMae });
            insert.Parameters.Add(new SqlParameter("@mae_cmp", SqlDbType.NVarChar, 500) { Value = maeCmp });
            insert.Parameters.AddWithValue("@source_as_of", batch.DataReferencia);
            observationId = Convert.ToInt64(await insert.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        }

        Guid? uuid = null;
        ResolutionStatus resolutionStatus;
        ResolutionMethod resolutionMethod;
        string? resolutionReason = null;
        var cpf = string.IsNullOrWhiteSpace(person.Cpf) ? null : CpfRules.NormalizeAndValidate(person.Cpf);
        if (!string.IsNullOrWhiteSpace(person.Cpf) && cpf is null)
        {
            resolutionStatus = ResolutionStatus.CONFLITO;
            resolutionMethod = ResolutionMethod.CPF_DETERMINISTICO;
            resolutionReason = CpfRules.StructurallyInvalidReason;
        }
        else if (cpf is not null)
        {
            var identity = await SqlIdentityMapRepository.ResolveOrCreateByCpfAsync(
                connection, tx, cpf,
                new IdentityCore(person.NomeCompleto, person.DataNascimento, person.NomeMae),
                batch.GestorId, person.CodigoPessoaOrigem, ct);
            uuid = identity.PessoaUuid;
            resolutionStatus = identity.Status;
            resolutionMethod = identity.MetodoResolucao;
            resolutionReason = identity.Motivo;
        }
        else
        {
            resolutionStatus = ResolutionStatus.NAO_RESOLVIDO;
            resolutionMethod = ResolutionMethod.PENDENTE_PROBABILISTICO;
            resolutionReason = "AGUARDA_LINKAGE_SOB_DEMANDA";
        }

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
            link.Parameters.Add(new SqlParameter("@metodo", SqlDbType.NVarChar, 40) { Value = resolutionMethod.ToString() });
            link.Parameters.Add(new SqlParameter("@status", SqlDbType.NVarChar, 30) { Value = resolutionStatus.ToString() });
            link.Parameters.Add(new SqlParameter("@resolvido_em", SqlDbType.DateTimeOffset) { Value = uuid.HasValue ? DateTimeOffset.UtcNow : DBNull.Value });
            link.Parameters.Add(new SqlParameter("@motivo", SqlDbType.NVarChar, 120) { Value = (object?)resolutionReason ?? DBNull.Value });
            await link.ExecuteNonQueryAsync(ct);
        }

        if (resolutionStatus == ResolutionStatus.CONFLITO)
            await RecordIdentityDivergenceAsync(connection, tx, batch.GestorId, observationId, person.CodigoPessoaOrigem, resolutionReason ?? "CONFLITO_IDENTIDADE", ct);

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
            // A constraint Silver também protege este contrato, mas a camada de aplicação deve
            // classificar o pacote inválido antes de deixar o SQL Server converter o defeito de
            // entrada em SqlException 547. Mantemos a validação dentro da transação para que os
            // testes de atomicidade ainda cubram rollback após persistência parcial do lote.
            if (string.Equals(attribute.StatusEvidencia, "COMPROVADO", StringComparison.OrdinalIgnoreCase)
                && !attribute.VerificadoEm.HasValue)
            {
                throw new InvalidDataException($"Atributo COMPROVADO sem verificadoEm: {attribute.AtributoCodigo}.");
            }

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
                // ENDERECO_RESIDENCIAL é dado cadastral de residência. Quando usado como fallback, sua geografia
                // é persistida somente no snapshot de Referência Territorial DOMICILIAR; não há tabela geográfica paralela.
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

        await TouchPersonOriginAsync(connection, tx, pessoaOrigemId, batch.DataReferencia, ct);
        await RecordProcessedItemAsync(connection, tx, batch, "PESSOA", pessoaOrigemId, null,
            person.CodigoPessoaOrigem, latest is null ? "INCLUIDO" : "VERSIONADO", internalVersion, person.ConteudoHash, ct);

        return new ProcessedPerson(observationId, pessoaOrigemId, batch.SistemaOrigemId, person.CodigoPessoaOrigem, person.Cpf, person.CpfAusenteMotivo, uuid, ToAssignmentState(resolutionStatus), selectedGeography.ReferenciaTerritorialObservacaoId, selectedGeography.NaturezaReferenciaTerritorial, selectedGeography.SubprefeituraId, selectedGeography.DistritoId);
    }

    private static async Task<AttributeIdentityRule> ResolveAttributeIdentityRuleAsync(
        SqlConnection connection, SqlTransaction tx, string attributeCode, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT cardinalidade,chave_instancia_codigo FROM ref.atributo_transversal WHERE atributo_codigo=@codigo AND ativo=1;";
        command.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 80) { Value = attributeCode });
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidDataException($"Atributo transversal inexistente/inativo: {attributeCode}.");
        return new AttributeIdentityRule(reader.GetString(0), reader.GetString(1));
    }

    private static async Task InsertTerritorialReferenceAsync(
        SqlConnection connection, SqlTransaction tx, long attributeObservationId, TerritorialReferenceNature nature, string semanticSource,
        long? subprefeituraId, long? distritoId, GeographicResolutionStatus? geographyStatus, ReferenceGeography? geography, DateTimeOffset dataReferencia, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT silver.referencia_territorial_observacao(
                pessoa_atributo_observacao_id,natureza_referencia,fonte_semantica,subprefeitura_id,distrito_id,situacao_geografia,origem_geografia,referencia_malha,resolvido_em)
            VALUES(@atributo,@natureza,@fonte,@subprefeitura,@distrito,@situacao,@origem,@malha,@resolvido);
            """;
        command.Parameters.AddWithValue("@atributo", attributeObservationId);
        command.Parameters.Add(new SqlParameter("@natureza", SqlDbType.NVarChar, 50) { Value = nature.ToString() });
        command.Parameters.Add(new SqlParameter("@fonte", SqlDbType.NVarChar, 30) { Value = semanticSource });
        command.Parameters.Add(new SqlParameter("@subprefeitura", SqlDbType.BigInt) { Value = (object?)subprefeituraId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@distrito", SqlDbType.BigInt) { Value = (object?)distritoId ?? DBNull.Value });
        AddNullable(command, "@situacao", SqlDbType.NVarChar, 40, geographyStatus?.ToString());
        AddNullable(command, "@origem", SqlDbType.NVarChar, 30, geography?.Origem.ToString());
        AddNullable(command, "@malha", SqlDbType.NVarChar, 120, geography?.ReferenciaMalha);
        command.Parameters.Add(new SqlParameter("@resolvido", SqlDbType.DateTimeOffset) { Value = geography is null ? DBNull.Value : geography.ResolvidoEm ?? dataReferencia });
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task PersistFactAsync(
        SqlConnection connection,
        SqlTransaction tx,
        ReservedBatch batch,
        ProcessedPerson person,
        ParsedFact fact,
        CancellationToken ct)
    {
        var source = await EnsureRecordOriginAsync(connection, tx, batch, fact.CodigoRegistroOrigem, ct);
        var latest = await GetLatestRecordVersionAsync(connection, tx, source.RegistroOrigemId, ct);

        if (latest is null)
        {
            if (fact.Operacao != RegistroOperacao.INCLUSAO)
                throw new InvalidDataException($"Primeiro envio de {fact.CodigoRegistroOrigem} deve usar operacao=INCLUSAO.");
        }
        else
        {
            var sameContent = string.Equals(latest.ConteudoHash, fact.ConteudoHash, StringComparison.Ordinal);
            if (sameContent && latest.Operacao == fact.Operacao)
            {
                await TouchRecordOriginAsync(connection, tx, source.RegistroOrigemId, batch.DataReferencia, ct);
                await RecordProcessedItemAsync(connection, tx, batch, "REGISTRO", null, source.RegistroOrigemId,
                    fact.CodigoRegistroOrigem, "RETRANSMITIDO", latest.VersaoInterna, fact.ConteudoHash, ct);
                // v3.45: o fato válido já materializa na primeira passagem, mesmo sem UUID.
                // Retransmissão idempotente não cria nova versão nem reescreve o sujeito declarado histórico.
                return;
            }

            if (latest.Operacao == RegistroOperacao.EXCLUSAO)
            {
                if (fact.Operacao != RegistroOperacao.INCLUSAO)
                    throw new InvalidDataException($"Registro {fact.CodigoRegistroOrigem} está excluído; somente INCLUSAO pode reabri-lo.");
            }
            else
            {
                if (fact.Operacao == RegistroOperacao.INCLUSAO)
                    throw new InvalidDataException($"Registro {fact.CodigoRegistroOrigem} já existe; use ALTERACAO, RETIFICACAO ou EXCLUSAO.");
                if (sameContent && fact.Operacao != RegistroOperacao.EXCLUSAO)
                    throw new InvalidDataException($"Registro {fact.CodigoRegistroOrigem} não mudou de conteúdo; {fact.Operacao} sem alteração de valores não cria nova versão.");
            }
        }

        var internalVersion = (latest?.VersaoInterna ?? 0) + 1;
        var processingResult = latest is null ? "INCLUIDO"
            : latest.Operacao == RegistroOperacao.EXCLUSAO && fact.Operacao == RegistroOperacao.INCLUSAO ? "REABERTO"
            : fact.Operacao == RegistroOperacao.EXCLUSAO ? "EXCLUIDO"
            : "VERSIONADO";
        long recordObservationId;
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT silver.registro_observacao(
                    registro_origem_id,codigo_registro_origem,versao_interna,operacao,conteudo_hash,
                    lote_id,gestor_id,natureza,tipo_registro_id,tipo_registro_versao_id,pessoa_observacao_id,
                    data_inicio_concessao,data_fim_concessao,data_evento_concessao,data_hora_servico,unidade_servico,situacao,situacao_vigencia,situacao_vigencia_desde,motivo_encerramento,valor_concedido,quantidade,unidade,source_as_of)
                OUTPUT INSERTED.registro_observacao_id
                VALUES(@registro_origem_id,@codigo_registro,@versao_interna,@operacao,@hash,
                       @lote_id,@gestor_id,@natureza,@tipo_id,@tipo_versao_id,@pessoa_obs,
                       @data_inicio_concessao,@data_fim_concessao,@data_evento_concessao,@data_hora,@unidade_servico,@situacao,@situacao_vigencia,@situacao_vigencia_desde,@motivo_encerramento,@valor_concedido,@quantidade,@unidade,@source_as_of);
                """;
            insert.Parameters.AddWithValue("@registro_origem_id", source.RegistroOrigemId);
            insert.Parameters.Add(new SqlParameter("@codigo_registro", SqlDbType.NVarChar, 255) { Value = fact.CodigoRegistroOrigem });
            insert.Parameters.AddWithValue("@versao_interna", internalVersion);
            insert.Parameters.Add(new SqlParameter("@operacao", SqlDbType.NVarChar, 20) { Value = fact.Operacao.ToString() });
            insert.Parameters.Add(new SqlParameter("@hash", SqlDbType.Char, 64) { Value = fact.ConteudoHash });
            insert.Parameters.AddWithValue("@lote_id", batch.LoteId);
            insert.Parameters.AddWithValue("@gestor_id", batch.GestorId);
            insert.Parameters.Add(new SqlParameter("@natureza", SqlDbType.NVarChar, 30) { Value = batch.Natureza!.Value.ToString() });
            insert.Parameters.AddWithValue("@tipo_id", batch.TipoRegistroId!.Value);
            insert.Parameters.AddWithValue("@tipo_versao_id", batch.TipoRegistroVersaoId!.Value);
            insert.Parameters.AddWithValue("@pessoa_obs", person.ObservationId);
            AddNullableDate(insert, "@data_inicio_concessao", fact.DataInicioConcessao);
            AddNullableDate(insert, "@data_fim_concessao", fact.DataFimConcessao);
            AddNullableDate(insert, "@data_evento_concessao", fact.DataEventoConcessao);
            AddNullableDto(insert, "@data_hora", fact.DataHoraServico);
            AddNullable(insert, "@unidade_servico", SqlDbType.NVarChar, 200, fact.UnidadeServico);
            AddNullable(insert, "@situacao", SqlDbType.NVarChar, 80, fact.Situacao);
            AddNullable(insert, "@situacao_vigencia", SqlDbType.NVarChar, 20, fact.SituacaoVigencia);
            AddNullableDate(insert, "@situacao_vigencia_desde", fact.SituacaoVigenciaDesde);
            AddNullable(insert, "@motivo_encerramento", SqlDbType.NVarChar, 30, fact.MotivoEncerramento);
            AddNullableDecimal(insert, "@valor_concedido", SqlDbType.Decimal, 18, 2, fact.ValorConcedido);
            AddNullableDecimal(insert, "@quantidade", SqlDbType.Decimal, 18, 4, fact.Quantidade);
            AddNullable(insert, "@unidade", SqlDbType.NVarChar, 50, fact.Unidade);
            insert.Parameters.AddWithValue("@source_as_of", batch.DataReferencia);
            recordObservationId = Convert.ToInt64(await insert.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        }

        if (fact.Operacao == RegistroOperacao.EXCLUSAO)
        {
            await MarkFactExcludedAsync(connection, tx, source.RegistroOrigemId, batch.Natureza!.Value, ct);
            await TouchRecordOriginAsync(connection, tx, source.RegistroOrigemId, batch.DataReferencia, ct);
            await RecordProcessedItemAsync(connection, tx, batch, "REGISTRO", null, source.RegistroOrigemId,
                fact.CodigoRegistroOrigem, processingResult, internalVersion, fact.ConteudoHash, ct);
            return;
        }

        var evaluation = quality.Evaluate(batch, fact);
        if (evaluation is not null)
        {
            await using var qc = connection.CreateCommand();
            qc.Transaction = tx;
            qc.CommandText = """
                INSERT qualidade.qc_registro_resultado(registro_observacao_id,resultado,regra_codigo,motivo,executado_em)
                VALUES(@registro,@resultado,@regra,@motivo,SYSDATETIMEOFFSET());
                """;
            qc.Parameters.AddWithValue("@registro", recordObservationId);
            qc.Parameters.Add(new SqlParameter("@resultado", SqlDbType.NVarChar, 30) { Value = evaluation.Resultado });
            AddNullable(qc, "@regra", SqlDbType.NVarChar, 80, evaluation.RegraCodigo);
            AddNullable(qc, "@motivo", SqlDbType.NVarChar, 500, evaluation.Motivo);
            await qc.ExecuteNonQueryAsync(ct);
        }

        await TouchRecordOriginAsync(connection, tx, source.RegistroOrigemId, batch.DataReferencia, ct);
        await RecordProcessedItemAsync(connection, tx, batch, "REGISTRO", null, source.RegistroOrigemId,
            fact.CodigoRegistroOrigem, processingResult, internalVersion, fact.ConteudoHash, ct);

        // v3.45: ocorrência factual e atribuição canônica são dimensões independentes.
        // Todo fato válido declarado pela finalística materializa; PessoaUuid pode ser NULL.
        if (batch.Natureza == IntegrationNature.BENEFICIO)
            await MaterializeBenefitGrantedAsync(connection, tx, batch, person, source.RegistroOrigemId, internalVersion, recordObservationId, fact, evaluation, ct);
        else
            await MaterializeServiceProvidedAsync(connection, tx, batch, person, source.RegistroOrigemId, internalVersion, recordObservationId, fact, evaluation, ct);
    }

    private static string ToAssignmentState(ResolutionStatus status) => status switch
    {
        ResolutionStatus.RESOLVIDO => "ATRIBUIDA",
        ResolutionStatus.CONFLITO => "CONFLITO_IDENTIDADE",
        _ => "PENDENTE_IDENTIDADE"
    };

    private static async Task RecordIdentityDivergenceAsync(SqlConnection connection, SqlTransaction tx, long gestorId, long pessoaObservacaoId, string codigoPessoaOrigem, string motivo, CancellationToken ct)
    {
        await using var command=connection.CreateCommand(); command.Transaction=tx;
        command.CommandText="""
            IF NOT EXISTS(SELECT 1 FROM qualidade.divergencia_gestor WHERE gestor_id=@gestor AND pessoa_observacao_id=@obs AND tipo='DIVERGENCIA_IDENTIDADE' AND motivo=@motivo AND status='ABERTA')
             INSERT qualidade.divergencia_gestor(gestor_id,tipo,motivo,pessoa_observacao_id,codigo_pessoa_origem,status) VALUES(@gestor,'DIVERGENCIA_IDENTIDADE',@motivo,@obs,@codigo,'ABERTA');
            """;
        command.Parameters.AddWithValue("@gestor",gestorId); command.Parameters.AddWithValue("@obs",pessoaObservacaoId);
        command.Parameters.Add(new SqlParameter("@codigo",SqlDbType.NVarChar,255){Value=codigoPessoaOrigem});
        command.Parameters.Add(new SqlParameter("@motivo",SqlDbType.NVarChar,120){Value=motivo});
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task TouchPersonOriginAsync(SqlConnection connection, SqlTransaction tx, long pessoaOrigemId, DateTimeOffset dataReferencia, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "UPDATE silver.pessoa_origem SET ultima_recepcao_em=SYSDATETIMEOFFSET(),ultima_referencia_recebida=CASE WHEN ultima_referencia_recebida IS NULL OR @ref > ultima_referencia_recebida THEN @ref ELSE ultima_referencia_recebida END WHERE pessoa_origem_id=@id;";
        command.Parameters.AddWithValue("@ref", dataReferencia);
        command.Parameters.AddWithValue("@id", pessoaOrigemId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task TouchRecordOriginAsync(SqlConnection connection, SqlTransaction tx, long registroOrigemId, DateTimeOffset dataReferencia, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "UPDATE silver.registro_origem SET ultima_recepcao_em=SYSDATETIMEOFFSET(),ultima_referencia_recebida=CASE WHEN ultima_referencia_recebida IS NULL OR @ref > ultima_referencia_recebida THEN @ref ELSE ultima_referencia_recebida END WHERE registro_origem_id=@id;";
        command.Parameters.AddWithValue("@ref", dataReferencia);
        command.Parameters.AddWithValue("@id", registroOrigemId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task RecordProcessedItemAsync(
        SqlConnection connection, SqlTransaction tx, ReservedBatch batch, string itemClass,
        long? pessoaOrigemId, long? registroOrigemId, string codigoOrigem, string result,
        int internalVersion, string contentHash, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT ingestao.item_processado(
                lote_id,classe_item,pessoa_origem_id,registro_origem_id,codigo_origem,resultado,versao_interna,conteudo_hash,data_referencia)
            VALUES(@lote,@classe,@pessoa,@registro,@codigo,@resultado,@versao,@hash,@ref);
            """;
        command.Parameters.AddWithValue("@lote", batch.LoteId);
        command.Parameters.Add(new SqlParameter("@classe", SqlDbType.NVarChar, 20) { Value = itemClass });
        command.Parameters.Add(new SqlParameter("@pessoa", SqlDbType.BigInt) { Value = (object?)pessoaOrigemId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@registro", SqlDbType.BigInt) { Value = (object?)registroOrigemId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 255) { Value = codigoOrigem });
        command.Parameters.Add(new SqlParameter("@resultado", SqlDbType.NVarChar, 30) { Value = result });
        command.Parameters.AddWithValue("@versao", internalVersion);
        command.Parameters.Add(new SqlParameter("@hash", SqlDbType.Char, 64) { Value = contentHash });
        command.Parameters.AddWithValue("@ref", batch.DataReferencia);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<long> EnsurePersonOriginAsync(
        SqlConnection connection, SqlTransaction tx, long sistemaOrigemId, string codigo, CancellationToken ct)
    {
        await using (var find = connection.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = """
                SELECT pessoa_origem_id
                FROM silver.pessoa_origem WITH (UPDLOCK,HOLDLOCK)
                WHERE sistema_origem_id=@sistema AND codigo_pessoa_origem=@codigo;
                """;
            find.Parameters.AddWithValue("@sistema", sistemaOrigemId);
            find.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 255) { Value = codigo });
            var value = await find.ExecuteScalarAsync(ct);
            if (value is not null && value is not DBNull) return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem)
            OUTPUT INSERTED.pessoa_origem_id VALUES(@sistema,@codigo);
            """;
        insert.Parameters.AddWithValue("@sistema", sistemaOrigemId);
        insert.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 255) { Value = codigo });
        return Convert.ToInt64(await insert.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<PersonVersionState?> GetLatestPersonVersionAsync(
        SqlConnection connection, SqlTransaction tx, long pessoaOrigemId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT TOP(1) pessoa_observacao_id,versao_interna,conteudo_hash
            FROM silver.pessoa_observacao WITH (UPDLOCK,HOLDLOCK)
            WHERE pessoa_origem_id=@origem
            ORDER BY versao_interna DESC;
            """;
        command.Parameters.AddWithValue("@origem", pessoaOrigemId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new PersonVersionState(reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2))
            : null;
    }

    private static async Task<ProcessedPerson> LoadProcessedPersonAsync(
        SqlConnection connection, SqlTransaction tx, long observationId, CancellationToken ct)
    {
        long pessoaOrigemId; long sistemaOrigemId; string codigo; string? cpf; string? cpfMotivo; Guid? uuid=null; string status="NAO_RESOLVIDO";
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = """
                SELECT po.pessoa_origem_id,pori.sistema_origem_id,po.codigo_pessoa_origem,po.cpf,po.cpf_ausente_motivo,vc.pessoa_uuid,COALESCE(vc.status,'NAO_RESOLVIDO')
                FROM silver.pessoa_observacao po
                JOIN silver.pessoa_origem pori ON pori.pessoa_origem_id=po.pessoa_origem_id
                LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
                WHERE po.pessoa_observacao_id=@obs;
                """;
            command.Parameters.AddWithValue("@obs", observationId);
            await using var reader=await command.ExecuteReaderAsync(ct);
            if(!await reader.ReadAsync(ct)) throw new InvalidDataException($"Pessoa observação {observationId} inexistente.");
            pessoaOrigemId=reader.GetInt64(0); sistemaOrigemId=reader.GetInt64(1); codigo=reader.GetString(2);
            cpf=reader.IsDBNull(3)?null:reader.GetString(3); cpfMotivo=reader.IsDBNull(4)?null:reader.GetString(4);
            if(!reader.IsDBNull(5)) uuid=reader.GetGuid(5); status=reader.GetString(6);
        }
        var geography = await SelectTerritorialReferenceAsync(connection, tx, observationId, ct);
        var assignment = status == "RESOLVIDO" && uuid.HasValue ? "ATRIBUIDA" : status == "CONFLITO" ? "CONFLITO_IDENTIDADE" : "PENDENTE_IDENTIDADE";
        return new ProcessedPerson(observationId,pessoaOrigemId,sistemaOrigemId,codigo,cpf,cpfMotivo,uuid,assignment,geography.ReferenciaTerritorialObservacaoId,geography.NaturezaReferenciaTerritorial,geography.SubprefeituraId,geography.DistritoId);
    }

    private static async Task<RecordSourceState> EnsureRecordOriginAsync(
        SqlConnection connection, SqlTransaction tx, ReservedBatch batch, string codigo, CancellationToken ct)
    {
        await using (var find = connection.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = """
                SELECT registro_origem_id,natureza,tipo_registro_id
                FROM silver.registro_origem WITH (UPDLOCK,HOLDLOCK)
                WHERE sistema_origem_id=@sistema AND codigo_registro_origem=@codigo;
                """;
            find.Parameters.AddWithValue("@sistema", batch.SistemaOrigemId);
            find.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 255) { Value = codigo });
            await using var reader = await find.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var id = reader.GetInt64(0);
                var nature = reader.GetString(1);
                var typeId = reader.GetInt64(2);
                if (!string.Equals(nature, batch.Natureza!.Value.ToString(), StringComparison.Ordinal) || typeId != batch.TipoRegistroId!.Value)
                    throw new InvalidDataException($"codigoRegistroOrigem {codigo} já pertence a outra Natureza/Tipo no sistema de origem.");
                return new RecordSourceState(id);
            }
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT silver.registro_origem(sistema_origem_id,codigo_registro_origem,natureza,tipo_registro_id)
            OUTPUT INSERTED.registro_origem_id VALUES(@sistema,@codigo,@natureza,@tipo);
            """;
        insert.Parameters.AddWithValue("@sistema", batch.SistemaOrigemId);
        insert.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 255) { Value = codigo });
        insert.Parameters.Add(new SqlParameter("@natureza", SqlDbType.NVarChar, 30) { Value = batch.Natureza!.Value.ToString() });
        insert.Parameters.AddWithValue("@tipo", batch.TipoRegistroId!.Value);
        return new RecordSourceState(Convert.ToInt64(await insert.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task<int> GetMaterializationStateAsync(
        SqlConnection connection, SqlTransaction tx, IntegrationNature nature, long observationId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        var goldTable = nature == IntegrationNature.BENEFICIO ? "gold.beneficio_concedido" : "gold.servico_prestado";
        command.CommandText = $"SELECT (SELECT COUNT(*) FROM {goldTable} WHERE registro_observacao_id=@obs) + (SELECT COUNT(*) FROM serving.registro_integrado WHERE registro_observacao_id=@obs);";
        command.Parameters.AddWithValue("@obs", observationId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<RecordVersionState?> GetLatestRecordVersionAsync(
        SqlConnection connection, SqlTransaction tx, long registroOrigemId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT TOP(1) registro_observacao_id,versao_interna,conteudo_hash,operacao
            FROM silver.registro_observacao WITH (UPDLOCK,HOLDLOCK)
            WHERE registro_origem_id=@origem
            ORDER BY versao_interna DESC;
            """;
        command.Parameters.AddWithValue("@origem", registroOrigemId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new RecordVersionState(reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), Enum.Parse<RegistroOperacao>(reader.GetString(3), false))
            : null;
    }

    private sealed record PersonVersionState(long ObservationId, int VersaoInterna, string ConteudoHash);
    private sealed record RecordSourceState(long RegistroOrigemId);
    private sealed record RecordVersionState(long ObservationId, int VersaoInterna, string ConteudoHash, RegistroOperacao Operacao);
}

using System.Data;
using System.Data.Common;
using Jornada.Contracts;

namespace Jornada.Processor.Worker;

internal sealed partial class PostgreSqlProcessorRepository
{
    private async Task PersistFactAsync(
        DbConnection connection, DbTransaction tx, ReservedBatch batch, PgProcessedPerson person, ParsedFact fact, CancellationToken ct)
    {
        var registroOrigemId = await EnsureRecordOriginAsync(connection, tx, batch, fact.CodigoRegistroOrigem, ct);
        var latest = await GetLatestRecordVersionAsync(connection, tx, registroOrigemId, ct);

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
                await TouchRecordOriginAsync(connection, tx, registroOrigemId, batch.DataReferencia, ct);
                await RecordProcessedItemAsync(connection, tx, batch, "REGISTRO", null, registroOrigemId,
                    fact.CodigoRegistroOrigem, "RETRANSMITIDO", latest.VersaoInterna, fact.ConteudoHash, ct);
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
                    throw new InvalidDataException(
                        $"Registro {fact.CodigoRegistroOrigem} não mudou de conteúdo; {fact.Operacao} sem alteração de valores não cria nova versão.");
            }
        }

        var version = (latest?.VersaoInterna ?? 0) + 1;
        var result = latest is null ? "INCLUIDO"
            : latest.Operacao == RegistroOperacao.EXCLUSAO && fact.Operacao == RegistroOperacao.INCLUSAO ? "REABERTO"
            : fact.Operacao == RegistroOperacao.EXCLUSAO ? "EXCLUIDO"
            : "VERSIONADO";

        long recordObservationId;
        await using (var insert = Command(connection, tx, """
            INSERT INTO silver.registro_observacao(
                registro_origem_id,codigo_registro_origem,versao_interna,operacao,conteudo_hash,
                lote_id,gestor_id,natureza,tipo_registro_id,tipo_registro_versao_id,pessoa_observacao_id,
                data_inicio_concessao,data_fim_concessao,data_evento_concessao,data_hora_servico,unidade_servico,
                situacao,situacao_vigencia,situacao_vigencia_desde,motivo_encerramento,valor_concedido,quantidade,unidade,source_as_of)
            VALUES(@registro_origem,@codigo_registro,@versao,@operacao,@hash,
                   @lote,@gestor,@natureza,@tipo,@tipo_versao,@pessoa_obs,
                   @data_inicio,@data_fim,@data_evento,@data_hora,@unidade_servico,
                   @situacao,@situacao_vigencia,@situacao_desde,@motivo_encerramento,@valor,@quantidade,@unidade,@source)
            RETURNING registro_observacao_id;
            """))
        {
            Add(insert, "@registro_origem", DbType.Int64, registroOrigemId);
            Add(insert, "@codigo_registro", DbType.String, fact.CodigoRegistroOrigem, 255);
            Add(insert, "@versao", DbType.Int32, version);
            Add(insert, "@operacao", DbType.String, fact.Operacao.ToString(), 20);
            Add(insert, "@hash", DbType.AnsiStringFixedLength, fact.ConteudoHash, 64);
            Add(insert, "@lote", DbType.Guid, batch.LoteId);
            Add(insert, "@gestor", DbType.Int64, batch.GestorId);
            Add(insert, "@natureza", DbType.String, batch.Natureza!.Value.ToString(), 30);
            Add(insert, "@tipo", DbType.Int64, batch.TipoRegistroId!.Value);
            Add(insert, "@tipo_versao", DbType.Int64, batch.TipoRegistroVersaoId!.Value);
            Add(insert, "@pessoa_obs", DbType.Int64, person.ObservationId);
            AddDate(insert, "@data_inicio", fact.DataInicioConcessao);
            AddDate(insert, "@data_fim", fact.DataFimConcessao);
            AddDate(insert, "@data_evento", fact.DataEventoConcessao);
            Add(insert, "@data_hora", DbType.DateTimeOffset, fact.DataHoraServico);
            Add(insert, "@unidade_servico", DbType.String, fact.UnidadeServico, 200);
            Add(insert, "@situacao", DbType.String, fact.Situacao, 80);
            Add(insert, "@situacao_vigencia", DbType.String, fact.SituacaoVigencia, 20);
            AddDate(insert, "@situacao_desde", fact.SituacaoVigenciaDesde);
            Add(insert, "@motivo_encerramento", DbType.String, fact.MotivoEncerramento, 30);
            Add(insert, "@valor", DbType.Decimal, fact.ValorConcedido);
            Add(insert, "@quantidade", DbType.Decimal, fact.Quantidade);
            Add(insert, "@unidade", DbType.String, fact.Unidade, 50);
            Add(insert, "@source", DbType.DateTimeOffset, batch.DataReferencia);
            recordObservationId = Convert.ToInt64(await insert.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        }

        if (fact.Operacao == RegistroOperacao.EXCLUSAO)
        {
            await MarkFactExcludedAsync(connection, tx, registroOrigemId, batch.Natureza!.Value, ct);
            await TouchRecordOriginAsync(connection, tx, registroOrigemId, batch.DataReferencia, ct);
            await RecordProcessedItemAsync(connection, tx, batch, "REGISTRO", null, registroOrigemId,
                fact.CodigoRegistroOrigem, result, version, fact.ConteudoHash, ct);
            return;
        }

        var evaluation = quality.Evaluate(batch, fact);
        if (evaluation is not null)
        {
            await using var qc = Command(connection, tx, """
                INSERT INTO qualidade.qc_registro_resultado(registro_observacao_id,resultado,regra_codigo,motivo,executado_em)
                VALUES(@registro,@resultado,@regra,@motivo,CURRENT_TIMESTAMP);
                """);
            Add(qc, "@registro", DbType.Int64, recordObservationId);
            Add(qc, "@resultado", DbType.String, evaluation.Resultado, 30);
            Add(qc, "@regra", DbType.String, evaluation.RegraCodigo, 80);
            Add(qc, "@motivo", DbType.String, evaluation.Motivo, 500);
            await qc.ExecuteNonQueryAsync(ct);
        }

        await TouchRecordOriginAsync(connection, tx, registroOrigemId, batch.DataReferencia, ct);
        await RecordProcessedItemAsync(connection, tx, batch, "REGISTRO", null, registroOrigemId,
            fact.CodigoRegistroOrigem, result, version, fact.ConteudoHash, ct);

        if (batch.Natureza == IntegrationNature.BENEFICIO)
            await MaterializeBenefitGrantedAsync(connection, tx, batch, person, registroOrigemId, version,
                recordObservationId, fact, evaluation, ct);
        else
            await MaterializeServiceProvidedAsync(connection, tx, batch, person, registroOrigemId, version,
                recordObservationId, fact, evaluation, ct);
    }

    private static async Task<long> EnsureRecordOriginAsync(
        DbConnection connection, DbTransaction tx, ReservedBatch batch, string code, CancellationToken ct)
    {
        await using var command = Command(connection, tx, """
            INSERT INTO silver.registro_origem(
                sistema_origem_id,natureza,tipo_registro_id,codigo_registro_origem,ultima_recepcao_em)
            VALUES(@sistema,@natureza,@tipo,@codigo,CURRENT_TIMESTAMP)
            ON CONFLICT(sistema_origem_id,natureza,tipo_registro_id,codigo_registro_origem)
            DO UPDATE SET ultima_recepcao_em=CURRENT_TIMESTAMP
            RETURNING registro_origem_id;
            """);
        Add(command, "@sistema", DbType.Int64, batch.SistemaOrigemId);
        Add(command, "@natureza", DbType.String, batch.Natureza!.Value.ToString(), 30);
        Add(command, "@tipo", DbType.Int64, batch.TipoRegistroId!.Value);
        Add(command, "@codigo", DbType.String, code, 255);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<PgLatestRecord?> GetLatestRecordVersionAsync(
        DbConnection connection, DbTransaction tx, long registroOrigemId, CancellationToken ct)
    {
        await using var command = Command(connection, tx, """
            SELECT registro_observacao_id,versao_interna,conteudo_hash,operacao
              FROM silver.registro_observacao
             WHERE registro_origem_id=@id
             ORDER BY versao_interna DESC
             LIMIT 1
             FOR UPDATE;
            """);
        Add(command, "@id", DbType.Int64, registroOrigemId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new PgLatestRecord(reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2),
            Enum.Parse<RegistroOperacao>(reader.GetString(3), ignoreCase: false));
    }

    private static async Task TouchRecordOriginAsync(
        DbConnection connection, DbTransaction tx, long registroOrigemId, DateTimeOffset dataReferencia, CancellationToken ct)
    {
        await using var command = Command(connection, tx, """
            UPDATE silver.registro_origem SET ultima_recepcao_em=CURRENT_TIMESTAMP,
                ultima_referencia_recebida=CASE WHEN ultima_referencia_recebida IS NULL OR @ref > ultima_referencia_recebida
                    THEN @ref ELSE ultima_referencia_recebida END
             WHERE registro_origem_id=@id;
            """);
        Add(command, "@ref", DbType.DateTimeOffset, dataReferencia);
        Add(command, "@id", DbType.Int64, registroOrigemId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task RecordProcessedItemAsync(
        DbConnection connection, DbTransaction tx, ReservedBatch batch, string itemClass,
        long? pessoaOrigemId, long? registroOrigemId, string sourceCode, string result,
        int version, string contentHash, CancellationToken ct)
    {
        await using var command = Command(connection, tx, """
            INSERT INTO ingestao.item_processado(
                lote_id,classe_item,pessoa_origem_id,registro_origem_id,codigo_origem,resultado,
                versao_interna,conteudo_hash,data_referencia)
            VALUES(@lote,@classe,@pessoa,@registro,@codigo,@resultado,@versao,@hash,@ref);
            """);
        Add(command, "@lote", DbType.Guid, batch.LoteId);
        Add(command, "@classe", DbType.String, itemClass, 30);
        Add(command, "@pessoa", DbType.Int64, pessoaOrigemId);
        Add(command, "@registro", DbType.Int64, registroOrigemId);
        Add(command, "@codigo", DbType.String, sourceCode, 255);
        Add(command, "@resultado", DbType.String, result, 40);
        Add(command, "@versao", DbType.Int32, version);
        Add(command, "@hash", DbType.AnsiStringFixedLength, contentHash, 64);
        Add(command, "@ref", DbType.DateTimeOffset, batch.DataReferencia);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task TransitionPreviousVersionAsync(
        DbConnection connection, DbTransaction tx, IntegrationNature nature, long registroOrigemId,
        RegistroOperacao operation, DateTimeOffset versionedAt, CancellationToken ct)
    {
        if (operation == RegistroOperacao.INCLUSAO) return;
        var targetStatus = operation == RegistroOperacao.ALTERACAO ? "HISTORICO" : "RETIFICADO";
        var goldTable = nature == IntegrationNature.BENEFICIO ? "gold.beneficio_concedido" : "gold.servico_prestado";
        await using (var gold = Command(connection, tx,
                         $"UPDATE {goldTable} SET status_analitico=@status,vigencia_versao_fim=@fim,atualizado_em=CURRENT_TIMESTAMP WHERE registro_origem_id=@origem AND status_analitico='VIGENTE';"))
        {
            Add(gold, "@status", DbType.String, targetStatus, 20);
            Add(gold, "@fim", DbType.DateTimeOffset, versionedAt);
            Add(gold, "@origem", DbType.Int64, registroOrigemId);
            await gold.ExecuteNonQueryAsync(ct);
        }
        await using var serving = Command(connection, tx,
            "UPDATE serving.registro_integrado SET status_analitico=@status,vigencia_versao_fim=@fim,atualizado_em=CURRENT_TIMESTAMP WHERE registro_origem_id=@origem AND status_analitico='VIGENTE';");
        Add(serving, "@status", DbType.String, targetStatus, 20);
        Add(serving, "@fim", DbType.DateTimeOffset, versionedAt);
        Add(serving, "@origem", DbType.Int64, registroOrigemId);
        await serving.ExecuteNonQueryAsync(ct);
    }

    private static async Task MarkFactExcludedAsync(
        DbConnection connection, DbTransaction tx, long registroOrigemId, IntegrationNature nature, CancellationToken ct)
    {
        var versionedAt = DateTimeOffset.UtcNow;
        var goldTable = nature == IntegrationNature.BENEFICIO ? "gold.beneficio_concedido" : "gold.servico_prestado";
        await using (var gold = Command(connection, tx,
                         $"UPDATE {goldTable} SET status_analitico='EXCLUIDO',vigencia_versao_fim=@fim,atualizado_em=CURRENT_TIMESTAMP WHERE registro_origem_id=@origem AND status_analitico='VIGENTE';"))
        {
            Add(gold, "@fim", DbType.DateTimeOffset, versionedAt);
            Add(gold, "@origem", DbType.Int64, registroOrigemId);
            await gold.ExecuteNonQueryAsync(ct);
        }
        await using var serving = Command(connection, tx,
            "UPDATE serving.registro_integrado SET status_analitico='EXCLUIDO',vigencia_versao_fim=@fim,atualizado_em=CURRENT_TIMESTAMP WHERE registro_origem_id=@origem AND status_analitico='VIGENTE';");
        Add(serving, "@fim", DbType.DateTimeOffset, versionedAt);
        Add(serving, "@origem", DbType.Int64, registroOrigemId);
        await serving.ExecuteNonQueryAsync(ct);
    }

    private static async Task MaterializeBenefitGrantedAsync(
        DbConnection connection, DbTransaction tx, ReservedBatch batch, PgProcessedPerson person,
        long registroOrigemId, int version, long recordId, ParsedFact fact, RegistryQcEvaluation? evaluation, CancellationToken ct)
    {
        var versionedAt = DateTimeOffset.UtcNow;
        await TransitionPreviousVersionAsync(connection, tx, IntegrationNature.BENEFICIO, registroOrigemId, fact.Operacao, versionedAt, ct);

        await using (var gold = Command(connection, tx, """
            INSERT INTO gold.beneficio_concedido(
                registro_observacao_id,registro_origem_id,codigo_registro_origem,versao_interna,operacao,status_analitico,
                pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem,cpf_declarado,cpf_ausente_motivo,pessoa_uuid,
                estado_atribuicao_identidade,gestor_id,tipo_registro_id,tipo_registro_versao_id,entrega_id,
                data_inicio_concessao,data_fim_concessao,data_evento_concessao,situacao_vigencia,situacao_vigencia_desde,
                motivo_encerramento,referencia_territorial_observacao_id,natureza_referencia_territorial,
                subprefeitura_referencia_id,distrito_referencia_id,valor_concedido,quantidade,unidade,
                source_as_of,qc_resultado,qc_especifico_implementado,vigencia_versao_inicio,vigencia_versao_fim)
            VALUES(@registro,@registro_origem,@codigo_registro,@versao,@operacao,'VIGENTE',
                   @pessoa_origem,@sistema_origem,@codigo_pessoa,@cpf,@cpf_ausente,@uuid,@estado,@gestor,@tipo,@tipo_versao,@entrega,
                   @data_inicio,@data_fim,@data_evento,@situacao_vigencia,@situacao_desde,@motivo_encerramento,
                   @referencia,@natureza_referencia,@subprefeitura,@distrito,@valor,@quantidade,@unidade,
                   @source,@qc,@qc_impl,@vigencia_inicio,NULL);
            """))
        {
            AddCommonFactParameters(gold, batch, person, registroOrigemId, version, recordId, fact, versionedAt);
            AddBenefitParameters(gold, fact, evaluation, batch);
            await gold.ExecuteNonQueryAsync(ct);
        }

        await using var serving = Command(connection, tx, """
            INSERT INTO serving.registro_integrado(
                registro_observacao_id,registro_origem_id,codigo_registro_origem,versao_interna,operacao,status_analitico,
                pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem,cpf_declarado,cpf_ausente_motivo,pessoa_uuid,
                estado_atribuicao_identidade,gestor_id,natureza,tipo_registro_id,tipo_registro_versao_id,entrega_id,entrega_completa,
                data_inicio_concessao,data_fim_concessao,data_evento_concessao,situacao_vigencia,situacao_vigencia_desde,
                motivo_encerramento,referencia_territorial_observacao_id,natureza_referencia_territorial,
                subprefeitura_referencia_id,distrito_referencia_id,valor_concedido,quantidade,unidade,
                source_as_of,qc_resultado,qc_especifico_implementado,vigencia_versao_inicio,vigencia_versao_fim)
            VALUES(@registro,@registro_origem,@codigo_registro,@versao,@operacao,'VIGENTE',
                   @pessoa_origem,@sistema_origem,@codigo_pessoa,@cpf,@cpf_ausente,@uuid,@estado,@gestor,'BENEFICIO',@tipo,@tipo_versao,@entrega,FALSE,
                   @data_inicio,@data_fim,@data_evento,@situacao_vigencia,@situacao_desde,@motivo_encerramento,
                   @referencia,@natureza_referencia,@subprefeitura,@distrito,@valor,@quantidade,@unidade,
                   @source,@qc,@qc_impl,@vigencia_inicio,NULL);
            """);
        AddCommonFactParameters(serving, batch, person, registroOrigemId, version, recordId, fact, versionedAt);
        AddBenefitParameters(serving, fact, evaluation, batch);
        await serving.ExecuteNonQueryAsync(ct);
    }

    private static async Task MaterializeServiceProvidedAsync(
        DbConnection connection, DbTransaction tx, ReservedBatch batch, PgProcessedPerson person,
        long registroOrigemId, int version, long recordId, ParsedFact fact, RegistryQcEvaluation? evaluation, CancellationToken ct)
    {
        var versionedAt = DateTimeOffset.UtcNow;
        await TransitionPreviousVersionAsync(connection, tx, IntegrationNature.SERVICO, registroOrigemId, fact.Operacao, versionedAt, ct);

        await using (var gold = Command(connection, tx, """
            INSERT INTO gold.servico_prestado(
                registro_observacao_id,registro_origem_id,codigo_registro_origem,versao_interna,operacao,status_analitico,
                pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem,cpf_declarado,cpf_ausente_motivo,pessoa_uuid,
                estado_atribuicao_identidade,gestor_id,tipo_registro_id,tipo_registro_versao_id,entrega_id,
                data_hora_servico,unidade_servico,situacao,referencia_territorial_observacao_id,natureza_referencia_territorial,
                subprefeitura_referencia_id,distrito_referencia_id,source_as_of,vigencia_versao_inicio,vigencia_versao_fim)
            VALUES(@registro,@registro_origem,@codigo_registro,@versao,@operacao,'VIGENTE',
                   @pessoa_origem,@sistema_origem,@codigo_pessoa,@cpf,@cpf_ausente,@uuid,@estado,@gestor,@tipo,@tipo_versao,@entrega,
                   @data_hora,@unidade_servico,@situacao,@referencia,@natureza_referencia,@subprefeitura,@distrito,@source,@vigencia_inicio,NULL);
            """))
        {
            AddCommonFactParameters(gold, batch, person, registroOrigemId, version, recordId, fact, versionedAt);
            AddServiceParameters(gold, fact);
            await gold.ExecuteNonQueryAsync(ct);
        }

        await using var serving = Command(connection, tx, """
            INSERT INTO serving.registro_integrado(
                registro_observacao_id,registro_origem_id,codigo_registro_origem,versao_interna,operacao,status_analitico,
                pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem,cpf_declarado,cpf_ausente_motivo,pessoa_uuid,
                estado_atribuicao_identidade,gestor_id,natureza,tipo_registro_id,tipo_registro_versao_id,entrega_id,entrega_completa,
                data_hora_servico,unidade_servico,situacao,referencia_territorial_observacao_id,natureza_referencia_territorial,
                subprefeitura_referencia_id,distrito_referencia_id,source_as_of,qc_resultado,qc_especifico_implementado,
                vigencia_versao_inicio,vigencia_versao_fim)
            VALUES(@registro,@registro_origem,@codigo_registro,@versao,@operacao,'VIGENTE',
                   @pessoa_origem,@sistema_origem,@codigo_pessoa,@cpf,@cpf_ausente,@uuid,@estado,@gestor,'SERVICO',@tipo,@tipo_versao,@entrega,FALSE,
                   @data_hora,@unidade_servico,@situacao,@referencia,@natureza_referencia,@subprefeitura,@distrito,@source,@qc,@qc_impl,
                   @vigencia_inicio,NULL);
            """);
        AddCommonFactParameters(serving, batch, person, registroOrigemId, version, recordId, fact, versionedAt);
        AddServiceParameters(serving, fact);
        Add(serving, "@qc", DbType.String, evaluation?.Resultado, 30);
        Add(serving, "@qc_impl", DbType.Boolean, string.Equals(batch.QcStatus, "IMPLEMENTADO", StringComparison.OrdinalIgnoreCase));
        await serving.ExecuteNonQueryAsync(ct);
    }

    private static void AddCommonFactParameters(
        DbCommand command, ReservedBatch batch, PgProcessedPerson person, long registroOrigemId,
        int version, long recordId, ParsedFact fact, DateTimeOffset versionedAt)
    {
        Add(command, "@registro", DbType.Int64, recordId);
        Add(command, "@registro_origem", DbType.Int64, registroOrigemId);
        Add(command, "@codigo_registro", DbType.String, fact.CodigoRegistroOrigem, 255);
        Add(command, "@versao", DbType.Int32, version);
        Add(command, "@operacao", DbType.String, fact.Operacao.ToString(), 20);
        Add(command, "@pessoa_origem", DbType.Int64, person.PessoaOrigemId);
        Add(command, "@sistema_origem", DbType.Int64, person.SistemaOrigemId);
        Add(command, "@codigo_pessoa", DbType.String, person.CodigoPessoaOrigem, 255);
        Add(command, "@cpf", DbType.AnsiStringFixedLength, person.CpfDeclarado, 11);
        Add(command, "@cpf_ausente", DbType.String, person.CpfAusenteMotivo, 30);
        Add(command, "@uuid", DbType.Guid, person.PessoaUuid);
        Add(command, "@estado", DbType.String, person.EstadoAtribuicaoIdentidade, 30);
        Add(command, "@gestor", DbType.Int64, batch.GestorId);
        Add(command, "@tipo", DbType.Int64, batch.TipoRegistroId!.Value);
        Add(command, "@tipo_versao", DbType.Int64, batch.TipoRegistroVersaoId!.Value);
        Add(command, "@entrega", DbType.Guid, batch.EntregaId);
        Add(command, "@referencia", DbType.Int64, person.ReferenciaTerritorialObservacaoId);
        Add(command, "@natureza_referencia", DbType.String, person.NaturezaReferenciaTerritorial, 50);
        Add(command, "@subprefeitura", DbType.Int64, person.SubprefeituraId);
        Add(command, "@distrito", DbType.Int64, person.DistritoId);
        Add(command, "@source", DbType.DateTimeOffset, batch.DataReferencia);
        Add(command, "@vigencia_inicio", DbType.DateTimeOffset, versionedAt);
    }

    private static void AddBenefitParameters(
        DbCommand command, ParsedFact fact, RegistryQcEvaluation? evaluation, ReservedBatch batch)
    {
        AddDate(command, "@data_inicio", fact.DataInicioConcessao);
        AddDate(command, "@data_fim", fact.DataFimConcessao);
        AddDate(command, "@data_evento", fact.DataEventoConcessao);
        Add(command, "@situacao_vigencia", DbType.String, fact.SituacaoVigencia, 20);
        AddDate(command, "@situacao_desde", fact.SituacaoVigenciaDesde);
        Add(command, "@motivo_encerramento", DbType.String, fact.MotivoEncerramento, 30);
        Add(command, "@valor", DbType.Decimal, fact.ValorConcedido);
        Add(command, "@quantidade", DbType.Decimal, fact.Quantidade);
        Add(command, "@unidade", DbType.String, fact.Unidade, 50);
        Add(command, "@qc", DbType.String, evaluation?.Resultado, 30);
        Add(command, "@qc_impl", DbType.Boolean, string.Equals(batch.QcStatus, "IMPLEMENTADO", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddServiceParameters(DbCommand command, ParsedFact fact)
    {
        Add(command, "@data_hora", DbType.DateTimeOffset, fact.DataHoraServico);
        Add(command, "@unidade_servico", DbType.String, fact.UnidadeServico, 200);
        Add(command, "@situacao", DbType.String, fact.Situacao, 80);
    }

    private sealed record PgLatestRecord(long ObservationId, int VersaoInterna, string ConteudoHash, RegistroOperacao Operacao);
}

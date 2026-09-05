using System.Data;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Processor.Worker;

internal sealed partial class SqlProcessorRepository
{
    private static async Task MaterializeBenefitGrantedAsync(
        SqlConnection connection, SqlTransaction tx, ReservedBatch batch, ProcessedPerson person,
        long registroOrigemId, int versaoInterna, long recordId, ParsedFact fact, RegistryQcEvaluation? evaluation, CancellationToken ct)
    {
        var versionedAt = DateTimeOffset.UtcNow;
        await TransitionPreviousVersionAsync(connection, tx, IntegrationNature.BENEFICIO, registroOrigemId, fact.Operacao, versionedAt, ct);

        await using (var gold = connection.CreateCommand())
        {
            gold.Transaction = tx;
            gold.CommandText = """
                INSERT gold.beneficio_concedido(
                    registro_observacao_id,registro_origem_id,codigo_registro_origem,versao_interna,operacao,status_analitico,
                    pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem,cpf_declarado,cpf_ausente_motivo,pessoa_uuid,estado_atribuicao_identidade,gestor_id,tipo_registro_id,tipo_registro_versao_id,entrega_id,
                    data_inicio_concessao,data_fim_concessao,data_evento_concessao,situacao_vigencia,situacao_vigencia_desde,motivo_encerramento,referencia_territorial_observacao_id,natureza_referencia_territorial,subprefeitura_referencia_id,distrito_referencia_id,valor_concedido,quantidade,unidade,
                    source_as_of,qc_resultado,qc_especifico_implementado,vigencia_versao_inicio,vigencia_versao_fim)
                VALUES(@registro,@registro_origem,@codigo_registro,@versao_interna,@operacao,'VIGENTE',
                       @pessoa_origem,@sistema_origem,@codigo_pessoa,@cpf_declarado,@cpf_ausente,@uuid,@estado_atribuicao,@gestor,@tipo,@tipo_versao,@entrega,@data_inicio_concessao,@data_fim_concessao,@data_evento_concessao,@situacao_vigencia,@situacao_vigencia_desde,@motivo_encerramento,@referencia_territorial,@natureza_referencia,@subprefeitura,@distrito,
                       @valor_concedido,@quantidade,@unidade,@source,@qc,@qc_impl,@vigencia_inicio,NULL);
                """;
            AddBenefitGrantedParameters(gold, batch, person, registroOrigemId, versaoInterna, recordId, fact, evaluation, versionedAt);
            await gold.ExecuteNonQueryAsync(ct);
        }

        await using var serving = connection.CreateCommand();
        serving.Transaction = tx;
        serving.CommandText = """
            INSERT serving.registro_integrado(
                registro_observacao_id,registro_origem_id,codigo_registro_origem,versao_interna,operacao,status_analitico,
                pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem,cpf_declarado,cpf_ausente_motivo,pessoa_uuid,estado_atribuicao_identidade,gestor_id,natureza,tipo_registro_id,tipo_registro_versao_id,entrega_id,entrega_completa,
                data_inicio_concessao,data_fim_concessao,data_evento_concessao,situacao_vigencia,situacao_vigencia_desde,motivo_encerramento,referencia_territorial_observacao_id,natureza_referencia_territorial,subprefeitura_referencia_id,distrito_referencia_id,valor_concedido,quantidade,unidade,
                source_as_of,qc_resultado,qc_especifico_implementado,vigencia_versao_inicio,vigencia_versao_fim)
            VALUES(@registro,@registro_origem,@codigo_registro,@versao_interna,@operacao,'VIGENTE',
                   @pessoa_origem,@sistema_origem,@codigo_pessoa,@cpf_declarado,@cpf_ausente,@uuid,@estado_atribuicao,@gestor,'BENEFICIO',@tipo,@tipo_versao,@entrega,0,@data_inicio_concessao,@data_fim_concessao,@data_evento_concessao,@situacao_vigencia,@situacao_vigencia_desde,@motivo_encerramento,@referencia_territorial,@natureza_referencia,@subprefeitura,@distrito,
                   @valor_concedido,@quantidade,@unidade,@source,@qc,@qc_impl,@vigencia_inicio,NULL);
            """;
        AddBenefitGrantedParameters(serving, batch, person, registroOrigemId, versaoInterna, recordId, fact, evaluation, versionedAt);
        await serving.ExecuteNonQueryAsync(ct);
    }

    private static async Task MaterializeServiceProvidedAsync(
        SqlConnection connection, SqlTransaction tx, ReservedBatch batch, ProcessedPerson person,
        long registroOrigemId, int versaoInterna, long recordId, ParsedFact fact, RegistryQcEvaluation? evaluation, CancellationToken ct)
    {
        var versionedAt = DateTimeOffset.UtcNow;
        await TransitionPreviousVersionAsync(connection, tx, IntegrationNature.SERVICO, registroOrigemId, fact.Operacao, versionedAt, ct);

        await using (var gold = connection.CreateCommand())
        {
            gold.Transaction = tx;
            gold.CommandText = """
                INSERT gold.servico_prestado(
                    registro_observacao_id,registro_origem_id,codigo_registro_origem,versao_interna,operacao,status_analitico,
                    pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem,cpf_declarado,cpf_ausente_motivo,pessoa_uuid,estado_atribuicao_identidade,gestor_id,tipo_registro_id,tipo_registro_versao_id,entrega_id,
                    data_hora_servico,unidade_servico,situacao,referencia_territorial_observacao_id,natureza_referencia_territorial,subprefeitura_referencia_id,distrito_referencia_id,source_as_of,
                    vigencia_versao_inicio,vigencia_versao_fim)
                VALUES(@registro,@registro_origem,@codigo_registro,@versao_interna,@operacao,'VIGENTE',
                       @pessoa_origem,@sistema_origem,@codigo_pessoa,@cpf_declarado,@cpf_ausente,@uuid,@estado_atribuicao,@gestor,@tipo,@tipo_versao,@entrega,@data_hora,@unidade_servico,@situacao,@referencia_territorial,@natureza_referencia,@subprefeitura,@distrito,@source,
                       @vigencia_inicio,NULL);
                """;
            AddServiceProvidedParameters(gold, batch, person, registroOrigemId, versaoInterna, recordId, fact, versionedAt);
            await gold.ExecuteNonQueryAsync(ct);
        }

        await using var serving = connection.CreateCommand();
        serving.Transaction = tx;
        serving.CommandText = """
            INSERT serving.registro_integrado(
                registro_observacao_id,registro_origem_id,codigo_registro_origem,versao_interna,operacao,status_analitico,
                pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem,cpf_declarado,cpf_ausente_motivo,pessoa_uuid,estado_atribuicao_identidade,gestor_id,natureza,tipo_registro_id,tipo_registro_versao_id,entrega_id,entrega_completa,
                data_hora_servico,unidade_servico,situacao,referencia_territorial_observacao_id,natureza_referencia_territorial,subprefeitura_referencia_id,distrito_referencia_id,source_as_of,qc_resultado,qc_especifico_implementado,
                vigencia_versao_inicio,vigencia_versao_fim)
            VALUES(@registro,@registro_origem,@codigo_registro,@versao_interna,@operacao,'VIGENTE',
                   @pessoa_origem,@sistema_origem,@codigo_pessoa,@cpf_declarado,@cpf_ausente,@uuid,@estado_atribuicao,@gestor,'SERVICO',@tipo,@tipo_versao,@entrega,0,@data_hora,@unidade_servico,@situacao,@referencia_territorial,@natureza_referencia,@subprefeitura,@distrito,@source,@qc,@qc_impl,
                   @vigencia_inicio,NULL);
            """;
        AddServiceProvidedParameters(serving, batch, person, registroOrigemId, versaoInterna, recordId, fact, versionedAt);
        AddNullable(serving, "@qc", SqlDbType.NVarChar, 30, evaluation?.Resultado);
        serving.Parameters.AddWithValue("@qc_impl", string.Equals(batch.QcStatus, "IMPLEMENTADO", StringComparison.OrdinalIgnoreCase));
        await serving.ExecuteNonQueryAsync(ct);
    }

    private static async Task TransitionPreviousVersionAsync(
        SqlConnection connection, SqlTransaction tx, IntegrationNature nature, long registroOrigemId,
        RegistroOperacao operation, DateTimeOffset versionedAt, CancellationToken ct)
    {
        if (operation == RegistroOperacao.INCLUSAO) return;
        var targetStatus = operation == RegistroOperacao.ALTERACAO ? "HISTORICO" : "RETIFICADO";
        var goldTable = nature == IntegrationNature.BENEFICIO ? "gold.beneficio_concedido" : "gold.servico_prestado";

        await using (var gold = connection.CreateCommand())
        {
            gold.Transaction = tx;
            gold.CommandText = $"UPDATE {goldTable} SET status_analitico=@status,vigencia_versao_fim=@fim,atualizado_em=SYSDATETIMEOFFSET() WHERE registro_origem_id=@origem AND status_analitico='VIGENTE';";
            gold.Parameters.Add(new SqlParameter("@status", SqlDbType.NVarChar, 20) { Value = targetStatus });
            gold.Parameters.AddWithValue("@fim", versionedAt);
            gold.Parameters.AddWithValue("@origem", registroOrigemId);
            await gold.ExecuteNonQueryAsync(ct);
        }
        await using (var serving = connection.CreateCommand())
        {
            serving.Transaction = tx;
            serving.CommandText = "UPDATE serving.registro_integrado SET status_analitico=@status,vigencia_versao_fim=@fim,atualizado_em=SYSDATETIMEOFFSET() WHERE registro_origem_id=@origem AND status_analitico='VIGENTE';";
            serving.Parameters.Add(new SqlParameter("@status", SqlDbType.NVarChar, 20) { Value = targetStatus });
            serving.Parameters.AddWithValue("@fim", versionedAt);
            serving.Parameters.AddWithValue("@origem", registroOrigemId);
            await serving.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task MarkFactExcludedAsync(
        SqlConnection connection, SqlTransaction tx, long registroOrigemId, IntegrationNature nature, CancellationToken ct)
    {
        var versionedAt = DateTimeOffset.UtcNow;
        var goldTable = nature == IntegrationNature.BENEFICIO ? "gold.beneficio_concedido" : "gold.servico_prestado";
        await using (var gold = connection.CreateCommand())
        {
            gold.Transaction = tx;
            gold.CommandText = $"UPDATE {goldTable} SET status_analitico='EXCLUIDO',vigencia_versao_fim=@fim,atualizado_em=SYSDATETIMEOFFSET() WHERE registro_origem_id=@origem AND status_analitico='VIGENTE';";
            gold.Parameters.AddWithValue("@fim", versionedAt);
            gold.Parameters.AddWithValue("@origem", registroOrigemId);
            await gold.ExecuteNonQueryAsync(ct);
        }
        await using (var serving = connection.CreateCommand())
        {
            serving.Transaction = tx;
            serving.CommandText = "UPDATE serving.registro_integrado SET status_analitico='EXCLUIDO',vigencia_versao_fim=@fim,atualizado_em=SYSDATETIMEOFFSET() WHERE registro_origem_id=@origem AND status_analitico='VIGENTE';";
            serving.Parameters.AddWithValue("@fim", versionedAt);
            serving.Parameters.AddWithValue("@origem", registroOrigemId);
            await serving.ExecuteNonQueryAsync(ct);
        }
    }

    private static void AddCommonVersionParameters(
        SqlCommand command, ReservedBatch batch, ProcessedPerson person,
        long registroOrigemId, int versaoInterna, long recordId, ParsedFact fact, DateTimeOffset versionedAt)
    {
        command.Parameters.AddWithValue("@registro", recordId);
        command.Parameters.AddWithValue("@registro_origem", registroOrigemId);
        command.Parameters.Add(new SqlParameter("@codigo_registro", SqlDbType.NVarChar, 255) { Value = fact.CodigoRegistroOrigem });
        command.Parameters.AddWithValue("@versao_interna", versaoInterna);
        command.Parameters.Add(new SqlParameter("@operacao", SqlDbType.NVarChar, 20) { Value = fact.Operacao.ToString() });
        command.Parameters.Add(new SqlParameter("@uuid", SqlDbType.UniqueIdentifier) { Value = (object?)person.PessoaUuid ?? DBNull.Value });
        command.Parameters.AddWithValue("@pessoa_origem", person.PessoaOrigemId);
        command.Parameters.AddWithValue("@sistema_origem", person.SistemaOrigemId);
        command.Parameters.Add(new SqlParameter("@codigo_pessoa", SqlDbType.NVarChar, 255) { Value = person.CodigoPessoaOrigem });
        AddNullable(command, "@cpf_declarado", SqlDbType.Char, 11, person.CpfDeclarado);
        AddNullable(command, "@cpf_ausente", SqlDbType.NVarChar, 30, person.CpfAusenteMotivo);
        command.Parameters.Add(new SqlParameter("@estado_atribuicao", SqlDbType.NVarChar, 30) { Value = person.EstadoAtribuicaoIdentidade });
        command.Parameters.AddWithValue("@gestor", batch.GestorId);
        command.Parameters.AddWithValue("@tipo", batch.TipoRegistroId!.Value);
        command.Parameters.AddWithValue("@tipo_versao", batch.TipoRegistroVersaoId!.Value);
        command.Parameters.AddWithValue("@entrega", batch.EntregaId);
        command.Parameters.Add(new SqlParameter("@referencia_territorial", SqlDbType.BigInt) { Value = (object?)person.ReferenciaTerritorialObservacaoId ?? DBNull.Value });
        AddNullable(command, "@natureza_referencia", SqlDbType.NVarChar, 50, person.NaturezaReferenciaTerritorial);
        command.Parameters.Add(new SqlParameter("@subprefeitura", SqlDbType.BigInt) { Value = (object?)person.SubprefeituraId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@distrito", SqlDbType.BigInt) { Value = (object?)person.DistritoId ?? DBNull.Value });
        command.Parameters.AddWithValue("@source", batch.DataReferencia);
        command.Parameters.AddWithValue("@vigencia_inicio", versionedAt);
    }

    private static void AddBenefitGrantedParameters(
        SqlCommand command, ReservedBatch batch, ProcessedPerson person,
        long registroOrigemId, int versaoInterna, long recordId, ParsedFact fact, RegistryQcEvaluation? evaluation, DateTimeOffset versionedAt)
    {
        AddCommonVersionParameters(command, batch, person, registroOrigemId, versaoInterna, recordId, fact, versionedAt);
        AddNullableDate(command, "@data_inicio_concessao", fact.DataInicioConcessao);
        AddNullableDate(command, "@data_fim_concessao", fact.DataFimConcessao);
        AddNullableDate(command, "@data_evento_concessao", fact.DataEventoConcessao);
        AddNullable(command, "@situacao_vigencia", SqlDbType.NVarChar, 20, fact.SituacaoVigencia);
        AddNullableDate(command, "@situacao_vigencia_desde", fact.SituacaoVigenciaDesde);
        AddNullable(command, "@motivo_encerramento", SqlDbType.NVarChar, 30, fact.MotivoEncerramento);
        AddNullableDecimal(command, "@valor_concedido", SqlDbType.Decimal, 18, 2, fact.ValorConcedido);
        AddNullableDecimal(command, "@quantidade", SqlDbType.Decimal, 18, 4, fact.Quantidade);
        AddNullable(command, "@unidade", SqlDbType.NVarChar, 50, fact.Unidade);
        AddNullable(command, "@qc", SqlDbType.NVarChar, 30, evaluation?.Resultado);
        command.Parameters.AddWithValue("@qc_impl", string.Equals(batch.QcStatus, "IMPLEMENTADO", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddServiceProvidedParameters(
        SqlCommand command, ReservedBatch batch, ProcessedPerson person,
        long registroOrigemId, int versaoInterna, long recordId, ParsedFact fact, DateTimeOffset versionedAt)
    {
        AddCommonVersionParameters(command, batch, person, registroOrigemId, versaoInterna, recordId, fact, versionedAt);
        AddNullableDto(command, "@data_hora", fact.DataHoraServico);
        AddNullable(command, "@unidade_servico", SqlDbType.NVarChar, 200, fact.UnidadeServico);
        AddNullable(command, "@situacao", SqlDbType.NVarChar, 80, fact.Situacao);
    }

    private static async Task RefreshGoldPersonAsync(SqlConnection connection, SqlTransaction tx, Guid uuid, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            ;WITH obs AS(
                SELECT po.*
                FROM silver.pessoa_observacao po
                JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
                WHERE vc.pessoa_uuid=@uuid AND vc.status='RESOLVIDO'
            ), stats AS(
                SELECT COUNT(DISTINCT gestor_id) fontes,
                       CASE WHEN COUNT(DISTINCT CONCAT(nome_cmp,'|',CONVERT(char(10),data_nascimento,23),'|',nome_mae_cmp))>1
                            THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END divergente
                FROM obs
            )
            MERGE gold.pessoa AS t
            USING(
                SELECT @uuid pessoa_uuid,
                       COALESCE((SELECT TOP(1) identificador FROM identidade.identity_map WHERE pessoa_uuid=@uuid AND tipo='CPF' AND vigencia_fim IS NULL AND estado='ATIVO' ORDER BY vigencia_inicio DESC),cpf_src.cpf) cpf,
                       nome_src.nome_completo,
                       nasc_src.data_nascimento,
                       mae_src.nome_mae,
                       st.fontes,st.divergente,cpf_src.cpf_ausente_motivo
                FROM stats st
                OUTER APPLY(
                    SELECT TOP(1) o.cpf,o.cpf_ausente_motivo
                    FROM obs o
                    LEFT JOIN silver.pessoa_campo_verificacao_observacao v ON v.pessoa_observacao_id=o.pessoa_observacao_id AND v.campo_codigo='CPF'
                    ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,v.verificado_em DESC,o.source_as_of DESC,o.pessoa_observacao_id DESC
                ) cpf_src
                OUTER APPLY(
                    SELECT TOP(1) o.nome_completo
                    FROM obs o
                    LEFT JOIN silver.pessoa_campo_verificacao_observacao v ON v.pessoa_observacao_id=o.pessoa_observacao_id AND v.campo_codigo='NOME_COMPLETO'
                    ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,v.verificado_em DESC,o.source_as_of DESC,o.pessoa_observacao_id DESC
                ) nome_src
                OUTER APPLY(
                    SELECT TOP(1) o.data_nascimento
                    FROM obs o
                    LEFT JOIN silver.pessoa_campo_verificacao_observacao v ON v.pessoa_observacao_id=o.pessoa_observacao_id AND v.campo_codigo='DATA_NASCIMENTO'
                    ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,v.verificado_em DESC,o.source_as_of DESC,o.pessoa_observacao_id DESC
                ) nasc_src
                OUTER APPLY(
                    SELECT TOP(1) o.nome_mae
                    FROM obs o
                    LEFT JOIN silver.pessoa_campo_verificacao_observacao v ON v.pessoa_observacao_id=o.pessoa_observacao_id AND v.campo_codigo='NOME_MAE'
                    ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,v.verificado_em DESC,o.source_as_of DESC,o.pessoa_observacao_id DESC
                ) mae_src
                WHERE nome_src.nome_completo IS NOT NULL AND nasc_src.data_nascimento IS NOT NULL AND mae_src.nome_mae IS NOT NULL
            ) s ON t.pessoa_uuid=s.pessoa_uuid
            WHEN MATCHED THEN UPDATE SET
                cpf=s.cpf,
                status_cpf=CASE WHEN s.cpf IS NOT NULL THEN 'PRESENTE' WHEN s.cpf_ausente_motivo='EM_REGULARIZACAO' THEN 'EM_REGULARIZACAO' ELSE 'SEM_CPF' END,
                nome_completo=s.nome_completo,data_nascimento=s.data_nascimento,nome_mae=s.nome_mae,fontes_distintas=s.fontes,
                estado_concordancia=CASE WHEN s.divergente=1 THEN 'DIVERGENTE' WHEN s.fontes>1 THEN 'CORROBORADO' ELSE 'BASELINE_FONTE_UNICA' END,
                atualizado_em=SYSDATETIMEOFFSET()
            WHEN NOT MATCHED THEN INSERT(pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia,atualizado_em)
                VALUES(s.pessoa_uuid,s.cpf,CASE WHEN s.cpf IS NOT NULL THEN 'PRESENTE' WHEN s.cpf_ausente_motivo='EM_REGULARIZACAO' THEN 'EM_REGULARIZACAO' ELSE 'SEM_CPF' END,
                       s.nome_completo,s.data_nascimento,s.nome_mae,s.fontes,CASE WHEN s.divergente=1 THEN 'DIVERGENTE' WHEN s.fontes>1 THEN 'CORROBORADO' ELSE 'BASELINE_FONTE_UNICA' END,SYSDATETIMEOFFSET());
            """;
        command.Parameters.AddWithValue("@uuid", uuid);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<(long? SubprefeituraId, long? DistritoId)> EnsureGeographyIdsAsync(
        SqlConnection connection,
        SqlTransaction tx,
        ReferenceGeography geography,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(geography.DistritoCodigo) || string.IsNullOrWhiteSpace(geography.SubprefeituraCodigo)
            || string.IsNullOrWhiteSpace(geography.DistritoNome) || string.IsNullOrWhiteSpace(geography.SubprefeituraNome))
            return (null, null);

        var observedAt = geography.ResolvidoEm ?? DateTimeOffset.UtcNow;
        long subprefeituraId;
        await using (var find = connection.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = """
                SELECT TOP(1) subprefeitura_id
                FROM ref.subprefeitura WITH (UPDLOCK,HOLDLOCK)
                WHERE codigo=@codigo AND nome=@nome
                ORDER BY subprefeitura_id DESC;
                """;
            find.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 30) { Value = geography.SubprefeituraCodigo });
            find.Parameters.Add(new SqlParameter("@nome", SqlDbType.NVarChar, 150) { Value = geography.SubprefeituraNome });
            var existing = await find.ExecuteScalarAsync(ct);
            if (existing is not null && existing is not DBNull)
                subprefeituraId = Convert.ToInt64(existing, System.Globalization.CultureInfo.InvariantCulture);
            else
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = "INSERT ref.subprefeitura(codigo,nome,observado_em) OUTPUT INSERTED.subprefeitura_id VALUES(@codigo,@nome,@observado);";
                insert.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 30) { Value = geography.SubprefeituraCodigo });
                insert.Parameters.Add(new SqlParameter("@nome", SqlDbType.NVarChar, 150) { Value = geography.SubprefeituraNome });
                insert.Parameters.AddWithValue("@observado", observedAt);
                subprefeituraId = Convert.ToInt64(await insert.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        long distritoId;
        await using (var find = connection.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = """
                SELECT TOP(1) distrito_id
                FROM ref.distrito WITH (UPDLOCK,HOLDLOCK)
                WHERE codigo=@codigo AND nome=@nome AND subprefeitura_id=@subprefeitura
                ORDER BY distrito_id DESC;
                """;
            find.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 30) { Value = geography.DistritoCodigo });
            find.Parameters.Add(new SqlParameter("@nome", SqlDbType.NVarChar, 150) { Value = geography.DistritoNome });
            find.Parameters.AddWithValue("@subprefeitura", subprefeituraId);
            var existing = await find.ExecuteScalarAsync(ct);
            if (existing is not null && existing is not DBNull)
                distritoId = Convert.ToInt64(existing, System.Globalization.CultureInfo.InvariantCulture);
            else
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = "INSERT ref.distrito(subprefeitura_id,codigo,nome,observado_em) OUTPUT INSERTED.distrito_id VALUES(@subprefeitura,@codigo,@nome,@observado);";
                insert.Parameters.AddWithValue("@subprefeitura", subprefeituraId);
                insert.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 30) { Value = geography.DistritoCodigo });
                insert.Parameters.Add(new SqlParameter("@nome", SqlDbType.NVarChar, 150) { Value = geography.DistritoNome });
                insert.Parameters.AddWithValue("@observado", observedAt);
                distritoId = Convert.ToInt64(await insert.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return (subprefeituraId, distritoId);
    }

    private static async Task<TerritorialReferenceSelection> SelectTerritorialReferenceAsync(
        SqlConnection connection, SqlTransaction tx, long pessoaObservacaoId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT referencia_territorial_observacao_id,natureza_referencia,subprefeitura_id,distrito_id
            FROM silver.v_pessoa_referencia_territorial
            WHERE pessoa_observacao_id=@pessoa;
            """;
        command.Parameters.AddWithValue("@pessoa", pessoaObservacaoId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.IsDBNull(0))
            return new TerritorialReferenceSelection(null, null, null, null);
        return new TerritorialReferenceSelection(
            reader.GetInt64(0), reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3));
    }

    private static async Task PromoteAttributeAsync(
        SqlConnection connection,
        SqlTransaction tx,
        long gestorId,
        Guid uuid,
        PersistedAttribute persisted,
        CancellationToken ct)
    {
        var value = persisted.Value;
        if (!value.VerificadoEm.HasValue)
            throw new InvalidDataException($"Atributo COMPROVADO sem verificadoEm: {value.AtributoCodigo}.");
        var precedence = value.ReferenciaEvidencia ?? value.VerificadoEm.Value;

        await using var current = connection.CreateCommand();
        current.Transaction = tx;
        current.CommandText = """
            SELECT pessoa_atributo_id,valor,precedencia_em,verificado_em
            FROM gold.pessoa_atributo WITH (UPDLOCK,HOLDLOCK)
            WHERE pessoa_uuid=@uuid AND atributo_codigo=@codigo AND atributo_instancia_chave=@instancia AND vigencia_fim IS NULL;
            """;
        current.Parameters.AddWithValue("@uuid", uuid);
        current.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 80) { Value = value.AtributoCodigo });
        current.Parameters.Add(new SqlParameter("@instancia", SqlDbType.NVarChar, 512) { Value = persisted.InstanceKey });
        long? currentId = null;
        string? currentValue = null;
        DateTimeOffset? currentPrecedence = null;
        DateTimeOffset? currentVerified = null;
        await using (var reader = await current.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                currentId = reader.GetInt64(0);
                currentValue = reader.GetString(1);
                currentPrecedence = reader.GetDateTimeOffset(2);
                currentVerified = reader.GetDateTimeOffset(3);
            }
        }

        if (currentId.HasValue)
        {
            if (precedence < currentPrecedence!.Value || (precedence == currentPrecedence.Value && value.VerificadoEm.Value <= currentVerified!.Value))
            {
                // Para MULTI, duas representações textuais com a mesma chave canônica são a MESMA instância
                // (ex.: telefone formatado ou e-mail com caixa ASCII distinta) e não configuram conflito de evidência.
                if (string.Equals(persisted.Cardinality, "SINGLE", StringComparison.OrdinalIgnoreCase)
                    && precedence == currentPrecedence.Value
                    && !string.Equals(currentValue, value.Valor, StringComparison.Ordinal))
                {
                    await using var conflict = connection.CreateCommand();
                    conflict.Transaction = tx;
                    conflict.CommandText = "UPDATE gold.pessoa SET estado_concordancia='CONFLITO_EVIDENCIA',atualizado_em=SYSDATETIMEOFFSET() WHERE pessoa_uuid=@uuid;";
                    conflict.Parameters.AddWithValue("@uuid", uuid);
                    await conflict.ExecuteNonQueryAsync(ct);
                }
                return;
            }

            await using var close = connection.CreateCommand();
            close.Transaction = tx;
            close.CommandText = "UPDATE gold.pessoa_atributo SET vigencia_fim=@fim,atualizado_em=SYSDATETIMEOFFSET() WHERE pessoa_atributo_id=@id;";
            close.Parameters.AddWithValue("@fim", precedence);
            close.Parameters.AddWithValue("@id", currentId.Value);
            await close.ExecuteNonQueryAsync(ct);
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT gold.pessoa_atributo(
                pessoa_uuid,atributo_codigo,atributo_instancia_chave,valor,fonte_gestor_id,pessoa_atributo_observacao_id,source_record_id,evidencia_tipo,
                referencia_evidencia,verificado_em,precedencia_em,vigencia_inicio,atualizado_em)
            VALUES(@uuid,@codigo,@instancia,@valor,@gestor,@obs,@source_record,@evidencia,@referencia,@verificado,@precedencia,@precedencia,SYSDATETIMEOFFSET());
            """;
        insert.Parameters.AddWithValue("@uuid", uuid);
        insert.Parameters.Add(new SqlParameter("@codigo", SqlDbType.NVarChar, 80) { Value = value.AtributoCodigo });
        insert.Parameters.Add(new SqlParameter("@instancia", SqlDbType.NVarChar, 512) { Value = persisted.InstanceKey });
        insert.Parameters.Add(new SqlParameter("@valor", SqlDbType.NVarChar, 2000) { Value = value.Valor });
        insert.Parameters.AddWithValue("@gestor", gestorId);
        insert.Parameters.AddWithValue("@obs", persisted.ObservationId);
        AddNullable(insert, "@source_record", SqlDbType.NVarChar, 255, value.SourceRecordId);
        AddNullable(insert, "@evidencia", SqlDbType.NVarChar, 80, value.EvidenciaTipo);
        AddNullableDto(insert, "@referencia", value.ReferenciaEvidencia);
        insert.Parameters.AddWithValue("@verificado", value.VerificadoEm.Value);
        insert.Parameters.AddWithValue("@precedencia", precedence);
        await insert.ExecuteNonQueryAsync(ct);
    }

    private static void AddNullable(SqlCommand command, string name, SqlDbType type, int size, string? value) =>
        command.Parameters.Add(new SqlParameter(name, type, size) { Value = (object?)value ?? DBNull.Value });

    private static void AddNullableDto(SqlCommand command, string name, DateTimeOffset? value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.DateTimeOffset) { Value = value.HasValue ? value.Value : DBNull.Value });

    private static void AddNullableDate(SqlCommand command, string name, DateOnly? value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Date) { Value = value.HasValue ? value.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value });

    private static void AddNullableDecimal(SqlCommand command, string name, SqlDbType type, byte precision, byte scale, decimal? value)
    {
        command.Parameters.Add(new SqlParameter(name, type) { Precision = precision, Scale = scale, Value = value.HasValue ? value.Value : DBNull.Value });
    }

    private sealed record ProcessedPerson(long ObservationId, long PessoaOrigemId, long SistemaOrigemId, string CodigoPessoaOrigem, string? CpfDeclarado, string? CpfAusenteMotivo, Guid? PessoaUuid, string EstadoAtribuicaoIdentidade, long? ReferenciaTerritorialObservacaoId, string? NaturezaReferenciaTerritorial, long? SubprefeituraId, long? DistritoId);
    private sealed record TerritorialReferenceSelection(long? ReferenciaTerritorialObservacaoId, string? NaturezaReferenciaTerritorial, long? SubprefeituraId, long? DistritoId);
    private sealed record PersistedAttribute(long ObservationId, ParsedTransversalAttribute Value, string InstanceKey, string Cardinality);
    private sealed record AttributeIdentityRule(string Cardinality, string InstanceKeyRule);
}

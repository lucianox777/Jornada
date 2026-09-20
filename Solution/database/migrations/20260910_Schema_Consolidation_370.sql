SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Fechamento fail-closed da consolidação do schema operacional v3.70.
-- O marcador só é promovido quando todo o conjunto estrutural declarado no
-- manifesto canônico estiver materializado, inclusive contratos adicionados
-- após a criação original deste arquivo em 10/09.
DECLARE @missing TABLE(item NVARCHAR(300) NOT NULL PRIMARY KEY);

INSERT @missing(item)
SELECT CONCAT(N'TABLE:',v.objeto)
FROM (VALUES
 (N'jornada.schema_migration'),
 (N'identidade.cpf_ancora'),
 (N'identidade.pessoa_origem_progressiva'),
 (N'identidade.pessoa_origem_progressiva_evento'),
 (N'identidade.composicao_uuid_reserva'),
 (N'identidade.composicao_plano'),
 (N'identidade.composicao_aplicacao'),
 (N'identidade.composicao_historico_aplicado'),
 (N'identidade.composicao_recomposicao_plano'),
 (N'identidade.composicao_publicacao'),
 (N'auditoria.decisao_identidade_evento'),
 (N'auditoria.modelo_linkage_estado_evento'),
 (N'identidade.blocking_chave'),
 (N'identidade.linkage_ruleset'),
 (N'identidade.linkage_ruleset_passe'),
 (N'identidade.linkage_ruleset_passe_campo'),
 (N'ref.frequencia_nome_versao'),
 (N'ref.frequencia_nome'),
 (N'ref.frequencia_nome_cobertura'),
 (N'identidade.linkage_quality_estimate'),
 (N'controle.runtime_componente')
) v(objeto)
WHERE OBJECT_ID(v.objeto, N'U') IS NULL;

INSERT @missing(item)
SELECT CONCAT(N'VIEW:',v.objeto)
FROM (VALUES
 (N'ref.v_frequencia_nome_ativa'),
 (N'ref.v_frequencia_nome_cobertura_ativa'),
 (N'serving.v_bi_qualidade_resolucao_operacional'),
 (N'serving.v_bi_qualidade_resolucao_calibrada'),
 (N'serving.v_bi_qualidade_resolucao_operacional_origem'),
 (N'serving.v_bi_qualidade_resolucao_operacional_estrato'),
 (N'serving.v_bi_completude_pessoa'),
 (N'serving.v_pessoa_nome_referencia'),
 (N'serving.v_pessoa'),
 (N'auditoria.v_decisao_identidade_evento'),
 (N'auditoria.v_modelo_linkage_estado_evento')
) v(objeto)
WHERE OBJECT_ID(v.objeto, N'V') IS NULL;

INSERT @missing(item)
SELECT CONCAT(N'TRIGGER:',v.objeto)
FROM (VALUES
 (N'ref.tr_frequencia_nome_bloqueia_versao_publicada'),
 (N'ref.tr_frequencia_nome_versao_metadado_immutavel'),
 (N'ref.tr_frequencia_nome_cobertura_bloqueia_versao_publicada'),
 (N'identidade.tr_modelo_linkage_fixa_frequencia_nome_versao'),
 (N'gold.tr_pessoa_nome_publicacao'),
 (N'identidade.tr_linkage_run_congela_frequencia_nome'),
 (N'identidade.tr_linkage_run_frequencia_nome_immutavel'),
 (N'identidade.tr_modelo_linkage_promotion_contract'),
 (N'identidade.tr_linkage_resultado_publicacao_imutavel'),
 (N'identidade.tr_linkage_resultado_bloqueia_delete'),
 (N'auditoria.tr_decisao_identidade_evento_append_only'),
 (N'auditoria.tr_modelo_linkage_estado_evento_append_only'),
 (N'identidade.tr_modelo_linkage_estado_evento')
) v(objeto)
WHERE OBJECT_ID(v.objeto, N'TR') IS NULL;

IF OBJECT_ID(N'ref.sp_publicar_frequencia_nome_versao',N'P') IS NULL
    INSERT @missing(item) VALUES(N'PROC:ref.sp_publicar_frequencia_nome_versao');
IF OBJECT_ID(N'identidade.sp_publicar_resolucao_progressiva_linkage',N'P') IS NULL
    INSERT @missing(item) VALUES(N'PROC:identidade.sp_publicar_resolucao_progressiva_linkage');
IF OBJECT_ID(N'auditoria.sp_registrar_decisao_identidade',N'P') IS NULL
    INSERT @missing(item) VALUES(N'PROC:auditoria.sp_registrar_decisao_identidade');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'auditoria.modelo_linkage_estado_evento') AND name=N'IX_modelo_linkage_estado_evento_modelo')
    INSERT @missing(item) VALUES(N'INDEX:auditoria.modelo_linkage_estado_evento.IX_modelo_linkage_estado_evento_modelo');

DECLARE @required_columns TABLE(tabela SYSNAME NOT NULL,coluna SYSNAME NOT NULL,PRIMARY KEY(tabela,coluna));
INSERT @required_columns(tabela,coluna) VALUES
 (N'identidade.modelo_linkage',N'frequencia_nome_versao_id'),
 (N'identidade.linkage_run',N'frequencia_nome_versao_id'),
 (N'identidade.linkage_run',N'frequencia_nome_versao_codigo'),
 (N'identidade.linkage_run',N'frequencia_nome_conteudo_sha256'),
 (N'identidade.linkage_resultado',N'resultado_publicacao'),
 (N'identidade.linkage_resultado',N'pessoa_uuid_publicado'),
 (N'identidade.linkage_resultado',N'status_publicacao'),
 (N'identidade.linkage_resultado',N'motivo_publicacao'),
 (N'identidade.linkage_resultado',N'pessoa_origem_id_publicado'),
 (N'identidade.linkage_resultado',N'progressiva_versao'),
 (N'identidade.linkage_resultado',N'politica_publicacao_versao'),
 (N'identidade.linkage_resultado',N'universo_referencia'),
 (N'identidade.linkage_resultado',N'publicado_em'),
 (N'identidade.pessoa_origem_progressiva_evento',N'linkage_run_id'),
 (N'gold.pessoa',N'estado_identidade'),
 (N'gold.pessoa',N'completude_nucleo'),
 (N'gold.pessoa',N'nome_publicacao_normalizado'),
 (N'gold.pessoa',N'nome_publicacao_metodo_versao'),
 (N'gold.pessoa',N'nome_publicacao_normalizacao_versao'),
 (N'controle.runtime_componente',N'configuration_bundle_version'),
 (N'controle.runtime_componente',N'solution_schema_expected');

INSERT @missing(item)
SELECT CONCAT(N'COLUMN:',tabela,N'.',coluna)
FROM @required_columns
WHERE COL_LENGTH(tabela,coluna) IS NULL;

INSERT @missing(item)
SELECT CONCAT(N'VIEW_COLUMN:',v.objeto,N'.',v.coluna)
FROM (VALUES
 (N'serving.v_pessoa',N'nome_referencia'),
 (N'serving.v_pessoa',N'nome_referencia_tipo'),
 (N'serving.v_pessoa',N'nome_referencia_fonte_tipo'),
 (N'serving.v_pessoa',N'nome_referencia_fonte_observacao_id'),
 (N'serving.v_pessoa',N'nome_referencia_fonte_gestor_id'),
 (N'serving.v_pessoa',N'nome_referencia_source_record_id'),
 (N'serving.v_pessoa',N'nome_referencia_em')
) v(objeto,coluna)
WHERE NOT EXISTS(
    SELECT 1
    FROM sys.columns c
    WHERE c.object_id=OBJECT_ID(v.objeto,N'V')
      AND c.name=v.coluna
);

IF EXISTS(
    SELECT 1
    FROM sys.columns
    WHERE object_id=OBJECT_ID(N'serving.v_pessoa',N'V')
      AND name=N'nome_completo')
    INSERT @missing(item) VALUES(N'VIEW_COLUMN_FORBIDDEN:serving.v_pessoa.nome_completo');

-- O núcleo cadastral é progressivo. Obrigatoriedade pertence ao schema da fonte,
-- não à representação canônica; Silver e Gold precisam aceitar ausência.
INSERT @missing(item)
SELECT CONCAT(N'NULLABILITY:',v.tabela,N'.',v.coluna)
FROM (VALUES
 (N'silver.pessoa_observacao',N'nome_completo'),
 (N'silver.pessoa_observacao',N'nome_cmp'),
 (N'silver.pessoa_observacao',N'data_nascimento'),
 (N'silver.pessoa_observacao',N'nome_mae'),
 (N'silver.pessoa_observacao',N'nome_mae_cmp'),
 (N'gold.pessoa',N'nome_completo'),
 (N'gold.pessoa',N'data_nascimento'),
 (N'gold.pessoa',N'nome_mae')
) v(tabela,coluna)
WHERE NOT EXISTS(
    SELECT 1
    FROM sys.columns c
    WHERE c.object_id=OBJECT_ID(v.tabela,N'U')
      AND c.name=v.coluna
      AND c.is_nullable=1
);

-- A margem V6 precisa aceitar log-odds; decimal(18,8) e as constraints abaixo
-- distinguem o schema novo do contrato legado limitado ao posterior.
IF NOT EXISTS(
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.object_id=OBJECT_ID(N'identidade.linkage_resultado',N'U')
      AND c.name=N'margem'
      AND t.name=N'decimal'
      AND c.precision=18
      AND c.scale=8
      AND c.is_nullable=1
)
    INSERT @missing(item) VALUES(N'COLUMN_SHAPE:identidade.linkage_resultado.margem decimal(18,8) NULL');

IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'identidade.linkage_resultado') AND name=N'ck_linkage_resultado_scores')
    INSERT @missing(item) VALUES(N'CHECK:identidade.linkage_resultado.ck_linkage_resultado_scores');
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'identidade.linkage_resultado') AND name=N'ck_linkage_resultado_candidatos_distintos')
    INSERT @missing(item) VALUES(N'CHECK:identidade.linkage_resultado.ck_linkage_resultado_candidatos_distintos');
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'identidade.linkage_resultado') AND name=N'ck_linkage_resultado_publicacao')
    INSERT @missing(item) VALUES(N'CHECK:identidade.linkage_resultado.ck_linkage_resultado_publicacao');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'identidade.pessoa_origem_progressiva_evento') AND name=N'UX_progressiva_evento_origem_linkage_run')
    INSERT @missing(item) VALUES(N'INDEX:identidade.pessoa_origem_progressiva_evento.UX_progressiva_evento_origem_linkage_run');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'auditoria.decisao_identidade_evento') AND name=N'UX_decisao_identidade_caso_evento')
    INSERT @missing(item) VALUES(N'INDEX:auditoria.decisao_identidade_evento.UX_decisao_identidade_caso_evento');
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'gold.pessoa') AND name=N'ck_gold_pessoa_nome_publicacao_completo')
    INSERT @missing(item) VALUES(N'CHECK:gold.pessoa.ck_gold_pessoa_nome_publicacao_completo');
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'gold.pessoa') AND name=N'ck_gold_pessoa_estado_identidade')
    INSERT @missing(item) VALUES(N'CHECK:gold.pessoa.ck_gold_pessoa_estado_identidade');
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'gold.pessoa') AND name=N'ck_gold_pessoa_completude_nucleo')
    INSERT @missing(item) VALUES(N'CHECK:gold.pessoa.ck_gold_pessoa_completude_nucleo');

IF EXISTS(SELECT 1 FROM @missing)
BEGIN
    DECLARE @lista NVARCHAR(MAX)=(SELECT STRING_AGG(item,N', ') WITHIN GROUP(ORDER BY item) FROM @missing);
    DECLARE @mensagem NVARCHAR(2048)=CONCAT(N'Schema v3.70 incompleto. Contratos ausentes/incompatíveis: ',@lista);
    THROW 51700, @mensagem, 1;
END;

IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema')
    EXEC sys.sp_updateextendedproperty @name=N'Jornada.SolutionSchema', @value=N'3.70';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'Jornada.SolutionSchema', @value=N'3.70';

IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.BaseNormativa')
    EXEC sys.sp_updateextendedproperty @name=N'Jornada.BaseNormativa', @value=N'3.62';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'Jornada.BaseNormativa', @value=N'3.62';
GO

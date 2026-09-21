SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
 Runtime v4 - cutover da Pessoa de origem
 ----------------------------------------
 Pré-requisitos: 20260913_Base_Pessoa_Origem.sql.
 Depois deste ponto toda LINHA EXISTENTE em silver.pessoa_origem possui Base explícita.
 Isto não obriga toda observação de Pessoa a possuir código/origem: quando a fonte não
 possui código local, silver.pessoa_observacao.pessoa_origem_id permanece NULL e nenhuma
 linha artificial é criada em silver.pessoa_origem. Para as origens que existem, a
 unicidade deixa de ser (sistema,codigo) e passa a ser (base,codigo).
*/

IF OBJECT_ID('ref.base_pessoa_origem','U') IS NULL
   OR OBJECT_ID('ref.sistema_origem_base_pessoa','U') IS NULL
   OR COL_LENGTH('silver.pessoa_origem','base_pessoa_origem_id') IS NULL
    THROW 51290,'Cutover v4 exige a migração Base_Pessoa_Origem aplicada.',1;
GO

UPDATE po
   SET base_pessoa_origem_id=sb.base_pessoa_origem_id
FROM silver.pessoa_origem po
JOIN ref.sistema_origem_base_pessoa sb
  ON sb.sistema_origem_id=po.sistema_origem_id
 AND sb.padrao=1
 AND sb.ativo=1
WHERE po.base_pessoa_origem_id IS NULL;
GO

IF EXISTS(SELECT 1 FROM silver.pessoa_origem WHERE base_pessoa_origem_id IS NULL)
    THROW 51291,'Cutover v4 encontrou Pessoa de origem sem Base autorizada.',1;
GO

IF EXISTS(
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id=OBJECT_ID('silver.pessoa_origem')
      AND name='uq_pessoa_origem')
    ALTER TABLE silver.pessoa_origem DROP CONSTRAINT uq_pessoa_origem;
GO

/* O índice filtrado preparatório depende da coluna ainda anulável. Ele precisa sair
   antes do ALTER COLUMN e é recriado abaixo já como unicidade definitiva v4. */
IF EXISTS(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID('silver.pessoa_origem')
      AND name='uq_pessoa_origem_base_codigo')
    DROP INDEX uq_pessoa_origem_base_codigo ON silver.pessoa_origem;
GO

ALTER TABLE silver.pessoa_origem
    ALTER COLUMN base_pessoa_origem_id BIGINT NOT NULL;
GO

CREATE UNIQUE INDEX uq_pessoa_origem_base_codigo
    ON silver.pessoa_origem(base_pessoa_origem_id,codigo_pessoa_origem);
GO


/* UUID_JORNADA é retroalimentação interna: vincula a observação a uma Pessoa já
   existente, sem criar identity_map externo e sem score/modelo probabilístico. */
IF EXISTS(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID('identidade.vinculo_fonte')
      AND name='ck_vinculo_metodo')
    ALTER TABLE identidade.vinculo_fonte DROP CONSTRAINT ck_vinculo_metodo;
GO
ALTER TABLE identidade.vinculo_fonte WITH CHECK ADD CONSTRAINT ck_vinculo_metodo
 CHECK(metodo_resolucao IN(
    'CPF_DETERMINISTICO','NIS_DETERMINISTICO','UUID_JORNADA_RETROALIMENTACAO',
    'PENDENTE_PROBABILISTICO','LINKAGE_PROBABILISTICO',
    'CORRECAO_GOVERNADA','CONFLITO_GOVERNADO'));
GO

IF EXISTS(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID('identidade.vinculo_fonte')
      AND name='ck_vinculo_modelo')
    ALTER TABLE identidade.vinculo_fonte DROP CONSTRAINT ck_vinculo_modelo;
GO
ALTER TABLE identidade.vinculo_fonte WITH CHECK ADD CONSTRAINT ck_vinculo_modelo CHECK(
    (metodo_resolucao='CPF_DETERMINISTICO' AND score IS NULL AND modelo_id IS NULL) OR
    (metodo_resolucao='NIS_DETERMINISTICO' AND score IS NULL AND modelo_id IS NULL) OR
    (metodo_resolucao='UUID_JORNADA_RETROALIMENTACAO' AND score IS NULL AND modelo_id IS NULL) OR
    (metodo_resolucao='PENDENTE_PROBABILISTICO' AND score IS NULL AND modelo_id IS NULL AND pessoa_uuid IS NULL) OR
    (metodo_resolucao='LINKAGE_PROBABILISTICO' AND score IS NOT NULL AND modelo_id IS NOT NULL) OR
    (metodo_resolucao='CORRECAO_GOVERNADA' AND score IS NULL AND modelo_id IS NULL AND pessoa_uuid IS NOT NULL) OR
    (metodo_resolucao='CONFLITO_GOVERNADO' AND score IS NULL AND modelo_id IS NULL AND pessoa_uuid IS NULL));
GO

/* BI identidade v4: classificação de CPF sem exposição do valor. */
CREATE OR ALTER VIEW serving.v_bi_linkage AS
SELECT po.pessoa_observacao_id,
       g.codigo gestor,
       so.codigo sistema_origem,
       bp.codigo base_pessoa_origem,
       po.source_as_of,
       vc.status,
       vc.metodo_resolucao,
       CASE WHEN vc.metodo_resolucao='LINKAGE_PROBABILISTICO' THEN vc.score END score,
       CASE WHEN vc.pessoa_uuid IS NULL THEN 0 ELSE 1 END possui_uuid,
       CASE WHEN vc.metodo_resolucao='CPF_DETERMINISTICO' AND vc.status='RESOLVIDO' THEN 1 ELSE 0 END resolvido_cpf,
       CASE WHEN vc.metodo_resolucao='LINKAGE_PROBABILISTICO' AND vc.status='RESOLVIDO' THEN 1 ELSE 0 END resolvido_fallback,
       CASE WHEN vc.status='NAO_RESOLVIDO' THEN 1 ELSE 0 END nao_resolvido,
       CASE WHEN vc.status='CONFLITO' THEN 1 ELSE 0 END conflito,
       CASE WHEN lrres.motivo IN(
             'SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO',
             'SEM_CANDIDATO_NOS_BLOCOS_NASCIMENTO_COMPONENTE',
             'SEM_CANDIDATO_NO_RULESET_BLOCKING') THEN 1 ELSE 0 END sem_candidato_no_bloco,
       lrres.motivo motivo_linkage,
       CASE WHEN po.cpf IS NULL THEN 0 ELSE 1 END cpf_preenchido,
       po.cpf_ausente_motivo,
       CASE
         WHEN po.cpf IS NULL THEN 'CPF_AUSENTE'
         WHEN vc.motivo='CPF_ESTRUTURALMENTE_INVALIDO' THEN 'CPF_ESTRUTURALMENTE_INVALIDO'
         WHEN im.estado='EM_CONFLITO'
           OR (vc.status='CONFLITO' AND vc.metodo_resolucao='CPF_DETERMINISTICO')
           THEN 'CPF_CONFLITO_DETERMINISTICO'
         WHEN os.qtd_cpfs_mesma_origem>1 THEN 'JUNCAO_CPFS_MESMA_ORIGEM'
         WHEN bs.qtd_codigos_mesma_base_ancora>1 THEN 'DUPLICACAO_CODIGO_MESMA_ANCORA'
         ELSE 'CPF_SEM_PROBLEMA_DETECTADO'
       END cpf_classificacao,
       CAST(CASE
         WHEN po.cpf IS NULL
           OR vc.motivo='CPF_ESTRUTURALMENTE_INVALIDO'
           OR os.qtd_cpfs_mesma_origem>1
           OR bs.qtd_codigos_mesma_base_ancora>1
           OR im.estado='EM_CONFLITO'
           OR (vc.status='CONFLITO' AND vc.metodo_resolucao='CPF_DETERMINISTICO')
         THEN 1 ELSE 0 END AS INT) cpf_problema,
       CASE
         WHEN po.cpf IS NULL THEN COALESCE(po.cpf_ausente_motivo,'SEM_CPF')
         WHEN vc.motivo='CPF_ESTRUTURALMENTE_INVALIDO' THEN vc.motivo
         WHEN im.estado='EM_CONFLITO' THEN COALESCE(im.estado_motivo,'CPF_EM_CONFLITO_IDENTIDADE')
         WHEN vc.status='CONFLITO' AND vc.metodo_resolucao='CPF_DETERMINISTICO'
           THEN COALESCE(vc.motivo,'CPF_CONFLITO_DETERMINISTICO')
         WHEN os.qtd_cpfs_mesma_origem>1 THEN 'MAIS_DE_UM_CPF_NA_MESMA_ORIGEM'
         WHEN bs.qtd_codigos_mesma_base_ancora>1 THEN 'MESMO_CPF_EM_MULTIPLOS_CODIGOS_DA_MESMA_BASE'
       END cpf_motivo_qualidade,
       im.estado cpf_estado_identificador,
       im.estado_motivo cpf_estado_motivo,
       CAST(CASE WHEN dg.divergencia_id IS NULL THEN 0 ELSE 1 END AS INT) divergencia_identidade_aberta,
       dg.motivo divergencia_identidade_motivo,
       CASE WHEN DATEPART(DAY,po.data_nascimento)=1 AND DATEPART(MONTH,po.data_nascimento)=1 THEN 1 ELSE 0 END nascimento_0101,
       CASE WHEN pg.subprefeitura_id IS NULL OR pg.distrito_id IS NULL THEN 0 ELSE 1 END geografia_preenchida,
       ml.versao modelo_fallback_versao,
       vc.linkage_run_id,
       lr.tipo_run,
       lr.iniciado_em linkage_run_iniciado_em,
       lr.finalizado_em linkage_run_finalizado_em,
       ptl.valor t_linkage,
       lr.status linkage_run_status,
       COALESCE(sp.nome,'SEM_REFERENCIA_TERRITORIAL') subprefeitura,
       COALESCE(d.nome,'SEM_REFERENCIA_TERRITORIAL') distrito,
       pg.natureza_referencia natureza_referencia_territorial
FROM silver.pessoa_observacao po
JOIN ref.gestor g ON g.gestor_id=po.gestor_id
JOIN ingestao.lote il ON il.lote_id=po.lote_id
JOIN ingestao.entrega ie ON ie.entrega_id=il.entrega_id
JOIN ref.sistema_origem so ON so.sistema_origem_id=ie.sistema_origem_id
LEFT JOIN silver.pessoa_origem pori ON pori.pessoa_origem_id=po.pessoa_origem_id
LEFT JOIN ref.base_pessoa_origem bp ON bp.base_pessoa_origem_id=pori.base_pessoa_origem_id
LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN identidade.modelo_linkage ml ON ml.modelo_id=vc.modelo_id
LEFT JOIN identidade.linkage_run lr ON lr.linkage_run_id=vc.linkage_run_id
LEFT JOIN identidade.parametro_linkage ptl ON ptl.modelo_id=vc.modelo_id AND ptl.nome='T_LINKAGE'
LEFT JOIN identidade.linkage_resultado lrres
  ON lrres.linkage_run_id=vc.linkage_run_id
 AND lrres.pessoa_observacao_id=po.pessoa_observacao_id
OUTER APPLY(
  SELECT TOP(1) m.estado,m.estado_motivo
  FROM identidade.identity_map m
  WHERE po.cpf IS NOT NULL
    AND m.tipo='CPF'
    AND m.identificador=po.cpf
    AND m.vigencia_fim IS NULL
  ORDER BY m.vigencia_inicio DESC,m.identity_map_id DESC
) im
OUTER APPLY(
  SELECT COUNT(DISTINCT po2.pessoa_origem_id) qtd_codigos_mesma_base_ancora
  FROM silver.pessoa_observacao po2
  JOIN silver.pessoa_origem o2 ON o2.pessoa_origem_id=po2.pessoa_origem_id
  WHERE po.cpf IS NOT NULL
    AND pori.base_pessoa_origem_id IS NOT NULL
    AND po2.cpf=po.cpf
    AND o2.base_pessoa_origem_id=pori.base_pessoa_origem_id
) bs
OUTER APPLY(
  SELECT COUNT(DISTINCT po3.cpf) qtd_cpfs_mesma_origem
  FROM silver.pessoa_observacao po3
  WHERE po.pessoa_origem_id IS NOT NULL
    AND po3.pessoa_origem_id=po.pessoa_origem_id
    AND po3.cpf IS NOT NULL
) os
OUTER APPLY(
  SELECT TOP(1) d0.divergencia_id,d0.motivo
  FROM qualidade.divergencia_gestor d0
  WHERE d0.pessoa_observacao_id=po.pessoa_observacao_id
    AND d0.tipo='DIVERGENCIA_IDENTIDADE'
    AND d0.status='ABERTA'
  ORDER BY d0.aberta_em DESC,d0.divergencia_id DESC
) dg
LEFT JOIN silver.v_pessoa_referencia_territorial pg
  ON pg.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN ref.subprefeitura sp ON sp.subprefeitura_id=pg.subprefeitura_id
LEFT JOIN ref.distrito d ON d.distrito_id=pg.distrito_id;
GO

-- A superfície de QC de identidade reutiliza exatamente a classificação exposta ao BI.
-- Não contém o CPF em claro.
CREATE OR ALTER VIEW serving.v_bi_qualidade_identidade_origem AS
SELECT l.* FROM serving.v_bi_linkage l;
GO

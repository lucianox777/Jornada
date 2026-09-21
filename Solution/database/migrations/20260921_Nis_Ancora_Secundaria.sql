SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  NIS/PIS/PASEP/NIT como âncora determinística secundária governada
  -----------------------------------------------------------------
  - pessoa_uuid continua sendo a identidade canônica interna;
  - CPF permanece a âncora externa principal;
  - tipo canônico NIS preserva a origem em namespace NIS|PIS|PASEP|NIT;
  - somente NIS estruturalmente válido + COMPROVADO é elegível à resolução;
  - um mesmo número social ativo aponta para no máximo uma Pessoa corrente;
  - uma Pessoa pode possuir vários números sociais;
  - conflito com CPF/UUID prioritário nunca transfere silenciosamente identidade.
*/

IF OBJECT_ID(N'ref.tipo_identificador_pessoa',N'U') IS NULL
   OR OBJECT_ID(N'silver.pessoa_identificador_observacao',N'U') IS NULL
   OR OBJECT_ID(N'identidade.identity_map',N'U') IS NULL
   OR OBJECT_ID(N'identidade.vinculo_fonte',N'U') IS NULL
    THROW 51960,'Pré-requisitos de identidade múltipla/NIS não instalados.',1;
GO

IF OBJECT_ID(N'identidade.fn_nis_estrutural_valido',N'FN') IS NULL
EXEC(N'
CREATE FUNCTION identidade.fn_nis_estrutural_valido(@nis CHAR(11))
RETURNS BIT
AS
BEGIN
    IF @nis IS NULL OR LEN(@nis)<>11 OR @nis COLLATE Latin1_General_100_BIN2 LIKE ''%[^0-9]%''
        RETURN 0;
    IF @nis=REPLICATE(LEFT(@nis,1),11)
        RETURN 0;

    DECLARE @weights TABLE(pos INT PRIMARY KEY,peso INT NOT NULL);
    INSERT @weights(pos,peso) VALUES
      (1,3),(2,2),(3,9),(4,8),(5,7),(6,6),(7,5),(8,4),(9,3),(10,2);

    DECLARE @sum INT=0,@dv INT;
    SELECT @sum=SUM(CONVERT(INT,SUBSTRING(@nis,pos,1))*peso) FROM @weights;
    SET @dv=11-(@sum%11);
    IF @dv IN(10,11) SET @dv=0;

    RETURN CASE WHEN @dv=CONVERT(INT,SUBSTRING(@nis,11,1)) THEN 1 ELSE 0 END;
END;');
GO

MERGE ref.tipo_identificador_pessoa AS t
USING (VALUES
    (N'NIS',N'Inscrição social CNIS (NIS/PIS/PASEP/NIT)',N'NIS_PIS_PASEP_NIT',N'NIS_BR_11_V1',N'CONDICIONAL',N'EXTERNO_HIERARQUICO',CAST(90 AS SMALLINT))
) AS s(tipo_identificador_codigo,nome,namespace_padrao,formato_codigo,elegibilidade_deterministica,papel_resolucao,prioridade_resolucao)
ON t.tipo_identificador_codigo=s.tipo_identificador_codigo
WHEN NOT MATCHED THEN
    INSERT(tipo_identificador_codigo,nome,exige_namespace,formato_codigo,elegibilidade_deterministica,papel_resolucao,prioridade_resolucao)
    VALUES(s.tipo_identificador_codigo,s.nome,1,s.formato_codigo,s.elegibilidade_deterministica,s.papel_resolucao,s.prioridade_resolucao)
WHEN MATCHED THEN UPDATE SET
    nome=s.nome,
    exige_namespace=1,
    formato_codigo=s.formato_codigo,
    elegibilidade_deterministica=s.elegibilidade_deterministica,
    papel_resolucao=s.papel_resolucao,
    prioridade_resolucao=s.prioridade_resolucao,
    ativo=1;
GO

IF EXISTS(
    SELECT 1
    FROM ref.tipo_identificador_pessoa cpf
    JOIN ref.tipo_identificador_pessoa nis ON nis.tipo_identificador_codigo=N'NIS'
    WHERE cpf.tipo_identificador_codigo=N'CPF'
      AND (cpf.papel_resolucao<>N'EXTERNO_HIERARQUICO'
           OR nis.papel_resolucao<>N'EXTERNO_HIERARQUICO'
           OR cpf.prioridade_resolucao<=nis.prioridade_resolucao))
    THROW 51961,'CPF deve permanecer acima de NIS na hierarquia determinística.',1;
GO

IF OBJECT_ID(N'silver.CK_pessoa_identificador_nis_namespace',N'C') IS NOT NULL
    ALTER TABLE silver.pessoa_identificador_observacao
      DROP CONSTRAINT CK_pessoa_identificador_nis_namespace;
GO
ALTER TABLE silver.pessoa_identificador_observacao WITH CHECK
ADD CONSTRAINT CK_pessoa_identificador_nis_namespace CHECK(
    tipo_identificador_codigo<>N'NIS'
    OR namespace_codigo IN(N'NIS',N'PIS',N'PASEP',N'NIT'));
GO

IF EXISTS(
    SELECT 1
    FROM identidade.identity_map
    WHERE tipo=N'NIS'
      AND identidade.fn_nis_estrutural_valido(CONVERT(CHAR(11),identificador))=0)
    THROW 51962,'Histórico NIS inválido exige reconciliação antes da ativação da âncora secundária.',1;
GO

IF OBJECT_ID(N'identidade.CK_identity_map_nis_valido',N'C') IS NULL
BEGIN
    ALTER TABLE identidade.identity_map WITH CHECK
    ADD CONSTRAINT CK_identity_map_nis_valido CHECK(
        tipo<>N'NIS'
        OR (
            LEN(identificador)=11
            AND identidade.fn_nis_estrutural_valido(CONVERT(CHAR(11),identificador))=1
        ));
END;
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'identidade.identity_map')
      AND name=N'UX_identidade_identity_map_nis_ativo')
    CREATE UNIQUE INDEX UX_identidade_identity_map_nis_ativo
      ON identidade.identity_map(tipo,identificador)
      WHERE tipo=N'NIS' AND vigencia_fim IS NULL;
GO

IF EXISTS(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'identidade.identity_map')
      AND name=N'ck_identity_map_metodo')
    ALTER TABLE identidade.identity_map DROP CONSTRAINT ck_identity_map_metodo;
GO
ALTER TABLE identidade.identity_map WITH CHECK
ADD CONSTRAINT ck_identity_map_metodo CHECK(
    metodo_resolucao IN(
      N'CPF_DETERMINISTICO',
      N'NIS_DETERMINISTICO',
      N'LINKAGE_PROBABILISTICO',
      N'CORRECAO_GOVERNADA'));
GO

IF EXISTS(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'identidade.identity_map')
      AND name=N'ck_identity_map_modelo')
    ALTER TABLE identidade.identity_map DROP CONSTRAINT ck_identity_map_modelo;
GO
ALTER TABLE identidade.identity_map WITH CHECK
ADD CONSTRAINT ck_identity_map_modelo CHECK(
    (metodo_resolucao IN(N'CPF_DETERMINISTICO',N'NIS_DETERMINISTICO',N'CORRECAO_GOVERNADA')
      AND score IS NULL AND modelo_id IS NULL)
    OR
    (metodo_resolucao=N'LINKAGE_PROBABILISTICO' AND modelo_id IS NOT NULL));
GO

IF EXISTS(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'identidade.vinculo_fonte')
      AND name=N'ck_vinculo_metodo')
    ALTER TABLE identidade.vinculo_fonte DROP CONSTRAINT ck_vinculo_metodo;
GO
ALTER TABLE identidade.vinculo_fonte WITH CHECK
ADD CONSTRAINT ck_vinculo_metodo CHECK(
    metodo_resolucao IN(
      N'CPF_DETERMINISTICO',
      N'NIS_DETERMINISTICO',
      N'UUID_JORNADA_RETROALIMENTACAO',
      N'PENDENTE_PROBABILISTICO',
      N'LINKAGE_PROBABILISTICO',
      N'CORRECAO_GOVERNADA',
      N'CONFLITO_GOVERNADO'));
GO

IF EXISTS(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'identidade.vinculo_fonte')
      AND name=N'ck_vinculo_modelo')
    ALTER TABLE identidade.vinculo_fonte DROP CONSTRAINT ck_vinculo_modelo;
GO
ALTER TABLE identidade.vinculo_fonte WITH CHECK
ADD CONSTRAINT ck_vinculo_modelo CHECK(
    (metodo_resolucao=N'CPF_DETERMINISTICO' AND score IS NULL AND modelo_id IS NULL) OR
    (metodo_resolucao=N'NIS_DETERMINISTICO' AND score IS NULL AND modelo_id IS NULL) OR
    (metodo_resolucao=N'UUID_JORNADA_RETROALIMENTACAO' AND score IS NULL AND modelo_id IS NULL) OR
    (metodo_resolucao=N'PENDENTE_PROBABILISTICO' AND score IS NULL AND modelo_id IS NULL AND pessoa_uuid IS NULL) OR
    (metodo_resolucao=N'LINKAGE_PROBABILISTICO' AND score IS NOT NULL AND modelo_id IS NOT NULL) OR
    (metodo_resolucao=N'CORRECAO_GOVERNADA' AND score IS NULL AND modelo_id IS NULL AND pessoa_uuid IS NOT NULL) OR
    (metodo_resolucao=N'CONFLITO_GOVERNADO' AND score IS NULL AND modelo_id IS NULL AND pessoa_uuid IS NULL));
GO

CREATE OR ALTER VIEW serving.v_bi_nis_qualidade AS
WITH n AS(
    SELECT
      i.pessoa_observacao_id,
      COUNT_BIG(*) qtd_nis,
      SUM(CASE WHEN i.status_validacao=N'VALIDO' THEN 1 ELSE 0 END) qtd_validos_comprovados,
      SUM(CASE WHEN i.status_validacao=N'INVALIDO' THEN 1 ELSE 0 END) qtd_invalidos,
      SUM(CASE WHEN i.status_validacao=N'NAO_VALIDADO' THEN 1 ELSE 0 END) qtd_nao_validados,
      SUM(CASE WHEN m.estado=N'EM_CONFLITO' THEN 1 ELSE 0 END) qtd_mapas_conflitantes
    FROM silver.pessoa_identificador_observacao i
    LEFT JOIN identidade.identity_map m
      ON m.tipo=N'NIS'
     AND m.identificador=i.valor_normalizado
     AND m.vigencia_fim IS NULL
    WHERE i.tipo_identificador_codigo=N'NIS'
    GROUP BY i.pessoa_observacao_id
)
SELECT
  po.pessoa_observacao_id,
  g.codigo gestor,
  so.codigo sistema_origem,
  CAST(COALESCE(n.qtd_nis,0) AS BIGINT) qtd_nis,
  CAST(COALESCE(n.qtd_validos_comprovados,0) AS BIGINT) qtd_nis_validos_comprovados,
  CAST(COALESCE(n.qtd_invalidos,0) AS BIGINT) qtd_nis_invalidos,
  CAST(COALESCE(n.qtd_nao_validados,0) AS BIGINT) qtd_nis_nao_validados,
  CAST(COALESCE(n.qtd_mapas_conflitantes,0) AS BIGINT) qtd_nis_conflitantes,
  CASE
    WHEN n.pessoa_observacao_id IS NULL THEN N'SEM_NIS'
    WHEN n.qtd_mapas_conflitantes>0 THEN N'NIS_CONFLITO_IDENTIDADE'
    WHEN n.qtd_invalidos>0 THEN N'NIS_ESTRUTURALMENTE_INVALIDO'
    WHEN n.qtd_validos_comprovados>0 THEN N'NIS_VALIDO_COMPROVADO'
    ELSE N'NIS_DECLARADO_NAO_COMPROVADO'
  END nis_classificacao,
  CAST(CASE
    WHEN COALESCE(n.qtd_mapas_conflitantes,0)>0 OR COALESCE(n.qtd_invalidos,0)>0
    THEN 1 ELSE 0 END AS INT) nis_problema
FROM silver.pessoa_observacao po
JOIN ref.gestor g ON g.gestor_id=po.gestor_id
JOIN ingestao.lote l ON l.lote_id=po.lote_id
JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id
JOIN ref.sistema_origem so ON so.sistema_origem_id=e.sistema_origem_id
LEFT JOIN n ON n.pessoa_observacao_id=po.pessoa_observacao_id;
GO

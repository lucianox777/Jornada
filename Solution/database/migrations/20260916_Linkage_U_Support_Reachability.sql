SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Reachability estrutural dos estados semânticos de nascimento no universo candidato
-- do ruleset congelado. Cada passe exige igualdade nos componentes de nascimento que
-- aparecem em seus campos; campos não relacionados a nascimento não restringem o mask.
-- Um estado é alcançável quando pelo menos uma configuração possível desse estado
-- satisfaz os componentes exigidos por algum passe.
CREATE OR ALTER FUNCTION identidade.fn_linkage_birth_semantic_reachability(
    @modelo_id UNIQUEIDENTIFIER)
RETURNS TABLE
AS
RETURN
(
    WITH pass_masks AS (
        SELECT
            rp.ruleset_id,
            rp.passe_ordem,
            CONVERT(INT,SUM(CASE rc.atributo
                WHEN N'birth_day' THEN 1
                WHEN N'birth_month' THEN 2
                WHEN N'birth_year' THEN 4
                ELSE 0
            END)) AS required_mask
        FROM identidade.linkage_ruleset r
        JOIN identidade.linkage_ruleset_passe rp
          ON rp.ruleset_id=r.ruleset_id
        LEFT JOIN identidade.linkage_ruleset_passe_campo rc
          ON rc.ruleset_id=rp.ruleset_id
         AND rc.passe_ordem=rp.passe_ordem
        WHERE r.modelo_id=@modelo_id
        GROUP BY rp.ruleset_id,rp.passe_ordem
    ), semantic_masks AS (
        SELECT estado,mask_value
        FROM (VALUES
            (N'EXACT',7),
            (N'DAY_MONTH_SWAP',4),
            (N'CENTURY_SHIFT',3),
            (N'ONE_DIGIT_ERROR',3),
            (N'ONE_DIGIT_ERROR',5),
            (N'ONE_DIGIT_ERROR',6),
            (N'TWO_DIGIT_ERROR',1),
            (N'TWO_DIGIT_ERROR',2),
            (N'TWO_DIGIT_ERROR',4),
            (N'PARTIAL_COMPONENT_AGREEMENT',1),
            (N'PARTIAL_COMPONENT_AGREEMENT',2),
            (N'PARTIAL_COMPONENT_AGREEMENT',3),
            (N'PARTIAL_COMPONENT_AGREEMENT',4),
            (N'PARTIAL_COMPONENT_AGREEMENT',5),
            (N'PARTIAL_COMPONENT_AGREEMENT',6),
            (N'OTHER_DISAGREEMENT',0)
        ) v(estado,mask_value)
    ), states AS (
        SELECT DISTINCT estado FROM semantic_masks
    )
    SELECT
        s.estado,
        CONVERT(BIT,CASE WHEN EXISTS(
            SELECT 1
            FROM semantic_masks sm
            CROSS JOIN pass_masks pm
            WHERE sm.estado=s.estado
              AND (sm.mask_value & pm.required_mask)=pm.required_mask
        ) THEN 1 ELSE 0 END) AS alcancavel
    FROM states s
);
GO

-- Reaplica o contrato de promoção acrescentando suficiência empírica mínima de u:
-- todo suporte semântico deve existir, e estados estruturalmente alcançáveis pelo
-- ruleset não podem depender exclusivamente de smoothing/prior com suporte bruto zero.
CREATE OR ALTER TRIGGER identidade.tr_modelo_linkage_promotion_contract
ON identidade.modelo_linkage
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        WHERE i.status IN ('VALIDADO','ATIVO')
          AND i.algoritmo_versao IN (
              'FELLEGI_SUNTER_SEMANTIC_BIRTH_V5',
              'FELLEGI_SUNTER_DECISION_EVIDENCE_V6')
          AND (
              NOT EXISTS (
                  SELECT 1
                  FROM identidade.parametro_linkage p
                  WHERE p.modelo_id=i.modelo_id
                    AND p.nome='SCORING_BIRTH_SEMANTIC_EVIDENCE_V5'
                    AND p.valor>=1)
              OR EXISTS (
                  SELECT 1
                  FROM (VALUES
                      ('M_NASCIMENTO_SEMANTICO_EXACT'),
                      ('M_NASCIMENTO_SEMANTICO_DAY_MONTH_SWAP'),
                      ('M_NASCIMENTO_SEMANTICO_CENTURY_SHIFT'),
                      ('M_NASCIMENTO_SEMANTICO_ONE_DIGIT_ERROR'),
                      ('M_NASCIMENTO_SEMANTICO_TWO_DIGIT_ERROR'),
                      ('M_NASCIMENTO_SEMANTICO_PARTIAL_COMPONENT_AGREEMENT'),
                      ('M_NASCIMENTO_SEMANTICO_OTHER_DISAGREEMENT'),
                      ('U_NASCIMENTO_SEMANTICO_EXACT'),
                      ('U_NASCIMENTO_SEMANTICO_DAY_MONTH_SWAP'),
                      ('U_NASCIMENTO_SEMANTICO_CENTURY_SHIFT'),
                      ('U_NASCIMENTO_SEMANTICO_ONE_DIGIT_ERROR'),
                      ('U_NASCIMENTO_SEMANTICO_TWO_DIGIT_ERROR'),
                      ('U_NASCIMENTO_SEMANTICO_PARTIAL_COMPONENT_AGREEMENT'),
                      ('U_NASCIMENTO_SEMANTICO_OTHER_DISAGREEMENT')
                  ) req(nome)
                  WHERE NOT EXISTS (
                      SELECT 1
                      FROM identidade.parametro_linkage p
                      WHERE p.modelo_id=i.modelo_id
                        AND p.nome=req.nome
                        AND p.valor>0 AND p.valor<1))
          )
    )
        THROW 51030, 'Promoção recusada: proveniência V5/V6 exige nascimento semântico completo e habilitado.', 1;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        CROSS APPLY identidade.fn_linkage_birth_semantic_reachability(i.modelo_id) r
        WHERE i.status IN ('VALIDADO','ATIVO')
          AND i.algoritmo_versao IN (
              'FELLEGI_SUNTER_SEMANTIC_BIRTH_V5',
              'FELLEGI_SUNTER_DECISION_EVIDENCE_V6')
          AND (
              NOT EXISTS (
                  SELECT 1
                  FROM identidade.parametro_linkage p
                  WHERE p.modelo_id=i.modelo_id
                    AND p.nome=N'SUPPORT_U_NASCIMENTO_SEMANTICO_'+r.estado)
              OR (
                  r.alcancavel=1
                  AND NOT EXISTS (
                      SELECT 1
                      FROM identidade.parametro_linkage p
                      WHERE p.modelo_id=i.modelo_id
                        AND p.nome=N'SUPPORT_U_NASCIMENTO_SEMANTICO_'+r.estado
                        AND p.valor>0)
              )
          )
    )
        THROW 51032, 'Promoção recusada: suporte u semântico ausente ou zero em estado alcançável pelo ruleset.', 1;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        WHERE i.status IN ('VALIDADO','ATIVO')
          AND i.algoritmo_versao='FELLEGI_SUNTER_DECISION_EVIDENCE_V6'
          AND (
              NOT EXISTS (
                  SELECT 1
                  FROM identidade.parametro_linkage p
                  WHERE p.modelo_id=i.modelo_id
                    AND p.nome='SCORING_DECISION_EVIDENCE_V6'
                    AND p.valor>=1)
              OR NOT EXISTS (
                  SELECT 1
                  FROM identidade.parametro_linkage p
                  WHERE p.modelo_id=i.modelo_id
                    AND p.nome='CONFLICT_MARGIN_LOG_ODDS'
                    AND p.valor>=0)
              OR NOT EXISTS (
                  SELECT 1
                  FROM identidade.parametro_linkage p
                  WHERE p.modelo_id=i.modelo_id
                    AND p.nome='M_NOME_MAE_MISSING'
                    AND p.valor>0 AND p.valor<1)
              OR NOT EXISTS (
                  SELECT 1
                  FROM identidade.parametro_linkage p
                  WHERE p.modelo_id=i.modelo_id
                    AND p.nome='U_NOME_MAE_MISSING'
                    AND p.valor>0 AND p.valor<1)
          )
    )
        THROW 51031, 'Promoção recusada: proveniência V6 exige decisão em log-odds e missingness completo.', 1;
END;
GO

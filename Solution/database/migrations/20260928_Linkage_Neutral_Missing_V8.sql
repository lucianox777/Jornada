SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Estende o contrato SQL de promoção para V8 preservando as regras V5/V6.
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
              'FELLEGI_SUNTER_DECISION_EVIDENCE_V6',
              'FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8')
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
              'FELLEGI_SUNTER_DECISION_EVIDENCE_V6',
              'FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8')
          AND (
              NOT EXISTS (
                  SELECT 1 FROM identidade.parametro_linkage p
                  WHERE p.modelo_id=i.modelo_id
                    AND p.nome=N'SUPPORT_U_NASCIMENTO_SEMANTICO_'+r.estado)
              OR NOT EXISTS (
                  SELECT 1 FROM identidade.parametro_linkage p
                  WHERE p.modelo_id=i.modelo_id
                    AND p.nome=N'POOL_SUPPORT_U_NASCIMENTO_SEMANTICO_'+r.estado)
              OR EXISTS (
                  SELECT 1
                  FROM identidade.parametro_linkage poolp
                  JOIN identidade.parametro_linkage samplep
                    ON samplep.modelo_id=poolp.modelo_id
                   AND samplep.nome=N'SUPPORT_U_NASCIMENTO_SEMANTICO_'+r.estado
                  WHERE poolp.modelo_id=i.modelo_id
                    AND poolp.nome=N'POOL_SUPPORT_U_NASCIMENTO_SEMANTICO_'+r.estado
                    AND poolp.valor>0
                    AND samplep.valor<=0)
              OR (
                  r.alcancavel=0
                  AND EXISTS (
                      SELECT 1 FROM identidade.parametro_linkage poolp
                      WHERE poolp.modelo_id=i.modelo_id
                        AND poolp.nome=N'POOL_SUPPORT_U_NASCIMENTO_SEMANTICO_'+r.estado
                        AND poolp.valor>0)
              )
          )
    )
        THROW 51032, 'Promoção recusada: amostra u perdeu estado presente no pool candidato ou proveniência de suporte está inconsistente.', 1;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        WHERE i.status IN ('VALIDADO','ATIVO')
          AND i.algoritmo_versao='FELLEGI_SUNTER_DECISION_EVIDENCE_V6'
          AND (
              NOT EXISTS (
                  SELECT 1 FROM identidade.parametro_linkage p
                  WHERE p.modelo_id=i.modelo_id
                    AND p.nome='SCORING_DECISION_EVIDENCE_V6'
                    AND p.valor>=1)
              OR NOT EXISTS (
                  SELECT 1 FROM identidade.parametro_linkage p
                  WHERE p.modelo_id=i.modelo_id
                    AND p.nome='CONFLICT_MARGIN_LOG_ODDS'
                    AND p.valor>=0)
              OR NOT EXISTS (
                  SELECT 1 FROM identidade.parametro_linkage p
                  WHERE p.modelo_id=i.modelo_id
                    AND p.nome='M_NOME_MAE_MISSING'
                    AND p.valor>0 AND p.valor<1)
              OR NOT EXISTS (
                  SELECT 1 FROM identidade.parametro_linkage p
                  WHERE p.modelo_id=i.modelo_id
                    AND p.nome='U_NOME_MAE_MISSING'
                    AND p.valor>0 AND p.valor<1)
          )
    )
        THROW 51031, 'Promoção recusada: proveniência V6 exige decisão em log-odds e missingness completo.', 1;

    -- V8 mantém os suportes de ausência, mas não admite probabilidades de ausência.
    IF EXISTS (
        SELECT 1 FROM inserted i
        WHERE i.status IN ('VALIDADO','ATIVO')
          AND i.algoritmo_versao='FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8'
          AND (
              NOT EXISTS (SELECT 1 FROM identidade.parametro_linkage p
                          WHERE p.modelo_id=i.modelo_id
                            AND p.nome='SCORING_DECISION_EVIDENCE_V6' AND p.valor>=1)
              OR NOT EXISTS (SELECT 1 FROM identidade.parametro_linkage p
                             WHERE p.modelo_id=i.modelo_id
                               AND p.nome='SCORING_MISSING_EVIDENCE_NEUTRAL_V1' AND p.valor>=1)
              OR NOT EXISTS (SELECT 1 FROM identidade.parametro_linkage p
                             WHERE p.modelo_id=i.modelo_id
                               AND p.nome='CONFLICT_MARGIN_LOG_ODDS' AND p.valor>=0)
              OR NOT EXISTS (SELECT 1 FROM identidade.parametro_linkage p
                             WHERE p.modelo_id=i.modelo_id
                               AND p.nome='SUPPORT_M_NOME_MAE_MISSING' AND p.valor>=0)
              OR NOT EXISTS (SELECT 1 FROM identidade.parametro_linkage p
                             WHERE p.modelo_id=i.modelo_id
                               AND p.nome='SUPPORT_U_NOME_MAE_MISSING' AND p.valor>=0)
              OR EXISTS (SELECT 1 FROM identidade.parametro_linkage p
                         WHERE p.modelo_id=i.modelo_id
                           AND p.nome IN ('M_NOME_MAE_MISSING','U_NOME_MAE_MISSING'))
          )
    )
        THROW 51035, 'Promoção recusada: V8 exige ausência neutra, suporte auditável e proíbe m/u MISSING.', 1;
END;
GO


-- V8 usa a mesma guarda nominal de monotonicidade e tolerância V6.
CREATE OR ALTER TRIGGER identidade.tr_modelo_linkage_llr_monotonicity
ON identidade.modelo_linkage
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        WHERE i.status IN ('VALIDADO','ATIVO')
          AND i.algoritmo_versao IN ('FELLEGI_SUNTER_DECISION_EVIDENCE_V6', 'FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8')
          AND EXISTS (
              SELECT 1
              FROM (VALUES
                  (N'M_NOME_EXACT'),(N'U_NOME_EXACT'),
                  (N'M_NOME_HIGH'),(N'U_NOME_HIGH'),
                  (N'M_NOME_MEDIUM'),(N'U_NOME_MEDIUM'),
                  (N'M_NOME_LOW'),(N'U_NOME_LOW'),
                  (N'M_NOME_MAE_EXACT'),(N'U_NOME_MAE_EXACT'),
                  (N'M_NOME_MAE_HIGH'),(N'U_NOME_MAE_HIGH'),
                  (N'M_NOME_MAE_MEDIUM'),(N'U_NOME_MAE_MEDIUM'),
                  (N'M_NOME_MAE_LOW'),(N'U_NOME_MAE_LOW')
              ) req(nome)
              WHERE NOT EXISTS (
                  SELECT 1
                  FROM identidade.parametro_linkage p
                  WHERE p.modelo_id=i.modelo_id
                    AND p.nome=req.nome
                    AND p.valor>0
                    AND p.valor<1
              )
          )
    )
        THROW 51033, 'Promoção recusada: V6/V8 exigem parâmetros nominais positivos para verificar monotonicidade de LLR.', 1;

    DECLARE @violacao_campo NVARCHAR(40);
    DECLARE @violacao_melhor NVARCHAR(20);
    DECLARE @violacao_pior NVARCHAR(20);
    DECLARE @violacao_llr_melhor FLOAT;
    DECLARE @violacao_llr_pior FLOAT;

    SELECT TOP(1)
        @violacao_campo=ord.campo,
        @violacao_melhor=ord.estado_melhor,
        @violacao_pior=ord.estado_pior,
        @violacao_llr_melhor=LOG(CAST(m_melhor.valor AS FLOAT) / CAST(u_melhor.valor AS FLOAT)),
        @violacao_llr_pior=LOG(CAST(m_pior.valor AS FLOAT) / CAST(u_pior.valor AS FLOAT))
    FROM inserted i
    CROSS JOIN (VALUES
        (1,N'NOME',N'EXACT',N'HIGH'),
        (2,N'NOME',N'HIGH',N'MEDIUM'),
        (3,N'NOME',N'MEDIUM',N'LOW'),
        (4,N'NOME_MAE',N'EXACT',N'HIGH'),
        (5,N'NOME_MAE',N'HIGH',N'MEDIUM'),
        (6,N'NOME_MAE',N'MEDIUM',N'LOW')
    ) ord(ordem,campo,estado_melhor,estado_pior)
    JOIN identidade.parametro_linkage m_melhor
      ON m_melhor.modelo_id=i.modelo_id
     AND m_melhor.nome=CONCAT(N'M_',ord.campo,N'_',ord.estado_melhor)
    JOIN identidade.parametro_linkage u_melhor
      ON u_melhor.modelo_id=i.modelo_id
     AND u_melhor.nome=CONCAT(N'U_',ord.campo,N'_',ord.estado_melhor)
    JOIN identidade.parametro_linkage m_pior
      ON m_pior.modelo_id=i.modelo_id
     AND m_pior.nome=CONCAT(N'M_',ord.campo,N'_',ord.estado_pior)
    JOIN identidade.parametro_linkage u_pior
      ON u_pior.modelo_id=i.modelo_id
     AND u_pior.nome=CONCAT(N'U_',ord.campo,N'_',ord.estado_pior)
    WHERE i.status IN ('VALIDADO','ATIVO')
      AND i.algoritmo_versao IN ('FELLEGI_SUNTER_DECISION_EVIDENCE_V6', 'FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8')
      AND LOG(CAST(m_melhor.valor AS FLOAT) / CAST(u_melhor.valor AS FLOAT)) + 1e-8
          < LOG(CAST(m_pior.valor AS FLOAT) / CAST(u_pior.valor AS FLOAT))
    ORDER BY ord.ordem;

    IF @violacao_campo IS NOT NULL
    BEGIN
        DECLARE @violacao_msg NVARCHAR(2048)=CONCAT(
            N'Promoção recusada: LLR nominal viola monotonicidade material em ',
            @violacao_campo,N' ',@violacao_melhor,N'->',@violacao_pior,
            N' (',CONVERT(NVARCHAR(60),@violacao_llr_melhor),
            N' < ',CONVERT(NVARCHAR(60),@violacao_llr_pior),N').');
        THROW 51034, @violacao_msg, 1;
    END;
END;
GO

-- V6: margem entre candidatos é diferença de log-odds, não diferença de posterior.
-- Amplia a coluna de auditoria, ajusta seu domínio e promove os contratos V5/V6
-- a invariantes do banco.
SET XACT_ABORT ON;
GO

IF OBJECT_ID('identidade.linkage_resultado','U') IS NOT NULL
   AND COL_LENGTH('identidade.linkage_resultado','margem') IS NOT NULL
BEGIN
    ALTER TABLE identidade.linkage_resultado ALTER COLUMN margem DECIMAL(19,8) NULL;

    IF EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id=OBJECT_ID('identidade.linkage_resultado')
          AND name='ck_linkage_resultado_scores')
        ALTER TABLE identidade.linkage_resultado DROP CONSTRAINT ck_linkage_resultado_scores;

    -- score_melhor/score_segundo continuam sendo posteriores. A margem, porém, é
    -- posterior em replay legado e diferença de log-odds na V6; em ambos os casos
    -- seu único limite universal é ser não-negativa.
    ALTER TABLE identidade.linkage_resultado WITH CHECK
        ADD CONSTRAINT ck_linkage_resultado_scores CHECK(
            score_melhor>=0 AND score_melhor<=1
            AND (score_segundo IS NULL OR (score_segundo>=0 AND score_segundo<=1))
            AND (margem IS NULL OR margem>=0));
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_modelo_linkage_promotion_contract
ON identidade.modelo_linkage
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- V5 e V6 dependem da distribuição semântica de nascimento. A proveniência não pode
    -- ser promovida com flag ausente/desabilitada nem com distribuição incompleta.
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
                      WHERE p.modelo_id=i.modelo_id AND p.nome=req.nome))
          )
    )
        THROW 51030, 'Promoção recusada: proveniência V5/V6 exige nascimento semântico completo e habilitado.', 1;

    -- V6 adiciona decisão em log-odds e missingness explícito de nome da mãe. O banco
    -- recusa VALIDADO/ATIVO se qualquer parte desse contrato estiver ausente ou inválida.
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
END;
GO

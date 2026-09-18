SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  Coerência interna do modelo V6.

  EXACT/HIGH/MEDIUM/LOW são estados ordenados de concordância nominal. Para NOME e
  NOME_MAE, a razão de verossimilhança m/u não pode aumentar à medida que a
  concordância degrada. MISSING fica fora desta ordem: ausência é um estado de
  missingness explícito, não um grau de similaridade.

  O trigger é separado do contrato geral de promoção para manter a migração aditiva.
  Ele protege inclusive promoções que não passam pelo Parameters Worker.
*/
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
          AND i.algoritmo_versao='FELLEGI_SUNTER_DECISION_EVIDENCE_V6'
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
        THROW 51033, 'Promoção recusada: V6 exige parâmetros nominais positivos para verificar monotonicidade de LLR.', 1;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        CROSS JOIN (VALUES
            (N'NOME',N'EXACT',N'HIGH'),
            (N'NOME',N'HIGH',N'MEDIUM'),
            (N'NOME',N'MEDIUM',N'LOW'),
            (N'NOME_MAE',N'EXACT',N'HIGH'),
            (N'NOME_MAE',N'HIGH',N'MEDIUM'),
            (N'NOME_MAE',N'MEDIUM',N'LOW')
        ) ord(campo,estado_melhor,estado_pior)
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
          AND i.algoritmo_versao='FELLEGI_SUNTER_DECISION_EVIDENCE_V6'
          AND (
              LOG(CAST(m_melhor.valor AS FLOAT) / CAST(u_melhor.valor AS FLOAT)) + 1e-12
              <
              LOG(CAST(m_pior.valor AS FLOAT) / CAST(u_pior.valor AS FLOAT))
          )
    )
        THROW 51034, 'Promoção recusada: LLR nominal viola monotonicidade EXACT >= HIGH >= MEDIUM >= LOW.', 1;
END;
GO

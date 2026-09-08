-- Cutover operacional do initial_uuid no Processor (PostgreSQL).
-- Pré-requisito: Jornada_Identidade_Progressiva.sql aplicado.
-- Não ativa Linkage probabilístico e não altera a atribuição canônica do vínculo.
BEGIN;

DO $$
BEGIN
 IF to_regclass('identidade.pessoa_origem_progressiva') IS NULL
    OR to_regclass('identidade.pessoa_origem_progressiva_evento') IS NULL
    OR to_regprocedure('identidade.assegurar_origem_progressiva(bigint)') IS NULL THEN
  RAISE EXCEPTION 'Persistência progressiva V1 não instalada; cutover recusado.';
 END IF;
END $$;

-- Fecha o universo existente de forma idempotente antes de ativar o gatilho.
DO $$
DECLARE r RECORD;
BEGIN
 FOR r IN
  SELECT o.pessoa_origem_id
    FROM silver.pessoa_origem o
    LEFT JOIN identidade.pessoa_origem_progressiva p
      ON p.pessoa_origem_id=o.pessoa_origem_id
   WHERE p.pessoa_origem_id IS NULL
   ORDER BY o.pessoa_origem_id
 LOOP
  PERFORM identidade.assegurar_origem_progressiva(r.pessoa_origem_id);
 END LOOP;
END $$;

CREATE OR REPLACE FUNCTION identidade.fn_vinculo_fonte_progressiva()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE v_source BIGINT;
BEGIN
 SELECT o.pessoa_origem_id INTO v_source
   FROM silver.pessoa_observacao o
  WHERE o.pessoa_observacao_id=NEW.pessoa_observacao_id;
 IF v_source IS NULL THEN
  RAISE EXCEPTION 'Vínculo sem origem Silver válida.';
 END IF;
 PERFORM identidade.assegurar_origem_progressiva(v_source);
 RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS tr_vinculo_fonte_progressiva ON identidade.vinculo_fonte;
CREATE TRIGGER tr_vinculo_fonte_progressiva
AFTER INSERT ON identidade.vinculo_fonte
FOR EACH ROW EXECUTE FUNCTION identidade.fn_vinculo_fonte_progressiva();

DO $$
BEGIN
 IF EXISTS(
  SELECT 1
    FROM silver.pessoa_origem o
    LEFT JOIN identidade.pessoa_origem_progressiva p
      ON p.pessoa_origem_id=o.pessoa_origem_id
   WHERE p.pessoa_origem_id IS NULL) THEN
  RAISE EXCEPTION 'Cutover recusado: existem origens Silver sem initial_uuid.';
 END IF;
END $$;

COMMIT;

-- Jornada PostgreSQL - fatia de ingestão e coordenação do Processor.
-- Complementa Jornada_Resultado_Core.sql sem declarar ainda paridade Silver/Gold/Identidade.

ALTER TABLE ref.gestor
    ADD COLUMN IF NOT EXISTS ativo BOOLEAN NOT NULL DEFAULT TRUE;

ALTER TABLE ref.sistema_origem
    ADD COLUMN IF NOT EXISTS ativo BOOLEAN NOT NULL DEFAULT TRUE;

ALTER TABLE ref.gestor_pessoa_versao
    ADD COLUMN IF NOT EXISTS status VARCHAR(20) NOT NULL DEFAULT 'ATIVA',
    ADD COLUMN IF NOT EXISTS pessoa_schema_ref VARCHAR(500) NULL,
    ADD COLUMN IF NOT EXISTS pessoa_schema_sha256 BYTEA NULL;

ALTER TABLE ref.tipo_registro
    ADD COLUMN IF NOT EXISTS gestor_id BIGINT NULL REFERENCES ref.gestor(gestor_id),
    ADD COLUMN IF NOT EXISTS natureza VARCHAR(30) NULL,
    ADD COLUMN IF NOT EXISTS ativo BOOLEAN NOT NULL DEFAULT TRUE;

-- O contrato canônico da Jornada aceita exatamente quatro caracteres alfanuméricos
-- (por exemplo AA01, AE01 e CRA1), não apenas o padrão legado LLDD.
ALTER TABLE ref.tipo_registro
    DROP CONSTRAINT IF EXISTS ck_pg_tipo_codigo_llnn;
ALTER TABLE ref.tipo_registro
    ALTER COLUMN codigo TYPE VARCHAR(4) USING BTRIM(codigo);
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
          FROM pg_constraint
         WHERE conrelid='ref.tipo_registro'::regclass
           AND conname='ck_pg_tipo_codigo_4_alnum'
    ) THEN
        ALTER TABLE ref.tipo_registro
            ADD CONSTRAINT ck_pg_tipo_codigo_4_alnum
            CHECK (codigo ~ '^[A-Z0-9]{4}$');
    END IF;
END;
$$;

ALTER TABLE ref.tipo_registro_versao
    ADD COLUMN IF NOT EXISTS status VARCHAR(20) NOT NULL DEFAULT 'ATIVA',
    ADD COLUMN IF NOT EXISTS schema_registro_ref VARCHAR(500) NULL,
    ADD COLUMN IF NOT EXISTS schema_registro_sha256 BYTEA NULL,
    ADD COLUMN IF NOT EXISTS qc_status VARCHAR(30) NULL,
    ADD COLUMN IF NOT EXISTS origina_endereco_casa_abrigo_sigilosa BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS data_inicio_permitida_concessao DATE NULL,
    ADD COLUMN IF NOT EXISTS data_fim_permitida_concessao DATE NULL,
    ADD COLUMN IF NOT EXISTS regime_vigencia VARCHAR(40) NULL;

ALTER TABLE bronze.entrega_arquivo
    ADD COLUMN IF NOT EXISTS estado_armazenamento VARCHAR(30) NOT NULL DEFAULT 'DISPONIVEL';

ALTER TABLE ingestao.lote
    ADD COLUMN IF NOT EXISTS lease_id UUID NULL,
    ADD COLUMN IF NOT EXISTS lease_owner VARCHAR(200) NULL,
    ADD COLUMN IF NOT EXISTS lease_adquirido_em TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS lease_expira_em TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS heartbeat_em TIMESTAMPTZ NULL;

CREATE INDEX IF NOT EXISTS ix_pg_lote_reserva
    ON ingestao.lote(status,proxima_tentativa_em,criado_em,lote_seq,lote_id);
CREATE INDEX IF NOT EXISTS ix_pg_lote_lease_expira
    ON ingestao.lote(lease_expira_em)
    WHERE status IN ('VALIDANDO','PROCESSANDO');

CREATE OR REPLACE FUNCTION ingestao.recalcular_entrega(p_entrega_id UUID)
RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_status VARCHAR(30);
BEGIN
    SELECT CASE
        WHEN bool_or(status IN ('QUARENTENA','POISON')) THEN 'QUARENTENA'
        WHEN bool_or(status='REJEITADO') THEN 'REJEITADA'
        WHEN bool_and(status='PROCESSADO') THEN 'PROCESSADA'
        WHEN bool_or(status='PROCESSANDO') THEN 'PROCESSANDO'
        WHEN bool_or(status='VALIDANDO') THEN 'VALIDANDO'
        ELSE 'RECEBIDA'
    END
    INTO v_status
    FROM ingestao.lote
    WHERE entrega_id=p_entrega_id;

    UPDATE ingestao.entrega
       SET status=COALESCE(v_status,'RECEBIDA'),ultima_atualizacao=CURRENT_TIMESTAMP
     WHERE entrega_id=p_entrega_id;
END;
$$;

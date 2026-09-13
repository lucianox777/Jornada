from pathlib import Path

# PostgreSQL: fresh installs and re-entry/upgrade must both drop the old NOT NULL contract.
pg = Path("Solution/database/postgresql/Jornada_Processor_Persistence_Core.sql")
text = pg.read_text(encoding="utf-8")
if text.count("nome_mae VARCHAR(500) NOT NULL") != 2:
    raise SystemExit("PostgreSQL: esperadas 2 colunas nome_mae NOT NULL")
if text.count("nome_mae_cmp VARCHAR(500) NOT NULL") != 1:
    raise SystemExit("PostgreSQL: esperada 1 coluna nome_mae_cmp NOT NULL")
text = text.replace("nome_mae VARCHAR(500) NOT NULL", "nome_mae VARCHAR(500) NULL")
text = text.replace("nome_mae_cmp VARCHAR(500) NOT NULL", "nome_mae_cmp VARCHAR(500) NULL")
marker = """CREATE INDEX IF NOT EXISTS ix_pg_pessoa_observacao_cpf
    ON silver.pessoa_observacao(cpf) WHERE cpf IS NOT NULL;
"""
addition = marker + """
-- Nome da mãe é evidência opcional. Reentrada do core também corrige bancos já criados.
ALTER TABLE silver.pessoa_observacao ALTER COLUMN nome_mae DROP NOT NULL;
ALTER TABLE silver.pessoa_observacao ALTER COLUMN nome_mae_cmp DROP NOT NULL;
"""
if text.count(marker) != 1:
    raise SystemExit("PostgreSQL: marcador Silver não encontrado de forma única")
text = text.replace(marker, addition)
marker = """CREATE UNIQUE INDEX IF NOT EXISTS ux_pg_gold_pessoa_cpf
    ON gold.pessoa(cpf) WHERE cpf IS NOT NULL;
"""
addition = marker + """
ALTER TABLE gold.pessoa ALTER COLUMN nome_mae DROP NOT NULL;
"""
if text.count(marker) != 1:
    raise SystemExit("PostgreSQL: marcador Gold não encontrado de forma única")
text = text.replace(marker, addition)
pg.write_text(text, encoding="utf-8")

# DEV seed: preserve v1/v2 as history and activate v3 only in the current seed.
seed = Path("Solution/database/Jornada_Seed_Dev.sql")
text = seed.read_text(encoding="utf-8")
text = text.replace(
    "-- v1 permanece aceito para Entregas históricas; v2 torna codigoPessoaOrigem opcional quando CPF válido está preenchido.",
    "-- v1/v2 permanecem aceitos para Entregas históricas; v3 torna nomeMae opcional/anulável sem reescrever contratos anteriores."
)
old = """UPDATE v SET pessoa_schema_ref=CONCAT('config/contracts/gestores/',g.codigo,'/pessoa/v2/pessoa.schema.json'),
             pessoa_schema_sha256=CASE g.codigo WHEN 'SEHAB' THEN 0xcf6033756c0409469e1defc76ac46650921b8e6c4bf5a76d8b0bc2087efb2e89 WHEN 'SMADS' THEN 0xfa1c70cb21eee04606a169c661c168edb83882f4812c0c6ce4656d551dce38a6 WHEN 'SMDET' THEN 0x2ff50ee5a53d095b4da625ab0d2014e1dd2aef91a12011ebea2e52d60da143ed WHEN 'SMS' THEN 0xa68da8985cbb709a5c7cd9225192b1ccb2300c7aaf184cfcd26ebf41e9570ec4 END,
             status='ATIVA',vigencia_inicio='2026-09-05',vigencia_fim=NULL,ativado_em=COALESCE(v.ativado_em,'2026-09-05')
FROM ref.gestor_pessoa_versao v JOIN ref.gestor g ON g.gestor_id=v.gestor_id
WHERE v.versao=2 AND g.codigo IN('SMS','SEHAB','SMADS','SMDET');
DECLARE @gpvSehab BIGINT=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSehab AND versao=2),
        @gpvSmads BIGINT=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSmads AND versao=2),
        @gpvSms BIGINT=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSms AND versao=2);
"""
new = """UPDATE v SET pessoa_schema_ref=CONCAT('config/contracts/gestores/',g.codigo,'/pessoa/v2/pessoa.schema.json'),
             pessoa_schema_sha256=CASE g.codigo WHEN 'SEHAB' THEN 0xcf6033756c0409469e1defc76ac46650921b8e6c4bf5a76d8b0bc2087efb2e89 WHEN 'SMADS' THEN 0xfa1c70cb21eee04606a169c661c168edb83882f4812c0c6ce4656d551dce38a6 WHEN 'SMDET' THEN 0x2ff50ee5a53d095b4da625ab0d2014e1dd2aef91a12011ebea2e52d60da143ed WHEN 'SMS' THEN 0xa68da8985cbb709a5c7cd9225192b1ccb2300c7aaf184cfcd26ebf41e9570ec4 END,
             status='ENCERRADA',vigencia_inicio='2026-09-05',vigencia_fim=COALESCE(v.vigencia_fim,'2026-09-12'),ativado_em=COALESCE(v.ativado_em,'2026-09-05')
FROM ref.gestor_pessoa_versao v JOIN ref.gestor g ON g.gestor_id=v.gestor_id
WHERE v.versao=2 AND g.codigo IN('SMS','SEHAB','SMADS','SMDET');

INSERT ref.gestor_pessoa_versao(gestor_id,versao,vigencia_inicio,pessoa_schema_ref,pessoa_schema_sha256,status,ativado_em)
SELECT g.gestor_id,3,'2026-09-12',CONCAT('config/contracts/gestores/',g.codigo,'/pessoa/v3/pessoa.schema.json'),
       0x2cec7a0ccda3e55770e2fe35b042904ed19ae0f55ca36694104e01a859276306,
       'ATIVA','2026-09-12'
FROM ref.gestor g WHERE g.codigo IN('SMS','SEHAB','SMADS','SMDET')
AND NOT EXISTS(SELECT 1 FROM ref.gestor_pessoa_versao v WHERE v.gestor_id=g.gestor_id AND v.versao=3);
UPDATE v SET pessoa_schema_ref=CONCAT('config/contracts/gestores/',g.codigo,'/pessoa/v3/pessoa.schema.json'),
             pessoa_schema_sha256=0x2cec7a0ccda3e55770e2fe35b042904ed19ae0f55ca36694104e01a859276306,
             status='ATIVA',vigencia_inicio='2026-09-12',vigencia_fim=NULL,ativado_em=COALESCE(v.ativado_em,'2026-09-12')
FROM ref.gestor_pessoa_versao v JOIN ref.gestor g ON g.gestor_id=v.gestor_id
WHERE v.versao=3 AND g.codigo IN('SMS','SEHAB','SMADS','SMDET');
DECLARE @gpvSehab BIGINT=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSehab AND versao=3),
        @gpvSmads BIGINT=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSmads AND versao=3),
        @gpvSms BIGINT=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSms AND versao=3);
"""
if text.count(old) != 1:
    raise SystemExit("Seed: bloco v2 corrente não encontrado de forma única")
text = text.replace(old, new)
seed.write_text(text, encoding="utf-8")

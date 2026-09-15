SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- As três Entregas do seed representam pacotes logicamente diferentes. O seed histórico
-- reutilizava o mesmo placeholder de 4 bytes (PK\x03\x04) e, por consequência, o mesmo
-- SHA-256/nome content-addressed para SMS, SEHAB e SMADS. Isso era matematicamente coerente
-- com os bytes artificiais, mas tornava a fixture enganosa no Monitor e em diagnósticos.
--
-- Mantemos SMS como o placeholder canônico já usado pelo backup drill e atribuímos bytes
-- determinísticos distintos às outras duas fixtures:
--   SMS   = "PK\x03\x04"       -> 8dcc7e60... (4 bytes)
--   SEHAB = "PK\x03\x04SEHAB" -> 08befc1b... (9 bytes)
--   SMADS = "PK\x03\x04SMADS" -> 52efeb29... (9 bytes)
-- Este arquivo corrige apenas a massa DEV; não é migração de Produção.

DECLARE @entSms UNIQUEIDENTIFIER='10000000-0000-4000-8000-000000000001';
DECLARE @entSehab UNIQUEIDENTIFIER='20000000-0000-4000-8000-000000000001';
DECLARE @entSmads UNIQUEIDENTIFIER='30000000-0000-4000-8000-000000000001';

DECLARE @smsSha CHAR(64)='8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2';
DECLARE @sehabSha CHAR(64)='08befc1b72bbe89348739d0d994b031aa28db85ffc817ab2a2598a0af3583084';
DECLARE @smadsSha CHAR(64)='52efeb293d001f170549c0bdf4196cf94af375858b96a98a0d486a2ae2f81923';

UPDATE ingestao.entrega
SET payload_sha256=@smsSha,bytes_recebidos=4
WHERE entrega_id=@entSms;
UPDATE bronze.entrega_arquivo
SET nome_arquivo=CONCAT('ENTREGA_SMS_SAUDE_v2_',@smsSha,'.zip'),
    objeto_chave=CONCAT('sha256/8d/cc/',@smsSha,'.zip'),
    payload_sha256=@smsSha,tamanho_bytes=4
WHERE entrega_id=@entSms;

UPDATE ingestao.entrega
SET payload_sha256=@sehabSha,bytes_recebidos=9
WHERE entrega_id=@entSehab;
UPDATE bronze.entrega_arquivo
SET nome_arquivo=CONCAT('ENTREGA_SEHAB_SEHAB_v2_',@sehabSha,'.zip'),
    objeto_chave=CONCAT('sha256/08/be/',@sehabSha,'.zip'),
    payload_sha256=@sehabSha,tamanho_bytes=9
WHERE entrega_id=@entSehab;

UPDATE ingestao.entrega
SET payload_sha256=@smadsSha,bytes_recebidos=9
WHERE entrega_id=@entSmads;
UPDATE bronze.entrega_arquivo
SET nome_arquivo=CONCAT('ENTREGA_SMADS_ASSISTENCIA_v2_',@smadsSha,'.zip'),
    objeto_chave=CONCAT('sha256/52/ef/',@smadsSha,'.zip'),
    payload_sha256=@smadsSha,tamanho_bytes=9
WHERE entrega_id=@entSmads;

IF (SELECT COUNT(DISTINCT payload_sha256)
    FROM ingestao.entrega
    WHERE entrega_id IN(@entSms,@entSehab,@entSmads)) <> 3
    THROW 51570, 'Seed DEV inválido: as três Entregas canônicas devem possuir payload_sha256 distintos.', 1;

IF EXISTS(
    SELECT 1
    FROM bronze.entrega_arquivo a
    JOIN ingestao.entrega e ON e.entrega_id=a.entrega_id
    WHERE a.entrega_id IN(@entSms,@entSehab,@entSmads)
      AND (a.payload_sha256<>e.payload_sha256 OR a.tamanho_bytes<>e.bytes_recebidos))
    THROW 51571, 'Seed DEV inválido: metadados Bronze divergem da Entrega após diferenciação dos payloads.', 1;

COMMIT TRANSACTION;
GO

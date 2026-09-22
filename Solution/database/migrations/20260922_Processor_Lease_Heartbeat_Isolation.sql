-- Heartbeat separado do Lote: a transação Serializable de Silver/item_processado
-- segura locks no Lote por chaves estrangeiras, mas não nesta linha.
IF OBJECT_ID(N'ingestao.lote_heartbeat',N'U') IS NULL
 CREATE TABLE ingestao.lote_heartbeat(
   lote_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY REFERENCES ingestao.lote(lote_id),
   lease_id UNIQUEIDENTIFIER NOT NULL,
   lease_owner NVARCHAR(200) NOT NULL,
   heartbeat_em DATETIMEOFFSET(7) NOT NULL,
   lease_expira_em DATETIMEOFFSET(7) NOT NULL
 );
GO
-- Backfill idempotente dos leases ativos existentes.
INSERT ingestao.lote_heartbeat(lote_id,lease_id,lease_owner,heartbeat_em,lease_expira_em)
SELECT l.lote_id,l.lease_id,l.lease_owner,l.heartbeat_em,l.lease_expira_em
FROM ingestao.lote l
WHERE l.status IN(N'VALIDANDO',N'PROCESSANDO')
 AND l.lease_id IS NOT NULL AND l.lease_owner IS NOT NULL
 AND l.heartbeat_em IS NOT NULL AND l.lease_expira_em IS NOT NULL
 AND NOT EXISTS(SELECT 1 FROM ingestao.lote_heartbeat h WHERE h.lote_id=l.lote_id);
GO

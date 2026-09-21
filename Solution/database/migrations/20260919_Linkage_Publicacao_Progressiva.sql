SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
 Publicação probabilística -> identidade progressiva
 ---------------------------------------------------
 O resultado bruto do scorer permanece imutável em identidade.linkage_resultado.
 Esta migração acrescenta uma decisão operacional separada e auditável. O initial_uuid
 nunca participa de candidate generation, score, LLR ou posterior: ele só pode ser
 promovido como identidade própria quando uma busca completa termina sem candidato.
*/

IF OBJECT_ID(N'identidade.linkage_resultado',N'U') IS NULL
   OR OBJECT_ID(N'identidade.linkage_run',N'U') IS NULL
   OR OBJECT_ID(N'identidade.pessoa_origem_progressiva',N'U') IS NULL
   OR OBJECT_ID(N'identidade.pessoa_origem_progressiva_evento',N'U') IS NULL
    THROW 51800,'Publicação progressiva exige Linkage e identidade progressiva instalados.',1;
GO

IF COL_LENGTH(N'identidade.pessoa_origem_progressiva_evento',N'linkage_run_id') IS NULL
    ALTER TABLE identidade.pessoa_origem_progressiva_evento ADD linkage_run_id UNIQUEIDENTIFIER NULL;
GO
IF NOT EXISTS(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'identidade.pessoa_origem_progressiva_evento')
      AND name=N'fk_progressiva_evento_linkage_run')
    ALTER TABLE identidade.pessoa_origem_progressiva_evento
      ADD CONSTRAINT fk_progressiva_evento_linkage_run
      FOREIGN KEY(linkage_run_id) REFERENCES identidade.linkage_run(linkage_run_id);
GO
IF NOT EXISTS(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'identidade.pessoa_origem_progressiva_evento')
      AND name=N'UX_progressiva_evento_origem_linkage_run')
    CREATE UNIQUE INDEX UX_progressiva_evento_origem_linkage_run
      ON identidade.pessoa_origem_progressiva_evento(pessoa_origem_id,linkage_run_id)
      WHERE linkage_run_id IS NOT NULL;
GO

IF COL_LENGTH(N'identidade.linkage_resultado',N'resultado_publicacao') IS NULL
    ALTER TABLE identidade.linkage_resultado ADD resultado_publicacao NVARCHAR(30) NULL;
IF COL_LENGTH(N'identidade.linkage_resultado',N'pessoa_uuid_publicado') IS NULL
    ALTER TABLE identidade.linkage_resultado ADD pessoa_uuid_publicado UNIQUEIDENTIFIER NULL;
IF COL_LENGTH(N'identidade.linkage_resultado',N'status_publicacao') IS NULL
    ALTER TABLE identidade.linkage_resultado ADD status_publicacao NVARCHAR(30) NULL;
IF COL_LENGTH(N'identidade.linkage_resultado',N'motivo_publicacao') IS NULL
    ALTER TABLE identidade.linkage_resultado ADD motivo_publicacao NVARCHAR(160) NULL;
IF COL_LENGTH(N'identidade.linkage_resultado',N'pessoa_origem_id_publicado') IS NULL
    ALTER TABLE identidade.linkage_resultado ADD pessoa_origem_id_publicado BIGINT NULL;
IF COL_LENGTH(N'identidade.linkage_resultado',N'progressiva_versao') IS NULL
    ALTER TABLE identidade.linkage_resultado ADD progressiva_versao BIGINT NULL;
IF COL_LENGTH(N'identidade.linkage_resultado',N'politica_publicacao_versao') IS NULL
    ALTER TABLE identidade.linkage_resultado ADD politica_publicacao_versao NVARCHAR(120) NULL;
IF COL_LENGTH(N'identidade.linkage_resultado',N'universo_referencia') IS NULL
    ALTER TABLE identidade.linkage_resultado ADD universo_referencia NVARCHAR(255) NULL;
IF COL_LENGTH(N'identidade.linkage_resultado',N'publicado_em') IS NULL
    ALTER TABLE identidade.linkage_resultado ADD publicado_em DATETIMEOFFSET(7) NULL;
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'identidade.linkage_resultado')
      AND name=N'fk_linkage_resultado_uuid_publicado')
    ALTER TABLE identidade.linkage_resultado
      ADD CONSTRAINT fk_linkage_resultado_uuid_publicado
      FOREIGN KEY(pessoa_uuid_publicado) REFERENCES identidade.pessoa(pessoa_uuid);
IF NOT EXISTS(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'identidade.linkage_resultado')
      AND name=N'fk_linkage_resultado_origem_publicada')
    ALTER TABLE identidade.linkage_resultado
      ADD CONSTRAINT fk_linkage_resultado_origem_publicada
      FOREIGN KEY(pessoa_origem_id_publicado) REFERENCES silver.pessoa_origem(pessoa_origem_id);
GO

-- Upgrade de runs já PUBLICADOS: preserva exatamente a semântica que estava visível
-- antes desta migração. Não inventa eventos progressivos retroativos.
UPDATE r
   SET resultado_publicacao=CASE WHEN r.status=N'RESOLVIDO' THEN N'ASSOCIACAO_EXISTENTE' ELSE N'INDEFINIDA' END,
       pessoa_uuid_publicado=CASE WHEN r.status=N'RESOLVIDO' THEN r.pessoa_uuid_resolvido END,
       status_publicacao=r.status,
       motivo_publicacao=COALESCE(r.motivo,CASE WHEN r.status=N'RESOLVIDO' THEN N'LEGACY_RAW_RESOLVIDO' ELSE N'LEGACY_RAW_PUBLICATION' END),
       pessoa_origem_id_publicado=po.pessoa_origem_id,
       politica_publicacao_versao=N'LEGACY_RAW_PUBLICATION_V1',
       universo_referencia=CONCAT(N'LEGACY_LINKAGE_RUN:',CONVERT(NVARCHAR(36),r.linkage_run_id)),
       publicado_em=COALESCE(lr.publicado_em,lr.finalizado_em,r.calculado_em)
FROM identidade.linkage_resultado r
JOIN identidade.linkage_run lr ON lr.linkage_run_id=r.linkage_run_id
JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id
WHERE lr.status=N'PUBLICADO'
  AND r.resultado_publicacao IS NULL;
GO

IF EXISTS(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'identidade.linkage_resultado')
      AND name=N'ck_linkage_resultado_publicacao')
    ALTER TABLE identidade.linkage_resultado DROP CONSTRAINT ck_linkage_resultado_publicacao;
GO
ALTER TABLE identidade.linkage_resultado WITH CHECK
 ADD CONSTRAINT ck_linkage_resultado_publicacao CHECK(
    (
      resultado_publicacao IS NULL
      AND pessoa_uuid_publicado IS NULL
      AND status_publicacao IS NULL
      AND motivo_publicacao IS NULL
      AND pessoa_origem_id_publicado IS NULL
      AND progressiva_versao IS NULL
      AND politica_publicacao_versao IS NULL
      AND universo_referencia IS NULL
      AND publicado_em IS NULL
    )
    OR
    (
      resultado_publicacao IN(N'ASSOCIACAO_EXISTENTE',N'NOVA_IDENTIDADE',N'INDEFINIDA')
      AND status_publicacao IN(N'RESOLVIDO',N'NAO_RESOLVIDO',N'CONFLITO')
      AND motivo_publicacao IS NOT NULL
      AND politica_publicacao_versao IS NOT NULL
      AND publicado_em IS NOT NULL
      AND (
        (resultado_publicacao=N'ASSOCIACAO_EXISTENTE' AND status_publicacao=N'RESOLVIDO' AND pessoa_uuid_publicado IS NOT NULL)
        OR
        (resultado_publicacao=N'NOVA_IDENTIDADE' AND status_publicacao=N'RESOLVIDO'
             AND pessoa_uuid_publicado IS NOT NULL AND pessoa_origem_id_publicado IS NOT NULL AND universo_referencia IS NOT NULL)
        OR
        (resultado_publicacao=N'INDEFINIDA' AND status_publicacao IN(N'NAO_RESOLVIDO',N'CONFLITO') AND pessoa_uuid_publicado IS NULL)
      )
    )
 );
GO

/*
 A evidência bruta do scorer é imutável. O envelope operacional pode ser finalizado
 apenas dentro da transação curta de publicação, enquanto o cabeçalho ainda está EXECUTANDO.
 Depois de PUBLICADO nem o envelope pode ser reescrito. DELETE nunca é permitido.
*/
CREATE OR ALTER TRIGGER identidade.tr_linkage_resultado_publicacao_imutavel
ON identidade.linkage_resultado
AFTER UPDATE
AS
BEGIN
 SET NOCOUNT ON;

 IF EXISTS(
   SELECT 1
   FROM inserted i
   JOIN deleted d ON d.linkage_resultado_id=i.linkage_resultado_id
   WHERE i.linkage_run_id<>d.linkage_run_id
      OR i.modelo_id<>d.modelo_id
      OR i.modelo_versao<>d.modelo_versao
      OR i.pessoa_observacao_id<>d.pessoa_observacao_id
      OR ISNULL(CONVERT(NVARCHAR(36),i.pessoa_uuid_resolvido),N'')<>ISNULL(CONVERT(NVARCHAR(36),d.pessoa_uuid_resolvido),N'')
      OR ISNULL(CONVERT(NVARCHAR(36),i.melhor_candidato_uuid),N'')<>ISNULL(CONVERT(NVARCHAR(36),d.melhor_candidato_uuid),N'')
      OR i.score_melhor<>d.score_melhor
      OR ISNULL(CONVERT(NVARCHAR(36),i.segundo_candidato_uuid),N'')<>ISNULL(CONVERT(NVARCHAR(36),d.segundo_candidato_uuid),N'')
      OR ISNULL(i.score_segundo,CONVERT(DECIMAL(9,8),-1))<>ISNULL(d.score_segundo,CONVERT(DECIMAL(9,8),-1))
      OR ISNULL(i.margem,CONVERT(DECIMAL(18,8),-1))<>ISNULL(d.margem,CONVERT(DECIMAL(18,8),-1))
      OR i.status<>d.status
      OR ISNULL(i.motivo,N'')<>ISNULL(d.motivo,N'')
      OR i.calculado_em<>d.calculado_em)
   THROW 51822,'Evidência bruta de linkage_resultado é imutável.',1;

 IF EXISTS(
   SELECT 1
   FROM inserted i
   JOIN identidade.linkage_run lr ON lr.linkage_run_id=i.linkage_run_id
   WHERE lr.status<>N'EXECUTANDO')
   THROW 51823,'Envelope de publicação só pode ser finalizado enquanto o run está EXECUTANDO.',1;
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_linkage_resultado_bloqueia_delete
ON identidade.linkage_resultado
INSTEAD OF DELETE
AS
BEGIN
 SET NOCOUNT ON;
 THROW 51824,'linkage_resultado é histórico e não admite DELETE.',1;
END;
GO

/*
 Aplica UMA decisão já derivada para uma origem persistente. O procedimento relê o
 próprio linkage_resultado, não recebe score/target do chamador e exige a transação
 serializável de publicação do linkage_run.
*/
CREATE OR ALTER PROCEDURE identidade.sp_publicar_resolucao_progressiva_linkage
 @linkage_run_id UNIQUEIDENTIFIER,
 @pessoa_observacao_id BIGINT,
 @versao_resultado BIGINT OUTPUT
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 IF @@TRANCOUNT=0 THROW 51801,'Publicação progressiva de Linkage exige transação explícita.',1;

 DECLARE @pessoa_origem_id BIGINT,
         @resultado NVARCHAR(30),
         @canonical_uuid UNIQUEIDENTIFIER,
         @status_publicacao NVARCHAR(30),
         @motivo_publicacao NVARCHAR(160),
         @politica NVARCHAR(120),
         @universo NVARCHAR(255),
         @raw_status NVARCHAR(30),
         @raw_motivo NVARCHAR(120),
         @modelo_id UNIQUEIDENTIFIER,
         @modelo_versao INT,
         @run_status NVARCHAR(30),
         @run_avaliados BIGINT,
         @run_elegiveis BIGINT,
         @run_itens BIGINT;

 SELECT @pessoa_origem_id=po.pessoa_origem_id,
        @resultado=r.resultado_publicacao,
        @canonical_uuid=r.pessoa_uuid_publicado,
        @status_publicacao=r.status_publicacao,
        @motivo_publicacao=r.motivo_publicacao,
        @politica=r.politica_publicacao_versao,
        @universo=r.universo_referencia,
        @raw_status=r.status,
        @raw_motivo=r.motivo,
        @modelo_id=r.modelo_id,
        @modelo_versao=r.modelo_versao,
        @run_status=lr.status,
        @run_avaliados=lr.avaliados,
        @run_elegiveis=lr.registros_elegiveis
 FROM identidade.linkage_resultado r WITH(UPDLOCK,HOLDLOCK)
 JOIN identidade.linkage_run lr WITH(UPDLOCK,HOLDLOCK) ON lr.linkage_run_id=r.linkage_run_id
 JOIN silver.pessoa_observacao po WITH(HOLDLOCK) ON po.pessoa_observacao_id=r.pessoa_observacao_id
 WHERE r.linkage_run_id=@linkage_run_id
   AND r.pessoa_observacao_id=@pessoa_observacao_id;

 SELECT @run_itens=COUNT_BIG(*)
 FROM identidade.linkage_run_item WITH(HOLDLOCK)
 WHERE linkage_run_id=@linkage_run_id;

 IF @resultado IS NULL THROW 51802,'Resultado de publicação do Linkage ausente.',1;
 IF @run_status<>N'EXECUTANDO' THROW 51803,'Evento progressivo só pode ser produzido antes da transição do run para PUBLICADO.',1;
 IF @run_avaliados<>@run_elegiveis OR @run_itens<>@run_elegiveis
    THROW 51818,'Run incompleto/materialização divergente não pode publicar resolução progressiva.',1;
 IF NOT EXISTS(
   SELECT 1 FROM identidade.linkage_run_item WITH(HOLDLOCK)
   WHERE linkage_run_id=@linkage_run_id AND pessoa_observacao_id=@pessoa_observacao_id)
    THROW 51820,'Observação não pertence ao universo materializado do run.',1;
 IF @pessoa_origem_id IS NULL THROW 51804,'Observação sem origem persistente não possui ledger progressivo.',1;
 IF @politica IS NULL OR LTRIM(RTRIM(@politica))=N'' THROW 51805,'Política de publicação ausente.',1;
 IF EXISTS(
   SELECT 1
   FROM identidade.vinculo_fonte vf WITH(HOLDLOCK)
   WHERE vf.pessoa_observacao_id=@pessoa_observacao_id
     AND vf.ativo=1
     AND vf.metodo_resolucao IN(
       N'CPF_DETERMINISTICO',N'NIS_DETERMINISTICO',N'UUID_JORNADA_RETROALIMENTACAO',
       N'CORRECAO_GOVERNADA',N'CONFLITO_GOVERNADO'))
   THROW 51819,'Vínculo determinístico/governado tem precedência sobre publicação probabilística.',1;

 DECLARE @ensure TABLE(
   initial_uuid UNIQUEIDENTIFIER NOT NULL,
   legacy_pessoa_uuid UNIQUEIDENTIFIER NULL,
   estado VARCHAR(20) NOT NULL,
   versao BIGINT NOT NULL);
 INSERT @ensure(initial_uuid,legacy_pessoa_uuid,estado,versao)
 EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@pessoa_origem_id;

 IF EXISTS(
   SELECT 1 FROM identidade.pessoa_origem_progressiva_evento WITH(HOLDLOCK)
   WHERE pessoa_origem_id=@pessoa_origem_id AND linkage_run_id=@linkage_run_id)
 BEGIN
   SELECT @versao_resultado=versao
   FROM identidade.pessoa_origem_progressiva_evento
   WHERE pessoa_origem_id=@pessoa_origem_id AND linkage_run_id=@linkage_run_id;
   RETURN;
 END;

 DECLARE @initial UNIQUEIDENTIFIER,@current UNIQUEIDENTIFIER,@estado VARCHAR(20),
         @versao BIGINT,@externo UNIQUEIDENTIFIER,@nova BIGINT,@now DATETIMEOFFSET(7),
         @reference_lock INT,@reference_resource NVARCHAR(255);

 SELECT @initial=initial_uuid,@current=canonical_uuid,@estado=estado,@versao=versao,
        @externo=ultimo_destino_externo_uuid
 FROM identidade.pessoa_origem_progressiva WITH(UPDLOCK,HOLDLOCK)
 WHERE pessoa_origem_id=@pessoa_origem_id;

 IF @resultado=N'ASSOCIACAO_EXISTENTE'
 BEGIN
   IF @status_publicacao<>N'RESOLVIDO' OR @canonical_uuid IS NULL
      THROW 51806,'Associação publicada exige UUID e status RESOLVIDO.',1;
   IF @estado=N'REFERENCIA' AND @current=@canonical_uuid
   BEGIN
     SET @versao_resultado=@versao;
     RETURN;
   END;
   IF @estado=N'REFERENCIA' AND (@current<>@canonical_uuid OR @current IS NULL)
      THROW 51807,'Linkage não pode trocar referência progressiva já estabelecida.',1;

   IF NOT EXISTS(SELECT 1 FROM identidade.cpf_ancora WITH(HOLDLOCK) WHERE pessoa_uuid=@canonical_uuid)
      AND NOT EXISTS(
        SELECT 1 FROM identidade.pessoa_origem_progressiva WITH(HOLDLOCK)
        WHERE pessoa_origem_id<>@pessoa_origem_id AND estado=N'REFERENCIA' AND canonical_uuid=@canonical_uuid)
      AND NOT EXISTS(
        SELECT 1 FROM identidade.vinculo_fonte WITH(HOLDLOCK)
        WHERE ativo=1 AND status=N'RESOLVIDO' AND pessoa_uuid=@canonical_uuid
          AND metodo_resolucao IN(N'CPF_DETERMINISTICO',N'UUID_JORNADA_RETROALIMENTACAO',N'CORRECAO_GOVERNADA'))
      THROW 51808,'Destino probabilístico não é referência canônica estabelecida.',1;
 END
 ELSE IF @resultado=N'NOVA_IDENTIDADE'
 BEGIN
   IF @status_publicacao<>N'RESOLVIDO' OR @canonical_uuid IS NULL OR @canonical_uuid<>@initial
      THROW 51809,'NOVA_IDENTIDADE só pode promover o initial_uuid da própria origem.',1;
   IF @raw_status<>N'NAO_RESOLVIDO' OR @raw_motivo IS NULL OR @raw_motivo NOT LIKE N'SEM_CANDIDATO_%'
      THROW 51810,'NOVA_IDENTIDADE exige resultado bruto sem candidato após busca completa.',1;
   IF @universo IS NULL OR LTRIM(RTRIM(@universo))=N''
      THROW 51811,'NOVA_IDENTIDADE exige referência do universo completo.',1;
   IF @estado=N'REFERENCIA' AND @current=@initial
   BEGIN
     SET @versao_resultado=@versao;
     RETURN;
   END;
   IF @estado=N'REFERENCIA'
      THROW 51812,'Referência existente exige recomposição explícita antes de NOVA_IDENTIDADE.',1;
   IF @externo IS NOT NULL
      THROW 51813,'Associação externa histórica exige recomposição explícita antes de retornar ao initial_uuid.',1;
 END
 ELSE IF @resultado=N'INDEFINIDA'
 BEGIN
   IF @canonical_uuid IS NOT NULL OR @status_publicacao NOT IN(N'NAO_RESOLVIDO',N'CONFLITO')
      THROW 51814,'INDEFINIDA não pode escolher destino.',1;
   IF @estado=N'REFERENCIA'
      THROW 51815,'Publicação indefinida não pode apagar referência progressiva existente.',1;
   IF @estado=N'INDEFINIDA'
   BEGIN
     SET @versao_resultado=@versao;
     RETURN;
   END;
 END
 ELSE
   THROW 51816,'Resultado de publicação progressiva desconhecido.',1;

 IF @canonical_uuid IS NOT NULL
 BEGIN
   SET @reference_resource=N'JORNADA:COMPOSICAO:REF:'+LOWER(CONVERT(NVARCHAR(36),@canonical_uuid));
   EXEC @reference_lock=sys.sp_getapplock
     @Resource=@reference_resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;
   IF @reference_lock<0 THROW 51817,'Não foi possível serializar referência progressiva com composição.',1;
 END;

 SET @nova=@versao+1;
 SET @now=TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00');

 INSERT identidade.pessoa_origem_progressiva_evento(
   evento_id,pessoa_origem_id,versao,tipo,estado,canonical_uuid,expected_version,resultado,target_uuid,
   evidencia_referencia,politica_versao,modelo_versao,universo_referencia,completo,ocorrido_em,linkage_run_id)
 VALUES(
   NEWID(),@pessoa_origem_id,@nova,'RESOLUCAO',
   CASE WHEN @resultado=N'INDEFINIDA' THEN 'INDEFINIDA' ELSE 'REFERENCIA' END,
   CASE WHEN @resultado=N'INDEFINIDA' THEN NULL ELSE @canonical_uuid END,
   @versao,@resultado,
   CASE WHEN @resultado=N'ASSOCIACAO_EXISTENTE' THEN @canonical_uuid ELSE NULL END,
   CONCAT(N'LINKAGE_RUN:',CONVERT(NVARCHAR(36),@linkage_run_id),N':OBS:',CONVERT(NVARCHAR(30),@pessoa_observacao_id)),
   @politica,
   CONCAT(N'LINKAGE:',CONVERT(NVARCHAR(36),@modelo_id),N':V',CONVERT(NVARCHAR(12),@modelo_versao)),
   @universo,1,@now,@linkage_run_id);

 UPDATE identidade.pessoa_origem_progressiva
 SET canonical_uuid=CASE WHEN @resultado=N'INDEFINIDA' THEN NULL ELSE @canonical_uuid END,
     estado=CASE WHEN @resultado=N'INDEFINIDA' THEN 'INDEFINIDA' ELSE 'REFERENCIA' END,
     versao=@nova,
     ultima_resolucao_em=@now,
     ultimo_destino_externo_uuid=CASE
       WHEN @resultado=N'ASSOCIACAO_EXISTENTE' AND @canonical_uuid<>@initial THEN @canonical_uuid
       WHEN @resultado=N'NOVA_IDENTIDADE' THEN NULL
       ELSE ultimo_destino_externo_uuid END,
     atualizado_em=@now
 WHERE pessoa_origem_id=@pessoa_origem_id;

 SET @versao_resultado=@nova;
END;
GO

-- Somente a decisão operacional publicada alimenta o vínculo corrente. Resultado bruto
-- sem decisão publicada permanece invisível mesmo se alguém marcar o cabeçalho como PUBLICADO.
CREATE OR ALTER VIEW identidade.v_vinculo_corrente AS
WITH probabilistico_publicado AS (
    SELECT r.pessoa_observacao_id,
           r.pessoa_uuid_publicado AS pessoa_uuid_resolvido,
           CASE
             WHEN r.resultado_publicacao=N'ASSOCIACAO_EXISTENTE'
              AND r.status=N'RESOLVIDO'
              AND r.pessoa_uuid_resolvido=r.pessoa_uuid_publicado
             THEN r.score_melhor
           END AS score_publicacao,
           r.status_publicacao AS status,
           r.motivo_publicacao AS motivo,
           r.modelo_id,r.linkage_run_id,r.publicado_em AS calculado_em,
           ROW_NUMBER() OVER(
             PARTITION BY r.pessoa_observacao_id
             ORDER BY lr.publicado_em DESC,lr.iniciado_em DESC,lr.linkage_run_id DESC) rn
    FROM identidade.linkage_resultado r
    JOIN identidade.linkage_run lr ON lr.linkage_run_id=r.linkage_run_id
    WHERE lr.status=N'PUBLICADO'
      AND r.resultado_publicacao IS NOT NULL
      AND r.status_publicacao IS NOT NULL
      AND r.publicado_em IS NOT NULL
), prob_corrente AS (
    SELECT * FROM probabilistico_publicado WHERE rn=1
), base_ativa AS (
    SELECT vf.* FROM identidade.vinculo_fonte vf WHERE vf.ativo=1
)
SELECT b.vinculo_id,b.pessoa_observacao_id,b.pessoa_uuid,b.metodo_resolucao,b.score,b.status,b.motivo,b.modelo_id,b.linkage_run_id,b.resolvido_em
FROM base_ativa b
WHERE b.metodo_resolucao IN(N'CPF_DETERMINISTICO',N'UUID_JORNADA_RETROALIMENTACAO',N'CORRECAO_GOVERNADA',N'CONFLITO_GOVERNADO')
   OR NOT EXISTS (SELECT 1 FROM prob_corrente p WHERE p.pessoa_observacao_id=b.pessoa_observacao_id)
UNION ALL
SELECT CAST(NULL AS BIGINT),p.pessoa_observacao_id,p.pessoa_uuid_resolvido,N'LINKAGE_PROBABILISTICO',
       p.score_publicacao,p.status,p.motivo,p.modelo_id,p.linkage_run_id,p.calculado_em
FROM prob_corrente p
WHERE NOT EXISTS (
    SELECT 1 FROM base_ativa b
    WHERE b.pessoa_observacao_id=p.pessoa_observacao_id
      AND b.metodo_resolucao IN(N'CPF_DETERMINISTICO',N'UUID_JORNADA_RETROALIMENTACAO',N'CORRECAO_GOVERNADA',N'CONFLITO_GOVERNADO'));
GO

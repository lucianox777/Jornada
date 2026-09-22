-- Ensaio sintético DEV: impede apagar dados e iniciar uma carga com Processors
-- residentes de outros nós que não compartilham o diretório Bronze temporário.
-- Executar APÓS provisionar o banco, mas ANTES da limpeza DEV.
SET NOCOUNT ON;
DECLARE @perfil NVARCHAR(32)=(
    SELECT CONVERT(NVARCHAR(32),value)
    FROM sys.extended_properties
    WHERE class=0 AND name=N'Jornada.EnvironmentProfile'
);
IF ISNULL(@perfil,N'')<>N'Development'
    THROW 51840,'Ensaio sintetico permitido somente no banco Development.',1;
IF OBJECT_ID(N'controle.runtime_componente',N'U') IS NULL
    THROW 51841,'Tabela de heartbeat operacional ausente; ensaio sintetico nao autorizado.',1;

IF EXISTS(
    SELECT 1 FROM controle.runtime_componente
    WHERE componente=N'Processor'
      AND status=N'RUNNING'
      AND heartbeat_em>=DATEADD(SECOND,-35,SYSUTCDATETIME())
)
BEGIN
    SELECT node_id,machine_name,heartbeat_em
    FROM controle.runtime_componente
    WHERE componente=N'Processor'
      AND status=N'RUNNING'
      AND heartbeat_em>=DATEADD(SECOND,-35,SYSUTCDATETIME())
    ORDER BY node_id;
    THROW 51842,'Ensaio sintetico bloqueado: Processor externo ativo neste banco. Use um banco DEV isolado ou pare os Processors externos antes da limpeza.',1;
END;
PRINT N'Preflight DEV: nenhum Processor externo ativo no banco.';

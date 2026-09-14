SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Monitor operacional: presença viva dos processos residentes por nó.
-- Estado transitório fica no SQL compartilhado para que qualquer API do cluster possa enxergar o conjunto.
IF OBJECT_ID(N'controle.runtime_componente', N'U') IS NULL
BEGIN
    CREATE TABLE controle.runtime_componente(
        node_id NVARCHAR(64) NOT NULL,
        componente NVARCHAR(80) NOT NULL,
        machine_name NVARCHAR(128) NOT NULL,
        instance_id UNIQUEIDENTIFIER NOT NULL,
        process_id INT NOT NULL,
        status NVARCHAR(20) NOT NULL,
        iniciado_em DATETIMEOFFSET(7) NOT NULL,
        heartbeat_em DATETIMEOFFSET(7) NOT NULL,
        encerrado_em DATETIMEOFFSET(7) NULL,
        versao NVARCHAR(80) NULL,
        configuration_bundle_version NVARCHAR(80) NULL,
        solution_schema_expected NVARCHAR(32) NULL,
        CONSTRAINT pk_runtime_componente PRIMARY KEY(node_id, componente),
        CONSTRAINT ck_runtime_componente_node CHECK(LEN(node_id) BETWEEN 1 AND 64),
        CONSTRAINT ck_runtime_componente_nome CHECK(LEN(componente) BETWEEN 1 AND 80),
        CONSTRAINT ck_runtime_componente_pid CHECK(process_id > 0),
        CONSTRAINT ck_runtime_componente_status CHECK(status IN(N'RUNNING',N'STOPPED')),
        CONSTRAINT ck_runtime_componente_temporal CHECK(heartbeat_em >= iniciado_em AND (encerrado_em IS NULL OR encerrado_em >= iniciado_em))
    );
END;
GO

IF COL_LENGTH(N'controle.runtime_componente', N'configuration_bundle_version') IS NULL
    ALTER TABLE controle.runtime_componente ADD configuration_bundle_version NVARCHAR(80) NULL;
GO

IF COL_LENGTH(N'controle.runtime_componente', N'solution_schema_expected') IS NULL
    ALTER TABLE controle.runtime_componente ADD solution_schema_expected NVARCHAR(32) NULL;
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'controle.runtime_componente')
      AND name=N'IX_runtime_componente_heartbeat')
BEGIN
    CREATE INDEX IX_runtime_componente_heartbeat
        ON controle.runtime_componente(heartbeat_em DESC)
        INCLUDE(node_id,componente,machine_name,status,instance_id,process_id,iniciado_em,encerrado_em,versao,configuration_bundle_version,solution_schema_expected);
END;
GO

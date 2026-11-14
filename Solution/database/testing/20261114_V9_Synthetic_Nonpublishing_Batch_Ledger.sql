SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO
-- TEST FIXTURE ONLY; NEVER APPLY AS A PRODUCTION MIGRATION.
-- Synthetic, isolated, nonpublishing rehearsal of batch accounting and
-- transactional status transitions. No linkage_run, linkage_resultado, Gold,
-- Serving, real observation IDs or TEST sealed-corpus artifacts.
IF DB_NAME() <> N'JornadaHarness'
    THROW 52540, 'V9 synthetic ledger schema permitted only in JornadaHarness.', 1;
GO
IF SCHEMA_ID(N'identidade') IS NULL EXEC(N'CREATE SCHEMA identidade');
GO
CREATE TABLE identidade.v9_synthetic_rehearsal_run
(
    rehearsal_run_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    modelo_id UNIQUEIDENTIFIER NOT NULL,
    winner_sha256 CHAR(64) NOT NULL,
    candidate_graph_sha256 CHAR(64) NOT NULL,
    decisions_sha256 CHAR(64) NOT NULL,
    identity_groups_sha256 CHAR(64) NOT NULL,
    status NVARCHAR(40) NOT NULL
        CONSTRAINT ck_v9_synthetic_run_status CHECK (status IN
            (N'PREPARANDO',N'EXECUTANDO',N'CONCLUIDO_SEM_PUBLICACAO')),
    elegiveis INT NOT NULL CHECK (elegiveis BETWEEN 2 AND 128),
    avaliados INT NOT NULL CHECK (avaliados >= 0),
    resolvidos INT NOT NULL CHECK (resolvidos >= 0),
    nao_resolvidos INT NOT NULL CHECK (nao_resolvidos >= 0),
    conflitos INT NOT NULL CHECK (conflitos >= 0),
    sem_candidato INT NOT NULL CHECK (sem_candidato >= 0),
    batch_size INT NOT NULL CHECK (batch_size BETWEEN 1 AND 128),
    expected_batches INT NOT NULL CHECK (expected_batches BETWEEN 1 AND 128),
    completed_batches INT NOT NULL DEFAULT 0,
    criado_em DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    encerrado_em DATETIME2(3) NULL,
    CONSTRAINT ck_v9_synthetic_run_accounting CHECK (
       avaliados <= elegiveis AND
       resolvidos + nao_resolvidos + conflitos = elegiveis AND
       sem_candidato <= nao_resolvidos AND
       completed_batches <= expected_batches),
    CONSTRAINT ck_v9_synthetic_run_terminal CHECK (
       status <> N'CONCLUIDO_SEM_PUBLICACAO' OR
       (avaliados = elegiveis AND completed_batches = expected_batches AND encerrado_em IS NOT NULL))
);
GO
CREATE TABLE identidade.v9_synthetic_rehearsal_batch
(
    rehearsal_run_id UNIQUEIDENTIFIER NOT NULL
        REFERENCES identidade.v9_synthetic_rehearsal_run(rehearsal_run_id),
    batch_number INT NOT NULL CHECK (batch_number BETWEEN 1 AND 128),
    observation_count INT NOT NULL CHECK (observation_count BETWEEN 1 AND 128),
    first_observation_id INT NOT NULL CHECK (first_observation_id > 0),
    last_observation_id INT NOT NULL CHECK (last_observation_id >= first_observation_id),
    observation_ids_sha256 CHAR(64) NOT NULL,
    CONSTRAINT pk_v9_synthetic_rehearsal_batch PRIMARY KEY (rehearsal_run_id, batch_number)
);
GO

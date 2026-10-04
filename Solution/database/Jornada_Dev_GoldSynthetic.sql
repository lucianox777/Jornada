SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID('identidade.pessoa','U') IS NULL OR OBJECT_ID('gold.pessoa','U') IS NULL
    THROW 51620, 'Schema Jornada ausente; aplique o schema DEV antes de carregar a Gold sintética.', 1;

DECLARE @fixture TABLE(
    pessoa_uuid UNIQUEIDENTIFIER PRIMARY KEY,
    nome_completo NVARCHAR(500) NOT NULL,
    data_nascimento DATE NOT NULL,
    nome_mae NVARCHAR(500) NOT NULL
);

INSERT @fixture(pessoa_uuid,nome_completo,data_nascimento,nome_mae) VALUES
('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbb001',N'Ana Clara Monteiro','1984-02-14',N'Helena Monteiro'),
('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbb002',N'Carlos Eduardo Lima','1979-06-03',N'Maria das Graças Lima'),
('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbb003',N'Fernanda Ribeiro Alves','1991-11-28',N'Lucia Ribeiro Alves'),
('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbb004',N'João Pedro Nascimento','1987-08-19',N'Rosa Maria Nascimento'),
('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbb005',N'Mariana Costa Freitas','1995-01-07',N'Patricia Costa Freitas');

BEGIN TRAN;

INSERT identidade.pessoa(pessoa_uuid,status)
SELECT f.pessoa_uuid,N'ATIVO'
FROM @fixture f
WHERE NOT EXISTS(SELECT 1 FROM identidade.pessoa p WHERE p.pessoa_uuid=f.pessoa_uuid);

UPDATE g
SET cpf=NULL,
    status_cpf=N'SEM_CPF',
    nome_completo=f.nome_completo,
    data_nascimento=f.data_nascimento,
    nome_mae=f.nome_mae,
    fontes_distintas=1,
    estado_concordancia=N'BASELINE_FONTE_UNICA',
    estado_identidade=N'REFERENCIA',
    completude_nucleo=N'COMPLETO',
    atualizado_em=SYSUTCDATETIME()
FROM gold.pessoa g
JOIN @fixture f ON f.pessoa_uuid=g.pessoa_uuid;

INSERT gold.pessoa(
    pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,
    fontes_distintas,estado_concordancia,estado_identidade,completude_nucleo,atualizado_em
)
SELECT
    f.pessoa_uuid,NULL,N'SEM_CPF',f.nome_completo,f.data_nascimento,f.nome_mae,
    1,N'BASELINE_FONTE_UNICA',N'REFERENCIA',N'COMPLETO',SYSUTCDATETIME()
FROM @fixture f
WHERE NOT EXISTS(SELECT 1 FROM gold.pessoa g WHERE g.pessoa_uuid=f.pessoa_uuid);

COMMIT;

SELECT COUNT_BIG(*) AS fixture_rows
FROM gold.pessoa g
JOIN @fixture f ON f.pessoa_uuid=g.pessoa_uuid;

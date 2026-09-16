SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;

DECLARE @people BIGINT = $(SCALE_PEOPLE);
DECLARE @seed INT = $(SCALE_SEED);
DECLARE @collisionModulo INT = $(SCALE_COLLISION_MODULO);

IF @people < 1000
    THROW 51560, 'SCALE_PEOPLE inválido para diversificação.', 1;

DECLARE @first TABLE(id INT NOT NULL PRIMARY KEY, value NVARCHAR(80) NOT NULL);
INSERT @first(id,value) VALUES
(1,N'Ana'),(2,N'Bruno'),(3,N'Carla'),(4,N'Daniel'),(5,N'Eduardo'),
(6,N'Fernanda'),(7,N'Gabriel'),(8,N'Helena'),(9,N'Igor'),(10,N'Juliana'),
(11,N'Lucas'),(12,N'Mariana'),(13,N'Nicolas'),(14,N'Patricia'),(15,N'Rafael'),
(16,N'Sabrina'),(17,N'Tiago'),(18,N'Vanessa'),(19,N'William'),(20,N'Yasmin');

DECLARE @motherFirst TABLE(id INT NOT NULL PRIMARY KEY, value NVARCHAR(80) NOT NULL);
INSERT @motherFirst(id,value) VALUES
(1,N'Maria'),(2,N'Ana'),(3,N'Francisca'),(4,N'Antonia'),(5,N'Adriana'),
(6,N'Juliana'),(7,N'Marcia'),(8,N'Fernanda'),(9,N'Patricia'),(10,N'Aline'),
(11,N'Sandra'),(12,N'Camila'),(13,N'Amanda'),(14,N'Bruna'),(15,N'Luciana'),
(16,N'Vanessa'),(17,N'Daniela'),(18,N'Simone'),(19,N'Renata'),(20,N'Claudia');

DECLARE @surname TABLE(id INT NOT NULL PRIMARY KEY, value NVARCHAR(80) NOT NULL);
INSERT @surname(id,value) VALUES
(1,N'Silva'),(2,N'Santos'),(3,N'Oliveira'),(4,N'Souza'),(5,N'Pereira'),
(6,N'Costa'),(7,N'Rodrigues'),(8,N'Almeida'),(9,N'Nascimento'),(10,N'Lima'),
(11,N'Araujo'),(12,N'Fernandes'),(13,N'Carvalho'),(14,N'Gomes'),(15,N'Martins'),
(16,N'Rocha'),(17,N'Ribeiro'),(18,N'Alves'),(19,N'Monteiro'),(20,N'Mendes'),
(21,N'Barbosa'),(22,N'Freitas'),(23,N'Cardoso'),(24,N'Correia'),(25,N'Dias'),
(26,N'Castro'),(27,N'Campos'),(28,N'Moraes'),(29,N'Ramos'),(30,N'Goncalves'),
(31,N'Lopes'),(32,N'Moreira'),(33,N'Teixeira'),(34,N'Vieira'),(35,N'Pinto'),
(36,N'Moura'),(37,N'Cunha');

CREATE TABLE #scale_names(
    n BIGINT NOT NULL PRIMARY KEY,
    nome NVARCHAR(500) NOT NULL,
    mae NVARCHAR(500) NOT NULL
);

;WITH source_n AS (
    SELECT DISTINCT TRY_CONVERT(BIGINT,RIGHT(po.codigo_pessoa_origem,10)) AS n
    FROM silver.pessoa_origem po
    WHERE po.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%'
)
INSERT #scale_names(n,nome,mae)
SELECT n.n,
       CONCAT(f.value,N' ',s1.value,N' ',s2.value),
       CONCAT(mf.value,N' ',ms1.value,N' ',ms2.value)
FROM source_n n
JOIN @first f ON f.id=CONVERT(INT,((n.n+@seed-1)%20)+1)
JOIN @surname s1 ON s1.id=CONVERT(INT,((n.n*7+@seed)%37)+1)
JOIN @surname s2 ON s2.id=CONVERT(INT,((n.n*13+@seed+5)%37)+1)
JOIN @motherFirst mf ON mf.id=CONVERT(INT,((n.n*11+@seed-1)%20)+1)
JOIN @surname ms1 ON ms1.id=CONVERT(INT,((n.n*17+@seed)%37)+1)
JOIN @surname ms2 ON ms2.id=CONVERT(INT,((n.n*19+@seed+7)%37)+1)
WHERE n.n IS NOT NULL AND n.n BETWEEN 1 AND @people;

IF (SELECT COUNT_BIG(*) FROM #scale_names)<>@people
    THROW 51561, 'Diversificação não encontrou toda a população SCALE-SEHAB.', 1;

-- A fonte determinística e a Gold recebem exatamente a mesma identidade textual.
UPDATE po
SET nome_completo=sn.nome,
    nome_cmp=UPPER(sn.nome),
    nome_mae=sn.mae,
    nome_mae_cmp=UPPER(sn.mae)
FROM silver.pessoa_observacao po
JOIN #scale_names sn ON sn.n=TRY_CONVERT(BIGINT,RIGHT(po.codigo_pessoa_origem,10))
WHERE po.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%';

UPDATE g
SET nome_completo=sn.nome,
    nome_mae=sn.mae
FROM gold.pessoa g
JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_uuid=g.pessoa_uuid AND vc.status='RESOLVIDO'
JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=vc.pessoa_observacao_id
JOIN #scale_names sn ON sn.n=TRY_CONVERT(BIGINT,RIGHT(po.codigo_pessoa_origem,10))
WHERE po.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%';

-- A segunda fonte conserva as mutações do corpus, mas sobre tokens alfabéticos variados.
UPDATE po
SET nome_completo=CASE
        WHEN sn.n%11=0 THEN CONCAT(sn.nome,N' Filho')
        WHEN sn.n%7=0 THEN CONCAT(LEFT(sn.nome,1),N'. ',SUBSTRING(sn.nome,CHARINDEX(N' ',sn.nome)+1,500))
        ELSE sn.nome END,
    nome_cmp=UPPER(CASE
        WHEN sn.n%11=0 THEN CONCAT(sn.nome,N' Filho')
        WHEN sn.n%7=0 THEN CONCAT(LEFT(sn.nome,1),N'. ',SUBSTRING(sn.nome,CHARINDEX(N' ',sn.nome)+1,500))
        ELSE sn.nome END),
    nome_mae=CASE
        WHEN sn.n%13=0 THEN CONCAT(N'Maria ',SUBSTRING(sn.mae,CHARINDEX(N' ',sn.mae)+1,500))
        ELSE sn.mae END,
    nome_mae_cmp=UPPER(CASE
        WHEN sn.n%13=0 THEN CONCAT(N'Maria ',SUBSTRING(sn.mae,CHARINDEX(N' ',sn.mae)+1,500))
        ELSE sn.mae END)
FROM silver.pessoa_observacao po
JOIN #scale_names sn ON sn.n=TRY_CONVERT(BIGINT,RIGHT(po.codigo_pessoa_origem,10))
WHERE po.codigo_pessoa_origem LIKE N'SCALE-SMADS-%';

-- As colisões deixam de apontar todas para "Pessoa Colisao 000000". Cada caso usa o nome
-- da pessoa vizinha do mesmo par de data de nascimento, mantendo a mãe verdadeira. Isso cria
-- concorrência evidencial controlada, sem transformar um único token numa chave universal.
;WITH pending AS (
    SELECT po.pessoa_observacao_id,
           po.codigo_pessoa_origem,
           TRY_CONVERT(BIGINT,RIGHT(po.codigo_pessoa_origem,10)) AS pending_n,
           ((TRY_CONVERT(BIGINT,RIGHT(po.codigo_pessoa_origem,10))-1)%@people)+1 AS truth_n
    FROM silver.pessoa_observacao po
    WHERE po.codigo_pessoa_origem LIKE N'SCALE-PEND-%'
), resolved AS (
    SELECT p.*,
           CASE WHEN p.truth_n%2=0 THEN p.truth_n-1
                WHEN p.truth_n<@people THEN p.truth_n+1
                ELSE p.truth_n-1 END AS neighbor_n
    FROM pending p
)
UPDATE po
SET nome_completo=CASE
        WHEN @collisionModulo>0 AND r.pending_n%NULLIF(@collisionModulo,0)=0 THEN neighbor.nome
        WHEN r.pending_n%9=0 THEN CONCAT(truth.nome,N' Junior')
        WHEN r.pending_n%6=0 THEN CONCAT(LEFT(truth.nome,1),N'. ',SUBSTRING(truth.nome,CHARINDEX(N' ',truth.nome)+1,500))
        ELSE truth.nome END,
    nome_cmp=UPPER(CASE
        WHEN @collisionModulo>0 AND r.pending_n%NULLIF(@collisionModulo,0)=0 THEN neighbor.nome
        WHEN r.pending_n%9=0 THEN CONCAT(truth.nome,N' Junior')
        WHEN r.pending_n%6=0 THEN CONCAT(LEFT(truth.nome,1),N'. ',SUBSTRING(truth.nome,CHARINDEX(N' ',truth.nome)+1,500))
        ELSE truth.nome END),
    nome_mae=CASE
        WHEN r.pending_n%8=0 THEN CONCAT(LEFT(truth.mae,1),N'. ',SUBSTRING(truth.mae,CHARINDEX(N' ',truth.mae)+1,500))
        ELSE truth.mae END,
    nome_mae_cmp=UPPER(CASE
        WHEN r.pending_n%8=0 THEN CONCAT(LEFT(truth.mae,1),N'. ',SUBSTRING(truth.mae,CHARINDEX(N' ',truth.mae)+1,500))
        ELSE truth.mae END)
FROM silver.pessoa_observacao po
JOIN resolved r ON r.pessoa_observacao_id=po.pessoa_observacao_id
JOIN #scale_names truth ON truth.n=r.truth_n
JOIN #scale_names neighbor ON neighbor.n=r.neighbor_n;

-- O hash acompanha o conteúdo sintético efetivamente persistido depois da diversificação.
UPDATE po
SET conteudo_hash=LOWER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',CONCAT(
        'SCALE-DIVERSE:',po.codigo_pessoa_origem,':',ISNULL(po.cpf,N''),':',
        ISNULL(po.nome_completo,N''),':',ISNULL(CONVERT(VARCHAR(10),po.data_nascimento,23),''),':',
        ISNULL(po.nome_mae,N''))),2))
FROM silver.pessoa_observacao po
WHERE po.codigo_pessoa_origem LIKE N'SCALE-%';

SELECT COUNT_BIG(*) AS diversified_people,
       COUNT(DISTINCT LEFT(nome,CHARINDEX(N' ',nome+N' ')-1)) AS distinct_first_names,
       COUNT(DISTINCT LEFT(mae,CHARINDEX(N' ',mae+N' ')-1)) AS distinct_mother_first_names
FROM #scale_names;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
 Materializa em gold.pessoa apenas o conceito de NOME que pode ser derivado de forma
 inequívoca a partir de nome_completo segundo a semântica publicada do produto
 Censo Demográfico 2022 - Nomes no Brasil: o primeiro nome informado.

 Não há inferência de SOBRENOME a partir dos tokens restantes. A fronteira original
 entre nome/nome composto e sobrenomes não existe no cadastro da Jornada.

 A expressão substitui TAB/CR/LF por espaço antes de localizar o primeiro token para
 manter paridade semântica com a tokenização por whitespace do contrato C#.
 A coluna é PERSISTED: fica materializada, participa de replay automaticamente e pode
 receber índice em etapa posterior sem duplicar lógica nos diferentes caminhos de
 recomposição de gold.pessoa.
*/
IF COL_LENGTH('gold.pessoa','nome') IS NULL
BEGIN
    ALTER TABLE gold.pessoa ADD nome AS (
        CONVERT(nvarchar(200),
            LEFT(
                LTRIM(RTRIM(
                    REPLACE(REPLACE(REPLACE(nome_completo,NCHAR(9),N' '),NCHAR(13),N' '),NCHAR(10),N' ')
                )),
                CHARINDEX(
                    N' ',
                    LTRIM(RTRIM(
                        REPLACE(REPLACE(REPLACE(nome_completo,NCHAR(9),N' '),NCHAR(13),N' '),NCHAR(10),N' ')
                    )) + N' '
                ) - 1
            )
        )
    ) PERSISTED;
END;
GO

IF COL_LENGTH('gold.pessoa','nome_semantica_versao') IS NULL
BEGIN
    ALTER TABLE gold.pessoa ADD nome_semantica_versao varchar(80) NOT NULL
        CONSTRAINT DF_gold_pessoa_nome_semantica_versao
        DEFAULT('IBGE_CENSO_2022_NOMES_PUBLICACAO_V1') WITH VALUES;
END;
GO

IF NOT EXISTS(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID('gold.pessoa')
      AND name='ck_gold_pessoa_nome_semantica_versao')
BEGIN
    ALTER TABLE gold.pessoa WITH CHECK ADD CONSTRAINT ck_gold_pessoa_nome_semantica_versao
        CHECK(nome_semantica_versao='IBGE_CENSO_2022_NOMES_PUBLICACAO_V1');
END;
GO

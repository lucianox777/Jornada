-- Consulta de identidade progressiva por chave de origem, sem efeitos colaterais.
-- O filtro do Gestor é obrigatório também no banco; não há consulta global por código interno.
-- Pré-requisito: 20260908_Identidade_Progressiva_Serving.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID('serving.v_identidade_origem_progressiva','V') IS NULL
    THROW 51371,'Projeção Serving da identidade progressiva não instalada.',1;
GO
CREATE OR ALTER PROCEDURE identidade.sp_consultar_origem_progressiva
    @gestor_codigo NVARCHAR(30),
    @sistema_codigo NVARCHAR(80),
    @codigo_pessoa_origem NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    IF @gestor_codigo IS NULL OR LEN(LTRIM(RTRIM(@gestor_codigo)))=0
       OR @sistema_codigo IS NULL OR LEN(LTRIM(RTRIM(@sistema_codigo)))=0
       OR @codigo_pessoa_origem IS NULL OR LEN(LTRIM(RTRIM(@codigo_pessoa_origem)))=0
        THROW 51372,'Chave de origem e Gestor são obrigatórios.',1;

    SELECT pessoa_origem_id,gestor_codigo,sistema_origem_codigo,codigo_pessoa_origem,
           initial_uuid,canonical_uuid,estado,versao,apta_contagem_pessoa_canonica,
           criado_em,atualizado_em,ultima_resolucao_em
    FROM serving.v_identidade_origem_progressiva
    WHERE gestor_codigo COLLATE Latin1_General_100_BIN2=@gestor_codigo COLLATE Latin1_General_100_BIN2
      AND sistema_origem_codigo COLLATE Latin1_General_100_BIN2=@sistema_codigo COLLATE Latin1_General_100_BIN2
      AND codigo_pessoa_origem COLLATE Latin1_General_100_BIN2=@codigo_pessoa_origem COLLATE Latin1_General_100_BIN2;
END;
GO

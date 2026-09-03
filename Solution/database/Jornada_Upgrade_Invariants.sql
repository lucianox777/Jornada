SET NOCOUNT ON;
DECLARE @snapshot NVARCHAR(MAX)=(
SELECT
  (SELECT COUNT_BIG(*) FROM ref.gestor) AS [counts.gestor],
  (SELECT COUNT_BIG(*) FROM bronze.entrega) AS [counts.bronzeEntrega],
  (SELECT COUNT_BIG(*) FROM bronze.entrega_arquivo) AS [counts.bronzeArquivo],
  (SELECT COUNT_BIG(*) FROM silver.pessoa_observacao) AS [counts.silverPessoaObservacao],
  (SELECT COUNT_BIG(*) FROM identidade.pessoa) AS [counts.identidadePessoa],
  (SELECT COUNT_BIG(*) FROM identidade.identity_map) AS [counts.identityMap],
  (SELECT COUNT_BIG(*) FROM identidade.vinculo_fonte) AS [counts.vinculoFonte],
  (SELECT COUNT_BIG(*) FROM gold.pessoa) AS [counts.goldPessoa],
  (SELECT COUNT_BIG(*) FROM gold.beneficio_concedido) AS [counts.goldBeneficio],
  (SELECT COUNT_BIG(*) FROM gold.servico_prestado) AS [counts.goldServico],
  (SELECT COUNT_BIG(*) FROM identidade.identity_map im LEFT JOIN identidade.pessoa p ON p.pessoa_uuid=im.pessoa_uuid WHERE p.pessoa_uuid IS NULL) AS [violations.identityMapMissingPerson],
  (SELECT COUNT_BIG(*) FROM identidade.vinculo_fonte vf LEFT JOIN identidade.pessoa p ON p.pessoa_uuid=vf.pessoa_uuid WHERE vf.status='RESOLVIDO' AND p.pessoa_uuid IS NULL) AS [violations.resolvedVinculoMissingPerson],
  (SELECT COUNT_BIG(*) FROM identidade.pessoa WHERE pessoa_uuid_sucessor=pessoa_uuid) AS [violations.selfSuccessor],
  (SELECT COUNT_BIG(*) FROM gold.pessoa gp LEFT JOIN identidade.pessoa p ON p.pessoa_uuid=gp.pessoa_uuid WHERE p.pessoa_uuid IS NULL) AS [violations.goldPersonMissingIdentity],
  (SELECT COUNT_BIG(*) FROM (SELECT identificador FROM identidade.identity_map WHERE tipo='CPF' AND vigencia_fim IS NULL GROUP BY identificador HAVING COUNT_BIG(*)>1) d) AS [violations.activeCpfDuplicate],
  (SELECT COUNT_BIG(*) FROM (SELECT pessoa_observacao_id FROM identidade.vinculo_fonte WHERE ativo=1 GROUP BY pessoa_observacao_id HAVING COUNT_BIG(*)>1) d) AS [violations.multipleActiveVinculoPerObservation]
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
SELECT @snapshot;

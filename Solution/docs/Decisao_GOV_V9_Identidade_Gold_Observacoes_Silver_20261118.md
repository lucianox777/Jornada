# GOV-V9-IDENTIDADE-GOLD-OBSERVACOES-SILVER-20261118

**Estado:** aprovado pelo Product Owner em 18/11/2026; implementação proposta nesta PR, condicionada a CI, revisão e validação física.

## Contexto e decisão
O Product Owner aprovou a separação explícita entre **identidade Gold consolidada** e **observações Silver individuais** no grafo operacional V9. Uma pessoa Gold pode possuir múltiplas observações Silver, inclusive de gestores distintos. Não se pode exigir relação 1:1 nem perder observações.

## Justificativa e semântica
- O UUID Gold representa a pessoa; `pessoa_observacao_id` identifica cada observação Silver.
- A consulta de bloqueio V9 retorna UUIDs Gold. Para construir arestas diagnósticas entre observações, cada UUID candidato é expandido para **todas** as observações Silver da coorte congelada associadas àquele Gold.
- Nunca construir aresta entre duas observações do **mesmo Gold**, mesmo de gestores diferentes: seria autocomparação da pessoa.
- Manter filtro de mesmo gestor, unicidade dos IDs Silver, cobertura da coorte e checagem de direções simétricas; qualquer divergência deve falhar fechado.
- Verificar pertencimento Gold por conjunto de UUIDs distintos, não pelo número de observações Silver.
- O censo SQL separado e a prévia do grafo devem compartilhar a mesma semântica de identidade, mas isso **não** constitui prova física independente.

## Impacto, limites e precedência
Altera exclusivamente o diagnóstico do executor operacional, sem modificar calibrador, artefato científico imutável, parâmetros, algoritmo de seleção ou persistência. A execução produtiva continua bloqueada. A expansão de UUID Gold em múltiplas observações Silver pode elevar o número de arestas; requer ensaios físicos de cardinalidade, desempenho e paridade antes de certificação. Não há autorização para truncar o grafo ou reinterpretar probabilidades.

**Precedência:** complementa GOV-V9-GRAFO-OPERACIONAL-INTEGRAL-COORTE-20261117 e GOV-V9-RUNNER-ARTEFATO-ARQUIVO-FONTE-UNICA-20261117; respeita GOV-DOC-01. Substitui apenas a hipótese diagnóstica implícita de um único Source por UUID Gold.

## Rastreabilidade
Aprovação expressa do PO na conversa do projeto em 18/11/2026 ("aprovo, pode continuar"). Implementação: branch `feat/v9-gold-silver-distinct-identity-20261118`, arquivos `V9OperationalCohortGraphPreview.cs`, `V9OperationalSqlEdgeCensus.cs`, `V9OperationalGoldCohortMembership.cs`, `V9OperationalSnapshotGraphVerification.cs`, teste `test_v9_gold_silver_distinct_identity.py`. Pendente: CI e validação física com multiplicidade real.

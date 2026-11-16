# GOV-V9-TEST-PARIDADE-GOLD-CANDIDATOS-RUNASYNC-20261116

**Autoridade:** continuidade técnica solicitada pelo Product Owner em 16/11/2026. **Estado:** proposta em PR (não vigente até CI e merge). **Política:** GOV-DOC-01. **Issue:** #1584.

## Contexto e problema
A PR #1588 (squash `8768de08546cbd297092a14f5c82547d8a52cb14`; CI `43805277204` e SQL físico `43805277246` verdes) comprovou, em SQL descartável, o mesmo grafo e decisões persistidas com 200 observações e páginas 100×2 versus 200×1. Contudo, a checagem operacional de `identidade.blocking_chave` lia somente UUIDs, não conferia a integridade do registro correspondente de `gold.pessoa`. Um índice válido e um Gold alterado poderiam produzir candidato aparentemente válido sem igualdade de dados.

## Alteração proposta
1. Antes do primeiro lote `identidade.linkage_resultado` do **RunAsync real** (ponte `TEST_ONLY` opt-in), confrontar os 200 registros físicos `gold.pessoa` com todo o universo Silver congelado. Exigir cardinalidade exata, UUID diagnóstico exclusivo, ausência de CPF, estado `REFERENCIA`, nome, nome materno e data de nascimento exatamente iguais. Falhar fechado se houver ausência, duplicidade, referência externa ou alteração desde a materialização do blocking.
2. Para cada observação, consumir a consulta de candidatos de `BlockingProjectionCandidateQueryBuilder` com **JOIN real a `gold.pessoa`**, nos campos de referência usados no scoring operacional, e verificar identidade e dados de cada candidato antes da prova de simetria, conjunto de arestas e digest.
3. Preservar o caminho `JornadaHarness` apenas, materialização imutável de 81 células TF4, winner SHA, sem publicação, sem ativação de modelo e sem acesso ao corpus científico selado.
4. Incluir teste SQL físico negativo: alterar temporariamente o nome materno de uma referência Gold mantendo o índice `blocking_chave`; a execução `RunAsync` deve falhar **antes** da primeira decisão persistida; restaurar o valor, garantir ausência de exposição e manter também o teste negativo de fingerprint da projeção.

## Justificativa
A paridade do grafo por UUID não comprova que as informações realmente lidas pelo scorer (nome, mãe, nascimento) correspondam ao universo congelado. Essa checagem impede aceitar silenciosamente candidatos obsoletos em uma coorte TEST diagnóstica e acrescenta evidência de integridade entre Silver, blocking e Gold.

## Impactos, precedência e limites
Arquivos: `Solution/src/Jornada.Linkage.Runner/V9OperationalBlockingChaveCohortParity.cs`, `Solution/tests/Jornada.Tests/Unit/V9FullSchemaNonPublishingRunnerTests.cs`, este registro e `Solution/docs/Indice_Decisoes_Vigentes.md`. CI: `jornada-pr-required-fast` e SQL físico `full-schema-runasync-nonpublish` do workflow `.github/workflows/v9-operational-model-sql-smoke.yml`.

Complementa `GOV-V9-RUNASYNC-PARIDADE-FISICA-LOTES-100-200-20261116` e não revoga nenhuma decisão científica anterior. Não muda a lógica FS, TF4, thresholds ou seleção de vencedor. Há custo adicional de leitura SQL proporcional à coorte de diagnóstico, com teto de 512 observações, portanto **não** é solução de escala 100k.

**Limitação explícita:** Gold e UUIDs neste ensaio são sintéticos e descartáveis; a ponte V9 ainda não substitui a seleção/ativação produtiva `GetActiveModelAsync`. O método permanece `ProductionGoldCertified=false`; não prova paridade integral de scoring, agrupamento e ciclo de publicação em dados operacionais reais. O bloqueio `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED` e a issue #1584 permanecem. TEST científico independente não autorizado por esta PR. O TRAIN científico não depende deste ensaio isoladamente e continua sujeito ao preflight e aos gates pertinentes.

## Evidências e rastreabilidade
- Base integrada: PR #1588, SHA `8768de08546cbd297092a14f5c82547d8a52cb14`.
- Implementação nova: branch `test/v9-gold-candidate-parity-runasync-20261116`.
- Provas da nova PR: **pendentes de CI física**, a registrar após execução.

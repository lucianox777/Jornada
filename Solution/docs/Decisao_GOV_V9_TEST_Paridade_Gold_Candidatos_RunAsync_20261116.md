# GOV-V9-TEST-PARIDADE-GOLD-CANDIDATOS-RUNASYNC-20261116

**Autoridade:** continuidade técnica solicitada pelo Product Owner em 16/11/2026. **Estado:** integrado à master pela PR #1589 (squash `d571262edc6f9d1e8686874f7cab7abe33dbd2ee`). CI obrigatória `43884370292` (3/3), SQL físico `43884370284` (2/2) e pré-voo `43884370283` SUCCESS. TEST científico permanece bloqueado. **Política:** GOV-DOC-01. **Issue:** #1584.

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
- PR #1589, squash `d571262edc6f9d1e8686874f7cab7abe33dbd2ee`.
- CI obrigatória #43884370292: `unit-runtime`, `security-guards`, `deterministic-build` SUCCESS.
- SQL físico #43884370284: `one-winner-sql-smoke` e `full-schema-runasync-nonpublish` SUCCESS.
- Pré-voo de compilação #43884370283: SUCCESS; matriz física Gold holdout omitida por escopo (skipped), não considerar como ensaio Gold holdout completo.

## Extensão de cobertura — 16/11/2026 (proposta, não vigente)
Após a integração da PR #1589, o PO solicitou continuidade da implementação do TEST. A branch `test/v9-gold-reference-completeness-runasync-20261116` adiciona regressão SQL física para uma referência Gold que deixa de ser `REFERENCIA`, embora suas chaves de blocking permaneçam no banco. A preparação global do `RunAsync` deve recusar o universo incompleto antes de produzir decisões. A referência é restaurada após o ensaio, e o fingerprint de publicação permanece inalterado. Complementa o teste de nome materno obsoleto; não altera o algoritmo ou o contrato de produção, nem libera o TEST 100k. Evidências CI/SQL pendentes de aprovação da PR. Issue #1584; GOV-DOC-01.

## Integração #1591 e nova verificação de persistência (16/11/2026)
A regressão de Gold sem referência foi integrada pela PR #1591 (squash `ca9aa2b38571c2f33545b772ea2b12685c062843`), com CI #43887910907 (3/3) e SQL físico #43887910833 (2/2) SUCCESS. A nova proposta `test/v9-missing-gold-no-partial-results-20261116` reforça a mesma regressão: não basta capturar `InvalidDataException`; é necessário comprovar no ledger físico que a execução terminou `FALHOU`, com `avaliados=0` e nenhuma decisão `linkage_resultado` persistida. O teste reutiliza `AssertRunRowsAsync` e preserva a verificação de não publicação. Não muda o algoritmo, não certifica scoring produtivo, não desbloqueia TEST científico e não substitui a issue #1584. Evidência CI desta extensão pendente de PR. GOV-DOC-01.

## Integração da prova de zero resultados — 16/11/2026
PR #1592 integrada à master pelo squash `8c1584a33f68ad27431dc215ee3d1a2acaaf6401`. CI obrigatória #43888714312 (3/3) e SQL físico #43888714596 (2/2) SUCCESS. Evidência física adicionada: quando a referência Gold deixa de ser `REFERENCIA` com blocking intacto, o `RunAsync` termina `FALHOU`, `avaliados=0`, e `AssertRunRowsAsync` comprova zero decisões persistidas. Este é ensaio sintético descartável; não comprova integração produtiva, não autoriza TEST científico 100k e não encerra issue #1584. GOV-DOC-01.

## Proposta de invariância TF4 em quatro lotes — 16/11/2026
Contexto: o teste físico do RunAsync real já compara 100×2 e 200×1 contra a mesma referência global congelada, mas não exercita quatro transações de paginação. Decisão técnica proposta: acrescentar execução `batchSize=50` (50×4) com o mesmo vencedor SQL pinado, grafo de candidatos e Gold, verificar SHA do grafo e todas as 200 decisões persistidas contra `pinned.ReplayTest` e ausência de publicação. Justificativa: detectar dependência indevida da fronteira dos lotes, inclusive ligações cruzadas. Impacto: mais uma execução SQL descartável e maior duração do smoke; nenhuma mudança na lógica de produção. Precedência: complementa #1588/#1589/#1591/#1592; não prova `ResolveWithoutCpfAsync` produtivo nem libera o TEST científico 100k. Issue #1584; evidências CI pendentes; GOV-DOC-01.

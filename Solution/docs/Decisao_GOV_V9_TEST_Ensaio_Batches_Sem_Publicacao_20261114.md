# GOV-V9-TEST-ENSAIO-BATCHES-SEM-PUBLICACAO-20261114 — recuperação SQL paginada e Resolve global

**Autoridade:** Product Owner, 14/11/2026 (America/Sao_Paulo): “Pode inte.”, resposta à proposta de prosseguir a integração do caminho operacional V9 em modo sem publicação, preservando TRAIN/VALIDATION. **Estado:** DECIDIDO; implementação incremental proposta em branch/PR (integração condicionada à CI). **Política:** GOV-DOC-01.

## Contexto, necessidade e precedência

Após o conversor SQL V3 TF4/81 e o replay/agrupamento compartilhado ([PR #1508](https://github.com/lucianox777/Jornada/pull/1508)) e a igualdade real do grafo de candidatos TEST vs consultas operacionais SQL no fixture de 4 observações ([PR #1510](https://github.com/lucianox777/Jornada/pull/1510)), ainda faltava verificar que a **recuperação SQL em lotes** não cria um conjunto truncado de candidatos nem fragmenta as identidades em fronteiras de páginas. O `ProbabilisticLinkageBatchRunner.RunAsync` legado continua proprietário da persistência/publicação, com modelo antigo, e não representa integralmente o algoritmo V9. Exigir `test_required=true` nesta fase seria uma certificação indevida da aplicação produtiva.

Precedência obrigatória: [GOV-V9-TEST-PARIDADE-GRAFO-CANDIDATOS-SQL-20261114](Decisao_GOV_V9_TEST_Paridade_Grafo_Candidatos_SQL_20261114.md), [GOV-V9-TEST-RUNNER-COORTE-COMPARTILHADA-20261114](Decisao_GOV_V9_TEST_Execucao_Compartilhada_Runner_Coorte_20261114.md), [GOV-V9-TEST-CONVERSOR-LOSSLESS](Decisao_GOV_V9_TEST_Conversor_Lossless_Para_Modelo_Operacional_20261113.md) e issue [#1488](https://github.com/lucianox777/Jornada/issues/1488).

## Escolha técnica aprovada e implementada nesta etapa incremental

Acrescentar ao verificador **read-only** `V9OperationalCandidateGraphParityAudit.CompareTestAsync` o parâmetro opcional **`batchSize`**, válido entre 1 e 128 registros para a coorte TEST sintética de 2..128. A versão sem argumento mantém leitura completa como antes, sem modificar o comportamento das chamadas existentes.

O ensaio:
1. Primeiro confirma `DB_NAME() == JornadaHarness`, modelo único SQL `TEST_ONLY`, SHA do vencedor, partição TEST, todos os IDs/gestores e todas as arestas de referência físicas válidas.
2. Ordena as observações por `ObservationId` e **faz paginação real no loop de consultas SQL** `BuildCandidateUuidQuery`, reutilizando o **mesmo query builder operacional**, com `UNION` e `INTERSECT` de passes, sem `TOP N` nem fallback de modelo/projeção.
3. Acumula no mesmo grafo todas as arestas **bidirecionais** recuperadas por SQL; falha se houver candidato estranho/duplicado, qualquer direção ausente, contagens de linhas/páginas divergentes, aresta intragestor ou diferença de conjunto contra o SQL físico do Worker.
4. **Apenas depois** de reunir e verificar o grafo global, executa `V9OperationalPinnedCohortRuntime.ReplayTest` uma única vez sobre o universo inteiro. Nunca cria grupos separados por lote. Compara decisões e mapa de identidades do grafo operacional contra o grafo físico de referência e retorna recibo com digest SHA-256 de arestas, decisões e agrupamentos, tamanho/número de lotes e `ProductionBatchPipelineCertified=false`.
5. Amplia o SQL smoke privado com tamanhos de lote **1, 2, 3 e 4** e ordem invertida de observações, exigindo digests idênticos ao ensaio sem paginação, mantendo os testes negativos de fingerprint de projeção adulterado e tamanhos inválidos 0 e 129.

**Esta é uma simulação operacional de paginação de consultas do Runner, não execução de `ProbabilisticLinkageBatchRunner.RunAsync`.** A rotina produtiva completa de aquisição da base Gold, batching/persistência, estado e publicação não é invocada nem modificada. Nenhum registro é inserido/atualizado/excluído pelo comparador. O limite de 128 observações é diagnóstico, **não** uma mudança da escala 100k nem uma medição de desempenho do calibrador.

## Impactos, ressalvas e condições de segurança

- **TRAIN/VALIDATION:** sem alteração em seus arquivos de ciência ou caminho de execução, 324 hipóteses, funções de TF/FDR/CP/P95, paralelismo AUTO, job TRAIN 360min/etapa física 340min, VALIDATION 360min/330min, TEST 360min/330min. O run manual [#43551194008](https://github.com/lucianox777/Jornada/actions/runs/43551194008), iniciado em SHA `8ce35420fbb015fb281d8edcbc99213252aac183`, permanece isolado.
- **TEST:** continua totalmente bloqueado pelos dois guards `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED`; não preparar, desencriptar ou consumir a base selada; não habilitar `test_required=true`; não alterar `productionRunnerFullPipelineParityCertified=false`.
- **Resultados operacionais:** digests iguais em 4 registros comprovam apenas consistência local de paginação e agrupamento global do fixture, não equivalência científica a 100k, não paridade de todas as variações TF4/missingness, não autorização de publicação/ativação.
- **Alternativas:** fazer Resolve por lote (**rejeitada**, pode separar grupos reais que atravessam páginas); usar apenas contagens de candidatos (**rejeitada**, não detecta diferenças de membros ou decisões); executar workflow TEST 100k diretamente (**rejeitada**, quebraria cegamento e provas de produção); reconstruir grafo completo por consultas SQL paginadas e avaliar uma vez (**aprovada**, simples, reversível e sem impacto científico).

## Critérios de aceite, rastreabilidade e próximos passos

Exigir `dotnet build Jornada.sln -c Release`; unit-runtime, security-guards e deterministic-build **3/3 verde no HEAD da PR**; SQL smoke descartável `one-winner-sql-smoke` em SQL Server **real** com 1/1 teste concluído e casos de bateladas 1,2,3,4, digest global de decisões e identidades, quebra de SHA/fingerprint rejeitada; documentação/índices GOV-DOC-01 integrada à `master` por squash merge. Não considerar resultado físico TEST/100k validado.

**Pendente além desta etapa:** implementar e provar o fluxo real V9 end-to-end do `ProbabilisticLinkageBatchRunner` com leitura de Gold/projeção real, resultado de lote, estados de execução, efeitos persistidos e controles de publicação (sem ativação), com auditoria de igualdade em fixtures diversos, escala e segurança/LOCK/TEST. A questão #1488 **permanece aberta**. Vincular recibo com PR/CI/SHA após aprovação dos gates.

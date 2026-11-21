# V9 — Contrato de emissão nativa de recibos diferenciais (não certificador)

Status: desenho de implementação; **não implementado nem certificado**. Este documento não remove o bloqueio `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED`.

## Objetivo

Emitir recibos `JORNADA_V9_DIFFERENTIAL_RECEIPT_V1` a partir de duas execuções **efetivamente distintas**, sem construir manualmente decisões ou grupos para satisfazer o comparador `Solution/scripts/v9-differential-receipt-compare.py`.

## Pontos de integração

1. **Científico:** instrumentar o ciclo de `V9PinnedSyntheticGraphCohortRunScorer.PrepareAsync` / `ResolvePreparedAsync` e a conclusão do `ProbabilisticLinkageBatchRunner.RunAsync`. O scorer atual é **TEST-only**, limitado a 512 observações; não representa o runtime científico integral nem certifica produção. Recibos desse adaptador devem ser explicitamente diagnósticos e não utilizados como prova de paridade produtiva.
2. **Operacional:** instrumentar o caminho realmente selecionado pela DI de produção de `SqlProbabilisticIdentityLinkage` e `ProbabilisticLinkageBatchRunner`, não os métodos diagnósticos `LoadPinnedV9OperationalRuntimeAsync` ou `ResolvePinnedV9FrozenTestCohortAsync` isoladamente.
3. **Captura:** congelar o mesmo universo de IDs SQL antes de qualquer resolução; coletar decisões efetivamente persistidas/confirmadas pelo RunAsync, e grupos finais do mesmo snapshot. Falhar se a resolução não cobrir integralmente o universo, se houver IDs duplicados ou se houver grupos incompletos.
4. **Candidatos:** calcular `operationalEdgesSha256` a partir de arestas efetivamente consultadas pelo blocking operacional, ordenadas canonicamente. O grafo físico de referência é oráculo separado, nunca substituto silencioso.
5. **Modelo:** calcular `modelSha256` a partir dos bytes do artefato operacional efetivamente carregado. `universeSha256` deve usar a mesma serialização canônica em ambos os caminhos.
6. **Proveniência:** `runId` e `runtimeArtifactSha256` devem vir do processo/artefato executado, e não de valores fornecidos pelo autor do recibo. Verificar SHA-256 contra bytes disponíveis no ambiente; se não verificável, não emitir recibo elegível à comparação.
7. **Isolamento:** duas execuções independentes, bancos descartáveis separados, sem publicação em Gold/Serving e sem verdade TEST injetada na decisão. A igualdade de recibos não demonstra, isoladamente, independência.

## Critérios de aceite

- Dois produtores nativos distintos, com testes de integração de cada caminho e verificação dos hashes contra bytes reais.
- Um teste positivo com o mesmo universo/modelo/arestas e decisões/grupos equivalentes, sem reaproveitar o mesmo runtime.
- Testes negativos para modelo divergente, universo divergente, aresta divergente, decisão divergente, grupos divergentes, runId repetido, artefato repetido, evidência ausente e falha de persistência.
- Comparação via CLI existente, preservando `certified:false` e `productionParityCertified:false`.
- Nenhum gate científico removido até avaliação separada de cobertura do caminho produtivo, ausência de vazamento TEST e evidências reprodutíveis.

## Ordem de execução

1. Criar coletor de evidências do `RunAsync` com contrato tipado e invariantes, sem exportação automática.
2. Instrumentar primeiro o caminho científico real, depois o operacional real, com testes de integração independentes.
3. Executar comparação diferencial end-to-end; publicar recibos e hashes em artefatos não publicadores.
4. Submeter a evidência a revisão dos gates científicos antes de qualquer proposta de desbloqueio.

## Autorização operacional de integração pelo PO — 2026-11-21

**ID:** GOV-V9-PR1795-1799-MERGE-20261121. **Autoridade:** Product Owner. **Estado:** DECIDIDO / AUTORIZADO; integração, validação e ativação são estados distintos e devem ser comprovados separadamente.

**Decisão:** o PO autorizou explicitamente os merges das cinco PRs #1795, #1796, #1797, #1798 e #1799. A autorização dispensa nova solicitação de consentimento para esses merges, mas **não dispensa verificações obrigatórias, regras de proteção, resolução de dependências, revisão técnica aplicável, nem o método squash**.

**Contexto e justificativa:** frente de preparação técnica V9 com coletor diferencial não certificador, guardas estáticas, verificação de hashes de bytes, provas negativas de proveniência e gate de comparação de dois artefatos. A autorização permite integrar essa preparação de forma ordenada, sem confundir CI verde com paridade científica ou execução real dos dois produtores nativos.

**Ordem e precedência:** #1795 antecede #1796 e #1797; #1797 antecede #1799; #1798 é independente. A cadeia de branches deve ser reconciliada para evitar squash que introduza diffs de dependências ou omita arquivos. Esta autorização é estritamente operacional e complementa GOV-DOC-01 e as políticas de CI/squash, sem substituí-las.

**Impactos e limites:** somente integração do código e dos testes das cinco PRs; não autoriza TRAIN/TEST científico, 100k, alterações LOCKED, certificação TEST/PROD, publicação ou ativação Gold/Serving. O bloqueio `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED` continua vigente. Não declarar integração concluída sem SHAs de merge e CI pós-merge.

**Rastreabilidade:** PRs #1795–#1799; correções de sintaxe da #1797 nos commits `23b55c15` e `d96a08cc`; sincronização de conteúdo na #1799 nos commits `f0787d58` e `33c638fd`. Os estados de CI e eventuais bloqueios restantes devem constar nas PRs; a aprovação do PO não equivale a sucesso dos gates.

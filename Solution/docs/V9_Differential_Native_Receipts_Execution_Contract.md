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

# GOV-V9-GRAFO-OPERACIONAL-INTEGRAL-COORTE-20261117

**Autoridade:** Product Owner, 17/11/2026. **Estado:** DECIDIDO; implementação e validação pendentes. **Política:** GOV-DOC-01.

## Decisão
Integrar ao runtime científico V9 o **grafo completo de candidatos operacionais**, preservando a resolução global por coorte. A execução científica deve **falhar fechada antes de pontuar, persistir decisões ou publicar** quando o grafo estiver incompleto, truncado, inconsistente com a origem operacional ou sem evidência verificável de completude. A etapa subsequente será persistência das decisões e controles de publicação, sem antecipar sua liberação.

## Contexto e justificativa
A ponte opt-in de leitura física SQL, comparação de contagem/hash de arestas e preparação da coorte antes da persistência foi documentada em GOV-V9-GRAFO-FISICO-RUNASYNC-REGRESSAO-20261116. Seus testes estruturais não demonstram paridade integral com o formato produtivo nem ausência de truncamento. A presente decisão exige a integração efetiva e verificável, não apenas regressão estrutural.

## Critérios de aceite da implementação
1. Obter o universo integral de candidatos da fonte operacional, incluindo arestas e identificadores necessários à competição entre observações, sem paginação parcial, limites silenciosos ou amostragem.
2. Preservar os limites e a semântica da coorte e resolver competição globalmente na coorte, não por página ou lote.
3. Comparar a representação consumida com a origem por contagens, identificadores, hash canônico e integridade de referências, detectando omissões, duplicações, arestas órfãs e truncamento; rejeitar qualquer divergência antes de efeitos persistentes.
4. Testar fisicamente SQL com coortes repartidas entre páginas/lotes, grafos densos, limites de volume, falha de leitura e discrepâncias deliberadas; comprovar bloqueio e ausência de decisões/publicação parcial.
5. Registrar evidências reproduzíveis (SHA do código, run, coorte, assinatura do grafo e resultado dos gates), sem expor dados pessoais.
6. Manter publicação desabilitada no runtime científico até decisão posterior expressa do PO sobre persistência e controles de publicação.

## Impactos e precedência
Afeta ingestão do grafo, validação de completude, scoring/resolução e gates de execução do V9. Não altera thresholds, TFs, vencedor, política de TEST one-use nem autoriza TRAIN/TEST ou publicação. Complementa e torna mais exigente GOV-V9-GRAFO-FISICO-RUNASYNC-REGRESSAO-20261116. Respeita as decisões vigentes de isolamento TEST, artefato científico e separação do executor operacional. Implementação e validação só serão declaradas após código, testes e evidências na master.

## Rastreabilidade
PO 17/11/2026; GOV-DOC-01; issue #1584; PR #1586; `Solution/src/Jornada.Linkage.Runner/V9PinnedSyntheticGraphCohortRunScorer.cs`; `Solution/src/Jornada.Linkage.Runner/ProbabilisticLinkageBatchRunner.cs`. Próximas entregas: integração física e provas; depois persistência de decisões e gates de publicação.

## Progresso de implementação na PR #1655 (17/11/2026; ainda não certificado)
A ponte sintética descartável `V9FrozenCohortSqlCandidateGraph` passou a exigir censo SQL independente das assinaturas efetivamente materializadas após `SqlBulkCopy`, censo de pares distintos e reconciliação exata com as arestas lidas, além de ordem estrita e limites fail-closed. Guardas de código-fonte foram ampliados em `test_v9_physical_runasync_graph_guards.py`. Commits: `e486576`, `3ef5789`, `1e87b7d`, `c9bb9fa`. **Não constituem teste físico executado nem certificação de completude operacional**: o adaptador permanece limitado a 512 observações e `JornadaHarness`; ainda faltam integração com o grafo operacional em escala, testes SQL reais, evidência de ausência de persistência parcial e controles de publicação. A autorização científica permanece bloqueada.

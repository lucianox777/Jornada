# GOV-V9-TEST-FLUXO-PROCESSOR-HISTORICO-PUBLICACAO-20261115

**Autoridade:** solicitação expressa do Product Owner em 15/11/2026 para comprovar a integração governada da nova entrada V9 ao fluxo de execução do Processor, preservando históricos e regras de publicação. **Política:** GOV-DOC-01. **Estado:** em implementação/testes, resultados CI pendentes. **TEST científico independente de 100 mil: BLOQUEADO e NÃO CONSUMIDO.**

## Lacuna factual e precedência

A [PR #1568](https://github.com/lucianox777/Jornada/pull/1568) integrou à master o método TEST_ONLY de `BlockingProjectionPersistence` que compartilha o leitor Gold/ Silver usado pelo Processor, mas sua prova executava esse método diretamente em transação shadow, sem passar pelo ramo efetivo `SqlProcessorRepository.RefreshGoldPersonAsync` e sem dois vínculos físicos históricos com nomes maternos distintos. A recomposição por `identidade.sp_recompor_gold_pessoa` e a disciplina de publicação do Processor ainda não estavam diretamente exercitadas em conjunto com a projeção V9.

## Decisão técnica aprovada pelo pedido do PO

1. **Reutilizar o mesmo método privado de recomposição de Gold do Processor real.** `SqlProcessorRepository.RefreshGoldPersonAsync` permanece chamado por `RefreshGoldRepresentationsAsync` sem qualquer nova opção na ingestão normal. Seu comportamento padrão continua invariavelmente `EXEC identidade.sp_recompor_gold_pessoa` seguido de `BlockingProjectionPersistence.RefreshSqlServerAsync` (LEGACY V2). Com um permit interno explícito, pode encaminhar a nova referência Gold sem CPF à materialização V9 pinada do mesmo componente de persistência. **Não há feature flag produtiva, registro DI nem interface HTTP/API para ativar V9.**
2. **Gate de ensaio separado, fail-closed e não-produtor**, `SqlProcessorRepository.RehearseV9GoldHistoryRefreshAsync`, `internal` acessível apenas à suíte friend `Jornada.Tests`: exige conexão + transação explícita, `JornadaHarness` exato, UUID diagnóstico, comprimento de prefixo V9 válido e fingerprint SHA congelado. Rejeita SHA incorreto antes de executar até a recomposição Gold. Exige no SQL **dois vínculos correntes reais** resolvidos, sem `linkage_run_id`, associados à mesma referência, com dois nomes e dois nomes maternos distintos e data de nascimento concordante. Lê `identidade.v_vinculo_corrente` + `silver.pessoa_observacao`, **não** uma lista de aliases fornecida pela fixture.
3. O fixture SQL do `RunAsync` já possui 200 observações e 200 Gold sintéticos; numa transação **shadow descartável e sempre revertida**, inserir somente dois vínculos `UUID_JORNADA_RETROALIMENTACAO` para as fontes físicas 0001 e 0003, exclusivamente em `JornadaHarness`. São vínculos sintéticos de prova, **não atos reais de governança de identidade**. O gate invoca o mesmo `RefreshGoldPersonAsync` real, prova recomposição Gold (REFERENCIA/COMPLETO/DIVERGENTE), o manifesto V9 SHA, versões e aliases de ambos os históricos, rejeita reentrada e não altera modelos publicados.
4. Medir hash canônico **antes e depois do ROLLBACK** para as projeções e registros Gold, blocking_chave, Silver, vínculos fonte e contadores de Serving/publicação; todas as tabelas/links devem voltar exatamente ao estado inicial. Dentro do shadow, verificar que runs anteriores com `Publish=false` não ganharam publicação: `linkage_run.status=PUBLICADO` e campos `linkage_resultado.resultado_publicacao` continuam ausentes. O teste positivo V9 sobre 200 Gold, TF4/81, dois pares interlotes e sem publicação é executado depois sem alterações.
5. Preservar explicitamente o contrato de modelagem/persistência V2, o código da ingestão normal, os schemas/migrations Gold/Serving, os guards científicos `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED` e a coorte 100k ainda selada. Não autorizar o Processor a produzir V9 em ambiente de produção.

## Consequências, limites e riscos

A prova é agora de integração **pelo método efetivamente usado pelo Processor** e pelo mecanismo SQL de recomposição Gold com duas fontes historicamente distintas, não só uma chamada direta ao método de blocking. O caminho V9 permanece condicionado a um **permit interno de ensaio** e a banco descartável, sem afetar as chamadas normais. Há um limite importante: os vínculos sintéticos na transação shadow demonstram o tratamento e não constituem uma correção governada real autorizada nem uma integração de Gold/Serving em produção. O hash de Serving inclui neste ensaio a sua cardinalidade; ainda falta teste com conteúdo Serving não vazio e igualdade integral byte a byte. A rotina V9 continua exigindo nova referência sem bloqueio anterior, e não cobre recomposição governada de uma referência já indexada. Não são demonstrados throughput/P95 em 100k, modelo V9 nativo no FK (sem âncora legado), LOCK/one-use, idempotência/replay em escala nem a certificação científica de TEST. Issues #1488/#1478 continuam abertas.

**Precedência preservada:** [GOV-V9-TEST-PROCESSOR-NATIVE-PROJECAO-GOLD-20261115](Decisao_GOV_V9_TEST_Processor_Native_Projecao_Gold_20261115.md) → este ensaio (não revoga suas restrições); [GOV-V9-TEST-MATERIALIZACAO-GOLD-BACKED-BLOCKING-20261115](Decisao_GOV_V9_TEST_Materializacao_Gold_Backed_Blocking_20261115.md) → origens Gold já provadas no fixture.

## Rastreabilidade e aceite

- Fluxo real do Processor: `Solution/src/Jornada.Processor.Worker/SqlProcessorRepository.Materialization.cs`
- Gate isolado de ensaio: `Solution/src/Jornada.Processor.Worker/SqlProcessorRepository.V9GovernedRehearsal.cs`
- Materializador de referência nova: `Solution/src/Jornada.Operational.Sql/BlockingProjectionPersistence.V9.cs`
- Prova SQL real: `Solution/tests/Jornada.Tests/Unit/V9FullSchemaNonPublishingRunnerTests.cs`
- Contrato estático de não ativação: `Solution/tests/scripts/test_v9_fullschema_runasync_smoke.py`
- CI: `.github/workflows/v9-operational-model-sql-smoke.yml`
- Aceite somente com fast 3/3 e SQL físico 2/2 no HEAD, manifesto histórico corretamente preservado, rollback íntegro, PR em master, recibo com evidências e índices GOV-DOC-01 sincronizados. **NÃO declarar ativação produtiva nem liberação TEST 100k.**

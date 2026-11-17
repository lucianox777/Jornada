# GOV-V9-TEST-ISOLAMENTO-RETROCOMPAT-20261117 — ratificação e diagnóstico de liberação

**Autoridade:** Product Owner, 17/11/2026. **Estado:** requisitos ratificados; implementação de liberação do TEST científico **PENDENTE**. **Política:** GOV-DOC-01.

## Decisão ratificada
O TEST científico do workflow do calibrador é uma execução **inteiramente isolada do ambiente produtivo**. Recebe **somente o pacote operacional do único vencedor** exportado pela VALIDATION, executa a mesma semântica de configuração, blocking, scoring, resolução e agrupamento que o executor da aplicação, porém em base independente e ambiente descartável controlado pelo workflow. Seu único produto funcional é o **relatório de resultados científicos para avaliação humana**. É proibido modificar ambiente, credenciais, configurações, dados, modelos ativos, Gold, Serving ou publicação da produção. TEST não ajusta parâmetros, não escolhe vencedor e não implanta modelos.

A **implantação** do pacote é uma operação administrativa **posterior, separada e explicitamente autorizada pelo operador MASTER**, sujeita à governança DT-15. Aprovação estatística não implica publicação automática.

**Separação de escopos:** retrocompatibilidade é requisito **exclusivo do executor operacional**, não do TEST científico. O TEST não precisa executar modelos legados nem selecionar contratos anteriores: avalia somente a configuração vencedora produzida pela campanha TRAIN → AUDIT → VALIDATION do mesmo workflow. TRAIN avalia hipóteses; VALIDATION seleciona e exporta o único vencedor; TEST consome esse pacote congelado, sem recalibrar ou escolher outra configuração.

O executor deve manter **compatibilidade retroativa com os contratos/modelos anteriores** ao incorporar V9: modelos legados continuam executando sob suas semânticas históricas; V9 exige versão e identidade explícitas. **Nunca** realizar fallback silencioso de V9 inválido para modelo legado, nem adaptar parcialmente TF4/81 a parâmetros antigos.

## Evidência documental e código observado
- VALIDATION já exporta `<winnerSha>.operational.json` e manifesto do vencedor, independentemente de TEST: `V9NestedCalibrationCli`, `V9ValidatedOperationalModelFile`, decisão `Decisao_GOV_V9_VALIDATION_Arquivo_Operacional_Final_TEST_Nome_20261114.md`.
- Conversor `V9OperationalSqlModelConverter` persiste payload V9 integral `TEST_ONLY`, valida ID/SHA, sem fallback para ATIVO legado.
- `SqlProbabilisticIdentityLinkage` preserva blocking legado por nascimento e caminho com ruleset, e oferece entrada V9 opt-in TEST_ONLY não conectada automaticamente ao modelo ATIVO.
- `V9IndependentTestCli` não certifica paridade completa: `productionRunnerFullPipelineParityCertified=false`.
- `.github/workflows/v9-standard-linear-100k.yml` bloqueia `test_required=true` na ADMISSION com `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED`. CLOSEOUT ainda marca `PREPARATION_RESULT=failure` para TEST solicitado. Portanto **TEST científico não está liberado**.
- Há provas parciais físicas em `V9FullSchemaNonPublishingRunnerTests` e documentos `Decisao_GOV_V9_TEST_RunAsync_FullSchema_Sem_Publicacao_20261115.md` e `Decisao_GOV_V9_RunAsync_Paridade_Fisica_Lotes_50_100_20261116.md`; não equivalem à certificação E2E 100k do workflow padrão.

## Lacunas para a liberação — ordem e critérios de aceite
1. **Integração real do executor V9:** comprovar que o TEST usa o mesmo núcleo/loader e semântica de `ProbabilisticLinkageBatchRunner.RunAsync` destinado à aplicação, com pacote V9 TF4/81 integral e sem tradução parcial; manter caminho legado intacto. Provar equivalência por candidato, observação, escore, conflito, desempate e cluster em fixtures físicas.
2. **Isolamento e não publicação:** banco descartável, credenciais restritas, endpoint separado de produção, `Publish=false`; teste negativo para qualquer acesso/escrita a produção, Gold/Serving, ATIVO e DT-15; validar efeitos indiretos, falhas por lote e rollback.
3. **Preparação independente TEST no Standard Linear:** criar e lacrar base e gabarito TEST pelo workflow, com seed/uso único e isolamento de TRAIN/VALIDATION; não usar `test_database` legado ou fonte externa como atalho. Conectar artifact, verificação de integridade e job TEST à saída VALIDATION.
4. **Métricas e closeout:** gerar relatório independente com recall, FDR, FP/FN, conflitos, desempenho, identidade SHA do pacote e provas de isolamento; ajustar `PREPARATION_RESULT` e gate E2E somente após evidências reais, nunca alterar `productionRunnerFullPipelineParityCertified` por simples constante.
5. **Regressão do executor (frente separada):** testes reais para modelos legados (blocking histórico/ruleset) e V9 pinado, garantindo que opt-in V9 não altera seleção do ATIVO, schema legado ou resultados antigos. **Não é etapa funcional do TEST científico**, que recebe somente o vencedor V9 do workflow.
6. **Habilitação controlada:** somente após CI, SQL smoke, E2E isolado e auditoria, retirar o fail-closed `test_required` na ADMISSION. Não executar TEST selado para depuração nem autorizar implantação operacional.

## Precedência e rastreabilidade
Este registro **esclarece e ratifica**, sem revogar, `Decisao_GOV_V9_TEST_Paridade_Formato_Operacional_20261113.md`, `Decisao_GOV_V9_TEST_Conversor_Lossless_Para_Modelo_Operacional_20261113.md`, `Decisao_GOV_V9_VALIDATION_Arquivo_Operacional_Final_TEST_Nome_20261114.md`, `Decisao_GOV_V9_TEST_Esquema_Operacional_RunAsync_Gold_Serving_20261114.md` e `Decisao_GOV_V9_Fluxo_Padrao_Provisorio_20261116.md`. O requisito de **paridade é do comportamento executável**, não de compartilhar o ambiente produtivo. A certificação e a implantação são etapas distintas. Pendências rastreadas nas issues #1488, #1478, #1575 e #1584. Nenhum gate é revogado por este registro.

## Início da implementação — 17/11/2026
A primeira entrega técnica na PR #1627 adiciona `Solution/tests/scripts/test_v9_test_executor_boundary_contract.py`, com contratos estáticos para: (a) manter o TEST do Standard Linear bloqueado até certificação; (b) exigir que TEST receba o vencedor operacional da VALIDATION sem refit/seleção; (c) preservar rotas legadas no executor e carga V9 explícita sem fallback. **Não constitui paridade física, não libera TEST e não altera o executor de produção.** Próximo incremento: integração executável V9 e evidência SQL física; em seguida, preparação independente TEST e closeout.

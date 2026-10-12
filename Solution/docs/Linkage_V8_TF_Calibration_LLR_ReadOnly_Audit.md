# Auditoria diagnóstica do termo de frequência V8: unidades estatísticas e LLR

**12/10/2026 — Fase P0, somente leitura.** Os ensaios reais da PR #943 demonstraram que um veto universal à geração de novos rascunhos V8 por ordenação EXACT > HIGH rompeu dois processos obrigatórios: `jornada-linkage-independent-validation` (folga LLR `−3,8397457154331853`) e `harness-smoke` (`−3,0191400475131465`). O teste sintético vermelho da PR #927 já reproduzia `−0,8754687373538999`. A produção **não** pode receber um gate novo que interrompe o calibrador sem resolver primeiro a relação estatística entre as frequências.

## Pergunta a responder com dados

O `U_NOME_EXACT` vem da ocorrência de **concordância no nome inteiro** entre pares condicionados ao bloqueio, com referência nominal IBGE versionada. Já `NominalTermFrequencySnapshot.PersonFirstNames` contabiliza frequências **marginais de prenomes**. No runtime V8, apenas o estado de comparação EXACT recebe o ajuste `weight × ln(U_EXACT/max(frequência_prenome, pisoTF))`. Esse arranjo pode ter efeito adverso grande sem que a fórmula de logaritmo, isoladamente, esteja errada. É uma hipótese de incompatibilidade de unidades, não uma conclusão estatística definitiva.

Esta PR cria um diagnóstico **não decisório**, em código puro no projeto `Jornada.Contracts`, sem alterar algoritmo, persistência ou contrato de decisão. Na geração de modelos V8, antes da seleção de Pareto, o worker registra para os **dois atributos separadamente** `NOME` e `NOME_MAE`: `mExact`, `uExact`, `mHigh`, `uHigh`, frequência **máxima** de primeiro nome publicada, peso TF, piso, LLR base EXACT, LLR base HIGH, penalização TF de pior caso, LLR EXACT final de pior caso e folga EXACT−HIGH. O campo `couldInvert` é uma observação matemática, **não** gate de `VALIDATE/ACTIVATE`, aprovação, conflito ou `RESOLVIDO`. Logs não contêm tokens/nome completos, UUID, linha de pessoa, variáveis de ambiente, segredos ou consulta SQL.

O diagnóstico **não estabelece** se uma inversão em contexto igual viola a calibração real: avalia um envelope contrafactual pessimista e não representa distribuição empírica conjunta de pares. Sem dados reservados com verdade-terreno, não inferir falso vínculo, recall ou um peso TF “adequado” a partir do envelope sozinho.

## Invariantes e testes

- O gerador existente continua com seus parâmetros `m/u`, `TERM_FREQUENCY_WEIGHT`, snapshot persistido, `T_LINKAGE`, margem, calibração TRAIN/VALIDATION/TEST e contrato de aprovação **inalterados**.
- Nenhuma chamada de `Rank`, `Resolve`, `FellegiSunterScoring` ou `SplinkCompatibleTermFrequency` é substituída, arredondada ou reordenada.
- Se a auditoria não encontrar fatores válidos, retorna `null`; registra aviso sem vetar o caminho operacional. Os demais gates de validade de parâmetros já presentes permanecem.
- Testes NUnit exercitam o caso P0 sintético de `MARIA`, decomposição exata da penalização, mãe independente, exemplo sem inversão, peso zero, dados inválidos, imutabilidade dos parâmetros e ausência de nomes pessoais na estrutura de saída.
- Validar em GitHub Actions **com dados sintéticos SQL reais e workers reais em runners descartáveis**: `unit-runtime`, `harness-smoke`, `jornada-linkage-independent-validation`, `integration-sql`, `e2e-console`, `e2e-private-sql`, CodeQL, deterministic-build, build Windows e agregadores. Inspecionar log agregado nos **dois datasets** e documentar fatores com versão/SHA antes de propor mudança no scoring.

A PR #943 de veto de publicação deve permanecer em draft e vermelha enquanto os ensaios estatísticos de sua condição não passarem. A PR #927 de regressão de ranking deve permanecer vermelha enquanto não existir correção validada de comportamento; **esta auditoria não fecha nem substitui a P0**.

**Isolamento:** apenas runners hospedados no GitHub e bases temporárias (que podem chamar sua base de `JornadaLocal` dentro do Compose). Nenhuma alteração no `JornadaLocal` físico de DEV, NODE1/NODE2, NAS, IBGE original, HML/PROD ou volumes reais.

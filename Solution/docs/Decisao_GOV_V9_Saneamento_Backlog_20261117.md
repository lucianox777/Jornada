# GOV-V9-SANEAMENTO-BACKLOG-20261117 — auditoria pré-nova execução

**Autoridade:** Product Owner, 17/11/2026, instrução para continuar saneamento antes de executar novamente o workflow. **Política:** GOV-DOC-01. **Estado:** auditoria documental em andamento; não autoriza executar TEST, consumir seed one-use, mesclar PRs científicas ou alterar parâmetros.

## Contexto e justificativa
O inventário identificou 29 PRs abertas inicialmente, com branches divergentes da master e propostas anteriores a alterações da cadeia TRAIN → AUDIT → VALIDATION → TEST. A divergência de commits não demonstra ausência de código quando houve squash; encerramentos exigem inspeção de conteúdo e sucessão explícita.

## Evidências e decisões operacionais
- PR #1452 encerrada como substituída explicitamente por #1454; PR #1454 encerrada após confirmação na master de leitura seletiva e SHA streaming em V9NestedCalibrationCli.cs. Isso não constitui ensaio físico >2 GiB.
- PR #1451 encerrada após confirmação de `reportHypothesis: null` e ausência das gravações individuais no código atual.
- Issue #1629 encerrada como obsoleta após PR #1634: TEST é gerado no próprio job após VALIDATION, não preparado antecipadamente com CMS. Gates científicos e tag de exposição única continuam obrigatórios.
- PR #1458 permanece aberta: `physicalProgressClock` e `ETA_EM_AQUECIMENTO` propostos não constam no arquivo equivalente da master; revisar necessidade e portar sobre master com CI, sem merge direto da branch antiga.
- PR #1429 permanece aberta: teste e validação explícita TF0 sem abstenção não constam nos scripts equivalentes da master; revisar pertinência científica e testar antes de integrar.
- PRs documentais #1619, #1610, #1547, #1536 e #1555: arquivos canônicos existem, mas recibos específicos de PR/SHA/CI não foram localizados no texto atual; conciliar semanticamente e incorporar evidências faltantes, sem encerrar por mera existência do arquivo.
- PR #1576 contém testes/código além de contrato de paridade desatualizado; não fechar sem inspeção. Replay/versionamento do Seletor já existe; a certificação de equivalência V9 é responsabilidade operacional do Seletor/Runner, não uma reimplementação do algoritmo do Calibrador. Issues #1575/#1584 permanecem até evidência.
- PRs #1620 e #1633 exigem reconciliação com replay histórico e referência estatística externa comum, respectivamente. `test_database` é dívida técnica futura e não bloqueia automaticamente o TRAIN científico.

## Impactos, precedência e restrições
Esta decisão de governança trata somente da classificação e rastreabilidade do backlog; não altera ciência, segurança, runtime, TF4, critérios de validação ou resultados. A PR #1634 prevalece sobre o desenho antigo de TEST selado; a implementação corrente da master prevalece sobre branches antigas. A capacidade de replay não implica certificação de paridade completa. Não iniciar TEST sem aprovação específica e preflight de seed one-use.

## Pendências verificáveis
1. Reconciliar recibos documentais e o índice de decisões.
2. Resolver #1458 e #1429 com teste e CI ou decisão fundamentada de não aplicabilidade.
3. Classificar demais PRs por patch versus master e fechar somente as comprovadamente substituídas/incorporadas.
4. Confirmar os gates do workflow atual antes de novo TRAIN; execução de TEST exige autorização separada.

**Rastreabilidade:** PRs #1451, #1452, #1454, #1458, #1429, #1576, #1619, #1610, #1547, #1536, #1555, #1620, #1633, #1634; issues #1629, #1575, #1584, #1488, #1478.

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


## Reconciliação semântica dos recibos históricos — 17/11/2026
A inspeção dos patches das PRs documentais confirmou que seus acréscimos registram fatos históricos de integração que **não constam literalmente** nos documentos canônicos atuais. Para preservar a rastreabilidade sem reintroduzir estados antigos ou requisitos de TEST lacrado, os recibos são consolidados aqui, sem alterar a ciência:

- **#1619 / #1611:** integração das fixtures independentes de cobertura TF4; squash `fa60330b861045304c71104151ad9c73dee3d5dd`; CI obrigatória `43965238095` (3/3 SUCCESS). Cobertura parcial de elegibilidade e frequências publicadas/ausentes nas quatro posições pessoa/mãe × prenome/sobrenome. **Não certifica** paridade numérica ou RunAsync.
- **#1610 / #1608:** índice SQL de assinaturas pós-bulk; squash `9520bb66c8d47096dfe5dd19ec43338d352610ba`; CI `43946961266` (3/3 SUCCESS). **Não comprova** aceleração física SQL.
- **#1547 / #1546:** observabilidade de timeout parcial; squash `4efd450b9b15b5caf145dc37db3ac9872d4e05f2`; CI `43717302364` (3/3 SUCCESS), testes focados `43717302345` e fixture `43717302357` reportados como SUCCESS na PR documental. **Não comprova** recuperação de performance.
- **#1536 / #1535:** opção de banco externo de TEST; squash `814238795abccf5c8c64f61a83c871bd0374cc7b`; CI `43629701678`, smoke SQL `43629701627` e auditoria TF `43629701698` reportados como SUCCESS na PR documental. **Não comprova** ensaio com database externo privado.
- **#1555 / #1554:** seletor manual TF4/100k com opção TEST; squash `7de4f3d5966ab0ccf6fb9801d748893667949d0e`; CI `43735403883` (3/3 SUCCESS), fixture `43735403970` reportada SUCCESS. A arquitetura de TEST foi **posteriormente substituída** pela PR #1634, portanto este é apenas recibo histórico, não especificação vigente.

**Fonte dos recibos:** patches e descrições das PRs documentais #1619, #1610, #1547, #1536 e #1555. Os estados de CI acima foram transcritos dos registros dessas PRs, não revalidados nesta auditoria por consulta individual a cada execução. **Precedência:** #1634 para arquitetura TEST atual; não interpretar os recibos históricos como aprovação de execução científica.

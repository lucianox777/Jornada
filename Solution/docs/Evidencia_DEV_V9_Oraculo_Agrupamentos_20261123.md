# Evidência técnica DEV — oráculo independente de agrupamentos V9 (23/11/2026)

## Contexto, justificativa e precedência

O PO aprovou a etapa 8 da issue #1851 no plano GOV-DEV-E2E-INTEGRAL-V9-PLANO-20261122, com gabarito sintético independente, avaliação por observação e proibição de inferir certificação científica. O contrato existente em scripts/e2e-independent-identity-oracle.py compara somente conjuntos de identidades. As mesmas observações podem pertencer ao conjunto correto e, ainda assim, estar indevidamente agrupadas. Por isso esse contrato é preservado e o verificador complementar é separado, não uma substituição.

A origem de verdade sintética foi reconciliada com scripts/v9-e2e-ibge-bridge-preflight.py e o sidecar externo bridge-truth.jsonl: BasePersonId e OpaquePersonId só ficam disponíveis fora do ZIP e fora do scorer; o manifesto informa truthSidecar.sha256 e allowedForScoring=false. A configuração operacional e a prova de cadeia HTTP→Gold→Runner V9 pertencem a #1584/#1488/#1851, não a este verificador.

## Entrada, saída e critérios fail-closed

Novo script: scripts/e2e-v9-grouping-oracle.py. Recebe manifesto de bridge e sidecar co-localizados, exportação observada independente de SQL/Runner, SHA de checkout esperado e SHA do vencedor esperado. A exportação observada ainda **não é produzida pela cadeia física atual**. Seu contrato mínimo de intercâmbio DEV é schema=E2E_V9_GROUPING_OBSERVED_DEV_V1, bridgeManifestSha256, commitSha, modelWinnerSha256 e rows contendo, para cada observação, opaquePersonId, groupId (ou null) e identityState (PROVISORIA/REFERENCIA/INDEFINIDA). A extração física correspondente requer outra entrega e verificação de proveniência; dados sintéticos simulados em testes offline não constituem essa prova.

O comparador verifica hash do sidecar contra manifesto, proibição de scoring com truth, vinculação explícita do manifesto/commit/vencedor, cobertura de todas as observações, unicidade e estrita tipagem; rejeita arquivos ausentes, symlink, chaves JSON duplicadas e valores numéricos não finitos. Limite DEV: até 4.000 observações e tamanho de arquivos controlado. Um groupId null é uma observação não agrupada, nunca integra automaticamente um grupo de outros null.

A contagem de pares compara o gabarito independente com os agrupamentos observados: TP = duas observações da mesma pessoa verdadeira agrupadas; FP = observações de pessoas verdadeiras distintas agrupadas; FN = observações da mesma pessoa verdadeira separadas. Calcula recall por pares e FDR observado apenas **descritivos** e impede medição sem pares positivos. Resultado emitido sem IDs de pessoa/observação, CPF, grupos, scores ou linhas do gabarito.

A saída preserva status=MEASURED_NOT_CERTIFIED, scope=GROUPING_ONLY_NO_BLOCKING_SCORES_OR_SCIENTIFIC_TEST, scientificValidation=false, operationalParityCertified=false, activated=false, E2E_NUCLEO_PASS=false e E2E_INTEGRAL_PASS=false, mesmo quando FP=FN=0. Comparador não mede blocking/candidatos, scores TF4/81, decisões operacionais completas, equivalência de SQL ou custo; não substitui TEST independente #38 e não certifica o Runner V9.

## Implementação, impacto e rastreabilidade

A PR Draft #1920 inclui script e testes offline adversariais; a CI rápida exige os testes. Não são alterados algoritmo, API, chaves, banco, modelo, workflows científicos nem decisão de ativação. O workflow E2E DEV owner-only não foi disparado. A futura etapa 7 precisa materializar um extrato real de agrupamentos e validar seu vínculo ao SQL descartável antes que qualquer comparação seja usada como evidência física.

GOV-DOC-01: detalhamento da implementação do plano já aprovado, **sem decisão nova do PO**; índice de decisões vigentes permanece inalterado. Esta documentação na branch Draft ainda precisa de CI no SHA novo e integração protegida antes de fazer parte da master. Precedência: plano de 22/11/2026, #1851 (etapa 8), #1584 (Runner), #1488 (modelo/seleção), preservação da campanha científica #38.

## Continuidade da #1584 — agrupamento do runtime e recibo SQL terminal (23/11/2026)

**Lacuna e justificativa:** o scorer V9 TEST_ONLY calcula agrupamentos globais em memória (global.IdentityByObservation), mas a tabela identidade.linkage_resultado registra a decisão por observação. O campo pessoa_uuid_resolvido pode ser nulo em abstenções sem representar o agrupamento global. Usá-lo como groupId no oráculo externo confundiria partição com decisão e produziria métricas falsas.

**Implementação proposta na PR #1920:** guardar cópia somente leitura de IdentityByObservation vinculada aos IDs SQL congelados, exigir cobertura integral, unicidade e GUID de grupo válido; método diagnóstico TEST_ONLY ReadCompletedGroupingDiagnosticAsync(runId). O método não lê gabarito nem escreve SQL; confirma banco descartável JornadaHarness, modelo pinado, mesmo run, todas as observações consumidas e estado CONCLUIDO_SEM_PUBLICACAO no SQL, sem publicado_em e com contagens coincidentes entre cabeçalho, linkage_run_item, linkage_resultado e agrupamento. Execuções FALHOU/parciais não fornecem diagnóstico terminal. Testes de smoke SQL 200 cobrem não publicação, cardinalidade, imutabilidade, run incorreto e falha de segundo lote.

**Limites e precedência:** incremento autorizado nas issues #1584/#1851 e no plano GOV-DEV-E2E-INTEGRAL-V9-PLANO-20261122; sem nova decisão do PO. Não cria exportador físico dos IDs opacos do bridge, não informa estado PROVISORIA/REFERENCIA/INDEFINIDA no extrato IBGE, não executa oráculo completo nem conclui HTTP-Runner-Gold. Não prova paridade produtiva ou TEST científico. Preservados RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED, E2E_NUCLEO_PASS=false, E2E_INTEGRAL_PASS=false, sem merge/ativação. GOV-DOC-01: documentação temática versionada na mesma PR, ainda não integrada à master.

# GOV-V9-BETA-CAMPANHAS-HISTORICAS-CI-20261119 — Escopo de CI e experimentos encerrados

**Autoridade:** Product Owner, 19/11/2026. **Estado:** decisão aprovada; implementação em PR, CI e integração pendentes. **Política:** GOV-DOC-01.

## Contexto e evidências
O sistema permanece em beta e não exige retrocompatibilidade do gerador sintético com versões experimentais anteriores. As campanhas ONE_MF × TF0 de seeds 44–47 e 48–57 foram fisicamente executadas em 05/11/2026 e consolidadas em `V9_TF_ONE_MF_Reprodutibilidade_Seeds42_47_202611.md` e `V9_TF_ONE_MF_Confirmacao_3Sigma_202611.md`. A issue #1275 permanece aberta por pendências de fechamento administrativo; seu estado não equivale a execução pendente. As execuções ocorreram em SHA de branch #1276, conforme relatórios.

## Decisão e justificativa
Preservar os manifests LOCKED e os resultados históricos, sem reescrever hashes ou simular novas evidências. Retirar da CI obrigatória da master os testes que exigem que o gerador **atual** corresponda ao gerador histórico das seeds 44–57. Não manter compilação dupla nem infraestrutura permanente de retrocompatibilidade do gerador beta. Manter os testes históricos no repositório para auditoria sob contexto/commit histórico e manter controles científicos vigentes em PR. Justificativa: remover bloqueio de evolução causado por identidade de fonte histórica sem reduzir a rastreabilidade das campanhas concluídas.

## Impactos, limites e precedência
Esta decisão afeta exclusivamente o escopo obrigatório de CI do gerador sintético beta; **não revoga** o protocolo 60/20/20 aprovado, independência TRAIN/VALIDATION/TEST, TEST one-use, hashes de novas campanhas, segurança fail-closed, requisitos de 100k históricos ou gates de certificação. Não certifica ONE_MF, FDR, HML nem Produção. Para o executor operacional, a obrigação de retrocompatibilidade permanece regida pela decisão de 17/11; qualquer refinamento do marco HML/Produção exige registro específico e revisão do documento original. Prevalece sobre a aplicação indiscriminada de gates de gerador histórico ao código beta vigente, mas não altera os próprios LOCKs.

## Implementação e verificações pendentes
- `.github/workflows/ci.yml`: retirar da etapa obrigatória `impact` os testes históricos `test_v9_tf_frozen_transfer_lock_gate.py` e `test_v9_tf_campaign_sign_test.py`; manter arquivos e outros testes vigentes.
- `.github/workflows/jornada-pr-required-fast.yml`: reforçar testes offline de coortes TEST/VALIDATION.
- Diagnosticar e corrigir separadamente a validação de proveniência física 30k, distinguindo campos novos de manifesto e conteúdo do corpus; **não** alterar fingerprints históricos nem dispensar integridade de novas campanhas.
- Verificar CI de PR, master, branch protection e fechamento administrativo #1275; não declarar concluído antes das evidências.

## Rastreabilidade
Issue #1734; issue #1275; PR #1721; relatórios de seeds 42–47 e 48–57; GOV-V9-TEST-COHORT-20PCT-20261118; GOV-DOC-01.

## Complemento de implementação (19/11/2026)
O PR fast passa a executar também os testes offline do `impact` atual (sem os dois gates históricos aposentados). O workflow manual de frozen transfer falha explicitamente quando disparado na master beta; execução histórica exige selecionar ref original e SHA exato. A decisão de 20% teve o cabeçalho atualizado para refletir o merge de #1721. A referência física 30k vigente ainda requer geração controlada e versionamento; não substituir hash por valor não verificado.

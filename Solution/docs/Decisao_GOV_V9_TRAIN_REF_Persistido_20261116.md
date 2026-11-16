# GOV-V9-TRAIN-REF-PERSISTIDO-20261116 — integração técnica concluída; TEST científico bloqueado

> **ESTADO EM 16/11/2026: SUPERADO OPERACIONALMENTE PARA O TRAIN 100K.** O PO aprovou voltar à geração local de 100 mil pessoas e deixar de consumir o ZIP sintético REF. Ver [GOV-V9-TRAIN-100K-GERACAO-LOCAL-SEM-REF-CORPUS-20261116](Decisao_GOV_V9_TRAIN_100K_Geracao_Local_Sem_REF_Corpus_20261116.md). Manter este documento e seus recibos como histórico; TEST científico permanece bloqueado.\n\n**Estado histórico atualizado em 2026-11-16:** PR #1581 integrada à `master` pelo squash commit `468429d0467fd0f737408ced3716d87c71c4599a`. Prova de admissão do corpus #43800637171, integração TF4 #43800637164 e CI obrigatória #43800637157: SUCCESS. Isto não representa execução do TRAIN científico nem liberação de TEST.

## Contexto e justificativa
A prova independente #43798179373 validou o ZIP persistido da coorte sintética 100k, mas o workflow científico `.github/workflows/v9-v3-four-name-100k.yml` ainda gerava novamente seed42. A integração proposta elimina essa geração no passo de preparação do TRAIN, tornando a entrada materialmente vinculada ao artefato já verificado.

## Contrato e rastreabilidade
- Fonte congelada: commit experimental `9058c5dfe9b7dd6b8e77827fbaa55e5dc4faca50`, `Solution/data/reference/v9-synthetic-corpus-100000/`.
- ZIP SHA-256 `d62ceaff27361591243c4625d9c9415bd342085ce517a71ba28598a8c79a298d`, tamanho 7.697.354 bytes.
- Verificador `v9-corpus-ref-zip.py` recuperado do mesmo commit; verificação do manifesto, quatro arquivos, hashes e extração fail-closed antes do binding.
- Diretório de destino mantido: `.local/v9-100k-technical/people-100k`. Plano V3/324, artefato histórico e políticas TF não modificados.
- PR #1578: consumidor independente; PR #1579: recibo; PR #1580: índice de evidências integrado; PR #1581: mecanismo de admissão do corpus integrado à `master`.

## Impactos, limites e precedência
A alteração visa eliminar divergência entre corpus comprovado e corpus efetivamente consumido pelo TRAIN. Não constitui execução científica, certificação, liberação de TEST ou mudança dos parâmetros científicos. Mantém precedência dos bloqueios de TEST, das regras de segurança, dos controles de recursos e da decisão GOV-V9-CORPUS-PERSISTENTE-REF-FAMILIAR-20261115, inclusive a condição experimental da PR #1567.

## Evidências concluídas e gates remanescentes
1. CI obrigatória #43800637157: SUCCESS; integração TF4 sintética #43800637164: SUCCESS.
2. Prova independente de admissão do REF persistido #43800637171: SUCCESS, sem executar física 324.
3. O TEST científico independente continua bloqueado explicitamente no workflow `.github/workflows/v9-v3-four-name-100k.yml` por `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED` (modelo diagnóstico `V9_FITTED_SNAPSHOT_V3` não é o formato consumido por `SqlProbabilisticIdentityLinkage`).
4. Para a campanha integral TRAIN → AUDIT → VALIDATION → TEST, implementar e demonstrar paridade de serialização/carregamento/execução do modelo no mecanismo de produção, preservar isolamento e pré-selagem do TEST, adicionar prova automatizada end-to-end e obter CI antes de qualquer execução física cara.

**Não iniciar campanha integral nem afirmar certificação de TEST enquanto a paridade de produção não estiver demonstrada.**

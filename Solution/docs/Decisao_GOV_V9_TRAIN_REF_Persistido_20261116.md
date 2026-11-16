# GOV-V9-TRAIN-REF-PERSISTIDO-20261116 — integração técnica em validação

**Estado:** implementação experimental em PR #1581 (DRAFT), não integrada, sem liberação de TRAIN ou TEST científico. Este registro documenta a continuidade técnica do PO conforme GOV-DOC-01, sem estabelecer aprovação de resultados científicos.

## Contexto e justificativa
A prova independente #43798179373 validou o ZIP persistido da coorte sintética 100k, mas o workflow científico `.github/workflows/v9-v3-four-name-100k.yml` ainda gerava novamente seed42. A integração proposta elimina essa geração no passo de preparação do TRAIN, tornando a entrada materialmente vinculada ao artefato já verificado.

## Contrato e rastreabilidade
- Fonte congelada: commit experimental `9058c5dfe9b7dd6b8e77827fbaa55e5dc4faca50`, `Solution/data/reference/v9-synthetic-corpus-100000/`.
- ZIP SHA-256 `d62ceaff27361591243c4625d9c9415bd342085ce517a71ba28598a8c79a298d`, tamanho 7.697.354 bytes.
- Verificador `v9-corpus-ref-zip.py` recuperado do mesmo commit; verificação do manifesto, quatro arquivos, hashes e extração fail-closed antes do binding.
- Diretório de destino mantido: `.local/v9-100k-technical/people-100k`. Plano V3/324, artefato histórico e políticas TF não modificados.
- PR #1578: consumidor independente; PR #1579: recibo; PR #1580: índice de evidências integrado; PR #1581: alteração proposta do TRAIN.

## Impactos, limites e precedência
A alteração visa eliminar divergência entre corpus comprovado e corpus efetivamente consumido pelo TRAIN. Não constitui execução científica, certificação, liberação de TEST ou mudança dos parâmetros científicos. Mantém precedência dos bloqueios de TEST, das regras de segurança, dos controles de recursos e da decisão GOV-V9-CORPUS-PERSISTENTE-REF-FAMILIAR-20261115, inclusive a condição experimental da PR #1567.

## Critérios pendentes
1. CI obrigatória da PR #1581 e análise de compatibilidade de todos os consumidores de `people-100k`.
2. Prova de admissão do corpus no workflow, sem regeneração e sem iniciar física 324.
3. Revisão da governança e integração do registro e índice somente após gates aprovados.

**Não executar TRAIN ou TEST com base exclusivamente neste registro.**

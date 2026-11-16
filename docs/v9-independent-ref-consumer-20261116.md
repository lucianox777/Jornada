# V9 — prova de consumo independente do REF físico 100k (2026-11-16)

## Contexto e precedência
Conforme GOV-DOC-01 e a decisão anterior de persistir o corpus físico seed42 de 100 mil registros, o PREPARE bootstrap #43789530462 produziu o candidato, e a publicação #43797713311 concluiu com SUCCESS. O commit da branch experimental que contém o REF é `9058c5dfe9b7dd6b8e77827fbaa55e5dc4faca50`.

## Evidência consolidada
- Branch de dados: `feature/v9-persistent-synthetic-corpus-20261115`.
- Arquivos: `Solution/data/reference/v9-synthetic-corpus-100000/corpus-seed42.zip` e `manifest.json`.
- ZIP: 7.697.354 bytes; SHA-256 `d62ceaff27361591243c4625d9c9415bd342085ce517a71ba28598a8c79a298d`.
- O manifesto descreve os quatro arquivos internos, seus tamanhos e hashes.

## Implementação de verificação (sem decisão científica nova)
Workflow opt-in `.github/workflows/v9-independent-ref-consumer-opt-in.yml`, com disparo exclusivo pelo proprietário, entrada SHA exata e checagem do HEAD da branch experimental. Em runner novo, sem download do artefato PREPARE, sem cache e sem geração, lê o ZIP versionado, verifica o manifesto, extrai para diretório TRAIN isolado e revalida os quatro hashes. A etapa não executa o TRAIN científico, não aprova hipóteses e não modifica os dados. Falha fechada em qualquer divergência.

## Critério de conclusão e rastreabilidade
Somente um run SUCCESS deste novo workflow, disparado a partir da `master` depois do merge do PR, comprovará consumo independente do REF persistido. Até lá, status **PENDENTE**. Após a execução, registrar aqui run ID, SHA, conclusão e atualizar o índice de decisões vigentes conforme GOV-DOC-01. A publicação do corpus não implica liberação automática da calibração completa.

# GOV-V9-TEST-SEED-GUID-UNICA-20261117

**Autoridade:** Product Owner, 17/11/2026. **Estado:** aprovado; implementação proposta em PR, pendente CI e integração. **Política:** GOV-DOC-01.

## Contexto e justificativa
O TEST do workflow Standard Linear usava seed fixa `20261113001` e tag global de exposição fixa, impedindo campanhas independentes posteriores. O PO aprovou implementar uma alternativa baseada em identificador único de execução, delegando a escolha técnica.

## Decisão técnica
Após VALIDATION selecionar vencedor e antes de gerar o corpus TEST, criar GUID aleatório UUIDv4; derivar seed unsigned 64-bit determinística dos primeiros oito bytes de SHA-256 do GUID. Recusar seed zero e seeds conhecidas de TRAIN, VALIDATION e TEST histórico. Reservar globalmente `refs/tags/v9-tf4-test-seed-v2-<seed>` via criação atômica de ref no GitHub. Se a criação falhar, consultar a ref: apenas existência comprovada permite gerar novo GUID/seed e repetir, até oito tentativas. Erro de permissão, autenticação ou rede não autoriza repetição silenciosa; falhar fechado. Não excluir a reserva após falha subsequente, para preservar o ledger de exposição.

## Impactos e limites
O corpus TEST recebe a seed reservada, nunca o GUID diretamente. Persistir GUID, seed, ref, SHA e run/attempt em recibo incluído no artifact TEST com SHA256SUMS; preserva reprodutibilidade e auditoria. TRAIN seed 42 e VALIDATION seed 20261112001 permanecem intactas. A semântica de `test_database` continua referência estatística opcional do PREPARE, conforme GOV-V9-PREPARE-BRUTO-TRAIN-PLANOS-20261117. Não alterar o protocolo de avaliação, ativação, seleção nem autorizar disparo científico.

## Precedência e rastreabilidade
Substitui a semente fixa e a tag fixa **somente** no workflow Standard Linear. O workflow legado `v9-v3-four-name-100k.yml` e seu lock histórico permanecem separados e exigem migração específica caso voltem a ser utilizados. Origem: autorização expressa do PO em conversa de 17/11/2026. Aceite: CI, regressões de colisão/permissão/reexecução, prova de reserva antes da geração, evidência de SHA e integração.

# GOV-V9-LOG-HIPOTESE-CONSOLE-20261112 — Progresso de hipótese somente no console

**Data e autoridade:** 12/11/2026, decisão explícita do Product Owner. **Status:** aprovada; implementação na PR vinculada a esta alteração.

## Contexto e motivação
Na campanha V9 TF4 100k, o progresso por hipótese era persistido em `hypothesis-progress.jsonl` e em arquivos individuais `hypotheses/hypothesis-*.json`. O PO associou o crescimento do log geral à falha da execução #43159426682 e determinou remover a escrita desses logs por hipótese, mantendo a mensagem na tela. O log dessa execução registra, na etapa de VALIDATION, `System.IO.IOException` em `File.ReadAllBytesAsync` para arquivo acima do limite de 2 GiB; não está demonstrado que o arquivo lido era um dos logs de hipótese. A remoção solicitada é uma decisão operacional independente da investigação da leitura de arquivo grande.

## Decisão aprovada
- Não criar, acrescentar nem publicar `hypothesis-progress.jsonl` ou `hypotheses/hypothesis-*.json` ao final de cada hipótese.
- Manter a mensagem `[V9 PROGRESS]` emitida pelo coordenador no console do GitHub Actions, com contagem, status, duração e ETA; `V9_PHYSICAL_PROGRESS_LOG=1` permanece habilitado.
- Retirar os caminhos desses logs da lista de artefatos do workflow.
- Preservar os demais registros agregados já existentes (startup, lote, memória, auditoria), a lógica de calibração, seleção, métricas e validação. Não alterar a política de snapshots dos finalistas.

## Implementação, impactos e precedência
O callback `reportHypothesis` do CLI deixa de gravar arquivos por hipótese; o coordenador mantém o `Console.Out.WriteLine` do progresso físico. A redução de I/O e artefatos não garante, por si só, correção do erro de leitura de arquivo >2 GiB durante VALIDATION, que deve ser acompanhado separadamente. Esta decisão substitui apenas a obrigação de persistência por hipótese, sem revogar a decisão anterior sobre visibilidade do progresso físico (`Decisao_GOV_V9_Progresso_Fisico_Hipoteses_20261111.md`). Rastreabilidade: execução #43159426682, arquivos `V9NestedCalibrationCli.cs` e `.github/workflows/v9-v3-four-name-100k.yml`.

# GOV-V9-1386 — Encerramento por superação de diagnóstico histórico (21/11/2026)

**Decisão do Product Owner:** encerrar a issue #1386 como **superada** (`not_planned`), não como defeito causalmente corrigido ou campanha científica certificada.

## Contexto e justificativa
A #1386 investigava `V9_CALIBRATOR_FAILED:INVALID_DATA` da campanha 24×100k de 09/11/2026. A evidência original não permite reconstruir o predicado exato que falhou nem o contexto suficiente para atribuir causa com segurança. O PO indicou como hipótese retrospectiva população amostral insuficiente; **não é causa forense comprovada**. A instrumentação segura foi integrada pela PR #1387. O Calibrador evoluiu para V3, com execuções físicas posteriores bem-sucedidas: #44927010029 (TRAIN SQL V3 30k) e #44927010035 (seis TRAIN V3 30k). Na consulta, a campanha 100k #44930426056 tinha ADMISSION e PREPARE concluídos e TRAIN V3 de 324 hipóteses em execução, **sem resultado final**.

## Efeito, limites e precedência
A investigação do erro legado deixa de ser pendência ativa; baixa administrativa como superada, sem afirmar causa corrigida, ausência universal de INVALID_DATA, êxito de 100k, certificação VALIDATION/TEST ou promoção. Novas falhas atuais exigem evidência própria no fluxo V3. Permanecem os gates científicos e operacionais, decisões Standard Linear, restrições #1349/#1253 aplicáveis e GOV-DOC-01. Não altera plano, multiplicidade ou parâmetros.

## Rastreabilidade
- Issue: https://github.com/lucianox777/Jornada/issues/1386
- Auditoria: https://github.com/lucianox777/Jornada/issues/1828
- Instrumentação: https://github.com/lucianox777/Jornada/pull/1387
- Execuções posteriores: https://github.com/lucianox777/Jornada/actions/runs/44927010029 e https://github.com/lucianox777/Jornada/actions/runs/44927010035
- Campanha em andamento: https://github.com/lucianox777/Jornada/actions/runs/44930426056
- Aprovação expressa do PO nesta conversa em 21/11/2026.

# GOV-V9-100K-DIRECT-EXEC-20261109 — prioridade à execução física direta

**Autoridade:** Product Owner, 09/11/2026. **Política:** GOV-DOC-01. **Estado:** DECISÃO APROVADA; execução NÃO INICIADA por este registro. **SHA de referência no momento da decisão:** `d4b4ac174355f8b9e97675b2342839ceb076b1ef` (master); o run exige SHA corrente verificado.

## Contexto e justificativa

O run físico manual [#42678462930](https://github.com/lucianox777/Jornada/actions/runs/42678462930) para 24 hipóteses V9 e 100 mil TRAIN terminou FAILURE em `V9_CALIBRATOR_FAILED:INVALID_DATA`, sem fits completos. A PR [#1390](https://github.com/lucianox777/Jornada/pull/1390) investiga os limites pré-SQL offline. A PR [#1388](https://github.com/lucianox777/Jornada/pull/1388) incorporou o governador conjunto CPU/RAM/SQL/disco após a tentativa anterior. O PO decidiu priorizar evidência física da implementação da master, sem esperar o término da investigação offline.

## Decisão, precedência e fronteiras

1. Priorizar disparo **manual, owner-only**, do workflow existente `.github/workflows/v9-multiparameter-100k-technical.yml` na master, sem condicionar a execução à conclusão da PR #1390.
2. Preservar exatamente as **24 hipóteses oficiais** (16 witness/diagnósticos e 8 candidatas CP), **100.000 identidades TRAIN sintéticas**, seed42, plano re-vinculado e hashes verificáveis, TF/M/U/FDR/CP/81 células, LOCK, V2 testemunha, V3 e governança de ciência. Não usar TEST/Gold, não ativar modelo, não modificar HML/PRD/DT-15.
3. **Não dispensar** o gate `security_preflight_approved`: confirmar verdadeiramente segurança de SQL efêmero, ausência de reutilização de credenciais e demais condições do workflow. A pendência histórica [#1253](https://github.com/lucianox777/Jornada/issues/1253) não se considera resolvida por esta priorização. Proibido preencher `true` sem verificação.
4. Exigir `expected_sha` igual ao SHA vigente da master e ao SHA efetivamente executado; não realizar merge ou push que invalide a execução durante o run. Uma alteração da master exige revalidar o pin antes do disparo.
5. Se INVALID_DATA reaparecer, conservar logs seguros, estado de interrupção e recibos para diagnóstico **posterior**. FAIL continua FAIL; não desativar caps, checks ou filtragens para conseguir PASS. A PR #1390 pode prosseguir independentemente, mas não é prerequisito operacional para solicitar a campanha.
6. Ao encerrar, registrar run ID, SHA, digest do plano e do corpus, 24/24 fits ou falhas, comparabilidade e métricas técnicas agregadas, flags de TEST/Gold/ativação, evidências de segurança e conclusão PASS/FAIL/NÃO COBERTO, atualizando este documento e os índices de decisões da GOV-DOC-01. Nenhuma certificação de FDR de população real é inferida.

## Impactos e rastreabilidade

**Impacto operacional:** aceita uma nova tentativa potencialmente falha e o consumo de recursos do runner; evita bloquear antecipadamente o ensaio por uma investigação offline. **Impacto científico:** nenhum, porque critérios, escopo e parâmetros são preservados. **Precedência:** altera apenas a sequência de execução do diagnóstico pré-SQL #1390 frente à campanha autorizada na [decisão 24×100k](Decisao_GOV_V9_100K_24_Configuracoes_Ordem_Execucao_20261109.md); **não** substitui condicionantes de segurança, gates e separação de ciência daquela decisão e das decisões do governador. **Execução:** indisponível via conector GitHub desta sessão (sem endpoint de dispatch); deve ocorrer pela interface owner-only do GitHub com confirmação real do preflight. Este documento não é recibo de run físico.

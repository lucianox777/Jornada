# GOV-V9-ETA-OBSERVABILIDADE-OFF-GPU-ENCERRADA-20261117

**Autoridade:** Product Owner, 17/11/2026. **Política:** GOV-DOC-01. **Estado:** decisão aprovada; triagem de implementação e verificação de regressão pendentes.

## Contexto e justificativa
O PO determinou eliminar qualquer cálculo de ETA cuja finalidade seja exclusivamente observabilidade, por preocupação com sobrecarga e desempenho. A pesquisa de uso de GPU deve ser encerrada, pois não existe GPU contratada e não há interesse atual em adoção. A investigação de GPU opcional estava registrada na issue #1301, agora fechada como **not planned**. A proposta de ETA da PR #1458 já está fechada, não integrada.

## Decisão vigente
1. Não executar nem reintroduzir cálculo de ETA, média de conclusão, projeção de término ou risco de timeout **apenas para exibição** no caminho de TRAIN/calibração. Remover implementações residuais exclusivamente observacionais após identificar seus consumidores; não remover deadlines reais, timeout nativo do GitHub, limites de segurança, métricas científicas ou timestamps exigidos para auditoria.
2. Preservar apenas o progresso mínimo já aprovado (contador/total e identificador da hipótese), sem ETA, cronômetros extras ou polling de observabilidade.
3. Não pesquisar, prototipar, contratar nem habilitar GPU no escopo atual. Manter CPU como execução vigente. Issue #1301 encerrada por decisão de prioridade/custo, **não** por prova de inviabilidade técnica; reabertura somente com nova decisão explícita do PO.
4. A fórmula histórica e seus documentos permanecem como trilha de auditoria, **não** como autorização de reativação.

## Evidência e limites
Na master revisada em 17/11/2026, `V9NestedCalibrationCoordinator.ReportPhysicalCompletion` emite somente `i/total - SHA12`, sem invocar `ForecastPhysicalProgress`. A decisão anterior GOV-V9-CALIBRADOR-LOG-MINIMO-20261115 permitia manter o helper matemático puro para compatibilidade de testes. A presente decisão mais recente prevalece: a PR de implementação remove o helper não invocado `ForecastPhysicalProgress`, seu DTO `V9PhysicalProgressForecast` e os testes exclusivos de previsão, sem modificar o progresso mínimo nem o timeout nativo. A presença anterior do helper isolado **não comprovava custo em runtime**; esta limpeza é preventiva, sem alegação de ganho físico. Nenhuma melhoria quantitativa de performance é alegada sem medição.

## Precedência e impactos
Esta decisão **prevalece**, para ETA operacional, sobre GOV-V9-ETA-TAXA-MEDIA-GLOBAL-CONCLUSOES-20261114, GOV-V9-TIMESTAMPS-JOB-360-20261114 e GOV-V9-PROGRESSO-PREVISAO-RISCO-TIMEOUT-20261114; mantém GOV-V9-CALIBRADOR-LOG-MINIMO-20261115 e sua simplificação. Não altera ciência V9, regras TF/M/U/FDR, seeds, TEST one-use, workflow_dispatch manual ou segurança. Rastreabilidade: issue #1301, PR #1458, PR #1551, GOV-DOC-01.

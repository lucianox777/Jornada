# Rastreabilidade do diagnóstico FS

A calibração é manual; o diagnóstico de suficiência é somente leitura.

Corte: 2026-10-10. Este índice não é aceite institucional nem evidência de implementação.

Fonte normativa: DT23_Diagnostico_Automatico_Suficiencia_FS.md.
Apenas diagnóstico automático de suficiência, sem recalibração automática.
O operador autorizado decide manualmente gerar, validar e ativar um modelo.

Matriz de rastreabilidade:
- DT-23: PR #900 implementa avaliador puro, sem integração no Monitor. Falta teste E2E de não invocação da calibração.
- IBGE #497: preparar e reutilizar referência congelada; falta demonstrar cache-hit e imutabilidade em CI.
- Blocking #612: PR #901 avalia soma conservadora; falta medir união e integrar Runner.
- Linkage DT-05 #494: separar auditoria por run de transição semântica; falta regressão de três ondas.
- Evidência representativa #31: permanece requisito externo; corpus sintético não certifica população real.

Em revisões, diferenciar especificação, PR, CI aprovada, merge e aceite institucional.
Não adicionar gate sem propriedade e risco verificáveis, conforme issue #404.

# V9 — auditoria ampliada V3, TF (prenome e sobrenome) e FDR configurável a 1%

**Escopo**: revisão técnica e regressão física no Calibrador C# único, sem certificado estatístico, sem TEST, sem publicação/DT-15 e sem alterar a V2 histórica. Evidências anteriores são de commits diferentes; comparações entre cenários não são efeitos causais.

## Diagnóstico inicial que motivou a revisão

Na execução técnica V3 seed 42 do mesmo Calibrador, a opção **TF_ONE_MF** (TF no primeiro nome da mãe) obteve **6.771/7.011 TP** (recall 96,5768%) com três passes, 0 FP, política CLASSWISE 0,99/M5/U5/tail. A opção **TF_TWO_PL_ML** (sobrenome da pessoa e da mãe) obteve **6.307/7.011 TP** (89,9586%) no teste de sobrenomes da PR #1294, com os mesmos 3 passes e a mesma política M/U: diferença descritiva de **−464 TP, −6,6182 pontos percentuais**. Não é uma contradição entre duas execuções do **mesmo TF**: são opções de **TF diferentes**. Um resultado usa prenome; o outro, sobrenomes. A diferença pode também envolver políticas de TF e a população efetiva; portanto só uma nova execução física pareada, no mesmo SHA/SQL/corpus/seed, é prova de regressão dessa hipótese.

Com 19 passes, ONE_MF V3 teve 6.826 TP e bilateral de sobrenomes V3 6.825 TP (−1 TP), ambos com 0 FP. **O fanout de 19 passes excedeu o orçamento** e inviabiliza sua seleção; não usar o recall 97% como alternativa operacional já aprovada. A medição histórica congelada V2→V3 (−4 TP) não é comparável com o novo TRAIN seed42 de 6.771 TP.

No teste pareado de sobrenomes da #1294, as duas hipóteses de dependência produziram decisões iguais, mas **3 passes tinham zero pares de sobrenome compartilhado afetados** e 19 passes tinham 2.848; o teste 3-pass é não informativo. Os testes unitários do scorer demonstram mudança de log-score entre os dois modos quando há sobrenome compartilhado, mas a igualdade agregada não prova igualdade de todos os scores nem ausência de efeito em outros dados.

## O que a auditoria amplia

O gate `v9-v3-broad-train-regression.yml` usa **três seeds sintéticas já conhecidas, 42/43/57** (não holdouts cegos). Em cada uma, duas famílias distintas:
- **surname**: dois modos de sobrenome, bilateral elegível e compartilhado diagnóstico não elegível;
- **first_name**: controle TF0 e ONE_MF, ambos parametrizados no JSON e sem M/U V2.

Para seed 42, 8 combinações V3 M/U (mass coverage 0,95/0,99; mínimo M 1/5; mínimo U 1/5; tail fixada true), duas opções TF, duas margens 0,5/2 e dois rulesets SQL: **64 configurações/família**. Para seeds 43/57, a política V3 de referência 0,99/M5/U5/tail com duas opções/margens/rulesets: **8 configurações/família/seed**. **Total pré-declarado: 160 medições** físicas (128+32), cada seed reconstruída em banco SQL descartável e matriz de report auditada.

Isso amplia **cobertura de condições e heterogeneidade do corpus sintético**. Os registros são TRAIN_SELECTION interno. Refit por seed: **não** caracteriza transferência congelada, nem teste independente, nem certificação de superioridade. Seeds 43/57 já foram expostas nas campanhas anteriores. O TEST permanece fechado. Não abrir seeds novas para explorar resultado favorável.

## Risco FDR agora explícito no plano

O produtor padrão escreve, também nos planos não-fixture:
```json
"Calibration": {
  "Budget": { "MaximumObservedAcceptedFdrPoint": 0.01 },
  "SelectionPolicy": { "MaximumAcceptedFdrUpper": 0.01 }
}
```
Este é um **fragmento** do plano completo. O builder V3 lê os dois valores do próprio JSON, não os substitui silenciosamente quando recebe `--initial-only`. Validação fail-closed exige `0 < upper <= observed <= 1`; as regras de governança na validação C# continuam exigindo limites consistentes. **Um limite escolhido de 1% não torna um intervalo condicional certificado**: `ClusterIidIndependentlyValidated=false` continua, e o seletor não poderá promover configuração sem evidência adequada.

## Inspeções e critérios de aceitação

O auditor independente `v9-v3-broad-train-audit.py` examina diretamente `plan.json` e `mu-statistical-analysis.json` gerados pelo C# para **cada linha**:
- presença de snapshot real, hash físico do mesmo conjunto de candidatos, estimador V3, política M/U, prior calculado e diagnóstico de sua proveniência; suporte das **81 células** em fit e auditoria;
- igualdade de todos os critérios necessários para parear duas opções no mesmo ruleset/M/U/margem; fórmula recalculada de recall e FDR; tratamento de zero FP como **descritivo**; ausência de selo IID; sem TEST/Gold/DT-15;
- integridade dos 64/8 braços exigidos, sem aceitar saltos ou configurações não medidas; registro das rejeições por fanout/P95 e de todos os deltas;
- para seed42 na política de referência, replay de regressão **ONE_MF 6.771 TP** e **bilateral de sobrenomes 6.307 TP** em 3 passes, 0 FP e 7.011 elegíveis. Uma divergência **interrompe o gate** em vez de ser tratada como uma melhoria.

Os dados por pessoa não são anexados ao artefato: relatórios agregados e seus hashes vinculados ao SHA exato serão preservados. Se ocorrer falha técnica, o gate falha sem alterar resultados históricos.

## Limites para o relatório final

O limiar de 1% é uma política selecionável no plano, não um estimador novo. O FDR observado em zero é `FP/(TP+FP)=0` no conjunto examinado; o intervalo condicional que aparece no relatório NÃO supera a ausência de certificação de independência entre unidades. O protocolo de comparação ampla é exploratório e reusa informações sobre seed conhecida; declara explicitamente toda a família testada. Se os dados de seed43/57 mudarem as conclusões, a investigação deve ser feita por hashes e divergências de TF/MU/frames, não por escolher apenas os casos mais favoráveis.

**Critérios para merge:** testes offline de pré-flight e de corrupção, C# compilando, **todos os braços físicos executados**, amostras reconciliadas, quatro FDR gates honestos, `jornada-ci` verde no HEAD exato. Não se aceita green check sintético substituindo o SQL físico. Sem resultado físico completo, manter PR aberta.

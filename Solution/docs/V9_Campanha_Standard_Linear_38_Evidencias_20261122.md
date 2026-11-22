# Evidência operacional e científica — campanha V9 Standard Linear #38 (22/11/2026)

**Classificação:** registro de evidência, não decisão de ativação nem certificação. **Fonte:** [GitHub Actions run 44970012048](https://github.com/lucianox777/Jornada/actions/runs/44970012048), tentativa 1, SHA de execução `30c71a0db1bdeac6ba536db5e428ade4d5c70c9c`. Este registro não altera decisões anteriores ou o escopo de issues.

## Execução e proveniência

Sete jobs observados concluíram `success`: ADMISSION, PREPARE, TRAIN V3 TF4 (324 hipóteses), AUDIT, VALIDATION, TEST independente e fechamento E2E. Artefatos da run: TEST `14742233807` (`independent-test.json`); relatório comparativo `14742621957` (`campaign-report.json`, `dt15-operator-candidate.json`); modelo operacional `14742452561` (JSON do vencedor). A existência de jobs bem-sucedidos não substitui a inspeção dos respectivos recibos. Os artefatos estão sujeitos à política de retenção do Actions; preservação externa com verificação de hashes continua pendente.

## Resultado do TEST independente (artefato 14742233807)

- Vencedor congelado (SHA-256): `6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee`.
- 100.000 identidades-base configuradas; 20.146 identidades-base selecionadas para TEST; 34.065 observações; 53.225 pares candidatos distintos; **uma configuração avaliada**, 20 repetições E2E.
- TP aceitos: **24.915**; FP aceitos: **1**; FN de observações de origem: **819**; recall global **96,81744%**; recall de blocking **98,66713%**; FDR observado após resolução **0,0040135%**; P95 **4.463,6968 ms**; P99 do grau falso: **22**.
- Limites reportados: Hoeffding superior por identidade-base de origem **3,05917%**; Clopper-Pearson superior de qualquer FP por identidade **0,0301344%**. São estimadores/denominadores distintos do FDR pontual, sem certificação IID independente.
- Manifesto TEST SHA-256: `2b9cd2afe5db95df15c809c7bd15d67e2adf044fb8fba14606e4bc262b195ad5`; observações TEST SHA-256: `b1130457ac4288a8bc32fe6eb250285146c2b3c31ecdc1ffab77bbc56293e4ac`.
- O recibo declara `certified=false`, `activated=false`, `independentlyCertifiedIid=false` e `productionRunnerFullPipelineParityCertified=false`. A igualdade de bytes do modelo SQL não comprova equivalência E2E com o Runner de produção.

## Relatório comparativo (artefato 14742621957)

Três finalistas; vencedor preservado após VALIDATION. `comparisonStatus=NOT_COMPARABLE_WITHOUT_PAIRED_EVIDENCE`, `promotionAllowed=false`, `productionAuthorization=OPERATOR_MASTER_ONLY_NOT_GRANTED`. `testAssessment=null`: **não há comparação numérica pareada VALIDATION×TEST no relatório**. TEST é relatório do vencedor, sem reseleção. Não inferir certificação, promoção, autorização DT-15 ou equivalência produtiva.

## Recursos e lacunas

A duração agregada aproximada reportada da campanha é 5h05, incluindo TRAIN ~4h52; estes tempos não são prova causal de desempenho nem de ausência de gargalo de cache SQL. **CPU, RAM, disco, fanout físico por hipótese e orçamento de recursos desta run ainda não foram auditados neste registro**; não importar medições de outra run. Preservar os artefatos e completar a auditoria de recursos antes de alegar cobertura dos critérios de #1349.

## Consequências e rastreabilidade (GOV-DOC-01)

- **#1089:** evidência de execução de calibração e vencedor; reprodução histórica, REF, bootstrap e gates residuais não são automaticamente satisfeitos.
- **#1349:** prova V3 TF4 100k, mas **não** substitui automaticamente o ensaio multiparâmetro V2/V3/misturas e a reclassificação PO de 21/11/2026. Mantida aberta até reconciliação explícita dos critérios.
- **#1488/#1584:** contrato e paridade operacional E2E permanecem pendentes.
- **#1718:** tempo de VALIDATION/TEST não mede isoladamente benefício do cache SQL; nenhuma decisão de dispensar o cache.
- **#1478/#1575:** permanecem fechadas conforme histórico; não reabrir.

**Precedência:** este documento adiciona evidência à governança vigente, sem revogar decisões anteriores, alterar LOCKED, liberar HML/PROD ou autorizar promoção pelo Operador Master. Revisão e checks da PR documental são necessários antes da integração em `master`.

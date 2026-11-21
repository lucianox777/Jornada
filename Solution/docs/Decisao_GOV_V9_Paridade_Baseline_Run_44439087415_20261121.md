# GOV-V9-PARIDADE-BASELINE-44439087415-20261121

**Autoridade:** PO, 21/11/2026. **Status:** APROVADA; paridade ainda não comprovada. **Política:** GOV-DOC-01.

## Decisão
O comportamento atual do calibrador e sistema deve corresponder ao comportamento efetivamente executado na campanha [#44439087415](https://github.com/lucianox777/Jornada/actions/runs/44439087415), SHA `317385ee3ee64cbb73add3bedd0149925f18b35f`. O PO confirmou expressamente essa execução, rejeitando as referências anteriormente sugeridas #42534643000 e #43105369099. A referência é **comportamental e observacional**, não certificação científica ou autorização de ativação.

## Evidências primárias verificadas
Jobs: ADMISSION success; PREPARE TRAIN 100k success; TRAIN físico 324 hipóteses V3 TF4 success; AUDIT independente success; E2E closeout failure; VALIDATION skipped; TEST skipped. **Não declarar workflow integral SUCCESS.**

Artefatos da execução: pré-auditoria ID **14490257319** (704352508 bytes); TRAIN ID **14490427116** (944330 bytes); auditoria ID **14490093743** (16482 bytes); preparação ID **14490101615** (83648 bytes); recibo REF ID **14490418052** (517 bytes). Conteúdo interno e métricas FP/TP/FN/FDR ainda não auditados nesta decisão.

## Justificativa, impacto e verificação
Fixar SHA e artefatos elimina ambiguidade de referência e permite detectar regressões. Comparar no mesmo corpus/seed/REF/configuração congelados: 324 hipóteses, fingerprints, parâmetros, seleção/finalistas, TP/FP/FN/FDR, scoring, Resolve, decisões, recibos e hashes. Diferenciar determinismo funcional de duração e recursos variáveis do runner. Documentar divergências e causas; **não afirmar paridade sem prova**. Não reproduzir a falha de closeout como requisito; preservar gates atuais.

## Precedência e rastreabilidade
Substitui somente a identificação incorreta da referência de comportamento; preserva LOCKED, segregação TRAIN/VALIDATION/TEST, gates fail-closed, GOV-DOC-01 e restrições de publicação/ativação. Referências: run #44439087415, SHA acima, [artefatos](https://github.com/lucianox777/Jornada/actions/runs/44439087415/artifacts). Próximo passo: ler artefatos e comparar SHA histórico com HEAD, registrando relatório de paridade.

## Verificação diferencial documental e de código — 21/11/2026

Comparação direta GitHub `317385ee3ee64cbb73add3bedd0149925f18b35f...master`: 390 commits posteriores; diferenças de código não equivalem automaticamente a regressão. O JSON `Solution/config/calibration/v9-synthetic-sampling.json` possui **blob SHA idêntico** nas duas versões (`97938032157e3f48f4eca8d85aae660b620b36c2`), inclusive `syntheticPopulation=100000`, divisão 60/20/20 e `tfNominalPolicy.unpublishedTermPolicy=0`.

**Mudança comportamental efetiva no gerador do plano TF4:** em `Solution/scripts/v9-v3-four-name-100k-plan.py`, a versão histórica atribuía `UnpublishedTermPolicy` a cada opção ativa TF a partir de `tf_policy["unpublishedTermPolicy"]` (quando fornecido); a versão atual fixa `UnpublishedTermPolicy=1` para TF ativa, preservando TF0. Assim, com o mesmo JSON congelado (`unpublishedTermPolicy=0`), o plano gerado **não mantém a mesma política TF ativa**. A documentação `Decisao_GOV_V9_TF4_Termo_Nao_Publicado_20261119.md` explica que essa mudança foi deliberada para evitar falha de TEST em nomes não publicados e preservar abstenção por candidato; não classificar automaticamente como regressão. Implicações: fingerprint do plano e resultados potencialmente distintos; exige comparação física para determinar efeitos em TP/FP/FN e finalistas.

**Mudança restrita de retrocompatibilidade:** `Solution/src/Jornada.Linkage.Core/V9FittedSnapshot.cs` mudou a rejeição genérica de codificação não canônica para admitir apenas SHAs históricos explicitamente fixados (V2 e V3 experimental) sob verificações de identidade e bytes. A decisão `Decisao_GOV_V9_Compatibilidade_V2_Historico_20261119.md` registra A2 run #44631607257 com `V2_READ_OK`, `V3_SHA_VERIFIED`, `V3_READ_OK`; isso demonstra leitura desses artefatos específicos, **não** equivalência geral de scoring/produção. `Decisao_GOV_V9_Compatibilidade_Retroativa_Bundle_Calibrador_Producao_20261117.md` exige execução produtiva de bundles históricos, sem homologação obrigatória automática.

**Artefato histórico inspecionado:** ZIP da auditoria #14490093743 contém `bound/technical-audit.json`; `physicalMeasured=324`, `tfOptions=81`, `replayedP95=0`, `preReplayRejectedWithoutP95=324`, `stopReason=NO_FEASIBLE_CONFIGURATION_NO_HOLDOUT_USED`, `finalistSnapshotShas=[]`, SHA do plano `10f37ed3ddd18b457c963d667b7edd4bcb78df9676c36e1470f21cf0939fafea`, SHA do corpus `d4c6fe2f96424ad704450a3d1cec5bac48b880243bab5add5e4f1783adef6efe`. Esses resultados são baseline histórico verificável, não resultado atual.

**Conclusão estrita:** a versão atual **não é comportamentalmente idêntica em todos os contratos de geração do plano** à versão histórica, pois há diferença deliberada na política TF ativa sob a mesma configuração. Há evidência positiva de retrocompatibilidade de leitura de dois snapshots fixados, mas **não há prova de paridade ponta a ponta nem de resultados físicos atuais**. Próxima etapa: gerar o plano atual em ambiente controlado e comparar opções/fingerprints, depois executar comparação física autorizada com corpus/REF idênticos; classificar diferenças por decisão vigente e nunca por simples igualdade de código.

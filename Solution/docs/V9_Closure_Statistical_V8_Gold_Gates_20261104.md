# V9 — fechamento científico, regressão V8 e Gold (auditoria pós-seed43)

**Base auditada:** master `f52de3f2e82dc4e12fa56e44cdcc1e6d691c6cbb`. Este documento é uma **matriz de pendências e critérios**, não um certificado. Não modifica snapshots, resultados, modelo, DT-15, TEST, IBGE nem política operacional.

## 1. Evidências existentes e seus limites

- Seed42: `execute_30k` run **41900357031**, commit de origem `935f704edd2bceb6a125ecfe532ad5b71f9c89b9`. Resultado exploratório; não transportar a aprovação para outro SHA.
- Seed43: run físico **41925355648**, master `b98a01180737909edf7cbd0bee4ba2757d52fe0f`; 30.000 identidades geradas, mas análise **somente VALIDATION**, 5.492 identidades e 7.704 fontes elegíveis. TEST não consultado.
- Dois braços pré-congelados × margens 0,5/2/4; comparação principal margem 0,5. ΔTP=+35, ΔFP=0, recall +35/7704 ≈ +0,4543 p.p.; 36 identidades melhoram, 6 pioram, 5.450 empatam. FDR CP ~0,25% é **condicional**; não é limite operacional certificado.
- O relatório da seed43 informa IC95% pareado [+0,24;+0,67] p.p. e teste de sinal unilateral ~1,4e-6, mas também **não rejeição pelo Hoeffding pré-declarado**. Não substituir retrospectivamente o teste confirmatório por um teste mais favorável.
- `ClusterIidIndependentlyValidated=false`; competição de candidatos pode produzir dependência **entre** identidades-base. Uma clusterização por identidade-base elimina dependência **dentro** do cluster, não garante independência entre clusters.
- Seed43 já exposta: não reutilizar como holdout confirmatório fresco. Multiplicidade histórica (3.861 opções) não desaparece ao restringir a análise descritiva a dois braços.
- P95 845/656 ms são medições no runner e podem sofrer efeito de ordem/aquecimento. Não equivalem a SLA de produção.

## 2. Reconciliação estatística — executar antes de dimensionar nova campanha

1. Reproduzir com dados agregados e fórmula publicada: `n=5492`, `ΔTP=35`, `eligible=7704`, `CAP=4`, `alpha=0.025`. No código `v9-tf-seed43-frozen-transfer-physical.py`, a variável pareada `X_i = TP_ONE_MF,i - TP_TF0,i` está limitada a [-4,+4]; o limite unilateral é `mean(X) - 8*sqrt(log(1/alpha)/(2*n))`. O ganho observado é `35/5492` por identidade, muito inferior à penalidade. **Checagem independente importante:** com efeito fixo observado `35/5492`, amplitude 8 e alfa 0,025, o tamanho mínimo algébrico para tornar o limite Hoeffding unilateral estritamente positivo é **2.906.494 identidades**, não 14,9 milhões. **Reconciliação documental:** `V9_TF_002_Statistical_Sample_Design_20261104.md` informa explicitamente que **14.860.865** é dimensionamento prospectivo para **80% de poder**, efeito mínimo relevante de **+0,36 p.p.**, média hipotética de **1,30 fontes elegíveis por identidade** e α=0,025. Já **2.906.494** é somente o n algébrico para o limite unilateral tornar-se positivo, assumindo o efeito **observado** fixo de 35/5492, sem requisito de poder. Os números não são diretamente comparáveis nem contraditórios. A fórmula exata do cálculo prospectivo C# ainda deve ser conferida independentemente; nenhum deles é orçamento aprovado. O script `Solution/scripts/v9-tf-seed43-aggregate-stat-audit.py` reproduz apenas o limite algébrico, sem simular poder ou certificar independência.
2. Explicar no dossiê a diferença entre inferência por IC assintótico/variância empírica, teste de sinal condicional aos não empates e limite Hoeffding uniforme. Os dois primeiros exploram a baixa frequência de discordâncias; o terceiro protege contra qualquer distribuição em [-4,4]. Não são testes intercambiáveis.
3. Auditar dependência cruzada: registrar grafo de competição de candidatos, componentes conectados, distribuição de tamanhos e interseção entre partições. Determinar unidade efetivamente independente ou propor método válido para grafo/dependência; se impossível, manter `NOT_CERTIFIED`.
4. Revisar o estimando FDR: denominador `FP/(TP+FP)`, agregação por cluster, tamanho de cluster e relação entre eventos `any FP` e FDR. Confirmar cobertura do limite para a unidade e família realmente avaliadas; não inferir independência a partir de zero FP.
5. **Antes de novo corpus:** pré-registrar hipótese primária, tamanho/poder, alfa e multiplicidade, método estatístico com justificativa, unidades independentes, protocolo de geração, hashes, critérios de parada e orçamento. TEST anterior permanece selado.

## 3. Duas referências distintas — baseline V9 TF0 e comportamento histórico V8

**Não confundir V8 com o indicador 97,4754%.** O valor **97,4754%** corresponde ao finalista **V9 TF0 de 19/10**, em TRAIN_SELECTION BLIND (6.834 TP / 7.011 elegíveis, zero FP), com suporte conjunto de 81 células, oito passes, upper 0,8671875 e TF=0; a VALIDATION independente relatada na mesma documentação foi **97,5417%** (7.539 / 7.729, dois FP). Fontes: `V9_Suporte_Massa_Backoff_20261019.md` e `DC-V9-CALIBRADOR-BASELINE-E-VALIDACAO-20261030.md`. Esses valores são de **duas partições/etapas diferentes** e não demonstram execução da V8.

**Gate A — reprodução do baseline V9 TF0:** a PR #1272 registra que os 97,4754% **não foram reproduzidos em condições originais** pela campanha TF0 × ONE_MF de três passes. A **evidência original está localizada** em `Solution/data/calibration/v9/mass-support-20261020/`: `README.md`, `evidence-manifest.json`, `final-study-report.json`, `selection-reference.json`, `plan.json` e o fit `0a3459585fcf3d20fbd0854e977b84a769127a9ab01ab0d72417dff42d0b3e5b.fitted.json`. O recibo informa 926 avaliações, corpus original SHA `28bbb9f4c63c97c353e7f29cdbc77091a4773b2f4c596e46103edadd93635d17`, oito passes, 6.834/7.011 TP/elegíveis e nenhum FP; o envelope integral comprimido e os demais arquivos possuem hashes no manifesto. **Auditoria curta e fail-closed** no script deste PR: `python3 Solution/scripts/v9-tf-seed43-aggregate-stat-audit.py --verify-v9-tf0-baseline` confirma bytes/SHAs de três evidências pequenas, parâmetros, proveniência e métricas agregadas **sem ler o CSV ou TEST**. Isso identifica a prova histórica e atesta integridade parcial, **não executa o replay** com o código V9 atual, nem reproduz o processamento físico. O replay do baseline nas mesmas condições (ou equivalência rigorosa com tolerâncias pré-definidas) permanece pendente. Status: `V9_TF0_BASELINE_EVIDENCE_LOCATED_REPLAY_NOT_VERIFIED`.

**Gate B — compatibilidade semântica com V8:** segundo `V9_TF_002_Transferencia_Seed43_Resultado_Fisico_20261104.md`, a V8 histórica aplicava TF de prenome da pessoa **e** da mãe; o braço `TF_TWO_PF_MF_W1_M0` na TF-001/seed42 teve 96,49% e um FP. Esse braço **não** é o Gold V9 TF0 de oito passes, nem a configuração `ONE_MF` de três passes, nem uma prova completa de compatibilidade. Verificar contra contrato e artefatos V8 originais nomes/TF/pesos, abstenção, ausência de candidato, persistência e identidade, sem alterar snapshots históricos. A norma DEV v1 não exige executar o binário V8 legado, mas não dispensa a preservação das evidências e da semântica aprovada. Até evidência específica: `V8_SEMANTIC_COMPATIBILITY_NOT_VERIFIED`.

**Regra de aceite:** não transferir percentual, aprovação estatística, algoritmo ou SHA entre esses ensaios. Se já existir evidência física original suficiente, anexar seu identificador e validar hashes sem repetir a campanha apenas para procurar resultado favorável.

## 4. Gold — diferenciar evidência histórica TF0 de validação física V3/TF

**Há execução física Gold histórica:** `V9_Gold_V6_Resultado_Fisico_Candidato_Validacao_20261025.md` registra o run **40156139352**, com **12/12 corridas físicas** (quatro políticas × três ordens) em LocalDB descartável, no SHA histórico `080cb6437ae8130562ddb0573802f6c58bfdc430`. Isso **não** valida automaticamente o novo fit V3/TF, nem certifica a política operacional.

**Pendente para a frente Gold com TF congelada (#1277):** exigir candidato completo V3/TF da **mesma** configuração (sem misturar os oito passes TF0 históricos com o ONE_MF V2 de três passes), bytes e SHA pinados, vínculo REF de leitura autorizado quando aplicável, e ensaio real incremental com **quatro ondas × três ordens pré-declaradas** (G0→G3; G3→G0; G1→G3→G0→G2). Cada ordem em SQL/LocalDB novo e vazio, mesmo corpus e snapshot, comparação por IDs estáveis do CSV. Medir falsas criações, fusões, fragmentação, B³ padrão/com cobertura, P/R, conflitos, pendências, FDR/FP, P95 com aquecimento e ordem controlada, além de idempotência. Bloquear inconsistências de proveniência, contagens, hashes ou partições.

**Gate:** CI verde e 30k manual no SHA exato **quando o classificador exigir**, execução física Gold V3/TF e testes negativos; só depois avaliar integração técnica. Não confundir a PR **#1242**, que trata de rejeição de overflow JSON em TRAIN/REF, com a frente Gold. Não ativar lower/DT-15 nem publicar modelo automaticamente.

## 5. Encerramento e governança

- **Engenharia TF transferida:** #1269–#1272 integradas; seed43 física descritiva concluída.
- **Certificação estatística:** pendente; não trocar critério após observar seed43.
- **Baseline V9 TF0 97,4754%:** evidência original localizada, auditoria SHA/contagens implantada nesta PR; replay com a V9 atual pendente. É uma medição V9, não V8.
- **Compatibilidade semântica V8:** verificar separadamente a configuração histórica TF de prenome da pessoa e da mãe.
- **Gold:** há 12/12 corridas históricas TF0 (V6); campanha incremental V3/TF e P95 operacional permanecem pendentes.
- **Operação:** DT-15 e autoridade separadas; default OFF.

**Ordem de trabalho:** (a) reconciliação matemática + auditoria do grafo; (b) localizar e distinguir provas V9 TF0 e V8; (c) implementar/executar Gold V3/TF; (d) definir novo holdout e método pré-registrado; (e) emitir dossiê final. Não rodar 14,9 milhões nem novo 30k apenas para tentar obter significância.

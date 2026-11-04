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

## 3. Retrocompatibilidade V8 — gate ainda sem evidência conclusiva

O dossiê da PR #1272 declara explicitamente que a reprodução dos **97,4754% nas condições originais** não foi verificada. O TF0 da transferência usa blocking fixo de três passes, portanto não equivale à V8 histórica.

Para fechar: localizar o run/artefato histórico V8 (commit, corpus, gerador, pin IBGE, blocking, thresholds, métricas e hash), reproduzir com o código V9 sob **as mesmas condições** ou demonstrar equivalência byte-a-byte e numérica, registrar tolerâncias pré-definidas e regressões em nomes/TF/abstenção/ausência de candidato. Não substituir pela comparação TF0 vs ONE_MF seed43. Se a prova existente for válida, **referenciá-la** sem duplicar execução.

**Gate:** evidência de reprodução do 97,4754%, ou divergência documentada e aprovada como mudança intencional. Até lá: `V8_COMPATIBILITY_NOT_VERIFIED`.

## 4. Gold #1242 — campanha física obrigatória antes de certificação

Manter a PR draft enquanto não existir evidência de execução real da grade upper × lower × **três ordens fixadas antes do ensaio** (G0→G3; G3→G0; G1→G3→G0→G2), cada uma em SQL/LocalDB descartável, vazio, mesmo corpus/snapshot e comparação por IDs estáveis do CSV. Medir falsas criações, fusões, fragmentação, P/R, conflitos, pendências, cobertura, P95 com aquecimento e ordem controlada, além de idempotência. Fail-closed em inconsistências de proveniência, contagem, hashes ou partição.

**Gate:** executor + testes negativos + campanha física com hashes e relatório; CI verde no SHA exato; só então considerar merge técnico da Gold. Não ativar lower/DT-15 nem publicar modelo automaticamente.

## 5. Encerramento e governança

- **Engenharia TF transferida:** #1269–#1272 integradas; seed43 física descritiva concluída.
- **Certificação estatística:** pendente; não trocar critério após observar seed43.
- **Compatibilidade V8:** pendente de prova histórica nas condições originais.
- **Gold e P95 operacional:** pendentes de campanha própria.
- **Operação:** DT-15 e autoridade separadas; default OFF.

**Ordem de trabalho:** (a) reconciliação matemática + auditoria do grafo; (b) localizar prova V8; (c) implementar/executar Gold; (d) definir novo holdout e método pré-registrado; (e) emitir dossiê final. Não rodar 14,9 milhões nem novo 30k apenas para tentar obter significância.

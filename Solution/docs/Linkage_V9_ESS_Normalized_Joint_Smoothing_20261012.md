# V9 — normalização do suavizador da distribuição nominal conjunta ponderada por ESS

**12/10/2026, etapa 11 da issue #946.** Correção metodológica no **estimador diagnóstico** de tabela conjunta 9×9 pessoa × mãe, introduzido pela PR #953. **Não é nova decisão canônica**, não habilita o scorer V9 e não modifica modelo ATIVO, runtime, banco, m/u publicados nem a V8 histórica para replay.

## Defeito estatístico identificado

A implementação V1 do `CandidateWeightedJointNominalEstimatorV9` calculava por classe:

`P_antiga(c) = (W_c + α) / (ΣW + 81α)`,

onde `W_c = Σ_{par∈c} 1/π_par` é o **peso de desenho de Horvitz–Thompson da célula**, `ΣW` o total ponderado da classe e `α` uma pseudocontagem constante para cada uma das 81 células nominais mutuamente exclusivas.

O resultado depende da **escala absoluta dos pesos inversos da probabilidade de inclusão**. Se um desenho equivalente multiplicar todas as ponderações por `k > 0`, a distribuição empírica `W_c/ΣW` não muda, mas `(kW_c + α)/(kΣW + 81α)` muda. Logo, sem justificativa, a aparente força da regularização é afetada pelo tamanho do frame ou pela fração amostral, **não apenas pela informação efetiva da amostra**.

Exemplo com `W_c=8`, `ΣW=10`, `ESS=5` e `α=0,5`: a V1 produzia `8,5/50,5` para aquela célula; se todos os pesos fossem multiplicados por dez, produziria `80,5/140,5`, embora a distribuição empírica seja idêntica.

## Correção implementada — V2 diagnóstica

Preserva os **pesos observados originais** nas colunas de suporte e os totais ponderados no DTO. Define o **tamanho efetivo de Kish por classe**:

`ESS = (ΣW)^2 / Σ(W_par²)`

E, exclusivamente para a suavização da tabela diagnóstica, normaliza a massa de cada célula à escala do ESS:

`W'_c = ESS × (W_c / ΣW)`

`P_V2(c) = (W'_c + α) / (ESS + 81α)`

O mesmo procedimento é aplicado separadamente a M e U, **preservando as 81 células exclusivas e `Missing` materno**.

Por construção, sob o redimensionamento uniforme `W → kW`, `W_c/ΣW` e `ESS` são invariantes; portanto, `P_V2(c)` permanece inalterada. Isso resolve a sensibilidade artificial à escala das ponderações — **não** afirma que `α` foi estatisticamente calibrado. Para a classe M do fixture existente (quatro pares com peso 2 cada, ESS=4), a célula com concordância nominal total muda de `8,5/48,5` para `4,5/44,5`; na classe U (oito pares com peso 1, ESS=8) seu cálculo permanece inalterado.

A mudança é **intencionalmente versionada**: `CANDIDATE_WEIGHTED_NOMINAL_JOINT_V9_DIAGNOSTIC_1` → `..._2`, com `SmoothingBasis=KISH_EFFECTIVE_SAMPLE_SIZE` no retorno. Artefatos diagnósticos V1 e V2 **não são intercambiáveis**; quaisquer comparações históricas devem considerar a convenção explícita de suavização.

## Evidência e limites

- Regressões NUnit verificam valores exatos, normalização de M/U, escala `W`, `10W` e `0,1W`, rejeição de suporte negativo/maior que total, ESS inválido, pseudocontagem inválida e matriz com cardinalidade nula.
- A tabela ainda inclui `MatchedObservedWeight` e `UnmatchedObservedWeight` **sem normalização**, para garantir separação entre evidência empírica e convenção de pseudocontagem.
- **ESS de Kish é diagnóstico de pesos por par**, não quantidade demonstrada de grupos independentes e **não captura automaticamente dependência intragrupo**. A PR #958 mede influência LOO separadamente; esta normalização não a substitui.
- Não há estimativa de incerteza com cobertura nominal, julgamento dos 81 estados raros, paridade C# × Splink, seleção validada de `α` nem inferência de falso vínculo/FDR.
- O frame é o desenho `CandidateSamplingDesign` com `BirthBlockingPlan`, **não** uma amostra certificada das chaves SQL materiais `VERSIONED_ALIAS`; permanece `DIAGNOSTIC_ONLY_BIRTH_BLOCKING_NOT_RUNTIME_SQL_CERTIFIED`. A origem nominal bruta ainda necessita cadeia de custódia aprovada.
- Permanece vigente a [DC-LK-TF](Decisoes_Canonicas_Identidade_Linkage_20260929.md): a marginal IBGE de sobrenome em qualquer posição só é aceita como **proxy inicial explicitamente marcado** para o último sobrenome significativo; não é uma distribuição conjunta nem observação posicional.

**Nenhum efeito operacional**: o estimador V9 ainda não publica parâmetros, não altera score, threshold, seleção/ativação de modelo, DT-15 nem V8 histórica; DT10 permanece por último.

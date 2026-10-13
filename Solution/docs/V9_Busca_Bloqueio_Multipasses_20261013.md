# V9 — busca determinística de uniões complementares de até quatro passes (diagnóstico opt-in)

## Motivação e decisão de implementação (13/10/2026)

A calibração de blocking não pode pressupor que dois passes OR recuperem todos os vínculos verdadeiros; uma combinação de três ou quatro regras independentes pode ser necessária para tolerar degradações individuais de nome, mãe e nascimento. O mecanismo lógico da Jornada já interpreta **AND dentro de cada passe**, **OR entre passes**. A implementação anterior, `BLOCKING_RULESET_SEARCH_V2`, gerava apenas regras isoladas ou pares de regras, pois `MaxPasses <= 2`.

`BLOCKING_RULESET_SEARCH_V3` mantém o **comportamento padrão de dois passes**, sem mudar `LinkageParameters:BlockingSearch:MaxPasses` quando a opção não é fornecida, nem alterar o ruleset ativo. Mas permite **opt-in explícito** com `MaxPasses=3` ou `4` e enumera **todas** as combinações sem repetição entre os `PrimitivePoolSize` passes selecionados. O otimizador continua medindo cobertura e retenção de U da **união deduplicada** e escolhendo a maior redução de candidatos entre as regras que atendem ao piso de recall, suporte U e demais critérios já existentes. Nenhum resultado funcional de TEST ou regra física é promovido por esse recurso.

## Controles de custo e seleção

- `MaxFieldsPerPass` continua limitado a 1..3; `MaxPasses` passa a 1..4; o pool continua 1..64, com valor padrão 8.
- Número exato de combinações avaliadas = `Σ C(pool, k)`, para `k=1..min(MaxPasses,pool)`. O orçamento fixo é **2.048 uniões candidatas**. Se a configuração exige mais, a busca **aborta com erro**, sem executar enumeração parcial nem fingir que o melhor ruleset global foi encontrado.
- O enumerador ordena as features canonicamente, preserva assinaturas estáveis, testa todas as alternativas admitidas e falha se houver contagem divergente. O limite não é um `TOP N` SQL: ele controla apenas o número de configurações que o Calibrador consegue avaliar.
- O gate `MinimumTrueMatchRecall=0.95` e `RequireObservedNonMatchSupport=true` **permanecem configurados** na entrada do Parameters Worker; em caso de nenhuma política viável, a promoção continua bloqueada. O gate é medido **no corpus de calibração entregue ao otimizador**, e não comprova, por si só, 95% de recall em qualquer outro conjunto SQL, split ou fonte.
- Cada passe que não acrescenta M ou suporte U recebe a penalidade diagnóstica atual. Redução, recall, contribuição marginal de cada passe e assinatura continuam auditáveis. O conjunto de primitivas é um pool limitado; não existe alegação de busca global sobre todo o espaço de features.

## Prova de limites e próximos experimentos

Testes unitários verificam que três passes são necessários para recuperar três classes M disjuntas que **nenhum par de passes** recupera sob recall mínimo de 95%; verificam o caso de quatro, sem retirar suporte U; garantem determinismo à ordem das features e falha explícita acima de 2.048 configurações. O padrão de dois passes é testado e mantido.

Esta ampliação é **capacidade configurável**, não prova empírica de ganho no SQL físico: o diagnóstico TEST da PR #1001 (seeds 42/43/44, massa sintética 30 mil por seed) tinha **43,16% recall de ponta a ponta** com o baseline SQL ATIVO+C1, mas o gate de 95% opera em *outro frame*. Antes de propor novos passes operacionais deve-se comparar o fingerprint do ruleset e o mesmo denominador M com a decisão do Calibrador; decompor os 10.250 pares M perdidos, inclusive divergências simultâneas de nome/mãe/nascimento; e fazer seleção no TRAIN/VALIDATION. O gerador possui `CommonCorruptionProbability=0.18` no perfil `correlated`; esse número é probabilidade de **tentar** corromper conjuntamente três atributos, **não** a proporção medida de triplas divergências. Não imputar causas sem a decomposição por pares.

Na próxima etapa da issue #999, calibrar `m/u`, prior, nascimento, TF e thresholds **condicionados à mesma união física SQL selecionada**, com IBGE congelado para marginais compatíveis e massa-base sintética com um indivíduo por `base_person_id` para diagnóstico de unidades conjuntas; nunca multiplicar marginais para inventar distribuição de nome integral. TEST externo congelado e projeções históricas `VERSIONED_ALIAS` continuam pendentes de prova. TF=0 nos ensaios de referência; não declarar redução de FP ou aceitação de V9 a partir de mais passes.

**Segurança:** nenhuma alteração em `JornadaLocal`, IBGE original, NODE/NAS, Gold, HML/PROD, regras `Rank/Resolve`, histórico V8 ou DT-15. Só mergear instrumentação após todos os gates CI no SHA exato.

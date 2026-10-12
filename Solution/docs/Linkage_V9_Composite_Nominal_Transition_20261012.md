# Linkage V9 — transição para comparação nominal composta

**Decisão do proprietário: 12/10/2026.** A V9 substitui a V8 como **única arquitetura-alvo para novos modelos**; a V8 fica congelada como semântica histórica e não é mais objeto de correções conceituais. Isso **não** converte automaticamente modelos ATIVOS, não altera `VALIDATE/ACTIVATE`, não apaga snapshots, fingerprints nem evidências e não autoriza ativação V9 sem DT-15.

Issue de execução: #946. Incidente motivador: #944; reprodução de ranking: #927; auditoria diagnóstica legada: #945. A tentativa de veto V8 em #943 quebrou calibradores reais sintéticos; não servir de solução V9.

## Referência externa (não é código de produção)

- Splink, `ForenameSurnameComparison`: https://moj-analytical-services.github.io/splink/topic_guides/comparisons/out_of_the_box_comparisons.html
- Splink, TF e comparação por níveis: https://moj-analytical-services.github.io/splink/topic_guides/comparisons/term-frequency.html

Splink explica que o modelo FS não deve presumir independência entre prenome e sobrenome: comparação única permite níveis mutuamente exclusivos e, quando disponível, frequência **conjunta** da combinação. A implementação da Jornada continua sendo **um único scorer FS C#**, com conferência independente em Splink. Uma string composta `primeiro + último` **não representa todo o nome**: nomes intermediários e agnomes continuam na comparação integral.

## Contrato proposto — estágio 1 (ainda não operacional)

`CompositeNominalComparisonV9.Classify(left, right)` produz **um nível apenas** por comparação de nome de pessoa ou mãe:

| Nível V9 experimental | Regra (em ordem de prioridade) |
|---|---|
| `Missing` | Nome de pelo menos um lado ausente |
| `ExactFull` | Nome integral normalizado idêntico |
| `ExactFirstLast` | Primeiro nome e último sobrenome iguais; integral diferente |
| `ReversedFirstLast` | Primeiro nome e último sobrenome invertidos |
| `HighFull` | Jaro–Winkler do nome integral ≥ 0,92 |
| `HighFirstLast` | Jaro–Winkler de primeiro e último ≥ 0,92 |
| `ExactLast` | Só há igualdade comprovada do último sobrenome |
| `ExactFirst` | Só há igualdade comprovada do primeiro nome |
| `Other` | Divergência/nível residual |

A precedência elimina dupla contagem **dentro do campo**. Os níveis e thresholds são **candidatos experimentais**: NÃO representam um contrato `m/u` calibrado nem identidade confirmada. A extração do último sobrenome reutiliza `BrazilianNameComponents.Project(...).LastContentSurname`; é heurística nominal, não verificação civil. A mãe recebe o mesmo classificador de forma separada; dependência entre nome da pessoa e da mãe continua uma hipótese a ser medida. Nomes só de um token nunca recebem último sobrenome inventado.

## Regra obrigatória de TF na futura integração

- Um nível `ExactFull` só pode receber ajuste TF estimado a partir da **frequência de nomes integrais** em população/universo compatível com o seu parâmetro de referência.
- Um nível `ExactFirstLast` exige **frequência conjunta desse par** para TF do par, sem produto de marginais IBGE presumidamente independentes.
- Um nível baseado em `ExactFirst` ou `ExactLast` pode considerar TF de seu próprio componente **somente se os m/u do nível e a frequência compartilharem a unidade e o condicionamento estatístico necessário**; a frequência IBGE de sobrenome em qualquer posição é apenas proxy para a última posição.
- Sem frequência compatível/suporte/proveniência, a TF daquele nível deve ser **neutra/desabilitada**, nunca substituída pela frequência de prenome ao corrigir nome inteiro. O peso TF, inclusive 0, depende de calibração.
- Não acrescentar LLRs de nome completo, prenome e sobrenome independentes. Para um estado V9, usar um fator `ln(m_nivel/u_nivel)` calibrado por universo bloqueado e ajuste TF do mesmo contrato, se válido.
- Correlação **entre pessoa e mãe** também precisa ser estudada: não interpretar dois fatores como independentes sem medição/validação.

## Critérios de promoção (não concluídos por esta PR)

1. Capturar estados V9 do TRAIN e dos pares U **condicionados ao blocking**; estimar distribuições `m/u` por nível com suavização, suporte e incerteza explícitos.
2. Persistir versão das classes, extrator, snapshots nominais conjuntos quando existirem e proveniência. Preservar reprodutibilidade exata de modelos V8 históricos.
3. Integrar `AlgorithmVersion=V9` no mesmo scorer; ausências neutras apenas se calibradas; thresholds e margens versionados; nenhum fallback silencioso ao scorer V8.
4. Confrontar o cálculo par-a-par C# vs Splink sob os mesmos dados, níveis, parâmetros e numerador/denominador; não afirmar equivalência antes de medir.
5. Executar TRAIN/VALIDATION/TEST independentes, adversários (MARIA SILVA / MARIA SILVAA, homônimos, mães ausentes, agnomes, nomes compostos), comparação com V8 e orçamento de FP/FDR + recall. Não ajustar TEST para torná-lo verde.
6. Somente após os gates, decidir `VALIDATE/ACTIVATE` em DT-15. Ensaio DEV/HML é evidência separada, não substituída por CI.

**Estado desta entrega:** somente biblioteca C# pura, enum experimental, testes unitários e especificação. O runtime V8, os modelos persistidos e o processo de decisão não mudam. Esta PR de fundação pode ser revisada isoladamente; **não** autoriza ativação V9.

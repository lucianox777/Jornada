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

## Implementação no núcleo FS existente — termo conjunto V9 (etapa posterior)

O método `FellegiSunterScoring.CalculateCompositeNominalJointV9` já está **no mesmo código C# do scorer Fellegi–Sunter utilizado pela Jornada**, e não em um segundo motor de decisão. Ele recebe os dois nomes de pessoa, os dois nomes de mãe, a versão congelada do comparador V9 e uma tabela completa de **81 estados conjuntos exclusivos** `(nível_pessoa, nível_mãe, m, u)`. Classifica os nomes com `CompositeNominalComparisonV9` e retorna a **única contribuição nominal** `ln(m/u)` daquele par de estados. Rejeita tabela parcial, duplicada, versão errada, probabilidades nulas/inválidas e distribuições `m/u` não normalizadas; não fabrica ausência neutra, porque `Missing` só será neutro quando o modelo calibrado assim determinar. Nome integral e componentes não geram LLRs nominais aditivos. Testes cobrem `m=u` (LLR zero), mãe ausente, primeiro+último com intermediário divergente e proteção contra duplicação de evidência.

**Limite de integração:** esta rotina implementa o **termo matemático nominal** no FS C#, não liga uma `AlgorithmVersion=V9` ao `Rank/Resolve`, não seleciona um modelo/TF, não prova que as 81 probabilidades foram certificadas e não altera parâmetros já ativos. Para usá-la operacionalmente ainda é necessário conectar o snapshot V9 governado ao `Evaluate` do FS, em substituição — e não em soma — às contribuições nominais legadas, e passar pelos gates de promoção abaixo. A prioridade é concluir essa integração e a calibração V9; **otimizações adicionais de censo opcional de pares no SQL não são pré-requisito para criar o scorer nominal**.

## Conexão com a matemática integral do FS C# — preview V9 (etapa seguinte)

A evolução `FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown` executa, **na mesma função interna `Evaluate` do scorer FS existente**, prior e evidência de nascimento **mais uma única contribuição nominal conjunta de pessoa × mãe**. A entrada V9 recebe os 81 estados `(m,u)`, classifica o par e exige versão, distribuição normalizada e 81 estados exclusivos; calcula `ln(m/u)` e passa essa evidência ao mesmo acumulador/logit/breakdown usado no runtime. **Não executa `NOME` e `NOME_MAE` legados quando existe estado nominal conjunto V9**, o que previne a dupla contagem. O nascimento continua com seus próprios parâmetros e cálculo padrão; ausência nominal da mãe é estado conjunto explícito, **não neutro por definição**.

A função é **preview matemático explícito**, não um novo scorer de produção nem `AlgorithmVersion=V9` ativado. Não altera `Rank/Resolve`, não consome dados do atendente, não publica nem altera modelo persistido e **não aplica TF** até que uma referência por unidade/nível seja calibrada. O fato de uma tabela somar 1 e produzir score finito não certifica os `m/u`: a verificação do corpus, fonte IBGE/proxy ou histórico real, condicionamento ao blocking, partições independentes, suporte, FDR/FP/recall e paridade com Splink continuam obrigatórios antes de permitir persistência/promoção.

Os testes verificam numericamente `logit(prior) + ln(m_j/u_j) + ln(m_nasc/u_nasc)`, a ausência de `NOME`/`NOME_MAE` duplos, mãe e nascimento ausentes, falhas de contrato, e que parâmetros marginais legados adicionais **não alteram o score V9**. Nenhum censo opcional de pares SQL é pré-requisito do preview; priorizar o **snapshot V9 governado e o dispatch seguro no mesmo scorer**.

## Ranking de candidatos V9 em preview, com deduplicação idêntica ao runtime

`ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview` percorre **objetos reais de candidato** com os mesmos campos do runtime e faz a mesma deduplicação determinística por `PessoaUuid` usada em `Rank`. Em cada dupla, utiliza **o mesmo método FS C#** `CalculateCompositeNominalV9PreviewWithBreakdown`, que compõe os `m/u` conjuntos nominais pessoa×mãe com nascimento e prior e ordena por log-odds, desempate por UUID. Preserva a marca diagnóstica de colisão exata de nome integral+nascimento e falha fechado para observações com CPF, snapshots nominais inválidos e duplicações contraditórias de candidatos.

**Escopo deliberadamente limitado:** é uma entrada `internal` para **ranking pré-operacional**, sem `AlgorithmVersion=V9`, sem persistência de modelo V9, sem `Resolve/ACTIVATE`, sem chamada a partir do executor SQL/HTTP e sem aplicação ao atendimento. Não cria um motor paralelo: o cálculo é o mesmo `FellegiSunterScoring.Evaluate` da V8, com os termos nominais substituídos em vez de somados. Não há alegação de TF conjunta, inferência FDR, validação em Splink ou seleção de threshold. Com o preview de ranking montado, a próxima integração V9 é **snapshot certificado + roteamento seguro por versão no único scorer**, seguido de TF, paridade numérica e DT-15. O censo SQL de pares opt-in não precisa ser ampliado para esta etapa.

## Critérios de promoção (não concluídos por esta PR)

1. Capturar estados V9 do TRAIN e dos pares U **condicionados ao blocking**; estimar distribuições `m/u` por nível com suavização, suporte e incerteza explícitos.
2. Persistir versão das classes, extrator, snapshots nominais conjuntos quando existirem e proveniência. Preservar reprodutibilidade exata de modelos V8 históricos.
3. Integrar `AlgorithmVersion=V9` no mesmo scorer; ausências neutras apenas se calibradas; thresholds e margens versionados; nenhum fallback silencioso ao scorer V8.
4. Confrontar o cálculo par-a-par C# vs Splink sob os mesmos dados, níveis, parâmetros e numerador/denominador; não afirmar equivalência antes de medir.
5. Executar TRAIN/VALIDATION/TEST independentes, adversários (MARIA SILVA / MARIA SILVAA, homônimos, mães ausentes, agnomes, nomes compostos), comparação com V8 e orçamento de FP/FDR + recall. Não ajustar TEST para torná-lo verde.
6. Somente após os gates, decidir `VALIDATE/ACTIVATE` em DT-15. Ensaio DEV/HML é evidência separada, não substituída por CI.

**Estado desta entrega:** somente biblioteca C# pura, enum experimental, testes unitários e especificação. O runtime V8, os modelos persistidos e o processo de decisão não mudam. Esta PR de fundação pode ser revisada isoladamente; **não** autoriza ativação V9.

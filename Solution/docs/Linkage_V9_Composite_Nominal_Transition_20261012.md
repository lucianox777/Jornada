> **Decisão V9, 13/10/2026:** não exigir comparação V8×V9
> como requisito para desenvolver, calibrar ou julgar se a V9
> funciona. A V8 só permanece para replay histórico.
> A V9 será avaliada diretamente por gabarito, TRAIN/VALIDATION/TEST,
> falsos vínculos, recall, suporte e modelo estatístico. A DT-15
> conserva exclusivamente o dossiê de substituição ATIVO × PROPOSTO
> quando houver uma decisão de ativação; esse dossiê não obriga
> superioridade da V9 em relação à versão histórica ativa.

# Linkage V9 — transição para comparação nominal composta

**Decisão do proprietário: 12/10/2026.** A V9 substitui a V8 como **única arquitetura-alvo para novos modelos**; a V8 fica congelada como semântica histórica e não é mais objeto de correções conceituais. Isso **não** converte automaticamente modelos ATIVOS, não altera `VALIDATE/ACTIVATE`, não apaga snapshots, fingerprints nem evidências e não autoriza ativação V9 sem DT-15.

Issue de execução: #946. Incidente motivador: #944; reprodução de ranking: #927; auditoria diagnóstica legada: #945. A tentativa de veto V8 em #943 quebrou calibradores reais sintéticos; não servir de solução V9.

## Referência externa (não é código de produção)

- Splink, `ForenameSurnameComparison`: https://moj-analytical-services.github.io/splink/topic_guides/comparisons/out_of_the_box_comparisons.html
- Splink, TF e comparação por níveis: https://moj-analytical-services.github.io/splink/topic_guides/comparisons/term-frequency.html

Splink explica que o modelo FS não deve presumir independência entre prenome e sobrenome: comparação única permite níveis mutuamente exclusivos e, quando disponível, frequência **conjunta** da combinação. A implementação da Jornada continua sendo **um único scorer FS C#**, com conferência matemática independente obrigatória; a comparação externa com Splink é **opcional/diagnóstica**, conforme decisão de 29/09/2026. Uma string composta `primeiro + último` **não representa todo o nome**: nomes intermediários e agnomes continuam na comparação integral.

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

A função é **preview matemático explícito**, não um novo scorer de produção nem `AlgorithmVersion=V9` ativado. Não altera `Rank/Resolve`, não consome dados do atendente, não publica nem altera modelo persistido e **não aplicava TF nessa etapa histórica**; a PR #974 passou a aceitar um único ajuste TF por unidade em **preview**, ainda sem estimativa/governança completa do Calibrador. O fato de uma tabela somar 1 e produzir score finito não certifica os `m/u`: a verificação do corpus, fonte IBGE/proxy ou histórico real, condicionamento ao blocking, partições independentes, suporte, FDR/FP/recall e **conferência matemática independente** continuam obrigatórios antes de permitir persistência/promoção; a paridade externa C# × Splink é diagnóstico opcional, **não** gate do Ensaio.

Os testes verificam numericamente `logit(prior) + ln(m_j/u_j) + ln(m_nasc/u_nasc)`, a ausência de `NOME`/`NOME_MAE` duplos, mãe e nascimento ausentes, falhas de contrato, e que parâmetros marginais legados adicionais **não alteram o score V9**. Nenhum censo opcional de pares SQL é pré-requisito do preview; priorizar o **snapshot V9 governado e o dispatch seguro no mesmo scorer**.

## Ranking de candidatos V9 em preview, com deduplicação idêntica ao runtime

`ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview` percorre **objetos reais de candidato** com os mesmos campos do runtime e faz a mesma deduplicação determinística por `PessoaUuid` usada em `Rank`. Em cada dupla, utiliza **o mesmo método FS C#** `CalculateCompositeNominalV9PreviewWithBreakdown`, que compõe os `m/u` conjuntos nominais pessoa×mãe com nascimento e prior e ordena por log-odds, desempate por UUID. Preserva a marca diagnóstica de colisão exata de nome integral+nascimento e falha fechado para observações com CPF, snapshots nominais inválidos e duplicações contraditórias de candidatos.

**Escopo deliberadamente limitado:** é uma entrada `internal` para **ranking pré-operacional**, sem `AlgorithmVersion=V9`, sem persistência de modelo V9, sem `Resolve/ACTIVATE`, sem chamada a partir do executor SQL/HTTP e sem aplicação ao atendimento. Não cria um motor paralelo: o cálculo é o mesmo `FellegiSunterScoring.Evaluate` da V8, com os termos nominais substituídos em vez de somados. Não há alegação de TF conjunta, inferência FDR, validação em Splink ou seleção de threshold. Com o preview de ranking montado, a próxima integração V9 é **snapshot certificado + roteamento seguro por versão no único scorer**, seguido de TF, paridade numérica e DT-15. O censo SQL de pares opt-in não precisa ser ampliado para esta etapa.

## Congelamento por rodada do snapshot nominal conjunto V9

Após a PR #967, a avaliação de um bloco de candidatos na V9 percorria e validava toda a tabela de 81 células **para cada candidato**. Para um bloco de 120 candidatos, isso repetia desnecessariamente cerca de 120 vezes o mesmo exame de distribuição, exclusividade, versão e `ln(m/u)`. A evolução seguinte introduz `FrozenFellegiSunterJointNominalV9`, uma **cópia defensiva exclusivamente em memória**, construída **uma vez por lista de candidatos**. A construção exige os 81 estados mutuamente exclusivos, contrato nominal V9 exato, probabilidades finitas e positivas e normalização de M/U; pré-calcula os logaritmos naturais para as células. Alterações posteriores na coleção fornecida pelo chamador não modificam o snapshot congelado.

A API pública `CalculateCompositeNominalJointV9` e o preview matemático continuam aceitando a coleção completa e passam pelo mesmo validador. Já `ProbabilisticLinkageDecisions.RankCompositeNominalV9Preview` congela e valida essa coleção **antes** de verificar a existência de candidatos, e reutiliza o mesmo snapshot validado ao visitar cada candidato. O cálculo continua no **único `FellegiSunterScoring.Evaluate`** do runtime; preserva prior, nascimento, estado `Missing` materno e substituição das contribuições `NOME`/`NOME_MAE` por um único estado nominal V9. Testes comprovam equivalência matemática completa entre a rota pública e a pré-congelada, integridade frente a alterações do input, invalidação de tabelas incompletas e recusa mesmo quando o bloco não contém candidatos.

**Limites inalterados:** congelamento de parâmetros **não prova a origem dos 81 m/u, nem certifica sua calibração, TF, probabilidades de inclusão ou FDR**. O preview continua interno e **não é roteado por `AlgorithmVersion=V9`**; nenhuma alteração operacional no `Rank/Resolve`, modelos ATIVOS, bancos ou V8 para replay. Esta etapa remove uma repetição matemática desnecessária sem exigir nova análise de blocking ou censo SQL. Prioridade subsequente: governar o snapshot persistido e conectar o dispatch sob gates reais, executar conferência matemática independente e testes estatísticos; Splink externo pode auxiliar como diagnóstico opcional.

## Proteção fail-closed do roteamento operacional V9 (incremento de continuidade)

O identificador `FELLEGI_SUNTER_DECISION_EVIDENCE_COMPOSITE_NOMINAL_V9`
está **reservado**, mas **não ativado**. Enquanto não houver 81 estados
`m/u` conjuntos certificados, proveniência versionada, TF por unidade
compatível, conferência matemática independente e autorização DT-15, `LinkageModelPolicy.Create`
rejeita explicitamente `V9` e qualquer versão de algoritmo terminada em
`_V9`. O objetivo é impedir que uma nova versão nominal seja carregada
com os parâmetros `M_NOME`/`M_NOME_MAE` antigos por fallback implícito.

As entradas operacionais `Rank`, `Resolve` e `ResolveRanked` repetem
o bloqueio, inclusive para listas vazias, protegendo contra chamadores
internos que construam diretamente `LinkageModel`. O preview matemático
`RankCompositeNominalV9Preview` permanece isolado, sem dispatch pelo
`AlgorithmVersion`, persistência, TF presumida ou ativação. Esta proteção
**não substitui** a futura integração do snapshot certificado ao scorer
FS único: antes dessa liberação o guard deverá ser trocado por preflight
positivo com contratos atestados no **mesmo SHA/modelo**.

Nenhuma lógica nominal V8 de replay, o modelo ATIVO, o SQL, o banco DEV
ou o roteamento de atendimento foram modificados. **DT10 fica por último.**

## Identidade canônica de conteúdo dos 81 m/u V9 (incremento pré-certificação)

A cópia imutável `FrozenFellegiSunterJointNominalV9` passa a oferecer
`ContentFingerprint` (SHA-256 hexadecimal minúsculo) **somente após** validar
versão do classificador, integralidade/uniqueness das 81 células, m/u positivos
e normalização. A entrada canônica contém marca de domínio
`JORNADA_V9_JOINT_NOMINAL_MU_81_V1`, versão do classificador e as 81
probabilidades ordenadas por enum pessoa × mãe, com formato decimal
invariante `G29`. Portanto, trocar a ordem de entrada, o locale do processo
ou a escala decimal sem mudar o valor matemático não altera a identidade;
alterar uma probabilidade, mesmo conservando a normalização, altera a
identidade. Não entram CPF, nomes, UUIDs nem identificadores de pessoas.

**Limite de confiança:** esse digest identifica apenas **o conteúdo numérico
in-memory** recebido pelo scorer de preview. **Não** é assinatura digital,
atestado de origem, vínculo a `ModelId`/`Version`, certificado de
TRAIN/VALIDATION/TEST, evidência de representatividade do blocking SQL,
TF calibrada ou autorização de `AlgorithmVersion=V9`. O Calibrador deverá
vincular explicitamente esse digest à versão de modelo e a um manifesto
imutável de proveniência e decisões DT-15; tal vínculo ainda não existe
e o guard operacional V9 continua bloqueando qualquer fallback para V8.


**Precedência da conferência:** a [decisão de avaliação sintética primária de 29/09](Decisao_Avaliacao_Sintetica_Primaria_20260929.md) tornou o Splink externo opcional como prova de equivalência de implementação; **não** dispensou TEST independente congelado, conferência matemática obrigatória nem validação representativa antes de HML/Produção. Os trechos das etapas V9 acima são fotografias históricas do momento de cada PR, não veto novo de executar o bootstrap sobre 30.000 pessoas sintéticas.

## Critérios de promoção (não concluídos por esta PR)

1. Capturar estados V9 do TRAIN e dos pares U **condicionados ao blocking**; estimar distribuições `m/u` por nível com suavização, suporte e incerteza explícitos.
2. Persistir versão das classes, extrator, snapshots nominais conjuntos quando existirem e proveniência. Preservar reprodutibilidade exata de modelos V8 históricos.
3. Integrar `AlgorithmVersion=V9` no mesmo scorer; ausências neutras apenas se calibradas; thresholds e margens versionados; nenhum fallback silencioso ao scorer V8.
4. Conferir o cálculo par-a-par C# contra **vetores com resultados independentes**, incluindo níveis, prior, m/u, TF, LLR e decisões de fronteira conforme DT-01/09/14. Comparação C# × Splink sobre os mesmos dados é **opcional/diagnóstica** (não gate de Ensaio ou promoção) e não se deve afirmar paridade Splink sem executá-la.
5. Executar TRAIN/VALIDATION/TEST independentes, adversários (MARIA SILVA / MARIA SILVAA, homônimos, mães ausentes, agnomes, nomes compostos), métricas absolutas da V9 e orçamento de FP/FDR + recall. Não ajustar TEST para torná-lo verde.
6. Somente após os gates, decidir `VALIDATE/ACTIVATE` em DT-15. Ensaio DEV/HML é evidência separada, não substituída por CI.

**Estado desta entrega:** somente biblioteca C# pura, enum experimental, testes unitários e especificação. O runtime V8, os modelos persistidos e o processo de decisão não mudam. Esta PR de fundação pode ser revisada isoladamente; **não** autoriza ativação V9.

## Incremento de TF por unidade nominal no scorer único — PREVIEW V9

`CompositeNominalTfPreviewV9` permite informar **uma** correção TF por
avaliação conjunta V9 (pessoa **ou** mãe), sem somar outra evidência
independente de `m/u`. O mesmo `FellegiSunterScoring.Evaluate` recebe
a contribuição conjunta `ln(m_ij/u_ij) + w_TF × ln(u_referencia_unidade / f_termo)`,
além de prior e nascimento. A função `SplinkCompatibleTermFrequency` é
reutilizada. `w_TF=0` deixa o termo original inalterado (fator neutro 1);
`w_TF=1` aplica o ajuste integral; apenas pesos dentro de [0,1] são
aceitos nesta pré-visualização, sem fixação obrigatória do peso na arquitetura.

A **unidade da referência** depende do nível selecionado: `ExactFull`
exige `FullName`; `ExactFirstLast`, frequência conjunta observada;
`ExactFirst`, prenome; `ExactLast`, último sobrenome significativo
(proxy IBGE explicitamente identificado quando aplicável). O valor
normalizado deve coincidir com os dois lados do par na unidade específica.
A verificação de proveniência `RequireMatchingReference` (V3) bloqueia
marginal IBGE usada como nome inteiro ou distribuição conjunta inventada;
referência real exige suporte independente e aprovação. `Missing`,
`High*`, `Other` e inversão não recebem TF não calibrada neste incremento.

**Fronteira:** `UnitReferenceU` é informado separadamente, tendo de ser o
`u` estimado para a **mesma unidade/nível e universo condicionado ao
blocking**, não `u` de alguma célula conjunta de 81 estados nem
`U_NOME_EXACT` histórico. Este código valida contrato/unidade e a
correspondência textual, **não autentica nem estima** as frequências ou o
`u` declarados, e não estima peso; esta é uma API de *preview* para
experimentos reprodutíveis, não publicação de parâmetros. Um único
ajuste foi implementado deliberadamente para impedir adição ingênua de
TF da pessoa e da mãe sem calibrar a dependência. Não equivale ainda ao
modelo completo de múltiplos ajustes Splink/Calibrador.

**Pendente para V9 operacional:** aprender/calibrar unidades e pesos
adequados inclusive combinações dependentes e estados fuzzy; ampliar
representações sem somar sinais correlacionados; comparar sobre mesmos
pares mediante conferência matemática independente (Splink externo opcional), certificar VALIDATION/TEST, anexar snapshot
governado e aprovar DT-15. Nada muda em `Rank/Resolve` operacional,
`GENERATE_DRAFT`, TF V8, ledger ou modelos ATIVOS.

## Precedência do bootstrap de 30 mil e entradas parciais (12/10/2026)

**30.000 Pessoas sintéticas = base inicial aprovada de calibração e comparação**, não demonstração automática de suporte para **81 células M/U**, nem FDR em SP. Não impedir a **execução inicial** por exigir evidência municipal real ou comparação externa Splink, ambas não obrigatórias para o bootstrap/Ensaio. O scorer só pode receber configurações sem parâmetros ausentes mascarados de neutros, com tratamento explícito de células raras e `Missing`. O Calibrador deve selecionar representação, `m/u` e pesos TF por evidência do corpus inicial, registrar células observadas, ESS/grupos, FP/FN/abstenções em partições independentes, e conservar o TEST isolado. A execução completa com relatório das 30 mil pessoas **ainda não foi comprovada** nesta revisão. [Parecer numérico/aceite](Parecer_Suficiencia_Bootstrap_30mil_V9_20261012.md).

O contrato versionado de entrada controla a obrigatoriedade dos atributos: **nenhuma regra universal exige nome, mãe, nascimento e CPF todos simultaneamente**. Ausências válidas podem permanecer como `Missing` calibrado; evidência insuficiente leva à abstenção. Ausência de **parâmetro FS obrigatório** é problema de modelo, não dado opcional. A paridade externa com Splink é opcional pela decisão de 29/09; a conferência matemática independente, TRAIN/VALIDATION/TEST e DT-15 **não foram revogados**. Nenhuma dessas afirmações autoriza ativar V9 atualmente em preview.

## Verificação de parâmetros declarados e diagnóstico observado (incremento seguinte)

No preview V9, uma estratégia **de nascimento explicitamente declarada** (semântica V5, conjunta V4, data única V3 ou componentes V2) requer toda a sua distribuição M/U, mesmo quando o par específico não possui datas de nascimento. Falta de parâmetro obrigatório não pode virar LLR zero. A ausência de **atributo na Pessoa** continua permitida conforme o contrato da origem, e seu tratamento estatístico segue a regra de evidência/ausência. O scorer histórico permanece intocado para replay V1–V8.

O diagnóstico ponderado FS V9 agora inclui `ObservedJointSupport`, relatório de 81 células M/U e TRAIN/EVALUATION com **contagens observadas**, ESS e grupos por classe, e frações de massa ponderada nas células rarefeitas. É somente descrição da amostra já processada; **não indica que as 30.000 pessoas canônicas foram de fato executadas**, que as células estão independentes ou que a V9 passou DT-15. O objetivo é fornecer uma medição reprodutível antes de deliberar aumento de N, colapso de níveis ou política de abstenção. [Parecer de suficiência](Parecer_Suficiencia_Bootstrap_30mil_V9_20261012.md).

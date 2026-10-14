# Jornada V9 — decisão integral: erros sintéticos, seleção de blocking e suficiência amostral
**Registro consolidado de 14/10/2026 — pesquisa DEV, não política ativa.**

## Decisões ratificadas
1. **O sistema descobre as melhores configurações de blocking** a partir de pares verdadeiros
   M e candidatos U deduplicados no universo real de *candidatos do próprio blocking*,
   em TRAIN. Não se fixa arbitrariamente três letras de prenome, uma letra materna, a
   ordem das células, nem aceita a priori qualquer “vencedor” de uma seed. O otimizador
   deve obedecer política explícita, versionada e com orçamento governado de
   custo/fanout e recall. VALIDATION não treina; TEST permanece lacrado.
2. A regra nominal é **nome da pessoa obrigatório E (nome da mãe OU DOB utilizável)**.
   Preservam-se os estratos sem mãe (pessoa+DOB) e sem DOB (pessoa+mãe).
   CPF válido é determinístico e **fica fora** do quadro de pares probabilísticos.
3. Comparar células indexadas SQL físicas quando possível; SQLite `:memory:` permite
   **experimento indexado**, mas não comprova tempo, plano ou escala do SQL Server
   municipal. A contabilização correta de candidatos não-vínculo é U, e seu grau
   médio por observação elegível é **2 × U / N**, incluindo grau zero.
   U não é FP de decisão, nem FDR. Os limites P95/P99 e a latência de 1 segundo
   são requisitos candidatos a governar, **não valores aprovados**.
4. Avaliar separadamente cenários `clean`, `correlated` e futuramente erros
   empiricamente medidos por secretaria, gestor, atributo e período. Manter
   `correlated` como **stress test** histórico adversarial, e `clean` apenas como
   hipótese de sensibilidade, **não** substituto de erro real municipal.
5. **Calibração FS/m/u/TF e ativação permanecem manuais**; qualidade, tamanho de amostra
   e suficiência (DT-23) podem gerar alertas automaticamente, sem executar calibração
   nem atualizar modelo em produção.
6. A arquitetura interativa/lote/temporal fica como **proposta**:
   bloqueio interativo seletivo com orçamento validado; em lote somente registros
   sem vínculo confiável aceito (incluindo quem teve candidatos U); temporal
   considera CPF tardio ou novas evidências, sem garantir que apareçam.
   Cada camada terá janela de observação, recall, abstinência, latência,
   custo e FDR **separados**, inclusive recuperação acumulada.

## De onde vieram os 18% — proveniência e implicações

O `SyntheticCorpusV2Rules.cs` usa `CommonCorruptionProbability=0.18` no
`correlated`, perfil de teste padrão. O commit `8dcf5b60` (13/09/2026)
introduziu os números em `gen_corpus_v2.py` **sem fonte empírica declarada**.
A probabilidade de prenome composto `0.18` é outro parâmetro **independente**.
As outras tentativas observacionais declaradas são nome 0.12,
mãe 0.16, DOB 0.08, mãe ausente 0.18 e DOB ausente 0.05. O evento Bernoulli
comum não está gravado no CSV original: **rótulos de três campos alterados não
são idênticos ao bit latente `common`**.

No corpus original seed42 medido (#1083), 58.264 observações:
corrupção observável dos três campos em 17,912% das observações;
32,470% dos 40.835 M intergestores tinham pelo menos um lado com
três atributos corrompidos; 47,758% tinham DOB diferente ou ausente.
Essas são taxas **sintéticas do próprio perfil**, não taxas municipais.
O antigo `generation-manifest.json` tinha lacuna de proveniência de
`error_profile`, corrigida **somente para novas gerações** com labels,
versões, perfil e hashes; artefatos originais imutáveis.
Não alterar parâmetros do gerador para atingir a meta de 95%.

**Literatura comparativa com interpretação cautelosa:**
Harron et al. (2017), DOI 10.1186/s12874-017-0306-8, encontraram
0,11% de DOB ausente ou errado em determinada comparação HES/PDS
do Reino Unido; o estudo PMC10163749 reportou 0,492% de DOB
divergente em pares vinculados entre sistemas britânicos. Populações,
instituições e definições de erro não são diretamente transferíveis
para os cadastros sociais de São Paulo. Dependência/correlação entre
erros existe e deve ser testada, não presumida nula.

## Resultado empírico sintético seed42 (PRs #1083–#1084)

Geração de 30.000 pessoas-base por perfil com IBGE/SP 3550308 + referência
nacional de mãe + projeção diária SP, exportação C# V2 de sobrenomes,
19 células SQLite com mesmo algoritmo TRAIN-only que seleciona uma união
`greedy_pruned` por menor U sob recall nominal **global** M≥95%.
A seleção é congelada e aplicada a VALIDATION sem refit. Resultados:

| Perfil | TRAIN M recuperado / M global | TRAIN U | VAL M recuperado / M global | VAL U | Limite inferior unilateral bootstrap por pessoa-base em TRAIN |
| :-- | --: | --: | --: | --: | --: |
| clean | 19.630 / 20.364 (96,396%) | **242** | 6.509 / 6.763 (96,244%) | **28** | 96,073% |
| correlated | 19.437 / 20.458 (95,009%) | **516.029** | 6.432 / 6.714 (95,800%) | **59.376** | **94,678%** |

No `clean`, o algoritmo selecionou duas células (`P_PHON_M_LAST`,
`P_PREFIX3_DOB`) e, em `correlated`, nove células. A comparação
é **não pareada** entre corpora: os ramos de PRNG dependem do perfil,
e `seed=42` comum NÃO garante a mesma identidade-base entre perfis.
No próprio `clean`, **união indiscriminada das 19 células** produziu
932.635 U TRAIN: custo depende da **seleção otimizada**, não apenas
do erro do gerador. Nenhum resultado sintetizado prova FDR de links
aceitos, SQL Server ou representatividade municipal.

Os 34 testes de prefixos 1–4 e inicial materna/meses (#1081)
são hipóteses pré-registradas, não escolha nominal fixa;
a #1082 implementou descoberta automática de fronteira de Pareto
do **ganho M e U marginal versus a união original congelada de 10 células**.
Esta fase seleciona no máximo uma célula nova, não prova ótimo global
de todas as combinações. Prefixo de uma letra só é aceito como
evidência nominal **com mãe ou DOB** e orçamento seletivo, nunca
como identificador único.

## Pergunta: “30 mil pessoas sintéticas é pouco? Até que amostra é perfeita?”

**Hipóteses distintas que não devem ser confundidas:**

- **H0**: recall da busca de candidatos sobre TODOS M verdadeiros sem CPF
  é ≤95%; **H1**: >95%. O alvo é teto inferior unilateral de incerteza,
  por identidade-base, no split independente VALIDATION, sem ajustar
  parâmetros nesse split. O limite de TRAIN é diagnóstico otimista,
  porque TRAIN escolheu a combinação de células.
- **Precisão da estimativa**: reduzir largura de intervalo para uma
  configuração **congelada**; independe de quantas células foram
  exploradas e exige contabilizar **identidades-base independentes**.
- **Generalização entre seeds e geradores**: 42,43,44 são repetições
  independentes da mesma **família de hipóteses sintéticas**. 
  Uma evidência estreita em uma seed **não** valida taxas de erros reais.
- **Desempenho e U em escala**: 30 mil vs 11,45 milhões é outro problema;
  colisões por chave podem crescer não linearmente conforme a distribuição
  conjunta por gestor e substrato de nomes. Não extrapolar U por ×380.
- **Representatividade externa**: **nenhuma amostra sintética, por maior
  que seja, se torna “perfeita” por tamanho**. Aumentar N reduz
  variância de Monte Carlo em cenário escolhido; não reduz viés do
  gerador, diferenças IBGE–municipais, erro de rotulagem, drift,
  cobertura e efeitos de múltiplas observações da mesma pessoa.

### Estimativa de tamanho efetivo — 80% de poder com teste unilateral 5%

Se H0 é 95%, sob modelo idealizado de M independentes e aproximação normal
`n_eff ≈ [z(.95)√(.95×.05)+z(.80)√(p1(1−p1))]²/(p1−.95)²`.
O resultado é em **pares M efetivamente independentes**, **NÃO 30 mil
registros nem pessoas**, antes de corrigir dependência intrapessoa e
efeitos de estrato/seleção:

| Recall verdadeiro sob H1 | M independentes efetivos necessários, aproximadamente |
|---|---:|
| 95,5% | **11.362** |
| 96,0% | **2.740** |
| 97,0% | **631** |
| 98,0% | **253** |
| 95,01% | ~**29 milhões** |

A linha 95,01% usa aproximação da alternativa p1=0,9501:
mostra que, quando o verdadeiro recall fica praticamente no limite,
crescer a amostra é uma forma **ineficiente** de “certificação”.
Se o recall verdadeiro for exatamente 95%, **nenhuma amostra finita**
oferece 80% de poder para concluir que ele é >95% mantendo alfa 5%;
sob a fronteira, a probabilidade de rejeição é no máximo 5%.
Uma estimativa pontual de 95,01% não é evidência de 95% garantidos.

### Experimento automático de tamanho amostral efetivamente implementado

`scripts/v9-cluster-sample-size-power.py` usa o **mesmo CSV canônico
e o mesmo SHA** do experimento V9; recupera do relatório a **escolha
já treinada em TRAIN**, reavalia apenas **pares verdadeiros entre gestores**
e exige que M recuperado coincida exatamente com a avaliação SQL
original. Não reconstrói U em subconjuntos, não retreina regras nem
abre TEST. A unidade é o **cluster de identidade-base com pelo
menos um par verdadeiro**, não par/observação; seleciona clusters
em ordem SHA determinística **antes** de avaliar os resultados.
Mede tamanhos aninhados 250, 500, 1000, 2000, 5000, 10000,
20000 e tamanho total disponível, separadamente em TRAIN e VAL,
com 400 réplicas bootstrap por identidade-base. Registra:

- N de **identidades-base com M** amostradas, total M e recuperados,
  recall global M, limite inferior unilateral 95%, margem até 95%;
- variância robusta (delta method) da razão ponderada por clusters,
  **pares M independentes efetivos** e design effect empírico
  em cada n, com flags de precisão e eventual insuficiência;
- curva teórica de poder da fórmula acima para p1=95,5/96/97/98%,
  **sem alegar que a fórmula substitui o bootstrap**;
- prova de que a seleção original, hashes e TEST foram preservados.

**Limite do subamostramento:** todos os n da mesma seed provêm da
mesma realização, logo curvas dentro de uma seed são **dependentes**
e condicionais ao corpus. São um teste de estabilidade/precisão,
não independentes provas científicas nem garantia em 11,45 milhões.
Para variabilidade de replicações externas, CI produz também
**seeds independentes 43 e 44**, nos dois perfis e com a mesma
referência e algoritmo. Mesmo assim o total é três simulações
por perfil, **não** taxa de erro municipal observada.

### Critério de suficiência para decisões

Não há tamanho perfeito pré-fixado. Determinar o menor N **por
estrato** que fornece (i) limite inferior de recall >95% em
VALIDATION de modo estável em seeds independentes, (ii) intervalo
de precisão/suficiência configurado e aprovado, (iii) número
efetivo de pessoas-base positivas adequado inclusive para
ausência da mãe, ausência DOB, erros múltiplos e gestores raros,
e (iv) U, fanout P95/P99 e orçamento físico SQL Server atendidos.
Relatar estratos não certificados como **inconclusivos**, não ocultá-los
nem alterá-los para favorecer a meta. Manter testes de regressão,
CPF determinístico, Gold e FS intactos; calibração e promoção são
decisões manuais e exigem rótulos governados.

## Rastreabilidade das PRs e gates

#1077: projeção estruturada C# V2. #1078: concentração por chave.
#1080: orçamento observacional U. #1081: 34 hipóteses prefixo/mãe/DOB.
#1082: parâmetro automaticamente descoberto em TRAIN sob orçamento.
#1083: origem e impacto real dos 18% na massa sintética e manifests.
#1084: sensibilidade de perfis clean/correlated 30k na seed42.
Este incremento: bootstrap/curva de N e **novas seeds 43/44**
no workflow `v9-synthetic-corruption-evidence`; artefatos
agregados (sem CPF/PII) ligados ao SHA e à CI. Só considerar
concluída a evidência multisseed **após o workflow efetivamente
SUCCESS e os artefatos presentes**, não apenas pelo código do job.

## Pendências após este incremento
(i) medir erro real por gestor/Secretaria e período com rótulos
independentes e consentimento/governança; (ii) calibrar taxas do
modelo de erro somente com evidência real, não por conveniência
do recall; (iii) avaliar FDR após decisão FS calibrada;
(iv) verificar paridade física e latência de índices SQL Server
e custo na escala municipal; (v) estabelecer orçamento de fanout
aprovado; (vi) revisar estratos, drift e critérios de suficiência
com a governança; (vii) manter TEST blindado até aprovação.
**Sem ativação V9/DT-15, sem alteração V8/Gold, HML/PROD, NAS,
JornadaLocal, dos snapshots IBGE ou das políticas ativas.**

## Registro de incidente estatístico multisseed e tratamento não permissivo

Na primeira execução física das seeds independentes, o perfil
`correlated` da seed **44** não disponibilizou plano
`greedy_pruned` capaz de atingir o alvo de 95% em TRAIN:
a rotina de tamanho amostral inicialmente abortava com
`cannot assess non-feasible frozen plan`. Isso **não**
autoriza descartar o caso adversarial, afirmar que sua
amostra convergiu, alterar probabilidades de corrupção,
baixar o alvo ou selecionar por VALIDATION. Foi corrigido
o tratamento: para splits cujo TRAIN não encontra plano
viável, calcular apenas a curva de **limite diagnóstico da
união predefinida de todas as 19 células**, rotulada
`UNSELECTED_FULL_19_DIAGNOSTIC_INFEASIBLE_TRAIN`, reconciliada
com M da união SQL do relatório congelado. Esta curva **não
é política selecionada, nem estatisticamente certificada**;
o relatório indica `original_train_95pct_plan_feasible=false`.
Se até essa união ficar abaixo de 95%, registrar a
insuficiência estrutural daquela semente e cenário, não
confundir com mero erro-padrão de N finito.

Resultados numéricos, eventuais falhas e distribuição de
N da seed 44 só devem ser considerados **confirmados**
depois dos quatro jobs do workflow com as respectivas
saídas integrais; os testes verificam o caso inviável
sem ocultar o resíduo. A busca de características
mais informativas, se necessária, é trabalho de nova
experiência TRAIN-only, jamais retoque dos dados para
satisfazer o gate.

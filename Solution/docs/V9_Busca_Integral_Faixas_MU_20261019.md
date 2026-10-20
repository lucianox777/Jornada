> **Fluxo V9 corrente — 19/10:** [seleção interna no TRAIN, parada heurística e avaliação única de até três finalistas congelados](V9_Selecao_Interna_TRAIN_Finalistas_20261019.md). VALIDATION histórica não volta a ser holdout novo. Nenhuma certificação ou ativação automática.

# V9 — busca integral, faixas de risco e contrato M/U (19/10/2026)

Este incremento amplia a busca experimental da #1158. Não declara a V9 homologada, o ótimo global encontrado ou o TEST avaliado. As evidências compactas estão em `data/calibration/v9/integral-search-exploratory/`; a CI reproduz o censo e os controles e publica os relatórios completos.

## Decisões preservadas

A decisão [Calibrador único, de 14/10](Decisao_Calibrador_Unico_Adaptativo_Bootstrap_20261014.md) exige **m/u condicionados à união deduplicada efetiva de candidatos SQL de TRAIN**. O corpus sintético tem identidade-base conhecida: M é par da mesma identidade e U é par de identidades distintas, entre gestores, no frame sem CPF. Não usar o próprio score FS para fabricar esses rótulos. A verdade sintética pode estimar e medir o modelo, mas não pode formar os grupos BLIND.

Há três universos distintos:

1. Todos os pares verdadeiros elegíveis **antes do blocking**: denominador do recall de blocking, incluindo os perdidos pelo SQL.
2. M/U recuperados pela **união SQL de TRAIN**: estimação das probabilidades conjuntas, nascimento e prior. Deduplicar um par recuperado por vários passes; nunca somar as incidências como pares distintos.
3. Amostra reproduzível de U pré-blocking para **propor passes**: heurística de busca, sem substituir as contagens físicas de estimação. Quando o catálogo fechado é declarado, o pool heurístico não limita sua enumeração.

TRAIN estima; VALIDATION compara decisões finais por fonte após Rank/Resolve. TEST continua lacrado. IBGE marginal não se transforma em `U_TRAIN_SQL`, frequência zero ou parâmetro conhecido. TF positiva exige resolver unidade e condicionamento; este estudo mantém TF=0. LR=1, TF=0, TF=1 e parâmetro obrigatório ausente preservam seus contratos distintos.

O usuário confirmou em 19/10 que o grau de degradação já fora aceito como conservador suficiente para o bootstrap. Preserva-se o **clean conservador provisório**. A [reconciliação de 18/10, seção 2.2](V9_Reconciliacao_Decisoes_Evidencias_Runners_20261018.md) registra, em TRAIN/VALIDATION: nome alterado 6,03%; mãe alterada 10,14% e ausente 4,98%; nascimento alterado 4,09% e ausente 1,00%. Em pares M sem CPF, mãe diverge ou falta em 27,21%; nascimento em 10,14%. Não é uma base sem erros, nem medição da população municipal. Não se exige novo perfil empírico para continuar esse bootstrap aceito.

## Implementação

- `V9DeclaredBlockingSearchSpace` enumera todos os subconjuntos do catálogo declarado, sem retenção de um pool heurístico. Máximo de passes nulo significa todos os passes. Contagem combinatória usa `BigInteger`; limites de memória declarados interrompem explicitamente, sem truncar a família.
- Retirados os tetos arquiteturais anteriores de 3 campos, 4 passes e 64 passes por ruleset. Defaults operacionais continuam os mesmos; ampliar a computação não amplia a tolerância a falso vínculo.
- `V9CandidateFrameEquivalence` enumera a família inteira e compara vetores **exatos** de recuperação de arestas em TRAIN **e** VALIDATION. Hash não decide equivalência. Não usa truth, scores ou resultados do TEST. O censo usa projeção C# e a mesma junção de assinaturas SQL do probe existente. SHA das arestas físicas e fingerprint da projeção vinculam a evidência ao frame.
- Equivalência só compartilha avaliação funcional com TF=0. Não prova igualdade de latência SQL entre planos. Só o representante tem seu próprio tempo medido. Os snapshots conservam os passes do representante; não copiar seu hash para outra combinação.
- `MaximumParallelRuleSets` usa conexões SQL independentes e tabelas temporárias por sessão. A fábrica que compartilha simultaneamente uma conexão é recusada. Resultados são reunidos em ordem determinística; cada worker utiliza o calibrador C# existente.
- Shards dividem representantes por ordinal, sem seleção por desempenho. Todos conservam a **mesma multiplicidade da família inteira**. `Shard.WholeSearchEvaluated=false` e `PARTIAL_SEARCH_NO_GLOBAL_SUGGESTION` impedem confundir parte da busca com conclusão global. Conferir todos os índices, o mesmo fingerprint da família/coortes/censo e a soma dos representantes antes de declarar a família concluída.
- `V9RiskBandFrontier` informa a configuração de maior recall em cada teto pré-declarado de FDR superior: 0,1%; 0,25%; 0,5%; 1%; 2%; 5%. Distingue resultado condicional de configuração elegível; faixas acima do orçamento operacional são exploratórias. As faixas reutilizam os mesmos bounds simultâneos, sem reiniciar Bonferroni. A seleção histórica por distância ao ponto ideal continua disponível como compatibilidade; não substitui os ótimos por faixa.
- `V9FittedJointSupportAudit` inverte a suavização de **todas as 81 células**, reconcilia contagens inteiras com o M/U físico e prior, e distingue suporte observado de probabilidade suavizada. Não força ordenação EXATO/HIGH sem contrato estatístico que a justifique.

## Evidência física local

Foi criada uma instância LocalDB **exclusiva** `JornadaV9Integral20261019`, com banco descartável `JornadaHarness`. Não foram iniciados ou alterados JornadaLocal, IBGE, os dois nós DEV ou HML/PROD. Observações históricas foram lidas sem alteração.

Corpus: clean, seed 42, 30.000 identidades-base, SHA CSV `28bbb9f4c63c97c353e7f29cdbc77091a4773b2f4c596e46103edadd93635d17`. O leitor físico utiliza apenas TRAIN/VALIDATION sem CPF; TEST não entra no estudo.

| Medida | Resultado |
|---|---:|
| Catálogo fechado histórico | 19 passes |
| Subconjuntos não vazios enumerados | 524.287 |
| Classes exatas de recuperação TRAIN/VALIDATION | 60.563 |
| Pares físicos na união completa | 1.146.177 |
| Controles efetivamente calibrados | 54 configurações: união completa + baseline histórico, 3 alphas × 3 thresholds × 3 margens |
| Bonferroni conservador | 14.159.502: (524.287 + 1 baseline) × 27 + 3.726 comparações anteriores conhecidas |

O baseline é contado conservadoramente mesmo quando contido no catálogo. O número completo de consultas históricas à VALIDATION continua não autenticado: esse M conhecido não certifica controle de seleção retrospectivo.

| Controle da união de 19 passes | TP | FP | Recall global por fonte | Bound FDR condicional, com M completo |
|---|---:|---:|---:|---:|
| alpha 0,5; threshold 0,95; margem 0,5; TF0 | 7.563 | 0 | 96,4545% | 1,21625% |
| alpha 0,5; threshold 0,90; margem 0,5; TF0 | 7.642 | 1 | 97,4621% | 1,39179% |

Denominador: 7.841 fontes elegíveis; 5.556 identidades-base em VALIDATION. Recuperação do blocking por fonte na união completa: 7.834/7.841 = 99,9107%. O primeiro controle tem 150 conflitos e 2.583 não resolvidos, sobre 10.296 observações totais. Zero FP observado não significa risco populacional nulo. Nenhum controle é elegível: independência residual do pipeline não atestada, P95 ponta a ponta ausente nessa massa e fanout acima da política em alguns controles. O gate não foi desligado.

**Engenharia reversa do v0** SHA `613f914114073d4a9100e848a01a56321354ae9a7885a77462941ae86936953d`: alpha 0,5; TRAIN M=19.896 e U=26.378; 31 células com M, 25 com U, 18 com ambos e **43 sem M nem U observados**. O teste confere as 81 contagens, somas, prior e recusa alpha/contagens incompatíveis. Na união completa: M=20.325 e U=1.009.142; 41 células com M, 54 com U, 37 com ambos e **23 sem ambos**. A cobertura melhorou, mas não virou suficiência independente de todas as células.

O censo é exaustivo. **A calibração das 60.563 classes ainda não foi concluída.** Os 54 controles não comprovam ótimo global. A grade é extensível e não representa todos os possíveis catálogos, comparadores, agrupadores ou parâmetros contínuos.

## Tamanho da amostra

`V9StatisticalSamplePlan` registra alpha, poder, diferença de recall, precisão, frequência mínima por célula, número de células/modelos, cap por identidade e rendimentos esperados. Recall é uma razão de contagens por identidade, não Bernoulli por observação: o cálculo considera cap e denominador. Precisão/cobertura das células requer **pares independentes por classe**, não a quantidade bruta de pares correlacionados.

Exemplo declarado: alpha 5%, poder 80%, recall 95%→97%, precisão ±1 ponto percentual, cap 4, médias esperadas de fontes elegíveis/aceitas 1,3 por identidade, retenção 90%, VALIDATION/TEST 20% cada. No cenário de **zero erro** e M=14.159.502, a condição FDR exige:

| Teto FDR | Identidades independentes VALIDATION | Identidades independentes TEST de um snapshot congelado |
|---|---:|---:|
| 0,1% | 68.921 | 16.239 |
| 0,25% | 28.198 | 6.644 |
| 0,5% | 14.461 | 3.407 |
| 1% | 7.493 | 1.766 |

São cálculos condicionais, não garantia de IID ou de zero erro. O projeto de precisão simultânea por Hoeffding é muito conservador: 4.252.235 unidades para recall, 402.869 para poder; 33.286 pares independentes por classe para precisão das células e 25.923 para observar cada célula de probabilidade mínima 0,001. A conversão conservadora integral resulta em 23.623.528 identidades-base **sob essas hipóteses específicas**. Não é estimativa do mínimo ótimo, não é autorização automática para gerar essa massa e não resolve dependência ou representatividade. Não supor probabilidade positiva para célula estruturalmente impossível. Antes de ampliar os 30 mil, medir variância/dependência e definir precisão necessária; não chamar os pares brutos de ESS.

## Reprodução e próximos trabalhos

Conexão somente para instância descartável e banco `JornadaHarness`, definida no processo:

```powershell
$env:ConnectionStrings__Jornada='Server=(localdb)\JornadaV9Integral20261019;Database=JornadaHarness;Integrated Security=true;Encrypt=false;TrustServerCertificate=true'
python Solution/scripts/v9-integral-search-study.py <observacoes.csv> <plano-novo.json> --workers 2
dotnet <Jornada.Linkage.Parameters.Worker.dll> --v9-adaptive-census <plano-novo.json> <observacoes.csv> <diretorio-novo>
dotnet <Jornada.Linkage.Parameters.Worker.dll> --v9-adaptive-study <plano-novo.json> <observacoes.csv> <outro-diretorio-novo>
```

Para controles somente, acrescentar `--full-union-controls` à preparação; o plano conta o M inteiro, mas avalia apenas união completa e baseline. Para limitar memória sem excluir combinações, preparar a mesma família com `ShardCount=N` e executar todos os `ShardIndex=0..N-1`, com diretórios novos. Os resultados ficam imutáveis; uma interrupção deixa marcador e plano, sem apagar tentativas.

A prova SQL pequena `v9-integral-pipeline-smoke.py` verifica igualdade serial/paralela, enumeração vs quociente e reconciliação dos dois shards. A CI também gera o clean de 30 mil, enumera 524.287 subconjuntos e reproduz os 54 controles; `v9-integral-control-verify.py` refaz as probabilidades de cada célula com Decimal e verifica contadores/SHAs.

Continuam necessários: terminar as avaliações da família e agregar todas as faixas; medir Runner incremental/Gold real com o snapshot; estabelecer incerteza residual defensável; P95 final dos candidatos; novo TEST congelado após seleção; persistência governada em `ref` e dispatch DT-15. Merge deste incremento não executa nenhum desses atos nem ativa V9.

GPU: as fases medidas são SQL, projeção e comparação textual C#. Não foi demonstrado kernel CUDA compatível nem ganho para esse percurso; o trabalho local usa paralelismo CPU/SQL. Não instalar ou substituir algoritmos por GPU sem equivalência e benchmark.

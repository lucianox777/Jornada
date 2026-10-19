# V9 — publicação e grade física adaptativa de 30 mil pessoas

A PR de consolidação é [#1158](https://github.com/lucianox777/Jornada/pull/1158).
Permanece draft: publicação da branch e execução da CI não aprovam merge nem ativação.

## Evidência física já executada

No commit `841a4482`, o [run 39336486517](https://github.com/lucianox777/Jornada/actions/runs/39336486517)
passou tanto no fixture do coordenador quanto no probe histórico de 30 mil pessoas.
O smoke reteve as 54 configurações, mediu 20 linhas de configuração em 20 repetições
cada, verificou 20 fits portáteis e não encontrou configuração elegível. P95 completo
do **fixture pequeno**: 30,0728–59,1016 ms. Esse tempo não representa P95 da massa de
30 mil pessoas nem SLA de produção.

Artefato `12226144192`, digest do ZIP verificado localmente:
`257e72ea21373cd6021eaeb0307610c82168612516a07461604f8d0f42789907`.
O corpus de 30 mil manteve SHA
`28bbb9f4c63c97c353e7f29cdbc77091a4773b2f4c596e46103edadd93635d17`.
O resultado reproduziu TP=7505, FP=0, recall global por fonte 0,9571483229179951 e
bound condicional 0,02223021756850521 para threshold .95/margem .5/FS cego, com 5556
identidades e 36 comparações. IID continua não atestado; não é aprovação estatística.

A CI encontrou dois problemas de integração que foram corrigidos: o leitor antigo
tinha floor operacional padrão de 1000 observações, incompatível com fixture pequeno;
e o novo projeto standalone de benchmark não estava declarado na matriz de arquitetura.
O leitor mantém seu padrão original, e o novo consumidor declara floor 4, igual ao
probe programático. Os gates de risco/independência não mudaram. O benchmark foi
declarado explicitamente sem ProjectReference; permanece fora da solution produtiva,
sem novo pacote. Há 23 projetos na matriz arquitetural e 22 grafos NuGet produtivos.

## Protocolo da nova grade exploratória

O primeiro ensaio de grade no commit `64f2d00b` passou no
[run 39337430558](https://github.com/lucianox777/Jornada/actions/runs/39337430558):
1224 configurações, 324 medidas e 900 recusadas por incidências acima do milhão
declarado; melhor recall observado 0,9452875908685117, com FP=0 nesse ponto.
Não melhorou o controle conhecido. Artefato `12226859709`, ZIP SHA-256
`346b85c67653bbbecb747f648e1cf63c3e41833d859c2dfb429447421d6058a8`,
conferido localmente. Nenhum gate foi relaxado para promover esse resultado.

O protocolo seguinte inclui explicitamente o ruleset anterior de três passes como
controle, além do pool descoberto. `BaselineRuleSets` é opcional no plano C#;
controles não são descartados por limite heurístico do pool e entram no mesmo
orçamento/multiplicidade/tabela. Passes e campos são congelados e ordem idêntica
não cria configurações duplicadas. A verificação compara os resultados do controle
com o probe de referência executado no mesmo run. Isso não cria nova certificação.

`scripts/v9-adaptive-30k-study.py` escreve o plano antes de executar a grade, sem ler
métricas de VALIDATION. O coordenador descobre propostas exclusivamente em TRAIN.

- Massa IBGE-derived clean seed42, 30 mil pessoas, já utilizada historicamente.
- Projeção opt-in K1..4, 378 primitivos avaliados; pool explícito de 16, sem alegar
  exaustividade de todos os primitivos; todas as 136 uniões de um/dois passes.
- Thresholds .90/.95/.99, margens .5/2/4, alpha de suavização proposto .5, TF0:
  1224 propostas mais 9 configurações do controle: **1233 configurações**. Esses valores são propostas declaradas, não constantes
  definitivas nem parâmetros certificados.
- Multiplicidade **2493**, incluindo as famílias anteriores conhecidas de 36 e 1224.
  O histórico completo de buscas não foi autenticado/enumerado; o relatório declara
  essa limitação. Os bounds são condicionais à família informada, não prova de cobertura
  de todas as decisões históricas após observar VALIDATION.
- TRAIN m/u usa a união SQL completa de cada ruleset. A amostra U de 100 mil é apenas
  para propor o pool; não substitui esse fitting físico.
- Limites computacionais explícitos de um milhão de incidências e assinaturas.
  Não há TOP N ou truncamento: candidato que excede o recurso fica rejeitado na tabela.
- Suporte TRAIN insuficiente é rejeição local do candidato; usa exatamente o mesmo
  predicado do adaptador e não aborta todas as regras seguintes.
- Bound máximo .01, recall mínimo .95, fanout P99 máximo 100, suporte mínimo 1000;
  sem relaxar esses gates por causa do orçamento computacional.
- Repetições ponta a ponta **zero nesta grade exploratória**. P95 fica desconhecido,
  jamais herdado do smoke ou da materialização SQL. Assim não há sugestão elegível,
  congelamento automático, publicação ou ativação por esta execução.
- TEST não participa. Não há novo holdout aberto ou otimização em TEST. A reutilização
  de VALIDATION histórica é declarada; os resultados servem para diagnóstico.

## Reprodução no harness isolado

Com `ConnectionStrings__Jornada` já apontando para o SQL descartável JornadaHarness,
na pasta `Solution`:

```powershell
python scripts/v9-adaptive-30k-study.py prepare `
  .local/v9-physical-clean/observacoes.csv .local/novo-plano-30k.json
dotnet run --no-build -c Release --project src/Jornada.Linkage.Parameters.Worker -- `
  --v9-adaptive-study .local/novo-plano-30k.json `
  .local/v9-physical-clean/observacoes.csv .local/novo-estudo-30k
python scripts/v9-adaptive-30k-study.py verify .local/novo-estudo-30k
```

A verificação exige a tabela inteira, reconcilia decisões/recall/FDR por fonte,
confere os hashes dos fits e rejeita alegação de P95 ou certificação. Os artefatos
da CI incluem o estudo agregado, plano, resumo e fits, sem observações/gabarito/nomes.
O andamento e o resultado final desse novo HEAD devem ser conferidos na PR/Actions;
o êxito físico do commit anterior não é apresentado como aprovação desse HEAD.

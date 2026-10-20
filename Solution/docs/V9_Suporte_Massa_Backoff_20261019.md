# V9 — suporte por massa e fallback marginal

**Contrato corrente, 19/10/2026.** Complementa a [seleção interna no TRAIN](V9_Selecao_Interna_TRAIN_Finalistas_20261019.md). A exigência histórica de M e U em todas as 81 células foi substituída por suporte no núcleo de massa de cada classe. FDR, fanout, recall mínimo, TF0 e governança de promoção não foram relaxados.

## Decisão estatística

A proposta literal de 5 M **e** 5 U nas células de 99% da massa conjunta também impediria o bootstrap: na união de 19 passes, `(ExactFull, ExactFull)` contém 9.220 M e zero U em TRAIN_ADJUST. U tem 505.169 pares contra 14.314 M; escolher apenas pela soma também pode ocultar células importantes de M. Por isso a política é **por classe**, congelada antes da busca:

1. Contar as 81 células exclusivas na união SQL deduplicada de TRAIN_ADJUST, separadamente para M e U. Mesmo blocking, partição e classificação para ambos; não usar IBGE marginal nem pares pré-blocking como substitutos.
2. Em cada classe, ordenar as células pela contagem decrescente, desempatar pela ordem canônica `(pessoa, mãe)` e incluir até atingir **pelo menos 99%** da massa observada. A célula que cruza a fronteira é incluída inteira.
3. Exigir **5 M** em cada célula do núcleo M e **5 U** em cada célula do núcleo U. A presença no núcleo M não exige uma contagem U positiva, e vice-versa. Núcleo abaixo do piso continua reprovado e registrado; não recebe suporte artificial.
4. Fora do núcleo, **reter a estimativa conjunta de uma classe quando há pelo menos 5 pares dessa classe**. Somente a cauda abaixo do piso usa fallback. Uma célula pode ter M marginal e U conjunto, mesmo sem estar em qualquer núcleo. O método `JOINT_DIRICHLET_SUPPORTED_TAIL` distingue esse caso. A escolha não olha TRAIN_SELECTION, VALIDATION ou TEST.

O contrato nominal universal permite todas as 81 combinações: os nove estados podem ser construídos independentemente para pessoa e mãe, inclusive ausências válidas. O teste de testemunhas verifica isso no classificador real. Portanto **não há exclusões estruturais universais demonstradas**. Zero observado não é declarado impossível. Restrições específicas de um blocking ou de um gerador não viram restrições gerais para registros novos; a tabela portátil continua completa e finita.

## Estimador único e normalização

Para uma classe C, com total N e alpha > 0, o núcleo e as células da cauda com suporte próprio mantêm `J_ij = (n_ij + alpha)/(N + 81 alpha)`. As marginais são as da **mesma tabela TRAIN suavizada**, `P_i = (n_i. + 9 alpha)/(N + 81 alpha)` e `Q_j = (n_.j + 9 alpha)/(N + 81 alpha)`.

Se R é o conjunto fora do núcleo **e abaixo do piso da classe**, seu orçamento é `r = soma_R J_ij`. Nele, usar `p_ij = r × P_i × Q_j / soma_R(P_i × Q_j)`. Preserva a massa das células conjuntas e da cauda sem suporte e normaliza cada distribuição em um. Se não há cauda, toda a tabela permanece conjunta. Não concatenar probabilidades conjuntas e produtos sem normalizar.

Alpha que perde massa positiva na precisão `decimal` disponível é recusado explicitamente. Contagem observada zero pode receber suavização/fallback; underflow numérico não pode exportar probabilidade zero, parâmetro desconhecido ou evidência neutra.

O produto das marginais é **hipótese de modelagem** para células raras, não evidência de independência, contagem observada ou garantia de FDR. Blocking pode induzir dependência. Essa hipótese precisa enfrentar o holdout congelado. Não se força ordenação entre EXATO, HIGH ou outros estados. TF permanece zero no fluxo padrão.

`V9JointSupportEstimation` é usado dentro do adaptador do calibrador único e na verificação de carga; não existe outro scorer ou motor de seleção. A política integra o fingerprint da configuração e do fit.

## Snapshot e auditoria

Novos fits usam `V9_FITTED_SNAPSHOT_V3`. `JointSupport` registra versão, SHA de TRAIN, alpha, totais, política, cobertura por classe, escalas de normalização e, para cada célula, contagens brutas, pertencimento aos núcleos e método M/U. O loader recalcula todos os valores e rejeita divergências. O SHA do snapshot cobre parâmetros e proveniência.

Snapshots V2 históricos continuam carregando com seus bytes/SHA originais; sua auditoria ainda inverte a suavização original. V3 lê contagens brutas verificadas e **não** inverte probabilidades de fallback como se fossem contagens Laplace. Leitores antigos recusam V3 em vez de ignorar a nova hipótese.

## Busca, multiplicidade e amostra

A execução padrão permanece: 36 configurações grossas, vizinhanças em lotes, parada por bootstrap pareado e no máximo três finalistas. A enumeração dos 524.287 subconjuntos continua diagnóstico opcional; não é invocada por `--v9-calibrate`. `batch-progress.jsonl` registra cada lote durante a execução.

`fresh-holdout-sample-plan.json` separa as famílias: **VALIDATION nova = número efetivo de finalistas**, TEST posterior = um único vencedor congelado. As 14.159.502 comparações conhecidas pertencem apenas à VALIDATION histórica consumida. A busca no TRAIN não entra na correção do novo holdout.

O plano externo usa o mesmo bound da avaliação: Clopper–Pearson de qualquer FP por identidade, Bonferroni entre finalistas e componentes, cap 4 e limite inferior Hoeffding da média de aceites. Rendimento previsto é declarado a partir de TRAIN_SELECTION e continua uma hipótese de desenho; a avaliação usa o rendimento realmente observado. Uma semente de 30 mil não garante antecipadamente FDR ≤ 0,3%. O plano antigo com quatro famílias de desenho e suporte em todas as células é diagnóstico histórico; não dimensiona automaticamente esta avaliação só de finalistas.

`MaximumAcceptedPerBaseIdentity=4` vem do contrato e do manifesto do gerador e limita as contagens por identidade-base no bound. O guard computacional `MaximumBlindClusterSize=16`, persistido no contrato do agrupador BLIND, controla grupos candidatos que podem conter várias identidades-base; ele não multiplica esse limite de FDR. A variante Gold usa UUIDs reais e não transporta esse guard BLIND. O cap sintético não é uma declaração sobre quantas observações uma pessoa pode acumular em produção.

As definições de m/u por classe estão na [documentação primária do Splink](https://moj-analytical-services.github.io/splink/demos/tutorials/04_Estimating_model_parameters.html); isso não estabelece a validade deste fallback particular. O limite binomial exato está descrito pelo [NIST](https://www.itl.nist.gov/div898/software/dataplot/refman2/auxillar/exacbino.htm). A razão limitada por cap exige ainda hipóteses sobre grupos independentes e denominador, além do limite binomial.

## Reprodução e limites

O manifesto do corpus-base identifica `clean` como `PROVISIONAL_SYNTHETIC_CLEAN_BOOTSTRAP_NOT_BRAZILIAN_DATA_QUALITY_ESTIMATE`. As probabilidades declaradas de **tentativa** são 6% para degradação independente do nome, 10% da mãe, 4% do nascimento, 5% de ausência materna e 1% de ausência do nascimento; a tentativa conjunta é zero. Elas não são taxas observadas de erro nem estimativas de qualidade de bases brasileiras. O perfil demográfico, o contrato de multiplicidade, os algoritmos de geração e os hashes das referências foram comparados com o manifesto original e permaneceram iguais na geração atual, variando a semente e os resultados sorteados. O caminho textual do arquivo de nascimento pode variar; seu SHA deve permanecer igual.

```powershell
python Solution/scripts/v9-nested-calibration-study.py <observacoes.csv> <plano-novo.json>
dotnet <Worker.dll> --v9-calibrate <plano-novo.json> <observacoes.csv> <saida-nova>
python Solution/scripts/v9-nested-calibration-verify.py search <saida-nova>
```

Conexão somente ao JornadaHarness descartável. A nova VALIDATION só pode ser aberta depois de congelados os finalistas, com seed inédita, reserva de exposição e sem refit. TEST continua lacrado. Avaliação BLIND não é medição de Gold/Runner incremental; resultados condicionais não autorizam ativação ou removem DT-15.

## Preparação da variante Gold

`--v9-prepare-gold-runner <finalist-validation.json> <diretorio-fits> <saida-nova>` prepara a variante somente se há vencedor condicional. `grouping=GOLD` troca apenas o contrato de agrupamento; m/u, prior, TF, blocking, limiares, SHA do fit e proveniência TRAIN ficam idênticos. O artefato ganha outro SHA e registra o snapshot pai. Gold usa UUIDs reais já recuperados, sem o guard BLIND; não cria grupos a partir do gabarito.

O runtime de preview aceita a variante para resolver a união completa de candidatos de Gold e mantém os checks de projeção, SHA e truncamento. O replay de coorte BLIND recusa explicitamente essa variante. Preparar e testar esse contrato **não equivale** a executar `ProbabilisticLinkageBatchRunner` incremental, publicar Gold ou habilitar o dispatch operacional: `LinkageModelPolicy.RequireCertifiedOperationalAlgorithm` continua recusando V9 sem integração governada/DT-15. O recibo da preparação declara `measured=false`, `certified=false` e `activated=false`. Não trocar esse gate por um modelo V8 para obter uma medição apresentada como V9.

Na continuidade de **20/10/2026**, foi acrescentado um comando de ensaio que chama o Runner incremental real, seus locks, persistência de resultados, publicação progressiva e recomposição canônica de Gold. Ele recebe a variante congelada por um adaptador privado, sem registro no DI operacional, sem modelo ATIVO e sem alterar o loader certificado. A conexão só admite LocalDB `JornadaV9GoldAAAAMMDD_<8 hex>` e banco `JornadaHarness`, com perfil Development e marcador residente de propriedade do ensaio. Recusa outra base de usuário na instância e schema que já contenha origens, modelos, runs ou Gold. Não há reset, limpeza ou aprovação simulada.

```powershell
dotnet <Runner.dll> --v9-gold-harness-create <diretorio-novo> Solution/database
dotnet <Runner.dll> --v9-gold-harness <SHA.selection.json> <finalist-validation.json> <SHA.fitted.json-Gold> <CSV-TRAIN-original> <saida-nova>
```

A criação instala os 74 arquivos do manifesto em arquivos MDF/LDF explicitamente novos, interrompe na primeira exceção e deixa recibo de falha sem tentar apagar dados. O segundo comando exige o vencedor condicional da mesma seleção e verifica que a variante é exatamente a transformação permitida do snapshot pai. As observações são exclusivamente de TRAIN_SELECTION; VALIDATION não é reaberta e TEST não é avaliado. A entrada é Silver sintética materializada por inserção controlada. As ondas seguem os códigos dos gestores; cada origem começa com UUID técnico independente atribuído pela procedure real. Somente as referências efetivamente publicadas em Gold alimentam a próxima onda, com projeção vinculada ao snapshot e aliases nominais das observações realmente associadas a essas referências. Nascimento usa a chave corrente de Gold. O gabarito não entra no adaptador, no blocking, no scorer ou no agrupamento. A homologação V9 pela API/Processor e o dispatch operacional continuam dependendo do binding governado/DT-15.

Após as ondas, uma execução INCREMENTAL adicional reavalia pendências. Cada fonte aparece uma vez nas métricas finais, substituindo sua decisão anterior quando reavaliada. Recall usa as fontes cuja identidade verdadeira já existia no Gold anterior à sua última tentativa; esse denominador incremental difere do denominador BLIND bidirecional. Um aceite só é correto se o destino anterior era um grupo puro daquela identidade. O relatório separa os runs, a elegibilidade, os aceites falsos e os grupos impuros finais. Além dos resultados brutos Rank→Resolve, registra as associações existentes efetivamente publicadas, sua FDR/recall e as contagens próprias de `NOVA_IDENTIDADE` e `INDEFINIDA`. Trata-se de diagnóstico após seleção no TRAIN, sem certificação externa ou ativação. Campos insuficientes para qualquer passe mantêm pendência por `EVIDENCIA_INSUFICIENTE_PARA_BLOCKING`, em vez de simular busca completa vazia que permitiria criar uma referência.

A classificação nominal do TRAIN é calculada uma vez por frame imutável do adaptador e reutilizada entre alphas/limiares/margens. Não há cache entre frames nem de decisões do holdout; as vinte medições ponta a ponta por candidato continuam executando recuperação e score reais. O job físico tem orçamento de até 360 minutos, pois a correção do veto de suporte permite que os lotes e as repetições realmente avancem. Aumentar esse orçamento não altera os gates estatísticos ou o critério de parada.

## Refinamento anterior à exposição externa

A regra corrente é `SupportedTailRule=RETAIN_CLASS_JOINT_WHEN_COUNT_AT_LEAST_K_V2`, registrada no plano, na configuração e no snapshot. A primeira implementação, V1, substituía toda a cauda de 1% pelo produto, incluindo observações já suficientes: no baseline, `(ExactLast, HighFull)` tinha 24 U de TRAIN. Apagar essa contagem elevava muito seu fator de Bayes. A primeira grade V1 mostrou recall máximo viável de 90,71% com zero FP, contra cerca de 96,65% no diagnóstico conjunto anterior no mesmo split.

A regra V2 preserva a evidência própria dessas células; fallback fica reservado à parte sem suporte. Foi declarada e testada **antes de qualquer nova VALIDATION**, sem reduzir 99%, piso 5, fanout 100, recall mínimo 95% ou epsilon 1%. O ensaio V1 é sensibilidade interna, não um holdout externo. A leitura de artefatos V1 continua reproduzindo sua regra original, sem reescrever seus parâmetros ou SHAs. Só resultados novos e finalistas fixados da regra corrente podem fundamentar a próxima avaliação externa.

O contexto de score congela uma vez por fit/snapshot a tabela nominal e os parâmetros `double`. Ranking e diagnóstico continuam chamando o mesmo `FellegiSunterScoring.Evaluate`; o ranking solicita apenas o score, sem montar listas de contribuições que não usa. A paridade compara 729 vetores (81 estados × nove combinações de nascimento) com a entrada pública de diagnóstico, e todos os 22 fits/métricas de uma execução SQL de fixture foram comparados antes/depois do cache. Nenhuma decisão de holdout é reutilizada entre repetições.

## Verificação local — 20/10/2026

- SDK **10.0.113**, restore da solução em `--locked-mode` e `System.Security.Cryptography.Pkcs` resolvido em **9.0.21**.
- Compilação da solução inteira em Release: **zero avisos e zero erros**.
- Suíte C# completa: **1.809 aprovados, zero falhas e zero ignorados**. Inclui suporte por classe, normalização, compatibilidade V2/V3, adulteração de proveniência, underflow, casos das 81 células, paridade do scorer, pin/projeção, TF, falta de evidência e isolamento do comando Gold.
- **Sete testes SQL de origem e publicação progressiva aprovados**, sem ignorados, em banco exclusivo provisionado pela própria suíte numa instância LocalDB privada. Esses testes de domínio usaram o motor local 15; a baseline SQL Server 2022 permanece uma verificação da CI final.
- Paridade SQL de fixture: **22 snapshots com os mesmos SHAs e todas as métricas por fonte idênticas** antes/depois da otimização do contexto e do caminho sem alocação de diagnóstico.

Uma fixture separada com a semente reservada já conhecida `20261019001` confirmou a reprovação por custo: vinte repetições, P95 **64.604,7622 ms**, acima dos 60.000 ms declarados, mesmo com bound condicional de FDR abaixo de 1%. Permaneceu sem vencedor condicional. Esse resultado pertence ao teste de infraestrutura; não abre a família final, não modifica seus limites e não autoriza Gold ou ativação.

## Rodada concluída — 20/10/2026

Os [artefatos verificáveis](../data/calibration/v9/mass-support-20261020/README.md) incluem o envelope integral comprimido, os snapshots com bytes originais, o comparativo pareado, a curva de parada, o plano amostral, a avaliação externa e o ensaio Gold. O envelope descomprimido tem SHA `b98121f9b4854e0db56026df1ddaef30e0f8c32717baa88a68549bd0ad08d75a`; suas 926 configurações foram verificadas independentemente antes de gerar a nova semente.

Foram seis lotes, com 36 configurações iniciais e refinamento das vizinhanças. Os melhores recalls por lote foram 90,7146%, 96,9904%, 97,2614%, 97,4326%, 97,4469% e 97,4754%, todos com zero FP no TRAIN_SELECTION. Os dois últimos lotes não ultrapassaram a resolução do bootstrap pareado. A parada é heurística e não comprova ótimo global. A fronteira final tem um ponto e, portanto, somente **um finalista**; não se inventaram vizinhos dominados para preencher três vagas. Permaneceram registradas 117 rejeições por fanout e nove por suporte no núcleo.

O snapshot `0a3459585fcf3d20fbd0854e977b84a769127a9ab01ab0d72417dff42d0b3e5b` usa alpha 0,1, TF 0, limiar 0,8671875, margem 0,609375 e oito passes. O fit contém 14.308 pares M e 273.726 U do TRAIN_ADJUST. Os núcleos contêm 17 células M e 16 U; a cauda suportada preserva estimativas conjuntas em outras dez M e 24 U. As demais 54 M e 41 U usam as marginais condicionadas, registradas individualmente. Não houve refit externo.

| Medição | Denominador de recall | TP | FP | Recall | FDR observada |
| --- | ---: | ---: | ---: | ---: | ---: |
| TRAIN_SELECTION, BLIND congelado | 7.011 fontes elegíveis | 6.834 | 0 | 97,4754% | 0% |
| VALIDATION nova, BLIND congelado | 7.729 fontes elegíveis | 7.539 | 2 | 97,5417% | 0,02652% |
| Runner incremental Gold, diagnóstico TRAIN_SELECTION | 3.401 fontes com identidade em Gold anterior à última tentativa | 3.382 | 3 | 99,4413% | 0,08863% |

A VALIDATION usou a semente inédita **20261020001**, 30 mil identidades, mesmo gerador e cap 4. A exposição única foi registrada no registro persistente, depois de congelada a família. Multiplicidade externa **1**, 5.563 identidades elegíveis, vinte repetições físicas, P95 **2.118,3407 ms** e fanout falso P99 **88**. O limite superior **condicional** de FDR foi **0,40479%**, abaixo de epsilon 1%. O status é `CONDITIONAL_ONLY_IID_NOT_INDEPENDENTLY_VALIDATED`: a independência dos grupos decisórios não foi certificada, `selectedApprovedSnapshotSha256` permanece nulo e o candidato não está aprovado para produção. A multiplicidade histórica 14.159.502 não foi aplicada a essa família.

A variante Gold `6d93690dccac2c92dc4d3113cbb9169dd19156c4f9b3d849ededfb4c54ae5f3f` preservou exatamente o fit. O Runner real processou 9.240 observações de 5.003 identidades, publicou quatro ondas e reavaliou 2.825 pendências, resolvendo 35 nessa última passagem. As associações existentes realmente publicadas coincidiram com os aceites medidos: 3.382 corretas e três falsas. Ao final havia 3.067 referências Gold, **uma impura**, e 2.788 fontes publicadas como pendentes. Esse resultado impede alegar ausência de falsos vínculos no Gold; seu recall incremental também não é comparável diretamente ao recall BLIND. Os 5.855 não aceites do resumo bruto incluem fontes criadoras de identidade e não devem ser apresentados como 5.855 pendências SQL.

O ensaio Gold é diagnóstico pós-seleção no TRAIN; não substitui uma avaliação externa de Gold, não certifica o bound BLIND para o fluxo incremental e não reabre VALIDATION. Permanecem necessários: demonstrar as hipóteses do bound, homologar a ingestão V9 pela API/Processor, persistir/bindar o modelo em `ref` pelo fluxo governado e liberar DT-15. TEST não foi consumido. Nenhum modelo foi ativado e nenhum banco original, referência IBGE ou nó DEV foi alterado. O merge deste código não representa homologação estatística ou autorização de ativação.

## Classificação do achado do scanner

O job `security-guards` do commit `d41a5876` sinalizou `generic-api-key` na linha 8 de `mass-support-20261020/finalist-validation.json`: o campo `fresh.ExposureKey`. O código `V9FreshHoldoutProtocol.Inspect` calcula esse valor exclusivamente como SHA-256 UTF-8 da string pública `V9_FRESH_SYNTHETIC_HOLDOUT_V1|20261020001`. `ReserveOnceAsync` o utiliza como nome do arquivo de recibo criado atomicamente; não há uso como credencial, autenticação ou segredo. A classificação é falso positivo de um identificador público reproduzível, após inspeção do relatório e do consumidor.

A exceção revisada na PR [#1160](https://github.com/lucianox777/Jornada/pull/1160) exige simultaneamente o caminho exato desse relatório e o valor exato desse hash, somente para a regra `generic-api-key`. Não exclui o arquivo inteiro, diretórios ou outras regras. Mantém os bytes/SHA científicos originais e não reescreve o histórico. A [configuração primária do Gitleaks 8.30.1](https://github.com/gitleaks/gitleaks/blob/v8.30.1/README.md#configuration) define a conjunção `AND`. A verificação local usa essa mesma versão, inclui o caso permitido e mutações de valor, caminho e credencial adicional que devem continuar sendo detectadas.

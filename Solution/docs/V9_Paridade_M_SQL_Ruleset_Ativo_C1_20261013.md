# V9 — paridade do ruleset ATIVO + C1 com o mesmo denominador de M do SQL físico

## Motivação — não confundir dois gates

A PR #1003 decompôs os **10.250 pares verdadeiros M** perdidos em TEST de três corpora sintéticos de 30 mil pessoas, com 18.036 M elegíveis e 7.786 M recuperados pelo SQL ATIVO+C1. Dentre os perdidos, 2.935 apresentavam divergências observadas simultâneas de nome, mãe e nascimento; 2.626 mantinham nome/nascimento iguais mas mãe ausente; 1.987 divergiam em nome/nascimento com mãe ausente. São estados observados, não atribuição causal ao gerador.

O piso `MinimumTrueMatchRecall=0.95` de `BlockingRuleSetOptimizer` só vale **no corpus entregue à busca do Calibrador** e no **ruleset candidato** ali avaliado. O censo SQL anterior executava o **ruleset ATIVO** do `JornadaHarness` somado aos cinco passes C1, com gabarito M de outro universo. Logo, não está demonstrada violação do gate nem paridade de frames; também não se pode reivindicar que o gate garante 95% nesse SQL.

## Nova prova, apenas no harness efêmero

O diagnóstico `V9SqlBlockingFrameParityAudit` executado por `V9PhysicalSqlBlockingProbe` acrescenta um controle estrito: **a mesma lista independente de M elegíveis**, com ambas as observações sem CPF, nascimento presente, gestores distintos e mesma partição, é usada para confrontar, par a par:

1. **C1 físico**: os pares presentes no `#v9pairs` SQL **antes** de incluir o ruleset ATIVO;
2. **ATIVO lógico**: os passes efetivamente lidos da tabela `identidade.linkage_ruleset_passe` do modelo `ATIVO`, com a semântica exata de AND dentro de cada passe, OR entre passes, igualdade de chaves projetadas por `BlockingProjectionKeyProjector` e tratamento incompleto de campos ausentes;
3. **União física real**: pertinência em `#v9pairs` SQL **depois** de adicionar os passes ATIVOS.

Se `(C1 OR ATIVO lógico) != (C1+ATIVO físico)` para **qualquer M**, o ensaio **aborta**. Não há aproximação por histogramas, seleção de pares de teste ou `TOP N`. A auditoria publica por TRAIN, VALIDATION, TEST: `EligibleM`, `C1M`, `ActiveLogicalM`, interseção, somente C1, somente ATIVO, união lógica, união SQL real e M não recuperados. Por passe ATIVO, publica `RetainedM` e `ExclusiveOutsideC1M` (só esse passe dentre os ATIVOS e fora do C1): **cobertura marginal não aditiva**.

O gate Python independente exige as igualdades da união, interseções, exclusividades e denominadores em cada seed/split, além do mesmo `SqlCandidateTruePairs` já auditado pela PR #1003. Testes puros verificam AND incompleto, cobertura sobreposta e falha fechada em M faltantes/estranhos.

Nenhum identificador de pessoa, observação, par, nome, CPF ou data individual sai em JSON/log: somente rótulo público de passe e métricas agregadas. Os valores e ordinais necessários existem só em memória efêmera.

## O que o resultado não demonstra

**Este não é um novo estimador do gate de 95% em TRAIN do Calibrador** e não promove um novo ruleset. Se a paridade SQL física/lógica for verificada e o recall sobre M continuar baixo, a causa restante envolve regras/dados efetivamente disponibilizados no frame SQL e diferença entre o conjunto de calibração e o gabarito do censo — não é um bug de OR/AND comprovado nesse recorte.

As chaves são **atuais**; faltam versões históricas `VERSIONED_ALIAS`. O denominador exclui pares com nascimento ausente e considera apenas observações sem CPF entre gestores. O experimento NÃO representa risco populacional municipal, 11 milhões de cidadãos, nem estabilidade após agregação de UUID. Os dados de TEST são congelados para relato **não para escolher passes**.

A próxima etapa da issue #999 deve reproduzir a **mesma** seleção de M/U, projeções, fingerprints e regras do Calibrador em TRAIN e SQL e comparar o recall usando denominadores idênticos; medir U/fan-out e custo na massa-base; escolher uniões somente em TRAIN/VALIDATION; calibrar m/u, prior, nascimento e TF no novo SQL físico com IBGE congelado para marginais compatíveis e massa-base única TRAIN para unidades conjuntas; então testar modelo congelado e orçamento FDR sem afrouxar gates.

**Barreira de segurança:** somente `JornadaHarness` com `DOTNET_ENVIRONMENT=Development`. Sem alteração do modelo ATIVO fora do harness, V9 decisória, DT-15, Rank/Resolve, V8 histórico, JornadaLocal, Gold, IBGE original, NODE/NAS ou HML/PROD. Merge técnico somente após CI verde no HEAD exato; não certifica qualidade estatística.

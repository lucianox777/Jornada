# GOV-V9-DESATIVACAO-PARAMETROS-CIENTIFICOS-SQL-20261117

**Autoridade:** Product Owner, 17/11/2026. **Estado:** decisão aprovada; levantamento de dependências e implementação pendentes. **Política:** GOV-DOC-01.

## Decisão
Concluir a PR #1655 e, na evolução do seletor V9, retirar o uso de propriedades/parâmetros científicos gravados pelo calibrador no banco como fonte de configuração da seleção. A configuração científica do executor V9 deve provir **exclusivamente do pacote/artefato imutável selecionado**, conforme GOV-V9-RUNNER-ARTEFATO-ARQUIVO-FONTE-UNICA-20261117. Não reintroduzir fallback SQL nem sincronização paralela de parâmetros do artefato para o banco.

## Contexto e justificativa
O PO identificou legado em que o calibrador grava propriedades no banco e o seletor as utiliza para decidir. Esse caminho deve deixar de governar a seleção V9, pois a fonte de verdade aprovada é o pacote de configuração. A PR #1655 trata do grafo de candidatos e suas verificações; o grafo é dado operacional de entrada, **não** parâmetro científico de calibração. O objetivo é fechar essa integração sem alterar os parâmetros científicos nem a política de TEST.

## Escopo e sequência segura
1. Inventariar exatamente colunas, tabelas, procedimentos, consultas, gravações do calibrador e leituras do seletor que representem **parâmetros científicos V9**; distinguir metadados de run, dados de blocking, identidades, decisões e referências históricas.
2. Implementar leitura e validação do pacote V9 no início do Runner, com SHA, compatibilidade, congelamento por execução e falha fechada. Preservar REPLAY DT-05 e comportamento legado não-V9 até migração específica.
3. Redirecionar o caminho V9 para o pacote, remover gravações/leituras **de parâmetros científicos V9** no SQL apenas após prova de ausência de dependências e testes E2E/rollback.
4. **Não executar DROP/DELETE ou migração destrutiva** sobre tabelas, colunas ou dados existentes nesta etapa; qualquer remoção física requer inventário, plano de migração, backup/rollback e validação explícita de impacto. Preservar dados e histórico operacionais.
5. Concluir provas do grafo operacional completo por coorte na PR #1655; não confundir testes estruturais com testes SQL físicos nem autorizar publicação.

## Impactos e precedência
Complementa GOV-V9-GRAFO-OPERACIONAL-INTEGRAL-COORTE-20261117 e operacionaliza GOV-V9-RUNNER-ARTEFATO-ARQUIVO-FONTE-UNICA-20261117. Esta última prevalece sobre origem SQL para parâmetros científicos V9. Não altera parâmetros, TFs, modelo vencedor, regras de holdout ou autorização de TRAIN/TEST. Persistência de decisões e publicação são etapas separadas, ainda bloqueadas.

## Rastreabilidade
PO, conversa de 17/11/2026: continuar PR #1655, concluir integração e retirar propriedades científicas SQL anteriormente gravadas pelo calibrador e usadas pelo seletor, substituindo-as pela configuração por pacote. Evidências e testes pendentes; documentação não implica implementação concluída.

## Inventário inicial confirmado no código (17/11/2026)
- **Já implementado no ramo principal:** `V9ConfiguredArtifactLoader.LoadCohortRuntimeAsync` lê `Linkage:V9SelectionFile`, valida o pacote via `V9ValidatedOperationalModelFile.ReadPinnedAsync` e instancia `V9OperationalPinnedCohortRuntime.FromVerifiedFileSnapshot` sem SQL. O `Program.cs` congela a configuração de startup e rejeita explicitamente a ativação produtiva V9 com `RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED`; sem arquivo, V9 falha com `RUNNER_V9_SELECTION_MISSING`, antes da busca do modelo SQL.
- **Caminho legado preservado:** `ProbabilisticLinkageBatchRunner.RunAsync` ainda usa `GetModelByVersionAsync` ou `GetActiveModelAsync` para o seletor SQL tradicional; `Program.cs` mantém o preflight de modelo/ruleset legado e exclui REPLAY da seleção de startup. Não remover essas consultas indiscriminadamente.
- **Lacuna real:** ligar o runtime V9 já carregado por arquivo ao executor operacional de coorte/batches, comprovar paridade produtiva e só então remover a dependência dos parâmetros científicos SQL específicos de V9. Não afirmar que o carregador de pacote ainda precisa ser criado.
- **Regressão adicionada:** `test_v9_physical_runasync_graph_guards.py` verifica estruturalmente a separação arquivo V9/SQL legado e o bloqueio fail-closed (commit `6594057`). Testes não executados nesta etapa.

## Coerência entre pacote e seleção do Operador Master (17/11/2026)
A presença de `Linkage:V9SelectionFile` agora exige `Linkage:SelectedModelVersion=9` e, quando informado, `--model-version=9`. Divergências falham antes da leitura do artefato e antes do preflight SQL (`RUNNER_V9_SELECTION_VERSION_MISMATCH`), evitando que a presença acidental do pacote V9 se sobreponha a outra versão escolhida. Commits `e1dfa57` e `f727ec8`. O gate `RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED` permanece ativo; a alteração não habilita produção nem remove parâmetros SQL legados. Teste estrutural adicionado, sem execução de CI confirmada.

## Proteção da seleção física do pacote (17/11/2026)
O loader V9 passa a recusar o caminho do artefato se inexistente ou se for link simbólico, antes da leitura imutável com verificação de SHA (commits `cd95c90`, `e53842a`). A seleção JSON já recusava links simbólicos. Essa defesa de caminho não resolve, isoladamente, substituição concorrente de arquivo nem certifica o adaptador produtivo. O gate de ativação permanece; o legado SQL não foi alterado.

## Auditoria de integração após merges #1655 e #1656 (17/11/2026)
Inspeção direta dos componentes `Program.cs`, `ProbabilisticLinkageBatchRunner.cs`, `V9PinnedSyntheticGraphCohortRunScorer.cs`, `V9ConfiguredArtifactLoader.cs` e `V9FrozenCohortSqlCandidateGraph.cs` na master:

1. **Seletor:** `Program.cs` verifica `Linkage:SelectedModelVersion=9`, `Linkage:V9SelectionFile`, SHA do artefato e instancia `V9OperationalPinnedCohortRuntime` sem consultar parâmetros científicos SQL; em seguida interrompe com `RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED`. Não retirar o gate sem paridade comprovada.
2. **Executor:** `ProbabilisticLinkageBatchRunner.RunAsync` ainda seleciona `GetModelByVersionAsync`/`GetActiveModelAsync` e opera com `IProbabilisticIdentityLinkage`, sem receber o runtime V9 de arquivo. Consultas SQL permanecem necessárias ao legado e REPLAY.
3. **Ponte de coorte:** `IV9NonPublishingFrozenCohortScorer` permite preparação global antecipada, mas `V9PinnedSyntheticGraphCohortRunScorer` carrega vencedor por `V9OperationalPinnedCohortRuntime.LoadPinnedAsync(connection,...)`, exige `JornadaHarness` e limita 512 observações. Não constitui adaptador produtivo.
4. **Grafo físico:** `V9FrozenCohortSqlCandidateGraph` reconcilia assinaturas e arestas contra oráculo C#, mas limita 512 observações, 8.192 assinaturas e 25.000 incidências de pares; não comprova completude operacional de `identidade.blocking_chave`.
5. **Próxima entrega:** criar despacho V9 separado do legado, injetar runtime imutável por run, mapear consulta operacional de candidatos e semântica de paginação/coorte, e executar provas físicas de completude, concorrência interlotes, falha fechada e ausência de publicação parcial. Não alterar parâmetros científicos, TEST one-use ou dados históricos.

**Rastreabilidade:** merges #1655 (`6c6ff6838336eb41cec97a5241baa68d72e773e7`) e #1656 (`c7fb318b977eb86ae346ac77dda454b5802e6b72`). Registro de lacunas, sem autorização de produção. Decisão já consta do índice vigente.

## Defesa em profundidade no despacho RunAsync (17/11/2026)
A fronteira `ProbabilisticLinkageBatchRunner.RunAsync` é invocável independentemente do `Program.cs`. Até a certificação do adaptador V9, a seleção explícita de `ModelVersion=9`, a configuração `Linkage:SelectedModelVersion=9` ou a presença de `Linkage:V9SelectionFile` passam a falhar fechadas **antes** da aquisição do lock, da seleção de modelo SQL e da criação de run, com `RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED`. O REPLAY histórico DT-05 mantém contrato próprio, sem alteração de seu despacho. Trata-se de proteção transitória, **não** de integração produtiva nem autorização para publicação; deve ser substituída por despacho V9 certificado após provas de grafo completo, coorte global e ausência de publicação parcial. Rastreabilidade: branch `fix/v9-runasync-failclosed-dispatch-20261117`, `ProbabilisticLinkageBatchRunner.cs`; CI e integração na master pendentes.

## Fronteira tipada de runtime (17/11/2026)
`V9OperationalRunBoundary` recebe exclusivamente `V9OperationalPinnedCohortRuntime` já verificado pelo loader de arquivo, valida a presença de SHA-256 e expõe a identidade imutável para futura integração. `Program.cs` constrói a fronteira após validar o artefato e chama `EnsureProductiveCertified()`, que **continua a lançar** `RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED` antes de qualquer consulta ao modelo legado ou processamento produtivo. É preparação estrutural do despacho, **não** passagem ao executor `RunAsync`, certificação, liberação de publicação ou prova de paridade. Próximo passo: transporte por escopo de execução e grafo operacional integral, mantendo o gate até evidência física. Rastreabilidade: branch `feat/v9-runtime-dispatch-boundary-20261117`; revisão e CI pendentes.

## Compatibilidade de smoke do cluster legado (17/11/2026)
O wrapper DEV `Solution/scripts/local-cluster.sh linkage` verificava o modelo legado calibrado ativo, mas iniciava o Runner sem `Linkage:SelectedModelVersion`, violando a exigência do Operador Master e causando `RUNNER_MODEL_CONFIGURATION_MISSING` na CI de topologia. Correção operacional: fornecer somente à invocação isolada `compose exec` a variável `Linkage__SelectedModelVersion` com a versão já lida/validada e o argumento concordante `--model-version`. Não altera configuração persistente, seleção V9, SQL científico ou REPLAY; preserva a proteção de startup. Evidência anterior: workflow de topologia #44185385301, falha no wrapper; nova CI pendente. Rastreabilidade: branch `fix/local-cluster-runner-model-pin-20261117`.


### Evidência CI de correção do wrapper DEV (2026-11-17)

A execução de topologia da PR #1662 (run `44187121231`) falhou na etapa de diagnóstico porque nenhum `linkage_run` ON_DEMAND `PUBLICADO` existia para o modelo ativo, embora o wrapper retornasse sucesso. A inspeção do script identificou uma falha concreta de serialização: sequências literais `\\n` haviam sido inseridas na linha de comentário anterior ao comando `compose exec`, fazendo o shell interpretar o comando como parte do comentário. O commit `e936ed8` restaura quebras de linha reais; o teste de regressão verifica a separação do comando e impede a reintrodução desse defeito. A certificação depende de nova CI aprovada; não altera o gate produtivo V9 nem a seleção do Operador Master.

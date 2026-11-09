# V9 100k — implementação diagnóstica multiparâmetro, sem certificação

**Mandato PO 09/11/2026, #1349. ESTADO: IMPLEMENTAÇÃO INTEGRADA via PR #1372, master `1813f2f10d57cce964c9dcfe50974be89ddff5bb`; TESTE FÍSICO 100K NÃO EXECUTADO; segurança #1253 pendente.** GOV-DOC-01.

## Contexto e justificativa
O SQL SCALE 100k existente é V8 e não valida V9. O J oficial tem prova física de 24 hipóteses em 30k no [run 42574801338](https://github.com/lucianox777/Jornada/actions/runs/42574801338); manter intactas as evidências, pins e fingerprints históricos. O pin IBGE 30k é uma *referência* fixa: NÃO é sinônimo do tamanho do novo corpus de treinamento. O método C# `V9NestedCalibrationCli.SearchAsync` recebe a proveniência TRAIN do `ObservationCapDeclaration`, independentemente de `PinnedIbgeBootstrap`, que conserva a referência externa IBGE 30k. Assim pode-se testar 100k sintético sem reescrever a referência.

## Desenho implementado em PR
- Workflow manual `v9-multiparameter-100k-technical.yml`, `expected_sha` exato da master, flag de preflight de segurança e gate de RAM >=24 GiB e disco disponível >=40 GiB **antes de SQL/geração** (são requisitos conservadores operacionais, não garantia de sucesso). Timeout 360min; runner GitHub `ubuntu-24.04` poderá não satisfazer a memória: nesse caso **FAIL ANTES DA CAMPANHA**, exigindo runner mais robusto e decisão operacional; não afrouxar limites para forçar PASS.
- SQL `JornadaHarness` descartável, senha randômica mascarada antes de Docker, transporte `--env-file` `0600` (PR #1368), cleanup obrigatório.
- Reconstituir referência IBGE/TF histórica 30k via script existente (sem rodar treinamento 30k). Gerar novo corpus sintético clean **100.000 pessoas, seed conhecida 42**, sem tocar seed reservada de TEST. Binário C# gerador e calibrador existentes, sem mudança de algoritmo.
- Autenticar artifact oficial PRIMARY J **13654549483**, ZIP SHA256 `5bdbce7ffd5fed55fc085fde42faf9793cb39e6e79c3db4c69990a818bf330d7`, SHA origem `3da5e932...`, recibo dos 24 fits; conservar `PinnedIbgeBootstrap.People=30000` e TF histórico.
- Emitir **novo e único `plan.json` técnico aplicável ao corpus 100k**: modificar exclusivamente `ObservationCapDeclaration.ManifestSha256/ObservationsSha256` e `StudyFingerprintSha256` (também seu espelho Budget). Provar por `restored == original` que todas as demais entradas científicas, defaults, 24 hipóteses, V2 WITNESS/ANCHOR, TF0/ONE_MF, 3/19 passes, FDR 1%, CPU/paralelismo e pins históricos são **idênticos**. O plano antigo não é sobreposto e hash original é preservado em recibo; nova fingerprint é necessária porque o corpus mudou. Sem `experiment.json`, sem escolhas ad hoc nem refit de TEST.
- Executar C# `--v9-calibrate` sobre 100k e auditar **24/24 físicos /16 testemunhas+diagnósticos /8 candidatos**, 81 células, igualdade TF/SQL dentro de cada frame, paridade λ0 V3 e comparações **V2×V3 no mesmo frame 100k**. Audit registrar TP, FP, FN=fontes elegíveis-TP, recall, FDR observado (não limite populacional), P95 por resolução, duração total e pico RSS **do processo**, CPU/paralelismo, SHA plano/corpus/manifesto/artefato. Disco: **NÃO COBERTO** se não instrumentado; não forjar medição #1363. HTTP 100k: **NÃO COBERTO**. Sem nomes, CPFs, base SQL, corpus individual ou logs brutos no artifact.
- Critérios fail-closed: faltar memória, falta SHA exato, falso pin, ausência de 24 fits, comparabilidade do mesmo frame, TEST usado, guard de FDR indevido, arquivo sensível, ou mismatch bloqueiam. Nunca afirmar PASS só pelo workflow existir.

## Segurança e precedência
#1302 encerrada com evidência de 160/160 30k, mas isso não dimensiona 100k. #1253 permanece aberta por histórico público de senha efêmera (o novo caminho Docker não a reutiliza). É obrigatório **preflight no momento da execução**: ausência de risco atual de segredo persistente, runner isolado, autoridade de recursos, versão e SHA. Se não demonstrado, **não marcar a flag** e não disparar.

O workflow é **somente workflow_dispatch**, não executa automaticamente no merge; o conector GitHub não expõe dispatch. A PR não executa físico, não consome TEST cego, não ativa modelo, não altera Gold/HML/Produção nem #1352/#1356. #1349 será encerrada somente com execução física 100k V9, artifact íntegro, CI posterior da master e relatório formal integrado. Caso não exista runner com recursos ou acionamento manual autorizado, estado **AGUARDA EXECUÇÃO MANUAL DO PO**.

**Estado documental:** código proposto/implementado em PR, sem validar até SHA+CI; requisitos do PO preservados. GOV-DOC-01.
## Enriquecimento de rastreabilidade técnica da PR (sem alterar ciência)

A evidência agregada é vinculada ainda a `codeSha` completo e `workflowRunId` efetivo do workflow (argumentos **não sensíveis**, SHA/run apenas), além de SHA dos planos/corpora. O auditor expõe `conditionalClusterCpFdrUpperNotCertified` somente como diagnóstico **condicional**, não como certificação geral de FDR. Ausência de valores não é inferida como zero. Esses campos não interferem em seleção científica ou motor operacional.


## Teto de registros — interpretação vinculante

**Não há teto universal 30k/100k na calibração V9.** A escolha 100k é específica deste workflow e não reduz a liberdade do único Calibrador C# para outros tamanhos declarados e auditados. `PinnedIbgeBootstrap.People=30000` refere-se exclusivamente à origem da referência histórica IBGE; os fingerprints do novo corpus são refeitos, sem adulterar referência nem código científico. Requisitos de capacidade da máquina (24 GiB RAM/40 GiB disco) não são contagem máxima de identidades. [Decisão vigente de 05/11 ratificada em 09/11](DC-V9-CALIBRACAO-SEM-EXCLUSIVIDADE-30K-20261105.md).

A via manual 100k está integrada, mas permanece SEM EXECUÇÃO FÍSICA até gates do SHA, preflight #1253 e infraestrutura. GOV-DOC-01.
## Execução viável em runner — correção de bloqueio material (09/11/2026)

Auditoria pós-merge #1372 detectou inconsistência operacional determinística: a rota física manual pedia **>=24 GiB RAM e >=40 GiB de disco livre**, mas tinha `runs-on: ubuntu-24.04`, que disponibiliza somente **16 GB RAM e 14 GB SSD** no runner padrão Linux para repositório público (documentação oficial GitHub Actions, consultada 09/11). Assim, **o job jamais poderia vencer o próprio preflight** — isso não era limite de registros do Calibrador.

A correção altera **somente o runner do workflow 100k** para `ubuntu-24.04-16core`, perfil de *larger runner* Linux de **16 vCPU/64 GB RAM/600 GB SSD**, sujeito à disponibilidade e à habilitação na conta/repositório do PO. Esta seleção **não cria nem habilita runner** e **não executa nem cobra campanha** por si só. Se o ambiente não estiver provisionado, `workflow_dispatch` ficará pendente sem agendamento de worker; **AGUARDA CONFIGURAÇÃO/EXECUÇÃO MANUAL DO PO**, sem contornar a guarda de recursos. O requisito de segurança #1253 e os gates de SHA/recursos permanecem exatamente iguais.

Controle adicional em teste Python garante que o workflow tenha runner potencialmente capaz de passar o preflight e preserve execução apenas manual. **Nenhum teto universal de registros foi criado**: 100k continua somente o escopo deste ensaio, conforme DC-V9-CALIBRACAO-SEM-EXCLUSIVIDADE-30K-20261105.md. É necessária aprovação explícita do PO para habilitar infraestrutura paga ou própria; sem dispacho automático, nem promessa de disponibilidade.

Referências: https://docs.github.com/en/actions/how-tos/write-workflows/choose-where-workflows-run/choose-the-runner-for-a-job ; https://docs.github.com/en/actions/reference/runners/larger-runners ; https://docs.github.com/en/actions/how-tos/manage-runners/larger-runners/use-larger-runners ; GOV-DOC-01.

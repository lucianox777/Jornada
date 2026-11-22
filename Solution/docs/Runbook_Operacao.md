> **Aditamento 09/10/2026 — não altera autorização de operação:** a
> Console DEV ganhou serviço de observação, toggle ON↔OFF dos
> três workers, RunOnce e parada individual no **sandbox GitHub-hosted
> efêmero `JornadaE2E`** (#851–#856). Essa implementação
> **não substitui** o scheduler corporativo descrito abaixo e
> **não autoriza** uso dos métodos de injeção de falha em NODE,
> HML/PROD, volumes/contêineres locais ou dados originais. A
> recuperação de lote sintético após falha do Processor foi
> comprovada na CI (#850); para incidentes de produção continuam
> exigidos runbooks, backup, autorização, logs de auditoria e
> infraestrutura específicos. Consulte
> [Manual integrado](Manual_Sistema_Consolidado_20261009.md) e
> [Console DEV](Console_DEV_Supervisao_Atual.md).
>
> **Precedência de decisão (29/09/2026):** [DC-LK/OP](Decisoes_Canonicas_Identidade_Linkage_20260929.md) prevalece sobre notas antigas V6/V7 e o guard demográfico; o código ainda exige conferência governada por modelo e mantém parâmetros históricos. Este runbook descreve passos executáveis com distinção de estado e objetivo futuro; não ativar a V8 revista antes dos gates de implementação.
>
> O procedimento da campanha sintética está em [Operação do calibrador V9](#operação-do-calibrador-v9). A autorização de campanha é específica ao SHA e ao preflight do proprietário.

# Jornada - Runbook operacional da Fase 1 (v3.55)

**Interpretação operacional do núcleo:** a Jornada é apoio à decisão e mantém a melhor representação disponível, não uma identidade civil certificada. Mudanças de evidência de identidade, de referências candidatas ou de modelo podem demandar reavaliação; novas entregas idempotentes ou mudanças só de atributos não identitários não devem forçar novo scoring. Quando a representação semântica não mudar, evitar nova versão operacional da decisão, mantendo a trilha de execução e o ledger de atos aplicáveis. A hierarquia pretendida da Gold é documentação mais recentemente apresentada, documentação anterior, autodeclaração mais recente e autodeclaração anterior, por atributo. **Estas são diretrizes a conferir/implementar no código atual, não procedimentos já homologados.** Ver `Diretrizes_Identidade_Progressiva_Apoio_Decisao.md`.

## Operação do calibrador V9

O caminho canônico é [V9 Standard Linear](../../.github/workflows/v9-standard-linear-100k.yml), que chama o calibrador C# em [Jornada.Linkage.Parameters.Worker](../src/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.csproj). [Program.cs](../src/Jornada.Linkage.Parameters.Worker/Program.cs) encaminha `--v9-calibrate` e `--v9-validate-finalists` para [V9NestedCalibrationCli](../src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCli.cs). O workflow legado TF4 é diagnóstico. O percurso padrão segue a [decisão de isolamento e TEST obrigatório com vencedor](Decisao_GOV_V9_TEST_Isolamento_Retrocompat_Liberacao_20261117.md) e o [protocolo de CI e pós-merge](Decisao_GOV_OPS_Correcao_Erros_Squash_CI_Master_20261111.md).

### Ambiente e insumos

| Item | Configuração confirmada |
| --- | --- |
| Ambiente | GitHub Actions `ubuntu-24.04`, SDK .NET `10.0.113`, Python 3, Docker, utilitários de SHA-256 e OpenSSL no job TEST. `DOTNET_ENVIRONMENT`, `ASPNETCORE_ENVIRONMENT` e `JORNADA_RUNTIME_MODE` em `DEV` nos jobs científicos, com bootstrap da referência SQL em `Development`. |
| SQL | SQL Server efêmero `JornadaHarness` em cada job físico. TRAIN/VALIDATION recebem `ConnectionStrings__Jornada` do bootstrap do workflow. TEST recebe `JORNADA_V9_TEST_SQL_CONNECTION` e `JORNADA_V9_TEST_SQL_MARKER` do próprio bootstrap, com loopback, TLS obrigatório, certificado efêmero fixado e checagem de ausência dos schemas Gold/Serving. Os jobs geram as credenciais e eliminam seus contêineres. |
| Referência e plano | Snapshot de nomes IBGE 2022 e projeção diária de nascimentos SP versionados no repositório. O TRAIN verifica o [artefato fixado 13654549483](https://api.github.com/repos/lucianox777/Jornada/actions/artifacts/13654549483), seu SHA-256 `5bdbce7ffd5fed55fc085fde42faf9793cb39e6e79c3db4c69990a818bf330d7` e a identidade do pin histórico IBGE/seed42 30k. O artefato é a origem do plano, e a coorte TRAIN é gerada pelo PREPARE. |
| Parâmetros científicos | [Configuração oficial](../config/calibration/v9-synthetic-sampling.json) com 100 mil identidades-base por coorte, perfil `clean`, partição interna 60/20/20. TRAIN usa seed `42`, avalia 324 hipóteses TF4 com 81 opções de TF e paralelismo automático `0`. VALIDATION usa seed `20261112001`. O TEST recebe uma seed inédita reservada por UUIDv4 e usa a partição TEST da coorte própria, com 20 repetições. |
| Artefatos e espaço | Handoffs transferem ZIP/tar.gz, manifestos e hashes vinculados a SHA, run e tentativa, conforme cada contrato. Os jobs verificam RAM, cgroup e disco antes da execução física. Separe a persistência da evidência das áreas temporárias do SQL e do corpus em `Solution/.local`. |

No TRAIN e na VALIDATION, a etapa de carga do IBGE importa as frequências de nomes do snapshot congelado no SQL descartável, confere os hashes e registra os metadados históricos da referência de 30 mil. O TRAIN restaura a coorte gerada pelo PREPARE na etapa `Verify PREPARE transfer and restore immutable TRAIN inputs`, com o tamanho definido no despacho.

### Diagnóstico preparatório

1. Fixe o HEAD da `master` e confira a CI de push, o classificador seed42, a proteção da branch e os gates específicos aplicáveis. Use a [matriz P0-A](Registro_Operacional_P0_Recuperacao_NuGet_Locks_20261120.md#matriz-de-aceite-da-p0-a-em-21112026) para localizar as evidências de LOCKED e DT-10.
2. Confira a integridade da referência e do artefato fixado. O job `deterministic-build` executa `--v9-startup-log-probe <diretório-novo>` no Worker compilado e verifica a saída `[V9 START]` e `startup-progress.jsonl`. Esse diagnóstico inicia somente a CLI. O smoke entre jobs ensaia o handoff e o fechamento com recibos sintéticos, e o smoke SQL dedicado verifica o mesmo arquivo do vencedor no loader fixado e SQL isolado. [Startup e build](https://github.com/lucianox777/Jornada/actions/runs/44913424166/job/136873908306) · [Handoff](https://github.com/lucianox777/Jornada/actions/runs/44880840021) · [SQL isolado](https://github.com/lucianox777/Jornada/actions/runs/44903776461)
3. Confirme o último HEAD validado da `master`, os checks no SHA, a ausência de bloqueios e o preflight efetivo. A [autorização dinâmica do PO](Decisao_GOV_V9_Autorizacao_Condicional_SHA_Final_20261121.md#autorização-dinâmica-vigente) cobre esse SHA dentro do escopo aprovado. O despacho é manual pelo proprietário, tentativa `1`, com `expected_sha` igual ao `GITHUB_SHA` e `security_preflight_approved=true` após a atestação formal. O workflow verifica os limites reais de recursos.

### Despacho autorizado e ponto de entrada

Na conta proprietária, defina `sha_master_aprovada` com os 40 caracteres do HEAD validado imediatamente antes do despacho. Registre SHA, gates e preflight no recibo operacional do GitHub. Após atestar o preflight, use o comando abaixo ou os mesmos campos do formulário **Run workflow**.

```bash
gh workflow run v9-standard-linear-100k.yml \
  --repo lucianox777/Jornada --ref master \
  -f expected_sha="$sha_master_aprovada" \
  -f security_preflight_approved=true \
  -f disable_python_sql_budget=true
```

Mantenha `synthetic_population` vazio para a população oficial e `test_database` vazio para IBGE congelado. O adaptador estatístico externo está adiado na [#1626](https://github.com/lucianox777/Jornada/issues/1626). Qualquer nome de banco fornecido interrompe PREPARE antes da geração. O default `disable_python_sql_budget=true` usa o orçamento nativo do SQL e os guardas de capacidade, com escalonamento C#.

O comando executado pelo job TRAIN, a partir de `Solution/`, recebe o plano vinculado à coorte, o CSV com `generation-manifest.json` adjacente e o diretório novo de resultados. O workflow carrega a referência IBGE e configura a conexão descartável antes de invocá-lo.

```bash
dotnet run --no-build -c Release --project src/Jornada.Linkage.Parameters.Worker -- \
  --v9-calibrate "$root/bound/plan.json" "$root/people-100k/observacoes.csv" "$root/results"
```

### Resultados e recuperação

O [run #44970012048](https://github.com/lucianox777/Jornada/actions/runs/44970012048) concluiu ADMISSION, PREPARE, TRAIN/AUDIT de 324 hipóteses, VALIDATION de três finalistas, TEST independente e CLOSEOUT `VERIFIED`. [Resultado e recibos](V9_Standard_Linear_100k_Resultado_Run_44970012048.md). A trilha de evidências usa o SHA executado, o número do run e a tentativa.

| Etapa | Evidência de conclusão |
| --- | --- |
| ADMISSION → PREPARE | Autorização do proprietário para o SHA exato e recibo do pacote TRAIN sintético com hashes e proveniência. |
| TRAIN → AUDIT | Medição e recibos das 324 hipóteses, hashes de entradas e saídas e auditoria dos finalistas congelados. Quando não houver finalistas elegíveis, o AUDIT produz recibo explícito e o CLOSEOUT pode terminar como `COMPLETED_WITHOUT_ELIGIBLE_FINALISTS`. |
| VALIDATION → TEST | Havendo finalistas, VALIDATION decide e emite o único `<sha>.operational.json` quando há vencedor. Esse arquivo vincula a projeção, o modelo TF4/81 e a materialização byte a byte no SQL descartável. Com vencedor, o TEST é obrigatório e gera `independent-test.json` e `SHA256SUMS` com métricas agregadas, seed, hashes da coorte, decisões e vencedor. O relatório preserva o vencedor em caso de falha ou ausência de evidência. Sem vencedor, nenhuma seed TEST é reservada. |
| CLOSEOUT | Reconciliar `e2e-status.json`, o relatório comparativo e os recibos de cada job. Conferir SHA, run, tentativa, hashes, estado da VALIDATION e `independentTestReportStatus`. Um recibo TEST `VERIFIED` comprova integridade do contrato. Qualquer decisão de produção pertence ao operador MASTER, sob DT-15. |

O progresso C# fica no log do job e em `startup-progress.jsonl`, `batch-progress.jsonl`, `memory-progress.jsonl` e no orçamento de recursos. O limite é o timeout nativo do runner de cada job. A seção de evidência de falha do TRAIN recolhe os arquivos disponíveis, incluindo `interrupted.json` e `calibrador.log` quando produzidos. Para timeout ou falha técnica, confira o primeiro step falho, a etapa C# e a integridade dos handoffs. Preserve os recibos e descarte os ambientes efêmeros. Eventual nova campanha usa novo despacho autorizado, diretórios novos e nova reserva TEST, mantendo a reserva anterior como trilha. Consulte o [procedimento de admissão do TEST](V9_TEST_Fisico_Preflight_Admissao_20261118.md) para a evidência histórica de regressão.

## 1. Princípio de operação

A Solution **não contém scheduler próprio**. O agendamento, a recorrência e o encadeamento de jobs devem ser configurados no mecanismo corporativo homologado pelo DESENVOLVEDOR (SQL Server Agent, Control-M, Kubernetes CronJob/Job ou equivalente aprovado no ambiente).

`Jornada.Pipeline.Coordination` permanece **biblioteca**, não executável. Processor, Parameters Worker e Linkage Runner adquirem seus próprios application locks SQL. Isso preserva a execução manual/run-once em HML e recuperação de incidente sem depender de um coordenador central.

O `Jornada.Operations.Maintenance.Worker` inclui, na v3.53, um **watchdog somente observacional**. Ele detecta sinais de estagnação e emite logs estruturados; não agenda jobs, não altera status, não libera locks, não mata sessões e não executa recuperação automática.

## 2. Processos e responsabilidade do scheduler

| Processo | Forma operacional | Scheduler corporativo |
|---|---|---|
| `Jornada.Api` | serviço contínuo | manter disponibilidade conforme padrão de hospedagem |
| `Jornada.Processor.Worker` | serviço contínuo | manter disponibilidade; recuperação de lease é interna |
| `Jornada.Operations.Maintenance.Worker` | serviço contínuo | manter disponibilidade; watchdog/retencões obedecem `Enabled` |
| `Jornada.Bronze.Maintenance.Worker` | serviço contínuo/periódico | manter conforme política homologada |
| `Jornada.Linkage.Parameters.Worker` | run-once ou periódico | Fluxo normal de calibração: `GENERATE_DRAFT` → revisão do operador → `VALIDATE` → `ACTIVATE`; a conferência independente é acionada pelos eventos da seção 3.3, ressalvado o gate de promoção ainda implementado |
| `Jornada.Linkage.Runner` | run-once | disparar `INCREMENTAL`, `REPLAY`, `FULL` ou `MODEL_VALIDATION` conforme procedimento |
| `Jornada.Bronze.Verify` | run-once | executar após restore/drill ou verificação operacional programada |

### 2.1 Execução finita explícita dos workers (F1.2, DEV)

Os três workers residentes mantêm o modo **contínuo por padrão**. Quando for necessário executar um ciclo finito, o scheduler/operador fornece o parâmetro de configuração indicado abaixo; `Operation` especifica uma ação, não o modo de hospedagem.

| Worker | Modo finito | Duração máxima configurável | Resultado |
|---|---|---|---|
| Processor | `Processor__RunOnce=true` | `Processor__RunOnceMaxSeconds=300` | processa lotes até ficar ocioso, respeitando `Processor__TargetEntregaId` opcional |
| Operations Maintenance | `MaintenanceExecution__RunOnce=true` | `MaintenanceExecution__RunOnceMaxSeconds=300` | executa somente ciclos habilitados de retenção/watchdog |
| Bronze Maintenance | `BronzeMaintenance__RunOnce=true` | `BronzeMaintenance__RunOnceMaxSeconds=300` | executa somente o ciclo Bronze habilitado |

O retorno **0 (OK)** pressupõe ciclo concluído. Quando o limite é atingido antes da conclusão, retorna **3 (INCOMPLETE)**; falhas não devem virar sucesso. Todos os limites precisam ser positivos. O Processor conserva temporariamente os aliases `Processor__Operation=PROCESS_ONE` (restrito a Development) e `Processor__Operation=PROCESS_UNTIL_IDLE`, emitindo aviso de depreciação; novos scripts devem usar `Processor__RunOnce=true`. Um timeout não implica que transações SQL/infraestrutura externas foram forçadas a abortar: conferir logs/estado e reexecutar por critérios de idempotência, nunca reiniciar ou resetar `JornadaLocal` por conta própria.

A configuração de `scheduler-jobs.json` para descoberta dos modos suportados pertence à F1.3 e ainda não é parte deste aceite F1.2. Nada nesta seção autoriza HML/PROD sem governança institucional e parametrização operacional.

## 3. Ordem e precondições

### 3.1 Carga inicial

1. Manter API e Processor operando normalmente para formar a primeira Gold elegível.
2. Acompanhar o backlog e o throughput por meio das métricas de ingestão e dos lotes pendentes.
3. Executar `GENERATE_DRAFT` após existir corpus suficiente. O Parameters Worker obtém janela exclusiva do corpus pela coordenação SQL.
4. O operador revisa o resultado do `GENERATE_DRAFT`, incluindo os diagnósticos, thresholds e budgets aplicáveis, e registra sua decisão de prosseguir ou corrigir o rascunho.
5. Validar o modelo (`VALIDATE`) após a revisão do operador, respeitando os gates efetivamente implementados descritos abaixo. **Direção DT-15, parcialmente implementada em DEV:** já existe prévia master independente do Monitor e somente leitura; a revisão decisória completa em HML/Produção ainda requer dossiê pareado ATIVO × RASCUNHO no mesmo corpus, diferenças de decisões, justificativa vinculada ao fingerprint e gates humanos. O histórico agregado permanece apenas contexto.
6. Ativar (`ACTIVATE`) somente a versão aprovada.

**Proteção operacional parcial DT-15:** o wrapper Windows agora executa `GENERATE_DRAFT` e a conferência governada, **encerra em RASCUNHO sem promover** e detecta troca do ATIVO-base. O Worker registra um comparativo limitado a recall/redução dos passes de blocking ATIVO×RASCUNHO **na mesma amostra de treino**; esse diagnóstico não é parecer decisório ou replay FS. **Replay parcial adicional:** o comando `Jornada.Linkage.Evaluation --dt15-compare-synthetic` [documentado aqui](DT15_Replay_FS_Pareado_Sintetico.md) mede, somente em DEV e sem publicação, as decisões FS agregadas dos dois modelos no mesmo corpus sintético com hashes/partições iguais. Ainda **faltam** a matriz por observação, custo SQL, dados representativos, o dossiê de decisão completo, a página master de aprovação HML/Produção e os gates humanos nos caminhos diretos `VALIDATE`/`ACTIVATE`. Até sua entrega, seguir rito institucional externo, sem anunciar a DT-15 como concluída.
7. Executar Linkage Runner incremental sobre observações elegíveis sem CPF, respeitando sua janela exclusiva de coordenação SQL.

### 3.2 Operação normal

- API e Processor podem permanecer contínuos.
- `GENERATE_DRAFT` e Linkage Runner obtêm janela exclusiva do corpus. O Processor termina o lote corrente e não inicia outro enquanto a janela exclusiva estiver declarada.
- O scheduler **não deve** executar `GENERATE_DRAFT` e Linkage Runner concorrentes entre si. A coordenação SQL falha fechado mesmo se houver disparo indevido, mas a política operacional deve evitar tentativas desnecessárias.
- O procedimento operacional **pretendido** para cada calibração sem gatilhos de reconferência é `GENERATE_DRAFT` → operador revisa o resultado → `VALIDATE` → `ACTIVATE`. A conferência decimal × float64 não é uma etapa recorrente desse procedimento.
- **Limitação da implementação atual (DT-14 apenas documental):** `VALIDATE` e `ACTIVATE` ainda não exigem congelamento do corpus, mas **exigem** tolerância `FROZEN` e evidência governada mais recente `CONFORME` **do mesmo modelo e fingerprint**. A configuração técnica corrente é `V1_2026-09-26` (LLR 0,01). Sem essa evidência e sem orçamento FP validado, a promoção é bloqueada. A evidência artesanal do PR #514 não substitui a evidência persistida por modelo; não contornar o gate. A remoção da exigência *por rascunho*, necessária para tornar o novo fluxo operacional inteiramente executável sem conferência, requer alteração posterior de código fora do escopo de DT-14. Nenhuma dessas verificações certifica representatividade (#31).
- `INCREMENTAL` cobre observações sem CPF ainda pendentes **e também reavalia `NAO_RESOLVIDO`/`CONFLITO` correntes**, porque o universo candidato pode mudar mesmo quando a observação não muda. Isso evita depender de uma nova versão da origem para descobrir evidência surgida do lado candidato.
- `FULL` e `REPLAY` permanecem operações excepcionais e devem registrar `--requested-by`, `--reason` e, quando aplicável, `--correlation-id`. `REPLAY` não deve ser transformado em rotina apenas para compensar mudança do lado candidato.

### 3.3 Quando executar `Jornada.Linkage.Conference`

A ferramenta **standalone continua disponível**; DT-14 retira a conferência C# `decimal` × C# `float64` do **ciclo normal de calibração**, não remove a ferramenta nem desativa os gates atualmente implementados. Executar uma nova conferência da implementação quando ocorrer qualquer um dos eventos abaixo:

1. Qualquer alteração em `FellegiSunterScoring.cs` **ou nos comparadores**. A conferência corrente recebe estados de comparação pré-computados e **não** valida a formação desses estados; mudanças nos comparadores exigem também evidências/testes próprios do comparador.
2. Migração da versão do runtime **.NET**. A **DT-02 (migração para .NET 10)** já foi executada e reconferida; qualquer migração futura de runtime volta a exigir esta conferência.
3. Um **threshold mudar de faixa significativa** entre modelos consecutivos, conforme a política de decisão vigente; registrar no processo de revisão qual faixa e qual mudança motivaram a reconferência, sem inventar um limite numérico novo.

A evidência `CONFORME` dirigida do [PR #514](https://github.com/lucianox777/Jornada/pull/514) (casos artesanais de fronteira) continua válida **somente em seu escopo** enquanto scorer, thresholds e runtime permanecerem inalterados. Não é necessário repetir esses testes a cada `GENERATE_DRAFT`; também não constituem conferência de comparadores, evidência SQL para um novo modelo nem validação estatística representativa (#31). Ver `Linkage_Implementation_Conference.md`.

## 4. Exemplos de comandos run-once

Os caminhos exatos de publicação pertencem ao ambiente. Exemplos conceituais, executados no diretório publicado do componente:

```bash
# Linkage incremental com modelo ATIVO
dotnet Jornada.Linkage.Runner.dll --mode INCREMENTAL --batch-size 20000 --max-parallelism 4 --requested-by "SCHEDULER" --reason "rotina incremental"

# Replay controlado de uma versão específica
dotnet Jornada.Linkage.Runner.dll --mode REPLAY --model-version 12 --gestor SMADS --requested-by "OPERACAO" --reason "reprocessamento autorizado"

# Validação sem publicação
dotnet Jornada.Linkage.Runner.dll --mode MODEL_VALIDATION --model-version 13 --max-records 100000 --publish false --requested-by "HML"
```

O Parameters Worker usa configuração (`LinkageParameters:Operation`) e, para `VALIDATE`/`ACTIVATE`, exige `LinkageParameters:TargetVersion` e o contrato indicado por `LinkageParameters:ConferenceToleranceConfigPath`. O **fluxo normal de referência** é `GENERATE_DRAFT` → operador revisa o resultado → `VALIDATE` → `ACTIVATE`; `Jornada.Linkage.Conference` é executada nos casos da seção 3.3, não a cada geração de rascunho. **A implementação atual ainda contém o assert de conferência `CONFORME` vinculada ao snapshot de cada modelo**, de modo que a execução efetiva permanece bloqueada se essa evidência faltar. O contrato corrente de tolerância está `FROZEN` (`V1_2026-09-26`); seu congelamento, isoladamente, não libera promoção.

## 5. Contrato mínimo do scheduler corporativo

O job configurado pelo DESENVOLVEDOR deve registrar, no mínimo:

- componente/comando e argumentos;
- instante de início e término;
- exit code do processo;
- stdout/stderr ou referência ao log centralizado;
- identidade técnica que disparou a execução;
- número máximo de tentativas e política de retry definida externamente;
- regra para não iniciar nova ocorrência enquanto a anterior do mesmo job ainda estiver ativa;
- janela/cadência homologada;
- escalonamento para operação quando houver falha repetida.

O scheduler não deve inferir sucesso apenas por tempo decorrido. Para Linkage, o estado persistido em `identidade.linkage_run`/`identidade.modelo_linkage` e os logs do processo são a evidência operacional.

## 6. Watchdog v3.53

Configuração em `Jornada.Operations.Maintenance.Worker/appsettings.json`:

```json
"PipelineWatchdog": {
  "Enabled": false,
  "IntervalMinutes": 5,
  "LinkageRunMaxMinutes": 120,
  "ModelGenerationMaxMinutes": 120,
  "ExpiredLeaseGraceMinutes": 5,
  "PendingBacklogMaxAgeMinutes": 60
}
```

Os valores são defaults técnicos e **não são política de Produção**. Devem ser homologados em HML.

Códigos de alerta estruturado:

- `LINKAGE_RUN_STALE` - run em `PREPARANDO/EXECUTANDO` além do limite;
- `LINKAGE_MODEL_GENERATION_STALE` - modelo em `GERANDO` além do limite;
- `PROCESSOR_LEASE_EXPIRED` - lote `VALIDANDO/PROCESSANDO` com lease expirado além da tolerância;
- `PROCESSOR_BACKLOG_OLD` - lote `PENDENTE` mais antigo acima da idade configurada;

O watchdog **não tenta detectar “application lock órfão”**. Locks `Session` são liberados pelo SQL Server quando a sessão física termina; se a sessão continuar viva, o lock tem proprietário. O watchdog observa os estados de negócio/execução persistidos.

## 7. Resposta a alertas

1. Confirmar o alerta com as consultas de `database/Jornada_HML_Observabilidade.sql`.
2. Verificar logs do processo e do scheduler corporativo.
3. Não alterar manualmente `linkage_run`, `modelo_linkage` ou lease de lote sem procedimento de recuperação aprovado.
4. Para lease expirado de lote, confirmar se o Processor está ativo; a recuperação periódica/fencing já pertence ao Processor.
5. Para run/modelo estagnado, investigar o processo que o iniciou e decidir cancelamento/novo disparo conforme rito operacional. O watchdog não toma essa decisão.
6. Para backlog antigo, distinguir indisponibilidade do Processor, janela exclusiva longa e volume acima da capacidade homologada.

## 8. Power BI

Os fontes `bi/Jornada.pbip`, `bi/Jornada.Report/` (PBIR) e `bi/Jornada.SemanticModel/` (TMDL) devem ser **abertos, validados e mantidos com Microsoft Power BI Desktop na versão homologada pelo ambiente municipal**.

Validação estrutural de JSON/TMDL em CI não substitui abrir e salvar o projeto no Power BI Desktop. A publicação no Power BI Service/Gateway, credenciais, refresh e homologação visual pertencem ao ambiente corporativo.

## 9. Ensaio anterior à HML

A próxima etapa é o **Ensaio com uma ou várias Secretarias participantes**, possivelmente em ondas, com dados preparados e fornecidos por elas nos respectivos contratos vigentes; a responsabilidade pela anonimização ou geração sintética derivada de bases reais é de cada Secretaria. O Ensaio deve usar os mesmos componentes, configuração funcional, contratos, integração de autenticação, autorização, scheduler, gates e procedimentos de HML; a única diferença funcional planejada são os dados. Credenciais e endpoints próprios de cada ambiente não autorizam controles diferentes. O roteiro e os critérios de paridade estão em `Ensaio_Secretarias_Paridade_HML.md`.

As evidências do Ensaio devem ser preservadas e avaliadas por Secretaria, contrato, estrato e onda. Podem constituir evidência forte de operação, desempenho e qualidade estatística do linkage, inclusive para a #31, quando a fidelidade dos dados, a verdade de referência, a amostragem e a incerteza sustentarem as conclusões. A mudança de massa em HML exige análise de transportabilidade e eventual medição complementar, não repetição automática de estudos válidos. Se a identidade corporativa permanecer pendente, não declarar Ensaio equivalente a HML com credenciais sintéticas de Development.

## 10. Evidências mínimas de HML

Antes de Produção, registrar:

- P95/P99 de duração de lote do Processor e tempo de drain;
- duração de `GENERATE_DRAFT`, validação, ativação e runs de linkage;
- backlog Bronze/lotes durante janelas exclusivas;
- teste de perda da sessão coordenadora com cancelamento fail-closed;
- teste do watchdog com estados sintéticos/temporários controlados, confirmando **somente alerta**;
- restore SQL + storage Bronze e execução de `Jornada.Bronze.Verify`;
- abertura e salvamento do PBIP no Power BI Desktop homologado e validação das 22 páginas;
- configuração final do scheduler corporativo, com evidência de jobs, cadências, retries e logs.


## 11. Separação entre desenvolvimento local e scheduler corporativo

O `docker-compose.yml` existe somente para desenvolvimento/teste local e sobe SQL Server Developer. Ele **não** implementa scheduler e não representa topologia de HML/Produção. A política desta página continua válida: jobs run-once são acionados pelo scheduler corporativo homologado no DESENVOLVEDOR. Para o ambiente local, consulte `Runbook_Desenvolvimento_Local.md`.


## Ensaios antes de HML

Os ensaios locais de escala, perda de coordenação e restore estão em `Runbook_Testes_Tecnicos.md`. Eles devem ser usados como regressão técnica antes de promover mudanças no pipeline, mas não substituem testes de capacidade, backup/DR e scheduler no ambiente corporativo.


## Novos modelos V8 — ausência neutra (28/09/2026)

Novos RASCUNHOS usam `FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8`. Nome, nome da mãe e nascimento ausentes são indisponíveis (LLR zero); a ausência materna permanece auditável nos suportes `SUPPORT_M_NOME_MAE_MISSING` e `SUPPORT_U_NOME_MAE_MISSING`, sem probabilidades m/u na V8. A V6 ATIVA não é alterada. Aplicar `20260928_Linkage_Neutral_Missing_V8.sql` antes de gerar ou promover V8. Como o scorer mudou, reexecutar a conferência governada para o novo modelo e manter VALIDATE/ACTIVATE fail-closed. Plano de comparação por estrato: `Linkage_Ausencia_Neutra_V8.md`.

## Alerta de implantação — DT-10 V9 inferior (28/10/2026; PR DRAFT)

**NÃO HABILITAR** `identidade.v9_dt10_lower_policy`, DT-15 para V9 nem emissão operacional de `LIMIAR_INFERIOR_V9_GOVERNADO` com base em CI verde ou ensaio sintético. A migration da PR #1199 continua em revisão; instala contrato **default OFF**, não publicação funcional certificada. O limiar `0,05` é candidato do bootstrap, não configuração permanente.

Antes de qualquer promoção, exigir prova positiva e negativa no LocalDB isolado, autorização da política por digest DT-15, revisão da integração do Calibrador C# único com REF, TEST independente novo e decisão institucional. No modo legado `SEM_CANDIDATO_%`, qualquer `melhor_candidato_uuid`, `segundo_candidato_uuid` ou UUID resolvido na observação líder deve impedir evento de nova identidade (`THROW 51835` proposto pela PR #1199). A ausência desses campos não substitui a prova da busca completa. Para incidentes, registrar SHA, política, run e observação sem alterar Gold, REF ativa, V8, bases reais ou produzir identidades por replay manual. O rollback do código **não** reverte identidades já persistidas.

## Adendo de rastreabilidade — 28/10/2026, correção de preflight e vínculo estrutural

Em caso de falha do preflight `V9GoldDisposableLowerProcedurePatch.Apply`, conferir a assinatura exata do motivo `SEM[_]CANDIDATO[_]%` na migration canônica e no patch **somente de LocalDB efêmero**; nunca ampliar o reconhecimento para motivos arbitrários nem executar patch em base real. Após mudança de DDL, medir clean/upgrade/flattened no SQL Server, verificar os três SHA iguais e atualizar `CANDIDATE_INFO.json` com o valor medido (run [40659908716](https://github.com/lucianox777/Jornada/actions/runs/40659908716): `8fe1db4da72aa420f3741c625270e5a6cd9cee2217ec9f60effbee8466a08a21`). Exigir nova CI completa no SHA final; preservar probe TRAIN já iniciado, política OFF, dados operacionais e TEST lacrado.

## Adendo de manutenção DT-10 — SEM_CANDIDATO literal (29/10/2026)

Na atualização da #1210, instalar `20261029_Linkage_NoCandidate_Literal_Guards.sql` **depois das migrations históricas de publicação escalar/lote**. Ao reconciliar a #1199, instalar a migration governada `20261026_DT10_V9_Lower_Governed_Off.sql` **depois** da #1210, independentemente da ordenação lexical das datas; ela redefine a procedure de lote e deve preservar digest/autorização DT-15 e as guardas 51810/51835. Não reescrever migrations históricas nem ativar V9.

Antes de merge, validar banco descartável por instalação limpa e upgrade, manifesto normativo, DDL consolidado/flattened e `CANDIDATE_INFO.json` (fingerprints medidos do HEAD final). Executar `LinkageNoCandidateLiteralGuardsSqlServerTests` no mesmo SHA, exigindo SQL real e TRX sem skip, com quase-prefixos, candidato persistido, lote misto atômico e replay. Em falha, manter a publicação fechada, registrar número SQL, SHA e job; não corrigir via desligamento de constraints. Rollback requer plano de DDL aditivo com prova de preservação de contratos, não restauração automática de definição histórica sobre a #1199. Nenhuma operação em HML/PROD é autorizada por este adendo. Detalhes: `DT10_NoCandidate_Literal_Physical_20261029.md`.


## Checkpoint DT-10 de integração — 30/10/2026 (PR #1199, sem ativação)

**Ordem e rollback.** A migration `20261029_Linkage_NoCandidate_Literal_Guards.sql` (#1210) precede `20261026_DT10_V9_Lower_Governed_Off.sql` (#1199) no manifesto e no instalador. A procedure de lote governada deve preservar o prefixo literal `SEM[_]CANDIDATO[_]%` e o erro 51835. O rollback controlado restaura a procedure **endurecida após a #1210**, nunca a definição histórica anterior. Qualquer formulação antiga de “procedure anterior” refere-se à versão endurecida imediatamente anterior à redefinição governada.

**Decisão multiobservação.** Para a mesma origem no mesmo run, a líder não pode publicar `SEM_CANDIDATO_*` se qualquer observação irmã materializada tiver `melhor_candidato_uuid`, `segundo_candidato_uuid` ou `pessoa_uuid_resolvido`. A guarda SQL percorre `#dt10_rows` por `pessoa_origem_id` e deve abortar o lote antes de qualquer evento, inclusive em lote misto; retry não pode produzir ledger. Isso não prova que o produtor executou todos os passes físicos de blocking.

**Evidência no SHA auditado `28125456ab349a865a52dc66f6d1e15234dbfa21`.** [Preflight LocalDB #41098709724](https://github.com/lucianox777/Jornada/actions/runs/41098709724): 1 teste NUnit PASS, 0 FAIL, 0 SKIP, TRX validado; cobre recusas OFF, candidato forjado na líder e em observação irmã, atomicidade mista e replay legado. [CI #41098709798](https://github.com/lucianox777/Jornada/actions/runs/41098709798), [schema #41098709771](https://github.com/lucianox777/Jornada/actions/runs/41098709771), [instalador #41098709760](https://github.com/lucianox777/Jornada/actions/runs/41098709760) e [validação independente #41098709936](https://github.com/lucianox777/Jornada/actions/runs/41098709936) concluíram SUCCESS. O candidato declara fingerprint estrutural `8b9adae24654e94bc668385ca9aa5c6c906744c0d8d88bd5b457f7ae9caa1f68` e SHA de manifest `c1d7d37f505563baea5d71d310e996945f5ffbf59d34fa6e24defbed3252e4b8`. [Holdout #41098709711](https://github.com/lucianox777/Jornada/actions/runs/41098709711): workflow SUCCESS, corridas físicas e agregador **SKIPPED**; não prova TEST selado. [Probe #41098709730](https://github.com/lucianox777/Jornada/actions/runs/41098709730): SUCCESS, job TRAIN 30k **SKIPPED**; não é novo ensaio de calibração.

**Bloqueios para merge.** Falta prova SQL **positiva** de autorização DT-15 ACTIVATE/digest legítimos (LOWER/intermediário/SEM_CANDIDATO/replay), negativos de adulteração individual de digest/fit/snapshot/limiares, atestado verificável de completude da busca física, contrato `upper` produzido pelo calibrador V9 e `lower` otimizado condicionado ao `upper`, revisão REF/loader #1089, VALIDATION/TEST selados e revisão humana. A PR segue DRAFT, política default OFF, sem aprovação, merge ou ativação. Revalidar todos os checks no SHA final após reconciliação com master. Os resultados históricos acima não certificam commits posteriores.

**Runbook vigente:** validar clean/upgrade/flattened e rollback em banco descartável, incluindo a procedure instalada e a ordem #1210→#1199. Em 51835, preservar run, origem, observações irmãs e evidência de candidato; nunca forçar motivo SEM_CANDIDATO nem apagar ledger. Não operar HML/PROD.


## Preparação documental da seleção V9 pelo Operador Master — P0-B etapa 5 (22/11/2026)

**Estado:** procedimento de preparação estático, sem habilitação do Runner, seleção operacional real, validação DT-15 ou ativação. **Origem/autorização:** plano de E2E integral GOV-DEV-E2E-INTEGRAL-V9-PLANO-20261122, etapa 5 (dependente da preservação/proveniência da etapa 0); responsabilidade exclusiva do contrato Calibrador→Runner e seleção MASTER na issue #1488. **Precedência:** GOV-RUNNER-STARTUP-MODELO-OPERADOR-MASTER-20261117, GOV-V9-RUNNER-ARTEFATO-ARQUIVO-FONTE-UNICA-20261117, GOV-V9-TEST-ISOLAMENTO-RETROCOMPAT-20261117, DT-15 e GOV-DOC-01. Não altera qualquer decisão científica, algoritmo ou gate.

### Contrato já presente no código (não é certificação)

1. O Operador Master decide o modelo **com o Runner parado**. Para uma futura escolha V9, a configuração congelada de startup deve conter `Linkage:SelectedModelVersion=9` e um caminho local `Linkage:V9SelectionFile`; `--model-version=9` é opcional e, se informado, precisa ser coerente. Nunca usar `--model-version` para substituir a escolha administrativa, configurar hot reload, ou ativar por arquivo sem aprovação.
2. O arquivo de seleção é JSON de **envelope**, não o modelo científico. Deve conter somente os campos tipados `ArtifactPath` e `WinnerSha256`. Exemplo **ilustrativo não executável** (substituir os marcadores somente após verificação e guarda institucional dos bytes):

   ```json
   {
     "ArtifactPath": "<sha256-do-vencedor>.operational.json",
     "WinnerSha256": "<sha256-do-vencedor-com-64-hex-minusculos>"
   }
   ```

3. `V9ConfiguredArtifactLoader.LoadAsync` resolve `ArtifactPath` **em relação à pasta do envelope** quando relativo. Exige envelope existente, não symlink, entre 1 e 65536 bytes, SHA com 64 caracteres hexadecimais minúsculos, artefato existente e não symlink, nome exatamente `<WinnerSha256>.operational.json`, leitura pinada e coincidência do SHA dos bytes internos. É obrigatório preservar o arquivo imutável, seus recibos/proveniência e acesso restrito. A presença do ZIP no GitHub não prova que seus bytes foram copiados ou arquivados no NAS; consultar etapa 0/#1830 e #1913.
4. Para a campanha Standard Linear #38, o identificador auditável do vencedor é `6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee`, vinculado ao run `44970012048` (attempt 1), SHA do código `30c71a0db1bdeac6ba536db5e428ade4d5c70c9c`, e ao artefato Actions `14742452561`. Esse valor identifica o **conteúdo operacional**; **não** substituí-lo pelo SHA-256 do ZIP baixável (SHA de recipiente distinto), nem preencher o envelope sem confirmar o conteúdo preservado. A cópia institucional e a conferência física continuam pendentes.
5. Em `Program.cs`, presença de `V9SelectionFile` com versão diferente de 9 (ou argumento divergente) encerra com `RUNNER_V9_SELECTION_VERSION_MISMATCH` antes da leitura. Versão 9 sem envelope encerra com `RUNNER_V9_SELECTION_MISSING`, **sem fallback** para parâmetros científicos SQL/legado. Quando o arquivo é válido, o runtime V9 é preparado em memória, porém `V9OperationalRunBoundary.EnsureProductiveCertified()` ainda encerra propositalmente com `RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED`. Esse bloqueio **é obrigatório**, não é incidente a resolver por contorno, e não pode ser removido por este procedimento.

### Checklist de admissibilidade e aceite futuro (não executar automaticamente)

- **Proveniência (etapa 0):** comprovar preservação do vencedor e do TEST, hash dos bytes, origem, run, tentativa, identidade do artefato e controle de acesso; distinguir cópia local verificada de arquivo institucional NAS certificado.
- **Segurança contratual (#1488):** demonstrar envelope, pacote único vencedor, SHA, esquema/versão, integridade TF4/81 células, frequências, blocking, parâmetros, margens e rejeições negativas sem defaults/fallback; nenhuma consulta SQL de parâmetros científicos V9.
- **Equivalência operacional (#1584; etapas 1–4):** exigir oráculo independente, grafo SQL real, paridade por observação e testes não publicantes no ambiente DEV descartável; a preparação do envelope não certifica Runner nem autoriza 10k/100k.
- **Governança e operação (DT-15):** registrar identidade do Operador Master, decisão explícita, motivo, fingerprint e autorização da seleção, rollback/compatibilidade e evidências de CI no SHA integrado; não importar, sincronizar, publicar ou ativar modelos automaticamente.
- **Gate de liberação:** somente revisão técnica, decisão do PO dentro do escopo, checks obrigatórios e comprovação de Runner V9 preparado permitirão propor a etapa 6 (habilitação DEV governada). HML/PROD/ativação exigem autorizações separadas. REPLAY histórico DT-05 conserva a referência do source run e não herda a seleção atual.

**Rastreabilidade:** plano aprovado `Solution/docs/Decisao_GOV_DEV_E2E_Integral_V9_Plano_Aprovado_20261122.md` (etapas 0/5/6), issues #1488, #1584, #1851 e #1830; `Solution/src/Jornada.Linkage.Runner/Program.cs`, `V9ConfiguredArtifactLoader.cs` e `V9OperationalRunBoundary.cs`; PR #1913 (etapa 0). **Estado desta entrega: documentação de preparação na PR, não validação nem ativação.**

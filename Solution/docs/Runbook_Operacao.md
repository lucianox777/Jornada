# Jornada - Runbook operacional da Fase 1 (v3.55)

## 1. Princípio de operação

A Solution **não contém scheduler próprio**. O agendamento, a recorrência e o encadeamento de jobs devem ser configurados no mecanismo corporativo homologado pela PRODAM (SQL Server Agent, Control-M, Kubernetes CronJob/Job ou equivalente aprovado no ambiente).

`Jornada.Pipeline.Coordination` permanece **biblioteca**, não executável. Processor, Parameters Worker e Linkage Runner adquirem seus próprios application locks SQL. Isso preserva a execução manual/run-once em HML e recuperação de incidente sem depender de um coordenador central.

O `Jornada.Operations.Maintenance.Worker` inclui, na v3.53, um **watchdog somente observacional**. Ele detecta sinais de estagnação e emite logs estruturados; não agenda jobs, não altera status, não libera locks, não mata sessões e não executa recuperação automática.

## 2. Processos e responsabilidade do scheduler

| Processo | Forma operacional | Scheduler corporativo |
|---|---|---|
| `Jornada.Api` | serviço contínuo | manter disponibilidade conforme padrão de hospedagem |
| `Jornada.Processor.Worker` | serviço contínuo | manter disponibilidade; recuperação de lease é interna |
| `Jornada.Operations.Maintenance.Worker` | serviço contínuo | manter disponibilidade; watchdog/retencões obedecem `Enabled` |
| `Jornada.Bronze.Maintenance.Worker` | serviço contínuo/periódico | manter conforme política homologada |
| `Jornada.Linkage.Parameters.Worker` | run-once ou periódico | disparar `GENERATE_DRAFT`, `VALIDATE` e `ACTIVATE` conforme rito aprovado |
| `Jornada.Linkage.Runner` | run-once | disparar `INCREMENTAL`, `REPLAY`, `FULL` ou `MODEL_VALIDATION` conforme procedimento |
| `Jornada.Bronze.Verify` | run-once | executar após restore/drill ou verificação operacional programada |

## 3. Ordem e precondições

### 3.1 Carga inicial

1. Ativar `controle.modo_carga_inicial` pelo procedimento administrativo/SQL aprovado.
2. Manter API e Processor operando normalmente para formar a primeira Gold elegível.
3. Não executar `GENERATE_DRAFT` nem Linkage Runner enquanto o modo estiver ativo. O Parameters Worker e o Runner já falham/saem de forma segura conforme sua regra de carga inicial.
4. Acompanhar `serving.v_bi_carga_inicial`, backlog e throughput.
5. Encerrar o modo somente após o critério operacional aprovado; **o watchdog não o encerra**.
6. Executar `GENERATE_DRAFT` após existir corpus suficiente.
7. Validar o modelo (`VALIDATE`) com evidência de HML.
8. Ativar (`ACTIVATE`) somente a versão aprovada.
9. Executar Linkage Runner incremental sobre observações elegíveis sem CPF.

### 3.2 Operação normal

- API e Processor podem permanecer contínuos.
- `GENERATE_DRAFT` e Linkage Runner obtêm janela exclusiva do corpus. O Processor termina o lote corrente e não inicia outro enquanto a janela exclusiva estiver declarada.
- O scheduler **não deve** executar `GENERATE_DRAFT` e Linkage Runner concorrentes entre si. A coordenação SQL falha fechado mesmo se houver disparo indevido, mas a política operacional deve evitar tentativas desnecessárias.
- `VALIDATE` e `ACTIVATE` não exigem congelamento do corpus; ainda assim devem seguir o rito de promoção e auditoria.
- `FULL` e `REPLAY` são operações excepcionais e devem registrar `--requested-by`, `--reason` e, quando aplicável, `--correlation-id`.

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

O Parameters Worker usa configuração (`LinkageParameters:Operation`) e, para `VALIDATE`/`ACTIVATE`, exige `LinkageParameters:TargetVersion`. Em HML/Produção esses valores devem ser injetados pelo mecanismo de configuração do ambiente, não alterados no código-fonte.

## 5. Contrato mínimo do scheduler corporativo

O job configurado pela PRODAM deve registrar, no mínimo:

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
  "PendingBacklogMaxAgeMinutes": 60,
  "InitialLoadMaxHours": 24
}
```

Os valores são defaults técnicos e **não são política de Produção**. Devem ser homologados em HML.

Códigos de alerta estruturado:

- `LINKAGE_RUN_STALE` - run em `PREPARANDO/EXECUTANDO` além do limite;
- `LINKAGE_MODEL_GENERATION_STALE` - modelo em `GERANDO` além do limite;
- `PROCESSOR_LEASE_EXPIRED` - lote `VALIDANDO/PROCESSANDO` com lease expirado além da tolerância;
- `PROCESSOR_BACKLOG_OLD` - lote `PENDENTE` mais antigo acima da idade configurada;
- `INITIAL_LOAD_MODE_STALE` - modo de carga inicial ativo além do limite.

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

## 9. Evidências mínimas de HML

Antes de Produção, registrar:

- P95/P99 de duração de lote do Processor e tempo de drain;
- duração de `GENERATE_DRAFT`, validação, ativação e runs de linkage;
- backlog Bronze/lotes durante janelas exclusivas;
- teste de perda da sessão coordenadora com cancelamento fail-closed;
- teste do watchdog com estados sintéticos/temporários controlados, confirmando **somente alerta**;
- restore SQL + storage Bronze e execução de `Jornada.Bronze.Verify`;
- abertura e salvamento do PBIP no Power BI Desktop homologado e validação das 22 páginas;
- configuração final do scheduler corporativo, com evidência de jobs, cadências, retries e logs.


## 10. Separação entre desenvolvimento local e scheduler corporativo

O `docker-compose.yml` existe somente para desenvolvimento/teste local e sobe SQL Server Developer. Ele **não** implementa scheduler e não representa topologia de HML/Produção. A política desta página continua válida: jobs run-once são acionados pelo scheduler corporativo homologado na PRODAM. Para o ambiente local, consulte `Runbook_Desenvolvimento_Local.md`.


## Ensaios antes de HML

Os ensaios locais de escala, perda de coordenação e restore estão em `Runbook_Testes_Tecnicos.md`. Eles devem ser usados como regressão técnica antes de promover mudanças no pipeline, mas não substituem testes de capacidade, backup/DR e scheduler no ambiente corporativo.

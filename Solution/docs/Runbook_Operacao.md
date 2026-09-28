# Jornada - Runbook operacional da Fase 1 (v3.55)

**Interpretação operacional do núcleo:** a Jornada é apoio à decisão e mantém a melhor representação disponível, não uma identidade civil certificada. Mudanças de evidência de identidade, de referências candidatas ou de modelo podem demandar reavaliação; novas entregas idempotentes ou mudanças só de atributos não identitários não devem forçar novo scoring. Quando a representação semântica não mudar, evitar nova versão operacional da decisão, mantendo a trilha de execução e o ledger de atos aplicáveis. A hierarquia pretendida da Gold é documentação mais recentemente apresentada, documentação anterior, autodeclaração mais recente e autodeclaração anterior, por atributo. **Estas são diretrizes a conferir/implementar no código atual, não procedimentos já homologados.** Ver `Diretrizes_Identidade_Progressiva_Apoio_Decisao.md`.

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
| `Jornada.Linkage.Parameters.Worker` | run-once ou periódico | Fluxo normal de calibração: `GENERATE_DRAFT` → revisão do operador → `VALIDATE` → `ACTIVATE`; a conferência independente é acionada pelos eventos da seção 3.3, ressalvado o gate de promoção ainda implementado |
| `Jornada.Linkage.Runner` | run-once | disparar `INCREMENTAL`, `REPLAY`, `FULL` ou `MODEL_VALIDATION` conforme procedimento |
| `Jornada.Bronze.Verify` | run-once | executar após restore/drill ou verificação operacional programada |

## 3. Ordem e precondições

### 3.1 Carga inicial

1. Manter API e Processor operando normalmente para formar a primeira Gold elegível.
2. Acompanhar o backlog e o throughput por meio das métricas de ingestão e dos lotes pendentes.
3. Executar `GENERATE_DRAFT` após existir corpus suficiente. O Parameters Worker obtém janela exclusiva do corpus pela coordenação SQL.
4. O operador revisa o resultado do `GENERATE_DRAFT`, incluindo os diagnósticos, thresholds e budgets aplicáveis, e registra sua decisão de prosseguir ou corrigir o rascunho.
5. Validar o modelo (`VALIDATE`) após a revisão do operador, respeitando os gates efetivamente implementados descritos abaixo. **Direção DT-15, ainda não implementada:** a revisão principal ocorrerá em [página master independente do Monitor](DT15_Governanca_Decisao_Modelo.md), com dossiê pareado ATIVO × RASCUNHO no mesmo corpus, diferenças de decisões e justificativa vinculada ao fingerprint; histórico agregado é apenas contexto.
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
2. Migração da versão do runtime **.NET**, inclusive **DT-02 (migração para .NET 10)**, a próxima execução obrigatória.
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

O `docker-compose.yml` existe somente para desenvolvimento/teste local e sobe SQL Server Developer. Ele **não** implementa scheduler e não representa topologia de HML/Produção. A política desta página continua válida: jobs run-once são acionados pelo scheduler corporativo homologado na PRODAM. Para o ambiente local, consulte `Runbook_Desenvolvimento_Local.md`.


## Ensaios antes de HML

Os ensaios locais de escala, perda de coordenação e restore estão em `Runbook_Testes_Tecnicos.md`. Eles devem ser usados como regressão técnica antes de promover mudanças no pipeline, mas não substituem testes de capacidade, backup/DR e scheduler no ambiente corporativo.


## Novos modelos V8 — ausência neutra (28/09/2026)

Novos RASCUNHOS usam `FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8`. Nome, nome da mãe e nascimento ausentes são indisponíveis (LLR zero); a ausência materna permanece auditável nos suportes `SUPPORT_M_NOME_MAE_MISSING` e `SUPPORT_U_NOME_MAE_MISSING`, sem probabilidades m/u na V8. A V6 ATIVA não é alterada. Aplicar `20260928_Linkage_Neutral_Missing_V8.sql` antes de gerar ou promover V8. Como o scorer mudou, reexecutar a conferência governada para o novo modelo e manter VALIDATE/ACTIVATE fail-closed. Plano de comparação por estrato: `Linkage_Ausencia_Neutra_V8.md`.

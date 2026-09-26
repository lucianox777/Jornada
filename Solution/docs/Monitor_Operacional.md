# Monitor Operacional da Jornada

## Objetivo

O Monitor Operacional responde **o que a Jornada está fazendo agora** e se os nós pertencem à mesma implantação técnica. Ele é uma superfície somente-leitura da aplicação e não substitui o monitoramento de infraestrutura da PRODAM.

A interface é servida pela própria `Jornada.Api` em:

```text
/monitor
```

Os dados são obtidos pela API protegida:

```text
GET /api/v1/monitor/status
```

A página atualiza o snapshot a cada 5 segundos. Em HML/Produção, a chave informada pelo operador é mantida apenas em `sessionStorage` e não é persistida pelo monitor. No perfil local `Test`, executado como `Development`, a página recebe automaticamente a credencial sintética declarada no próprio `Jornada.Cluster.Test.json`; essa credencial é colocada apenas na sessão do navegador e o snapshot continua passando pela mesma autenticação/autorização de `GET /api/v1/monitor/status`. Nenhum bypass de autorização é criado.

## O que é mostrado

O snapshot reúne:

- presença viva dos nós e dos cinco componentes residentes;
- readiness de SQL Server, Bronze, Staging e identidade/autorização corporativa;
- **saúde do bundle de configuração**;
- quantidade de lotes por estado;
- lotes em validação/processamento e o respectivo lease;
- entregas recentes;
- último ciclo de manutenção da Bronze;
- governança read-only do modelo de Linkage ATIVO, incluindo identidade/versão, algoritmo/normalização, referência nominal fixada, fonte de u nominal e transições recentes;
- últimas execuções de linkage e seus resultados agregados.

O monitor lê as tabelas operacionais canônicas para fila, processamento, manutenção e linkage. A única persistência criada especificamente para o painel é `controle.runtime_componente`, usada para heartbeat dos processos residentes.

## Saúde do bundle de configuração

`config/release/configuration-bundle.json` define a identidade técnica do conjunto instalado, incluindo:

```text
bundleVersion
solutionSchema
clusterConfigSchemaVersion
```

O instalador cluster injeta nos processos residentes:

```text
JORNADA_CONFIGURATION_BUNDLE_VERSION
JORNADA_SOLUTION_SCHEMA_VERSION
```

Cada heartbeat persiste esses dois valores. O monitor compara três fontes independentes:

1. bundle/schema esperado pela própria API que está servindo o monitor;
2. bundle/schema reportado pelos componentes `ONLINE` de NODE1/NODE2;
3. `Jornada.SolutionSchema` efetivamente gravado como extended property no SQL Server.

Estados:

- `OK` — todos os processos online reportam o bundle esperado e o SQL está no `SolutionSchema` esperado;
- `DIVERGENTE` — ao menos um nó/componente ou o SQL pertence a outra versão; o estado geral do monitor vira `FALHA`;
- `NAO_CONFIGURADO` — as variáveis de identidade do bundle não foram fornecidas; usado para compatibilidade de desenvolvimento antes da instalação cluster versionada e não é tratado como divergência explícita.

Essa checagem detecta, por exemplo, NODE1 atualizado e NODE2 ainda executando configuração/binários de outra implantação, mesmo que os dois processos estejam vivos.

## Heartbeat

Os processos residentes publicam presença no SQL compartilhado:

```text
Api
ResultadoApi
Processor
OperationsMaintenance
BronzeMaintenance
```

O intervalo padrão é 10 segundos. Um componente com heartbeat `RUNNING` mais antigo que 35 segundos é apresentado como offline.

A identidade do nó segue:

1. `JORNADA_NODE_ID`, quando configurado pela topologia de cluster;
2. `Environment.MachineName`, como fallback.

Assim, na instalação de dois nós o painel apresenta `NODE1` e `NODE2` sem depender do balanceador externo. A parada limpa registra `STOPPED`; em queda abrupta, o heartbeat envelhece e o painel passa a considerar o componente offline.

A publicação é **best-effort**. Falha no mecanismo de monitoramento nunca deve derrubar API, Processor ou Maintenance.

### Heartbeat dos lotes em processamento

O painel de lotes ativos consulta `ingestao.lote_heartbeat` pelo `lote_id`,
`lease_id` e `lease_owner` correntes. O valor exibido é o heartbeat renovado
nessa linha, não o snapshot de reserva em `ingestao.lote`. Quando não há
linha correspondente (lease legado), o painel recorre ao snapshot inicial.
Uma linha de heartbeat pertencente a um lease antigo jamais é atribuída ao
lease atual. O monitor continua somente leitura e não controla a recuperação
de leases; watchdog e Processor seguem responsáveis por ela.

## Execuções únicas

Calibrador, Linkage Runner, Bronze Verify e Linkage Evaluation não são processos residentes do monitor.

O estado de Linkage é lido de `identidade.linkage_run`. Na implantação cluster, Calibrador e Runner ficam instalados na VM como scripts/ferramentas manuais, sem scheduler automático. O wrapper de Linkage exige exatamente um modelo `ATIVO`, e o wrapper de calibração executa `GENERATE_DRAFT -> CONFERENCIA -> VALIDATE -> ACTIVATE` antes de liberar o Runner. A tolerância técnica corrente está `FROZEN` em `V1_2026-09-26` (LLR máximo 0,01), mas isso não cria evidência `CONFORME`: a conferência precisa executar no modelo/fingerprint corrente, e os orçamentos FP persistidos também passam pelo gate. O Calibrador é ferramenta de execução explícita; reiniciar Api/Processor não o executa.

## Onde o monitor fica

O monitor não ganha VM/container próprio. Ele faz parte de `Jornada.Api` e, portanto, existe nos dois nós:

```text
http://NODE1:5080/monitor
http://NODE2:5080/monitor
```

Com balanceador/VIP externo, o caminho preferencial é `/monitor` no endereço do balanceador. Como o estado é lido do SQL compartilhado, qualquer instância da API apresenta a visão do cluster inteiro.

## Autorização

A API do monitor exige:

```text
credential type: GESTOR
scope: jornada.monitor.read
```

Credenciais `BENEFICIO` e `SERVICO` não podem receber esse scope. O perfil Development concede o scope às credenciais GESTOR sintéticas de `config/security/test-access-keys.json`.

No cluster local, o `entrypoint.sh` exporta `OperationalMonitor__LocalAutoGestor` e `OperationalMonitor__LocalAutoAccessKey` somente quando `environment=Test`. A página `/monitor` injeta essa sessão automaticamente apenas quando o runtime ASP.NET está em `Development`. Fora de `Development`, esses valores não são usados e a tela continua exigindo autenticação explícita do operador.

## Limite de responsabilidade

O Monitor Operacional mostra a saúde funcional da Jornada e seu fluxo de trabalho. Métricas de host e infraestrutura permanecem responsabilidade da plataforma de infraestrutura, por exemplo:

- CPU e memória das VMs;
- saúde física/virtual do disco;
- latência de rede;
- disponibilidade do hipervisor;
- telemetria detalhada de SQL Server;
- saúde física do NAS/SAN;
- balanceador/VIP externo.

A Jornada pode usar readiness e seus próprios heartbeats para indicar que uma dependência não está utilizável, mas não replica a ferramenta corporativa de observabilidade de infraestrutura.

## Instalação

A migração `database/migrations/20260914_Operational_Monitor.sql` é idempotente e faz parte do baseline candidato v3.70. A tabela armazena também `configuration_bundle_version` e `solution_schema_expected`.

O bundle Windows contém `config/release/configuration-bundle.json` e `MANIFEST.sha256`. O primeiro fornece a identidade lógica usada em runtime; o segundo garante integridade de cada arquivo do payload durante a validação do bundle.


## Painel do Calibrador e referência IBGE\n\nO `/monitor` passou a ler, de forma **somente leitura**, a preparação da referência IBGE separada de qualquer modelo: número de versões `ATIVAS`, código e hash do snapshot e estado `PRONTA`, `INCOMPLETA`, `AUSENTE` ou `DIVERGENTE`. Não consulta a rede do IBGE nem executa carga; a referência deve estar pronta **antes da primeira Entrega**, e uma inicialização normal não executa `LOAD_NAME_FREQUENCY_SNAPSHOT`.\n\nO bloco `Linkage · Calibrador e referência IBGE` apresenta o **último modelo gerado**, mesmo `RASCUNHO`, `VALIDADO` ou `FALHOU`, em seção distinta do modelo `ATIVO`. Exibe budgets FP de VALIDATION/TEST em basis points nominais e tetos efetivos, positivos e negativos leave-truth-out, FN, inconclusivos, FP por classe em TEST, taxas operacionais FP/positivo rotulado, amostras m/u e referência fixada no último modelo. Como o numerador FP inclui negativos e o denominador são positivos, a taxa exibida **não** é a FPR clássica. Dados ausentes de modelos legados ou falhos nunca viram zero artificial. O Monitor não revela `T_LINKAGE`, margem ou qualquer dado individual.\n\n**Pendente de implementação separada:** o status do bootstrap nominal IBGE imutável derivado em `ref` (método/seed/contagem/hash e reutilização pelo Worker) exige migração e teste de read-through antes de surgir como estado `PRONTO` no painel. O status de `ref.frequencia_nome_*` que já aparece no painel **não** comprova que esse derivado exista. A calibração operacional de `m`/`u` condicionado ao blocking permanece dependente do corpus e não deve ser confundida com o preparo da referência externa. Consultar [decisões revisadas](Decisoes_Linkage_Calibracao_IBGE_20260926.md).\n\n## Governança do modelo de Linkage

O bloco `Linkage · governança do modelo` é separado da lista de execuções. Ele mostra somente metadados que já são parte do contrato técnico do modelo e da sua proveniência:

- `modelo_id`, versão, algoritmo, normalização e instante de ativação;
- referência nominal **fixada no modelo**, nunca inferida da referência que estiver ATIVA no momento da consulta;
- fonte de u nominal para nome/nome da mãe (`BLOCKING_CONDITIONED`, `IBGE_BOOTSTRAP` ou `NAO_DECLARADO`);
- estado explícito da validação estatística representativa: `PENDENTE_ISSUE_31`;
- últimas transições persistidas em `auditoria.modelo_linkage_estado_evento`.

A trilha de transição é append-only e registra executor **técnico** (aplicação/login/host), estado anterior/novo e operação inferida. Ela não fabrica autoria humana/corporativa: essa identidade depende da integração PRODAM da issue #378.

Esta fatia deliberadamente **não expõe T_LINKAGE, margem ou outros parâmetros sensíveis no monitor**. A política de quem pode enxergar esses valores em HML/PRD depende das issues #378/#379. A promoção continua sem rota de mutação no `/monitor`.

A promoção de um modelo vale para execuções futuras. Runs e vínculos históricos continuam associados ao modelo que efetivamente os decidiu e não são recalculados automaticamente.


### Conferência, round-trip e validação estatística

O bloco de governança do Linkage apresenta evidências com semânticas separadas:

- **Conferência de implementação**: última linha persistida em `auditoria.linkage_conferencia_evidencia` para o `modelo_id` ATIVO. O painel mostra status, método, versão da tolerância, instante, quantidade agregada de candidatos sintéticos e os diagnósticos `sameFinalDecision`/`sameTop1`. O fingerprint do snapshot decisório é recalculado e comparado com o registrado na evidência; o painel mostra `ATUAL` ou `OBSOLETA`. O valor numérico da tolerância, threshold e margem não são expostos.
- **Round-trip do formato**: contrato `JORNADA_CALIBRATION_AUDIT_ROUNDTRIP_V1`, executado obrigatoriamente quando ocorre o export de auditoria. Como o resultado desse round-trip não é persistido por modelo, o monitor exibe `OBRIGATORIO_NO_EXPORT_NAO_PERSISTIDO`; isso não deve ser lido como evidência `CONFORME`.
- **Validação estatística representativa**: permanece `PENDENTE_ISSUE_31` até a avaliação externa/representativa correspondente.

Se o modelo ATIVO não possuir evidência de conferência persistida, o estado mostrado é `SEM_EVIDENCIA_MODELO_ATIVO`. O monitor é read-only: nenhuma dessas informações cria rota de ativação, validação ou promoção.


### Identidade do modelo por execução

Cada execução de Linkage exibe seu próprio `modelo_id` e `modelo_versao` vindos de `identidade.linkage_run`. O painel não atribui a runs históricos o modelo que estiver ATIVO no momento da consulta.

Não existe ainda uma política de expiração temporal da evidência de conferência; por isso o monitor não inventa um SLA de frescor por idade. A validade exibida nesta etapa é estrutural, baseada no fingerprint do snapshot do modelo.


#### Suporte condicionado por passe

Para o modelo ATIVO, o monitor lê os parâmetros `BLOCKING_PASS_U_XX_*` e o ruleset fixado no modelo. Para cada passe exibe:

- identificador/ordem do passe (exibição 1-based; `passe_ordem=0` corresponde a `BLOCKING_PASS_U_01_*`);
- tamanho da amostra u condicionada;
- suporte de nome da mãe presente;
- mínimo por passe persistido em `NOMINAL_U_MIN_CONDITIONED_PAIRS_PER_PASS`;
- suficiência separada para nome e nome da mãe.

Esses valores são suporte/proveniência do universo de blocking. Não são score, threshold ou margem e não autorizam promoção.

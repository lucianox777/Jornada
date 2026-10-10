# C3.3d — Diário persistente de operações dos três workers (CI DEV)

**Status:** incremento funcional candidato em PR, não é telemetria
corporativa HML/PROD. Somente **runner GitHub-hosted descartável**,
`JORNADA_RUNTIME_MODE=DEV`, SQL `JornadaE2E`, identidade Compose
`ci<GITHUB_RUN_ID><GITHUB_RUN_ATTEMPT>` e allowlists que já guardam
as rotas da Console. Não acessar ou executar no NODE canônico.

## Problema

`ConsoleActivityLog` mantém apenas as 1.000 entradas mais recentes
**na memória da instância da Console**, logo um processo encerrado
perde sua sessão. É inadequado usar esse log efêmero como prova de
que uma ação anterior de supervisor/RunOnce realmente ocorreu.
Um histórico durável de produção ainda requer desenho de
auditoria/identidade e armazenamento corporativo.

## Solução deste incremento

`IsolatedWorkerAuditJournal` tem um **esquema intencionalmente
mínimo**, não aceita corpo HTTP nem campos livres:

`{timestampUtc, project, category, operation, outcome}`

Categorias: `SUPERVISAO` (ON/OFF), `RUN_ONCE` e
`PARAR_PROCESSO` (três workers allowlisted).
Desfechos: `ADMITIDO` (**antes da mutação**, fail-closed se não
conseguir gravar), `SUCESSO` após confirmação pelo controlador e
`ERRO` quando a operação falhou.

Os eventos JSONL são acrescentados em
`Solution/.local/e2e/c3-3d-private-worker-audit/events.jsonl`
com `FileMode.Append`, `FileOptions.WriteThrough`, lock e
`Flush(flushToDisk:true)`; cada instância nova da Console escreve
no **mesmo arquivo do projeto CI efêmero**, sem truncar o anterior.
O run/attempt deriva de variáveis GitHub **validadas**, não da URL.
Não guardar nomes, CPF, SQL credentials, argumentos de requisição,
mensagens de exceção, saída do Docker ou paths de cliente.

A escrita de `ADMITIDO` falha antes da execução Docker quando o
journal não pode ser aberto. Erros posteriores de auditoria
**não** transformam uma mutação em êxito presumido. O ledger é
suplemento de evidência: o controle real continua validando
PIDs/labels do Docker e heartbeat/estado SQL.

## Aceite real

A mesma bateria E2E privada que comprova ON↔OFF, RunOnce de cada
worker e Parar processo confirma **ao final** que o journal
sobreviveu a múltiplos processos Console e registra os seis
eventos operacionais esperados (ON, OFF, três RunOnce e stop)
como `SUCESSO`. As linhas precisam conter **somente** os
cinco campos permitidos e o projeto ci<run><attempt> correto.
Teste negativo estático no job `unit` protege allowlists, paths,
flush e ausência de campos sensíveis. O artefato da CI conserva
os arquivos `.local/e2e` para consulta.

## Limites

- **Não** é log durável de produção nem mecanismo de auditoria
  homologado: o runner e seus arquivos deixam de existir após
  a retenção do artefato Actions.
- Não registra intervenções de outros hosts, operações com
  a Console desligada ou eventos da infraestrutura fora desse
  controlador. Não afirmar cobertura completa nem persistência
  pós-expiração do artefato.
- **Não** implementa ainda cancelamento explicitamente confirmado
  de RunOnce ativo (C3.3b3b), telemetria institucional nem DT-22.
  A DT-22 permanece a **última prioridade**, por decisão do
  responsável em 09/10.

**Segurança:** jamais acessar `JornadaLocal`, IBGE original,
NODE/Compose, HML/PROD, dados ou volumes/contêineres de usuário.
Sem `docker prune`, `down -v`, arquivos de host ou bind mounts.

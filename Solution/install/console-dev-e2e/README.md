# C3.2a–b — fronteiras independentes de API e workers (DEV descartável)

**Estado: scaffold, não aceite operacional.** Este diretório é totalmente
separado de `Solution/docker-compose.yml` (`jornada-local`) e não
altera o supervisor `entrypoint.sh` dos NODEs existentes.

## O que está especificado no Compose isolado

- Um SQL Server independente, sem porta de host publicada, com
  volume de nome **gerado pelo projeto Compose E2E**.
- **API e Resultado.Api são agora contêineres separados** (sem profile
  `continuous`), com PID1 distinto, launcher allowlisted
  `api-entrypoint.sh`, healthcheck e restart externo próprios.
  Ambos permanecem ativos em modo OFF e não devem ser encerrados
  quando qualquer worker falhar. Resultado acessa API pelo DNS
  privado `http://api:5080`, nunca `localhost` nem a API do usuário.
- Três serviços workers independentes com `PID 1` próprio,
  `worker-entrypoint.sh` com allowlist e validação do banco efetivo
  `JornadaE2E`, volumes e rede somente do projeto descartável.
- Workers sob `profiles: [continuous]`, **ausentes no modo OFF**
  (nenhum worker sobe quando o Compose é lido sem ativar esse perfil).
  Quando o modo ON for implementado na Console, o controlador
  ativará o perfil e iniciará **os três** automaticamente; não haverá
  botão individual de iniciar.
- `restart: unless-stopped` para cada worker **quando o perfil é
  efetivamente ativado**, sem `wait -n` compartilhado. Ensaio de
  falha tem de matar o PID 1 **dentro do contêiner**, e não executar
  `docker compose stop`, porque a parada administrativa pode
  suprimir o reinício definido pela política de restart.
- Comando global OFF deve terminar residentes de forma **administrativa**
  e impedir restart, sem confundir isso com a ação `Parar processo`
  que injeta falha individual apenas em modo ON.

## Limites deste commit

O arquivo Compose não sobe SQL, APIs nem trabalhadores sozinho; a CI
executa só `docker compose config --format json` e valida os
guardas, allowlist, profiles, volumes e **isolamento de APIs**.
**Ainda falta** bootstrap do SQL/schema `JornadaE2E` dentro da
instância descartável, ensaio de disponibilidade real da API,
controlador de modos/locking na Console, evidência
de `SIGKILL`→restart automático, heartbeat/recovery e E2E
sem duplicação. Portanto **C3.2 não pode ser marcado concluído**
por este commit isolado.

Nunca combinar esta configuração com o Compose de `JornadaLocal`,
não usar `docker compose down --volumes` fora de um projeto efêmero
verificado, e nunca utilizar volumes externos, bind-mounts de dados,
IBGE original, HML ou PROD. Trilha 4 permanece suspensa.

Ver as decisões [DT-18](../../docs/DT18_Servicos_Independentes_Console_DEV.md),
[DT-19](../../docs/DT19_Console_Acoes_Workers.md),
[DT-20](../../docs/DT20_Supervisao_Opt_In_Workers.md) e
[DT-21](../../docs/DT21_Testes_Resiliencia_Workers.md).

## Pendências e limites do C3.2b

O teste C3.2b somente valida `bash -n`, `--check` e a topologia
resolvida do Compose (`OFF`: SQL + duas APIs; `ON`: esses componentes
mais os três workers). **Não** sobe APIs nem comprova disponibilidade
real após SIGKILL ou recuperação de lotes. O SQL dentro do projeto
isolado ainda exige migrações governadas no próprio `JornadaE2E`
antes de qualquer teste operacional. Não inventar progresso além
das evidências da CI.


## C3.2c — Bootstrap SQL privado antes dos serviços (em validação)

O serviço `sql-bootstrap` é um processo **finito, create-only**,
executado **somente dentro** do projeto `jornada-workers-e2e-*` e
dependente do `sqlserver` privado desse mesmo Compose. Ele utiliza
imagem SQL específica com cópias limitadas aos scripts do esquema
canônico e seeds DEV; **não monta nenhum volume ou diretório do host**.
Não lê nem carrega a referência IBGE original.

O script recusa execução quando falta opt-in, modo DEV, ID exclusivo,
endereço privado `sqlserver` ou o banco desejado não é exatamente
`JornadaE2E`. **Recusa banco já existente** e não executa DROP/RESET:
a recriação do ambiente só pode ser tratada por infraestrutura E2E
descartável e verificada, nunca pelos scripts do `JornadaLocal`.
Executa baseline canônico, seeds sintéticos e guardas de leases.
A API, Resultado e os três workers aguardam
`service_completed_successfully` para iniciar.

**Importante:** o teste incluído nesta PR é apenas
`docker compose config` + `bash --check` com guardas negativos e
conferência de scripts. Ele **não cria banco nem inicia contêineres**.
Ainda falta evidência operacional real na CI com o SQL isolado:
readiness de API/Resultado, supervisão OFF↔ON, kill e reinício efetivos,
recuperação de lote sintético sem duplicação. Não marcar C3.2 concluído
somente com esta PR.

## C3.2d — Primeiro aceite operacional real SQL (CI isolada)

O script `scripts/e2e-private-sql-bootstrap-runtime.sh` roda **somente**
no job `e2e` do GitHub Actions com opt-in, projeto Compose exclusivo
`jornada-workers-e2e-ci<run><attempt>`, senha aleatória e SQL privado.
Sem host ports, bind mounts, DB existente, volumes externos nem
qualquer acesso a `JornadaLocal`. Preflight de contrato fail-closed
é verificado também no job `unit`, sem abrir containers.

O teste operacional inicia **apenas `sqlserver` e `sql-bootstrap`**,
espera saída zero do one-shot, confere schema, seed DEV, marcador de
ambiente, lease/heartbeat e banco exclusivo `JornadaE2E`. Depois
tenta executar bootstrap novamente: a operação deve recusar banco
existente **sem alterar os dados**. A evidência vai para
`.local/e2e/c3-2d-sql-bootstrap/summary.json` e logs. Não há
comando de limpeza de volumes nesse script: GitHub descarta seu runner
efêmero no término do job.

Este é **SQL-only**, não comprova APIs prontas, os três workers
residentes, restarts independentes após SIGKILL nem recuperação de
lotes. Esses cenários ainda exigem aceites posteriores e gates
próprios. Um PR/commit verde de C3.2d não conclui o contrato DT-21.

O teste operacional é novo: somente considerar comprovado quando o
job E2E da **HEAD exata** terminar `completed/success` e o artefato
confirmar `status=PASS`. Nenhuma execução fora da CI foi autorizada.

## C3.2e — APIs independentes, readiness real e modo OFF (CI)

Após o aceite SQL create-only do C3.2d na mesma execução e no **mesmo
projeto Compose exclusivo**, a CI constrói a imagem de aplicações
somente uma vez e executa `api` e `resultado-api` em contêineres
independentes (com PID1 distintos). O teste lê `/health/ready` da
API, `/health` do ResultadoApi e acessa a API por
`http://api:5080/health/ready` a partir do ResultadoApi. Requer
healthchecks reais, DNS privado e consulta SQL/schema para readiness;
não confunde HTTP 200 de liveness com readiness. Não são abertas
portas de host.

O SQL E2E usa certificado autofirmado. Somente nesta rede privada e
descartável a conexão mantém `Encrypt=true` e aceita o certificado
com `TrustServerCertificate` habilitado no E2E privado. Fora do perfil isolado nenhum
parâmetro é alterado.

Em OFF, o script confere **ausência dos três workers residentes**
por labels exclusivas do projeto, SQL e APIs vivos e processos distintos.
A evidência fica em `.local/e2e/c3-2d-sql-bootstrap/`, incluindo
readiness das APIs, e deve ser conferida no E2E do HEAD exato.

Este aceite **não prova** que RunOnce dos 3 workers está integrado
à Console, não injeta SIGKILL, não demonstra reinício automático dos
workers, não observa leases/recovery de lote e não implementa toggle
global/indicadores/controles. Essas provas ficam para C3.2f, C3.3 e
C3.4. Nenhuma alegação de conclusão DT-21 é permitida.

## C3.2f1 — SIGKILL e reinício independente dos três workers (CI real)

O mesmo job E2E da CI (e não um novo job pesado) reutiliza a imagem já
construída e o banco SQL privado `JornadaE2E`. **Somente após** obter
sucesso nos gates de bootstrap create-only e de API/ResultadoApi readiness,
`e2e-private-worker-restart-runtime.sh` inicia os **três workers** sob
`--profile continuous` dentro do projeto `jornada-workers-e2e-ci*`.

Antes de injetar falha, o teste exige identidade real de cada contêiner
pelas labels do Compose, restart `unless-stopped`, processos distintos e
heartbeat SQL recente em `controle.runtime_componente`.
A falha é enviada por SIGKILL ao **PID de host do contêiner alvo**
identificado por labels/PID no runner efêmero; `docker compose stop`
administrativo poderia desarmar a política de reinício. SIGKILL do PID1
executado *dentro* do próprio namespace já falhou (run 37883567829),
por isso não é usado como evidência de morte do processo. Repete separadamente para Processor, Operations Maintenance e
Bronze Maintenance. Para cada alvo exige **novo PID do host, incremento no
RestartCount e novo instance_id de heartbeat** sem outro clique. O teste
também confirma identidade e PID de SQL, API, Resultado e dos workers
não atingidos, e testa readiness de API novamente ao final.

Resultados devem existir no artefato E2E
`.local/e2e/c3-2f1-worker-restart/` em `summary.json` e
`restart-evidence.tsv`, contendo **valores antes/depois**, além de logs
do runner. O script é negado fora da CI autorizada, de projeto privado
prefixado e de banco descartável exclusivo. Nunca acessa
`JornadaLocal`, referência IBGE original, HML/PROD ou volumes reais.

**Limite explícito:** C3.2f1 comprova apenas supervisão automática de
processos isolados; `processing_recovery_verified=false` é intencional.
Ele **NÃO comprova** que um lote interrompido retomou o processamento,
que leases expirados foram reconciliados, nem que não houve duplicação
de entregas/itens. Esses casos exigem C3.2f2 com lote sintético,
rollback, lease/heartbeat/fencing e idempotência em SQL. DT-21 só poderá
ser concluída após todos os aceites, incluindo Console global OFF↔ON
e Chromium E2E.


## C3.2f2b — ZIP sintético da API ao Processor residente e SQL real (baseline)

Depois do aceite C3.2f1, o **mesmo job E2E** usa a imagem já construída,
sem novo restore/build, para enviar um ZIP de
`tests/fixtures/ingestao/AA01_v2` à API privada por HTTP de dentro do
contêiner isolado. A credencial é **sintética Development** e não aparece
nos artefatos. O projeto Compose é
`jornada-workers-e2e-ci<GITHUB_RUN_ID><GITHUB_RUN_ATTEMPT>`; nenhuma
porta do host ou bind mount externo é criado.

`scripts/e2e-private-api-ingestion-baseline-runtime.sh` comprova
`/health/ready` SQL, heartbeat recente do Processor e entrega real
`PROCESSADA`, com 1 lote, 1 pessoa Silver, 1 registro Silver,
2 itens processados e materialização Serving por `entrega_id`.
O script repete o **POST HTTP** com a mesma chave e ZIP, exige o
mesmo `entregaId` e cardinalidades idênticas (sem duplicação), e grava
`.local/e2e/c3-2f2b-api-sql-baseline/summary.json` como evidência.

O novo contrato negativo em `unit` é barato e **não invoca Docker/SQL**.
Apenas a execução E2E da HEAD exata poderá confirmar processamento
operacional. A evidência deve declarar
`fault_injected=false` e `processing_recovery_verified=false`.

**C3.2f2b NÃO testa interrupção de transação, rollback, lease vencido,
fencing ou autorrecuperação.** O próximo C3.2f2c deverá injetar
SIGKILL com transação **realmente aberta** e comprovar em SQL o
reprocessamento sem duplicações. Não considerar DT-21 concluída.

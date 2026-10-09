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

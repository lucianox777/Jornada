# Jornada — cluster local canônico

O ambiente local padrão reproduz a topologia operacional de duas VMs sem incorporar o balanceador externo:

```text
jornada-node1 ----\
                   +---- SQL Server
jornada-node2 ----/
       \          /
        +-- Bronze compartilhada
        +-- Staging local por nó
```

Há **três containers persistentes**: `jornada-node1`, `jornada-node2` e `sqlserver`. O balanceamento HTTP fica fora da Jornada; no teste, cada nó é exposto diretamente.

## Subir

PowerShell:

```powershell
.\scripts\local-cluster.ps1 up
```

Bash:

```bash
bash ./scripts/local-cluster.sh up
```

O comando inicializa o SQL/seed de Development, constrói a imagem Jornada e sobe os dois nós.

Endpoints:

- NODE1 API: `http://127.0.0.1:5080`
- NODE1 Resultado API: `http://127.0.0.1:5081`
- NODE2 API: `http://127.0.0.1:5180`
- NODE2 Resultado API: `http://127.0.0.1:5181`
- SQL Server: `localhost:14333`

As credenciais SQL são sintéticas e vêm de `.env.example`. Não são válidas para HML/Produção.

## Persistência

Os dois containers montam `jornada_bronze` em `/data/bronze`.

Cada nó recebe volume próprio em `/data/staging`:

- `jornada_staging_1` -> NODE1;
- `jornada_staging_2` -> NODE2.

Assim o caminho lógico é igual nos dois nós, mas a persistência de Staging é fisicamente isolada. Isso espelha a produção, onde cada VM pode usar `D:\Jornada\staging`, enquanto a Bronze aponta para o mesmo compartilhamento/NAS.

## Configuração

`install/windows-production/Jornada.Cluster.Test.json` usa o mesmo schema estrutural de `Jornada.Cluster.Production.example.json`.

O supervisor do container inicia apenas tarefas `enabled=true` com trigger `AtStartup`. Jobs `Daily`/`Weekly` continuam sendo jobs e não são convertidos em daemons.

## Operações

```text
local-cluster up
local-cluster reset
local-cluster status
local-cluster logs
local-cluster down
local-cluster clean
```

`clean` remove também os volumes locais.

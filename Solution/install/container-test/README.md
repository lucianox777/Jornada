# Jornada — cluster local canônico

O ambiente local padrão reproduz a topologia operacional de duas VMs e um armazenamento compartilhado, sem incorporar o balanceador externo:

```text
                 +---------------- SQL Server
                 |
jornada-node1 ---+--- Bronze compartilhada --- jornada-nas (SMB)
jornada-node2 ---+
   |                             |
   +-- Staging local NODE1       +-- share bronze
   +-- Staging local NODE2
```

Há **quatro containers persistentes**: `jornada-node1`, `jornada-node2`, `sqlserver` e `jornada-nas`. O balanceamento HTTP fica fora da Jornada; no teste, cada nó é exposto diretamente.

## Subir

PowerShell:

```powershell
.\scripts\local-cluster.ps1 up
```

Bash:

```bash
bash ./scripts/local-cluster.sh up
```

O comando inicializa o SQL/seed de Development, constrói as imagens Jornada/NAS, sobe os dois nós e imprime:

- endpoints NODE1/NODE2;
- SQL Server;
- endpoint SMB do NAS local;
- `configurationBundleVersion` e `solutionSchema`;
- URLs do monitor operacional;
- comandos e caminhos dos executáveis de execução única.

Endpoints padrão:

- NODE1 API: `http://127.0.0.1:5080`
- NODE1 Resultado API: `http://127.0.0.1:5081`
- NODE2 API: `http://127.0.0.1:5180`
- NODE2 Resultado API: `http://127.0.0.1:5181`
- NODE1 Monitor: `http://127.0.0.1:5080/monitor`
- NODE2 Monitor: `http://127.0.0.1:5180/monitor`
- SQL Server: `localhost:14333`
- NAS SMB: `localhost:1445`, share `bronze`

No primeiro bootstrap DEV, os scripts criam `.env` com credencial SQL aleatória própria, usando `.env.example` somente como modelo de variáveis. O exemplo contém uma senha pública ilustrativa; não a use em DEV compartilhado, HML ou Produção. Um `.env` existente não é modificado automaticamente.

## Persistência e NAS

Os dois containers de aplicação montam `jornada_bronze` em `/data/bronze`. O container `jornada-nas` monta o mesmo volume em `/srv/jornada/bronze` e o publica como share SMB `bronze`.

Isso permite ao smoke test provar que um objeto publicado por NODE1/NODE2 também é visível pelo serviço NAS. No perfil local padrão, porém, a aplicação continua acessando `/data/bronze` diretamente; o harness não pretende provar todas as semânticas de rename/cache/falha de um NAS SMB de produção. Um ensaio de fidelidade de storage deve forçar a I/O pelo protocolo real homologado.

Cada nó recebe volume próprio em `/data/staging`:

- `jornada_staging_1` -> NODE1;
- `jornada_staging_2` -> NODE2.

Assim o caminho lógico é igual nos dois nós, mas a persistência de Staging é isolada. Isso espelha a produção, onde cada VM pode usar `D:\Jornada\staging`, enquanto a Bronze aponta para o mesmo NAS.

## Processos residentes

O supervisor do container inicia somente cinco componentes `AtStartup`:

- API;
- Resultado API;
- Processor;
- Operations Maintenance;
- Bronze Maintenance.

Eles ficam em background como filhos supervisionados do container. Se um filho residente termina, o nó encerra para que a política de restart reinicie o conjunto de forma coerente.

## Execuções únicas

Os binários ficam dentro da mesma imagem, mas não são iniciados pelo supervisor:

```text
/opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll
/opt/jornada/apps/Jornada.Linkage.Runner/Jornada.Linkage.Runner.dll
/opt/jornada/tools/Jornada.Bronze.Verify/Jornada.Bronze.Verify.dll
/opt/jornada/tools/Jornada.Linkage.Evaluation/Jornada.Linkage.Evaluation.dll
```

A convenção é executar no NODE2. Os atalhos principais são:

```powershell
.\scripts\local-cluster.ps1 calibrate
.\scripts\local-cluster.ps1 linkage
```

ou:

```bash
./scripts/local-cluster.sh calibrate
./scripts/local-cluster.sh linkage
```

`calibrate` executa `GENERATE_DRAFT -> VALIDATE -> ACTIVATE`. `linkage` consulta o SQL e recusa iniciar se não houver exatamente um modelo `ATIVO`.

## Bundle de configuração

`install/windows-production/Jornada.Cluster.Test.json` usa o mesmo schema estrutural de `Jornada.Cluster.Production.example.json` e ambos devem corresponder a `config/release/configuration-bundle.json`.

O entrypoint do nó falha fechado se houver divergência de:

- `configurationBundleVersion`;
- `solutionSchema`;
- `schemaVersion` do contrato cluster.

## Operações

```text
local-cluster up
local-cluster reset
local-cluster status
local-cluster logs
local-cluster calibrate
local-cluster linkage
local-cluster down
local-cluster clean
```

`clean` remove também os volumes locais.

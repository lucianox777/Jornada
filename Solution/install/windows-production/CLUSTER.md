# Instalação cluster — Test e Production

A topologia canônica da Jornada possui dois nós de aplicação simétricos. O balanceamento/VIP, quando existente, é infraestrutura externa e não é instalado pela Jornada.

Os dois perfis versionados usam o **mesmo schema**:

- `Jornada.Cluster.Test.json` — Docker local, credenciais sintéticas;
- `Jornada.Cluster.Production.example.json` — exemplo de produção, sem segredo real.

## Modelo

Campos compartilhados definem SQL, Bronze, HTTP, runtime e tarefas. `nodes[]` define apenas a identidade e os caminhos locais de cada nó.

```text
storage.bronzeRoot      -> compartilhado pelo cluster
nodes[].stagingRoot     -> local a cada VM/container
nodes[].logsRoot        -> local a cada VM/container
nodes[].dataRoot        -> raiz operacional local do host
```

É válido que NODE1 e NODE2 tenham o mesmo texto para `D:\Jornada\staging`: como são VMs diferentes, os discos continuam locais e independentes.

## Produção

Execute o mesmo bundle em cada VM, selecionando o nó:

```powershell
.\Install-JornadaCluster.ps1 `
  -ConfigPath C:\Jornada-Install\Jornada.Cluster.Production.json `
  -PayloadRoot C:\Jornada-Install\bundle `
  -NodeId NODE1
```

Na segunda VM, use `-NodeId NODE2`.

O instalador:

1. valida o schema e seleciona o nó;
2. confirma, na instalação real, que `node.host` corresponde à máquina;
3. cria/valida Bronze compartilhada e os caminhos locais do nó;
4. traduz a configuração cluster para o instalador host já existente;
5. injeta `BronzeStorage__RootPath` compartilhado e `IngestionStaging__RootPath` local em todas as tarefas;
6. mantém os mesmos componentes e triggers nos dois nós.

Para validar sem alterar a máquina:

```powershell
.\Install-JornadaCluster.ps1 `
  -ConfigPath .\Jornada.Cluster.Production.example.json `
  -PayloadRoot C:\Jornada-Install\bundle `
  -NodeId NODE1 `
  -ValidateOnly
```

## Segurança

O perfil `Test` pode conter connection string e chaves sintéticas conhecidas. O perfil `Production` não deve receber esses valores. A validação de Production continua recusando connection string que desabilite TLS ou use `TrustServerCertificate=true`.

O instalador não provisiona load balancer, VIP, DNS ou reverse proxy. Se a infraestrutura externa encaminhar `X-Forwarded-For`, os proxies/redes confiáveis devem ser configurados explicitamente na configuração da API.

## Perfil Test

O perfil Test é executado pelo `docker-compose.yml` e pelos scripts `scripts/local-cluster.*`. O instalador Windows aceita `-ValidateOnly`/`-PlanOnly` para validar o schema Test, mas não o instala em Windows Server.

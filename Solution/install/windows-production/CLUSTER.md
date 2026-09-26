# Instalação cluster — Test e Production

A topologia canônica da Jornada possui **dois nós de aplicação simétricos**. O balanceamento/VIP, quando existente, é infraestrutura externa e não é instalado pela Jornada.

Os dois perfis versionados usam o mesmo schema:

- `Jornada.Cluster.Test.json` — Docker local, valores sintéticos de Development;
- `Jornada.Cluster.Production.example.json` — exemplo de produção, sem segredo real.

Cada perfil declara também `configurationBundleVersion` e `solutionSchema`. Esses valores devem corresponder a `config/release/configuration-bundle.json`; a instalação falha fechado quando configuração e payload não pertencem ao mesmo bundle técnico.

## Processos residentes e execuções únicas

Somente cinco processos são residentes e iniciados em background no boot de cada nó:

- `Jornada.Api`;
- `Jornada.Resultado.Api`;
- `Jornada.Processor.Worker`;
- `Jornada.Operations.Maintenance.Worker`;
- `Jornada.Bronze.Maintenance.Worker`.

No Windows Server eles são tarefas `AtStartup` do Agendador do Windows. No cluster Docker eles são filhos supervisionados do container NODE1/NODE2.

As operações abaixo ficam **instaladas na VM, mas não são agendadas**:

- Calibrador: `jobs\Invoke-JornadaLinkageCalibration.ps1`;
- Linkage: `jobs\Invoke-JornadaLinkageRun.ps1`;
- `tools\Jornada.Bronze.Verify\Jornada.Bronze.Verify.exe`;
- `tools\Jornada.Linkage.Evaluation\Jornada.Linkage.Evaluation.exe` — somente DEV/HML;
- `tools\Jornada.Linkage.Conference\Jornada.Linkage.Conference.exe` — conferência governada antes da promoção;
- Integrador C# — cliente sob demanda.

A convenção operacional é executar as rotinas manuais no NODE2, embora o mesmo bundle seja instalado nos dois nós. Os locks SQL continuam sendo a proteção de concorrência; a convenção de nó não substitui a coordenação no banco.

### Ordem Calibrador → Linkage

`Invoke-JornadaLinkageCalibration.ps1` executa, na mesma operação governada:

```text
GENERATE_DRAFT → CONFERENCIA → VALIDATE → ACTIVATE
```

O wrapper só termina com sucesso quando a nova versão fica `ATIVO`. `CONFERENCIA` usa `config\linkage\implementation-conference-tolerance.json`; com o arquivo técnico `FROZEN` em `V1_2026-09-26`, a rotina ainda falha fechado sem evidência de conferência `CONFORME`. `VALIDATE` e `ACTIVATE` reaplicam o mesmo assert SQL de conferência e a verificação compartilhada do orçamento FP.

`Invoke-JornadaLinkageRun.ps1` faz preflight no SQL e só inicia o Runner quando existe **exatamente um** `identidade.modelo_linkage` em `ATIVO`. O próprio runtime do Linkage já rejeita ausência de modelo ativo; o wrapper torna essa pré-condição explícita antes de iniciar a execução manual.

## Armazenamento

```text
storage.bronzeRoot      -> compartilhado pelo cluster
nodes[].stagingRoot     -> local a cada VM/container
nodes[].logsRoot        -> local a cada VM/container
nodes[].dataRoot        -> raiz operacional local do host
```

É válido que NODE1 e NODE2 tenham o mesmo texto para `D:\Jornada\staging`: como são VMs diferentes, os discos continuam locais e independentes.

Em produção, `storage.bronzeRoot` deve apontar para o compartilhamento NAS homologado. O dimensionamento físico de vCPU/RAM/SAN/NAS é requisito de infraestrutura a ser homologado com a PRODAM; não é embutido no software nem inferido pelo diagrama.

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
2. valida `configurationBundleVersion`/`solutionSchema` contra o payload;
3. confirma, na instalação real, que `node.host` corresponde à máquina;
4. cria/valida Bronze compartilhada e os caminhos locais do nó;
5. traduz a configuração cluster para o instalador host existente;
6. injeta Bronze compartilhada, Staging local e identidade do nó nos processos residentes;
7. registra somente os cinco processos residentes como tarefas `AtStartup`;
8. instala ferramentas e scripts de execução única sem criar triggers;
9. grava `config\manual-runtime.json` com o ambiente necessário aos jobs manuais.

Depois da instalação, em PowerShell elevado na VM:

```powershell
cd 'C:\Program Files\Jornada'
.\jobs\Invoke-JornadaLinkageCalibration.ps1
.\jobs\Invoke-JornadaLinkageRun.ps1
```

Para validar sem alterar a máquina:

```powershell
.\Install-JornadaCluster.ps1 `
  -ConfigPath .\Jornada.Cluster.Production.example.json `
  -PayloadRoot C:\Jornada-Install\bundle `
  -NodeId NODE1 `
  -ValidateOnly
```

## Monitor operacional

O monitor pertence à `Jornada.Api`, não é um processo separado. Como a API existe nos dois nós, a página pode ser aberta diretamente em qualquer um deles:

```text
http://NODE1:5080/monitor
http://NODE2:5080/monitor
```

Com VIP/load balancer, o endereço operacional preferencial é `https://<vip>/monitor`.

A visão é de cluster porque lê o SQL compartilhado. Além de fila, processamento e heartbeat, o contrato de saúde deve comparar a versão do bundle de configuração esperada com a versão reportada pelos processos residentes e com `Jornada.SolutionSchema` no SQL Server.

## Perfil Test

O ambiente local canônico usa quatro containers persistentes:

```text
jornada-node1
jornada-node2
sqlserver
jornada-nas
```

`jornada-nas` expõe via SMB a mesma persistência Bronze usada pelo harness. Os dois nós continuam montando o named volume em `/data/bronze` no perfil padrão para manter o CI simples e determinístico; portanto o container NAS prova presença/visibilidade SMB do compartilhamento, mas **não substitui um ensaio dedicado em que toda a I/O da aplicação passe pelo protocolo SMB real de produção**.

Suba com:

```powershell
.\scripts\local-cluster.ps1 up
```

ou:

```bash
bash ./scripts/local-cluster.sh up
```

O comando imprime NODE1/NODE2, SQL, NAS, versão do bundle, URLs do monitor e os caminhos/comandos das execuções únicas. Também existem os atalhos:

```text
local-cluster calibrate
local-cluster linkage
```

`linkage` falha antes de iniciar se a pré-condição do modelo ativo não estiver satisfeita.

## Segurança

O perfil `Test` referencia a senha SQL sintética fornecida pelo ambiente local (`.env.example`) e pode usar a chave sintética de Development. O perfil `Production` não recebe esses valores. A validação de Production exige transporte SQL protegido e não aceita confiança irrestrita no certificado do servidor.

O instalador não provisiona load balancer, VIP ou DNS. Se a infraestrutura externa encaminhar `X-Forwarded-For`, os proxies/redes confiáveis devem ser configurados explicitamente na API.

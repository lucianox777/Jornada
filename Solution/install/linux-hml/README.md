# Homologação RHEL 10 x64 — Docker single-node

Adaptação do instalador local Windows `Solution/Install-JornadaLocal.ps1`. **Somente DEV/HML**, sem liberação produtiva. O baseline SQL e as credenciais de teste são os do harness Development existente. A homologação formal de segurança, imagens, SQL Server e desempenho permanece pendente.

## Pré-requisitos

RHEL 10 x86_64, Docker Engine com Compose plugin, daemon ativo e acessível pelo usuário instalador, .NET SDK 10, Python 3, curl e git; espaço livre e conectividade para restore/build e imagens. A instalação do Docker no host é responsabilidade da equipe de infraestrutura, conforme políticas de subscrição, SELinux e firewall. Não desabilite SELinux.

A partir da raiz `Solution`:

```bash
bash ./Install-JornadaHml-RHEL10.sh
bash ./Install-JornadaHml-RHEL10.sh status
bash ./Install-JornadaHml-RHEL10.sh logs
bash ./Install-JornadaHml-RHEL10.sh down
```

O instalador não limpa volumes e não reinicializa o banco de forma destrutiva. A opção `--open-monitor` apenas tenta abrir o navegador local. O SQL fica em `127.0.0.1:14333` se não houver configuração alternativa; portas publicadas pelo Compose base devem ser protegidas pelo firewall da HML. Não exponha a API, SQL ou SMB diretamente à Internet.

O perfil monta o Bronze em volume Docker compartilhado apenas com NODE1, sem necessidade de NAS físico ou contêiner SMB. A configuração single-node é derivada do schema `Jornada.Cluster.Test.json` e usa os mesmos contratos e hashes de bundle.

## Execuções sob demanda

As rotinas de ciência **não** são iniciadas automaticamente na instalação. Execute explicitamente, após verificar as regras de governança e o modelo/artefato aplicável:

```bash
docker compose --env-file .env -f docker-compose.yml -f install/linux-hml/compose.single-node.yml exec -T jornada-node1 dotnet /opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll --help
docker compose --env-file .env -f docker-compose.yml -f install/linux-hml/compose.single-node.yml exec -T jornada-node1 dotnet /opt/jornada/apps/Jornada.Linkage.Runner/Jornada.Linkage.Runner.dll --help
```

Os comandos acima apenas consultam ajuda; não ativam nem publicam modelos. A automação `local-cluster.sh calibrate` é específica do harness DEV de dois nós e **não** deve ser usada como atalho de homologação científica V9.

## Limites de validação

O script verifica o host, o Compose, o build, a saúde do NODE1, o monitor e a ausência de NODE2 ativo no projeto. Não certifica uma execução científica, uma migração real de produção ou a segurança do ambiente. A imagem SQL Server é fixada no Compose base e exige ensaio real no host RHEL 10 x64.

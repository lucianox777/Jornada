# GOV-HML-RHEL10-X64-DOCKER-SINGLE-NODE-20261118

**Aprovação do PO:** 18/11/2026. **Status:** implementação proposta em PR; CI e ensaio físico RHEL 10 pendentes. **Política:** GOV-DOC-01.

## Decisão, contexto e justificativa

O destino é um servidor de **homologação**, com **Red Hat Enterprise Linux 10 x64 (x86_64)**, **Docker Engine/Compose** e **um nó lógico NODE1**. Adaptar o instalador local Windows existente, sem migrar os processos para serviços nativos e sem modificar o instalador Windows. A escolha evita divergência desnecessária do ambiente Docker e reduz a infraestrutura de HML.

## Implementação e impactos

- `Solution/Install-JornadaHml-RHEL10.sh`: preflight RHEL 10 x64, Docker/Compose, .NET 10, Python; restore/build, bootstrap SQL, inicialização e verificações HTTP.
- `Solution/install/linux-hml/compose.single-node.yml`: override de Compose para NODE1; NODE2 e NAS de teste sob perfil inativo; Bronze em volume Docker.
- `Solution/install/linux-hml/Jornada.Cluster.Hml.SingleNode.json`: gerado a partir do perfil Test vigente, retendo somente NODE1 e os identificadores de bundle/schema.
- `Solution/install/linux-hml/README.md`: instruções, limites e testes de homologação.

O banco e a imagem SQL Server Developer seguem o **harness de Development existente**, não equivalendo a ambiente produtivo certificado. A rotina `local-db.sh` carrega massa sintética de DEV; o instalador não executa calibrador nem Linkage automaticamente, nem ativa modelos. Compatibilidade real SQL Server/RHEL 10, SELinux, segurança, licenças, backups e capacidade devem ser validadas em máquina de homologação. Sem autorização de mudança científica.

## Precedência e rastreabilidade

Substitui sugestão anterior de Ubuntu e enquadramento anterior de produção. Preserva decisões científicas, bloqueios de publicação e governança. Base técnica: `Solution/Install-JornadaLocal.ps1`, `Solution/docker-compose.yml`, `Solution/scripts/local-db.sh`, `Solution/install/windows-production/Jornada.Cluster.Test.json`.

**Critério de conclusão:** revisão, CI, integração e instalação física no RHEL 10 x64 com verificação de persistência e dos serviços. PR aberta não equivale a homologação.

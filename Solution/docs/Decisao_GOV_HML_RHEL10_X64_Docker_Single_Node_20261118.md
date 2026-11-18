# GOV-HML-RHEL10-X64-DOCKER-SINGLE-NODE-20261118 — Plataforma do instalador Linux de homologação

**Data:** 18/11/2026  
**Autoridade:** Product Owner  
**Estado:** plataforma confirmada; implementação e homologação pendentes.  
**Política:** GOV-DOC-01.

## Contexto e decisão

O PO esclareceu que o destino é **um servidor de homologação**, não produção. O instalador Linux deve ser uma adaptação do instalador local Windows existente, mantendo **Docker / Docker Compose** como mecanismo de instalação e execução, em vez de instalar os componentes como serviços nativos do host.

O PO confirmou o sistema operacional **Red Hat Enterprise Linux 10 (RHEL 10)** e a arquitetura do processador **x64 (x86_64/AMD64)**. A topologia pretendida é **um único servidor e um único nó lógico (NODE1)**, com SQL Server e componentes Jornada em contêineres, e execução sob demanda do calibrador e do Linkage.

## Justificativa

Preservar o modelo Docker já utilizado no instalador Windows, reduzir divergências entre os ambientes e permitir testes de homologação em infraestrutura de um servidor. RHEL 10/x64 é requisito informado pelo PO, não uma sugestão de distribuição.

## Escopo e impactos

- Criar um instalador Bash equivalente ao `Solution/Install-JornadaLocal.ps1`, adaptado às dependências, permissões, SELinux, firewall, volumes e comandos Docker/Compose do RHEL 10.
- Adaptar a topologia de dois nós do ambiente local atual para um nó, sem remover mecanismos de coordenação e segurança aplicáveis.
- Preservar o instalador Windows e o código científico; mudanças funcionais/científicas não estão autorizadas por esta decisão.
- Confirmar tecnicamente a compatibilidade de Docker Engine/Compose e **da imagem SQL Server selecionada** com o host RHEL 10 x64; a confirmação do SO não constitui homologação de imagens, drivers, volumes ou desempenho.
- Executar testes de instalação limpa, reinicialização, persistência, saúde dos serviços, calibrador, Linkage e monitoramento antes de declarar o ambiente homologado.
- Segurança, credenciais, licenciamento SQL Server e requisitos de homologação permanecem sujeitos aos controles existentes; não há liberação produtiva implícita.

## Precedência e rastreabilidade

Esta decisão **corrige o enquadramento anterior de produção** para homologação e substitui a sugestão anterior de Ubuntu por **RHEL 10 x64 confirmado pelo PO**. Mantém a decisão de reaproveitar Docker e o instalador Windows como referência. Não revoga decisões de governança científica ou produção.

Referências técnicas: `Solution/Install-JornadaLocal.ps1`, `Solution/docker-compose.yml`, `Solution/scripts/local-cluster.sh`, `Solution/install/windows-production/README.md` e `Solution/install/windows-production/CLUSTER.md`.

**Situação:** decisão documentada nesta proposta; implementação, validação e integração à branch principal pendentes.

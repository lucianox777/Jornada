# GOV-INSTALL-PARITY-WIN-LINUX-20261118

**Aprovado pelo Product Owner:** 18/11/2026. **Política:** GOV-DOC-01. **Estado:** aprovado; implementação sujeita à CI e integração.

## Contexto e decisão
O instalador Windows `Solution/Install-JornadaLocal.ps1` deve permanecer preservado. O instalador Linux `Solution/Install-JornadaHml-RHEL10.sh` deve evoluir de forma funcionalmente sincronizada com o Windows, compartilhando o Compose base e os contratos de instalação. Alterações em qualquer um exigem avaliação do outro e atualização do contrato automatizado, quando aplicável.

## Justificativa e impactos
Evitar deriva silenciosa de pré-requisitos, restore/build, inicialização do banco, imagens, saúde da API e monitor. Adicionar teste de paridade por capacidade e workflow de CI que reage a mudanças dos instaladores, scripts de orquestração e Compose. O teste é estático, não executa Docker nem prova equivalência integral de comportamento.

## Divergências intencionais
- Windows: ambiente local com NODE1 e NODE2; script PowerShell; abertura opcional de navegador.
- RHEL 10 x64: homologação com NODE1 somente; script Bash; bloqueio de NODE2 preexistente; comando `verify`; override Compose e configuração de cluster gerada.
- Ambos: Docker Engine/Compose, SDK .NET, bootstrap `.env`, restore NuGet bloqueado, build Release, imagens NODE1/NAS, SQL Server, rotas de prontidão e monitor.
- Não se exige igualdade de comandos, topologia, caminhos, perfis ou operações de administração.

## Rastreabilidade, precedência e critérios
Complementa GOV-HML-RHEL10-X64-DOCKER-SINGLE-NODE-20261118, sem alterar decisões científicas nem autorizar produção. Arquivos: `Solution/tests/scripts/test_installer_windows_linux_parity.py`, `.github/workflows/installer-windows-linux-parity.yml`, instaladores e `Solution/docker-compose.yml`. Critério de integração: CI obrigatória e teste de paridade aprovados. Critério de homologação física: instalação no RHEL 10 real, evidências de saúde, persistência, SELinux/firewall, licenciamento e desempenho; CI não substitui essa validação.

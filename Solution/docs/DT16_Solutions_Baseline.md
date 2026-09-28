# DT-16 — primeira etapa: solutions especializadas

Esta etapa adiciona `Jornada.Core.sln`, `Jornada.Runtime.sln`, `Jornada.Linkage.sln`, `Jornada.Dev.sln` e `Jornada.Tests.sln`, com pertencimento exclusivo dos 21 projetos existentes. Os projetos continuam nos caminhos físicos atuais e as `ProjectReference` permanecem intactas; referências transitivas entre solutions são permitidas. A solução `Jornada.sln` é mantida temporariamente para compatibilidade com o CI e os scripts existentes.

O gate `scripts/dt16-solutions-gate.py` verifica cobertura integral, exclusividade e existência dos `.csproj`. O CI também compila as cinco solutions em Release. A migração de caminhos físicos, CI especializado e remoção da solution monolítica ficam para etapas posteriores, após inventário de Dockerfiles, scripts, workflows e documentação. Não se reivindica ganho de desempenho sem medição.

Rollback: reverter esta PR; não há alteração de namespace, API, código de execução ou localização de projetos. Referência: issue #576.

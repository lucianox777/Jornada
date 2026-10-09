# DT-18 — serviços independentes e ciclo de vida por worker (Console DEV)

**Revisão de estado — 09/10/2026:** **implementado e validado tecnicamente
em DEV/CI efêmero**. Os serviços independentes/API/Resultado e três workers
têm aceites em #845–#847, recuperação transacional/lease em #848–#850.
O Compose `install/console-dev-e2e/docker-compose.workers.yml` e os
entrypoints privados NÃO substituem o `container-test`/NODE canônico
mencionado na decisão original. O texto abaixo permanece como
desenho/critério de aceite de 08/10, não fotografia atual de pendência.
Não há homologação nem implantação HML/PROD; ver
[Manual integrado](Manual_Sistema_Consolidado_20261009.md),
[Console DEV](Console_DEV_Supervisao_Atual.md) e
[estado atual](Estado_Atual_Projeto.md).

## Contexto confirmado

Os executáveis `Jornada.Processor.Worker.dll`,
`Jornada.Operations.Maintenance.Worker.dll` e
`Jornada.Bronze.Maintenance.Worker.dll` já existem separadamente, e cada um
suporta RunOnce e execução residente (configurações
`Processor__RunOnce`, `MaintenanceExecution__RunOnce` e
`BronzeMaintenance__RunOnce`). API e Resultado.Api também são componentes
distintos. Entretanto, no ambiente container-test, o
`install/container-test/entrypoint.sh` inicia diversos processos no mesmo
contêiner e usa `wait -n`: a saída de um filho derruba os demais e o NODE
reinicia. **Essa cascata não é o comportamento desejado para testes DEV.**

## Decisão

- Cada worker opera com PID, estado e política de reinício **independentes**,
  administrados pelo supervisor externo de serviços (Docker/service manager);
  não reimplementar um supervisor de todos os workers dentro da Console.
- A morte de um worker **não** termina API, Resultado.Api nem os demais
  workers. NODE1/NODE2 permanecem conceitos de alocação, não unidades
  indivisíveis de falha.
- O supervisor reinicia o **processo do worker** quando configurado; o
  próprio worker recupera **seu trabalho** por leases/heartbeat, controle
  transacional, idempotência e retry. Um PID novo **não** comprova
  recuperação de dados.
- Mudança de topologia será introduzida apenas em projeto Compose
  **descartável e isolado**, com credenciais e volumes de testes próprios;
  não modificar/implantar de imediato a topologia de `JornadaLocal` nem
  substituir o supervisor atual sem prova de migração.
- Trabalhos por lote e serviços de manutenção têm mecanismos e limites
  próprios de recuperação: não inventar a mesma métrica de lote para
  Maintenance; reportar saúde e ciclos relevantes de cada worker.

## Aceite

Matar somente o Processor mantém os PIDs da API, Resultado,
Operations Maintenance e Bronze Maintenance; comprovar comportamento
equivalente para cada worker, além de recuperação do próprio processamento.
Se o supervisor estiver ativo, somente o serviço morto deve reiniciar.
Sem evidência real da CI em `JornadaE2E`, o item permanece pendente.

**Limites:** nenhuma implementação da Trilha 4 (reprocessamento automático
de RESOLVIDOS), acesso à referência IBGE original ou HML/PROD.

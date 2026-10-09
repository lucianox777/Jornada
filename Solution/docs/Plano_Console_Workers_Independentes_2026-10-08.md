# Console DEV — quatro ações e supervisão opcional de workers independentes

**Decisão de escopo (08/10/2026).** Decisões formais vinculantes desta frente: [DT-18 — independência](DT18_Servicos_Independentes_Console_DEV.md), [DT-19 — quatro ações](DT19_Console_Acoes_Workers.md), [DT-20 — supervisão opcional](DT20_Supervisao_Opt_In_Workers.md) e [DT-21 — resiliência/testes](DT21_Testes_Resiliencia_Workers.md). A implementação continua pendente até a CI e o merge respectivos. O trabalho anterior de aceite de ingestão
da Console (TC-05/TC-06, PRs #837, #839 e #840) foi concluído antes de iniciar
esta frente. Esta evolução é **uma frente separada**, com PRs pequenas e
gates da CI por HEAD. A Trilha 4 continua expressamente suspensa.

## Contrato de interface — exatamente quatro ações

A interface disponibilizará os mesmos controles para os três executáveis
`Jornada.Processor.Worker`, `Jornada.Operations.Maintenance.Worker` e
`Jornada.Bronze.Maintenance.Worker`:

| Ação | Semântica |
|---|---|
| **Executar uma vez** | RunOnce finito, aguardando conclusão e exit code. Recusar se a instância residente conflitar com a execução. |
| **Iniciar contínuo** | Garantir **uma instância** residente deste worker. Se já iniciada, retornar estado existente, sem duplicar. |
| **Parar processo** | Injetar falha abrupta **no PID do worker**, sem shutdown coordenado e sem desligar o serviço; o supervisor reinicia **somente esse worker**. |
| **Status do processo** | Consultar estado real, PID, geração/reinícios, uptime e heartbeat operacional. Resposta nunca deve inventar saúde com base só no PID. |

**Não** haverá botões distintos de “Parar contínuo”, “Desligar” ou
“Simular falha”. O nome `Parar processo` significa, neste DEV de teste,
morte abrupta. **Reinício automático é configurável pelo toggle de supervisão**
da DT-20, e não é incondicional. Indicar esta diferença no tooltip e
exigir confirmação antes do SIGKILL.

**Quinto controle (toggle separado dos quatro botões):**
`Supervisão automática: ATIVADA / DESATIVADA`. Quando ativada, uma morte
abrupta do worker contínuo produz reinício apenas daquele serviço; quando
desativada, permanece parado. Alternar a política não mata nem inicia o
processo por si só. O RunOnce jamais é reiniciado automaticamente.

## Motivo arquitetural e fases em ordem

O supervisor atual `install/container-test/entrypoint.sh` lança vários
processos no mesmo container e, quando qualquer um sai (`wait -n`),
derruba todo o NODE. A semântica desejada exige que os três workers
tenham processos e políticas de restart **independentes**, em vez de
alterar o `wait -n` isoladamente e perder a supervisão.

1. **Infraestrutura isolada (C3.1).** Preparar um entrypoint de
   **worker único** com allowlist de executáveis e opt-in fail-closed
   somente DEV/`JornadaE2E`. Validar comando e rejeições na CI.
   Nesta primeira PR, não modificar supervisor, Compose padrão nem
   iniciar workers reais.
2. **Supervisão independente (C3.2).** Compor cada worker como serviço
   separado, com PID 1 próprio e **política de reinício selecionável**
   (inicialmente DESATIVADA no perfil descartável; toggle ON habilita
   reinício automático, toggle OFF impede reinício), no projeto
   Docker **descartável**, sem usar containers, DB ou volumes do
   `JornadaLocal`. Desabilitar os workers residentes dentro dos NODES
   **somente no perfil novo**, mantendo API e Resultado independentes.
   Provar que SIGKILL no Processor reinicia somente Processor; os PIDs
   de API/Resultado/Operations/Bronze continuam estáveis. Não ativar em
   HML/PROD e não alterar Compose padrão sem ensaios.
3. **API da Console (C3.3).** Endpoints DEV exclusivos com allowlist
   explícita para `run-once`, `start-continuous`,
   `kill-process`, `status` e **`supervision` on/off/status**. Falhar fechado quando não houver
   perfil descartável confirmado. Proteção de exclusão/concorrência
   **atômica** (verificar PID antes de iniciar é insuficiente).
   Nunca executar shell de texto vindo do navegador.
4. **Interface e E2E (C3.4).** Exatamente os quatro botões **mais
   um toggle de supervisão** por worker, com estados e acessibilidade.
   Testar RunOnce, dois inícios simultâneos,
   PID morto, reinício automático, heartbeat/leases e recuperação sem
   duplicidade em lote sintético. Comprovar API acessível durante morte
   do Processor e demais workers sem troca de PID.

## Gates / segurança

Nenhuma etapa toca, reseta, limpa ou migra `JornadaLocal`, referência
IBGE original, volumes compartilhados ou HML/PROD. Os testes usam
`JornadaE2E` e recursos temporários independentes; qualquer
configuração ambígua ou divergência de banco **recusa executar**.

A parada abrupta poderá deixar lease em vigor até expiração. O status
deve distinguir `REINICIADO`, `RECUPERANDO` e `OPERACIONAL` com
**evidência verificável**; PID novo não prova recuperação do lote.
Nenhum trabalho de reprocessamento automático de RESOLVIDOS ou Trilha 4.

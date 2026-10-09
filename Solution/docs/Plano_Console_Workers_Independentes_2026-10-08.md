# Plano Console DEV — supervisor global e workers independentes

**Decisão atualizada em 08/10/2026:** a supervisão controla o
**modo de execução dos três workers em conjunto**. Esta decisão mais
recente **prevalece** sobre a proposta anterior de start contínuo
individual e toggle de supervisão por worker, que não deve ser
implementada. Histórico do aceite de ingestão da Console: PRs
#837, #839 e #840 concluídas no master; não reabrir esses trabalhos.

## Contratos vinculantes

- [DT-18 — serviços independentes](DT18_Servicos_Independentes_Console_DEV.md)
- [DT-19 — RunOnce, Parar processo e Status individuais](DT19_Console_Acoes_Workers.md)
- [DT-20 — supervisor GLOBAL OFF/ON com start dos três](DT20_Supervisao_Opt_In_Workers.md)
- [DT-21 — testes e evidências de resiliência](DT21_Testes_Resiliencia_Workers.md)

**Interface:** um toggle global `Supervisão: DESATIVADA / ATIVADA`,
inicialmente **DESATIVADO** no novo ambiente descartável. Cada
worker mantém **Executar uma vez**, **Parar processo** e
**Status do processo**. Não há `Iniciar contínuo` individual:
ao ativar a supervisão, **os três** workers iniciam continuamente.

| Modo | RunOnce de todos | Workers contínuos | Parar processo |
|---|---|---|---|
| **OFF — inicial** | Habilitados, respeitando pré-requisitos. | Três ausentes/parados. | Interrompe execução finita ativa sem restart. |
| **ON** | Todos desabilitados, inclusive pela API. | Os três iniciados automaticamente e supervisionados individualmente. | Mata o worker escolhido; supervisor reinicia somente esse worker. |

**ON:** bloquear RunOnce e confirmar interrupção de qualquer execução
finita ativa; matar/encerrar **somente os três workers** (não API,
Resultado, SQL ou contêineres compartilhados), preparar supervisão,
iniciar os três residentes e validar estados reais antes de declarar
ativação concluída.

**OFF:** desarmar a política de restart dos três **antes** de encerrar
suas instâncias; confirmar ausência de residentes, habilitar RunOnce.
O toggle é comando operacional de transição; não apenas preferência
visual. Uma transição parcial não é sucesso e não pode liberar os
dois modos simultaneamente.

## Sequência em PRs pequenas

1. **C3.1 — boundary individual:** entrypoint com allowlist,
   guardas DEV/`JornadaE2E` e gate CI sem executar Docker/SQL.
   Apenas incluir o entrypoint na imagem; **não ativá-lo** na
   topologia do NODE padrão. Primeira PR #841.
2. **C3.2 — isolamento real descartável:** criar projeto Compose
   próprio para testes com três serviços independentes, PIDs e
   restart configuráveis, sem volume/container/banco do cluster
   comum. Provar que matar um processo não mata os outros nem API.
3. **C3.3 — controlador global:** backend DEV fail-closed, API de
   transição OFF→ON→OFF **serializada**, e endpoints por worker de
   RunOnce, Parar processo, Status. Checagem de PID e serviço
   reais; impedir processos duplicados e qualquer execução de
   shell arbitrário recebida do navegador.
4. **C3.4 — interface e E2E:** toggle global + três ações por
   worker, sem botão redundante de iniciar contínuo individual.
   Chromium/E2E de toda a matriz [DT-21](DT21_Testes_Resiliencia_Workers.md),
   incluindo kill sob ON, restart seletivo, recuperação sem
   duplicação, corrida e OFF após ON.

A **Console não substitui o supervisor externo**: aciona política
do gerenciador de serviços; cada worker recupera seu trabalho
via mecanismos próprios de lease/heartbeat e idempotência.

## Gates, estado e não-escopo

A ativação só está disponível no ambiente DEV **descartável**,
com `JornadaE2E` e fixtures sintéticas. Estado OFF só pode ser
afirmado como padrão de uma **primeira implantação descartável**;
recarregar a Console deve refletir estado efetivo do backend,
inclusive se já estiver ON. Os testes devem provar o processo e
a recuperação, não presumir saúde por PID.

**Nunca** usar/limpar/migrar `JornadaLocal`, IBGE original,
volumes compartilhados, HML/PROD ou dados reais. Não implementar
Trilha 4/reprocessamento automático de RESOLVIDOS. Merge somente
com gates obrigatórios completos e `success` na HEAD exata,
com verificação horária e evidência real.

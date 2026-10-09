# DT-19 — ações e layout da Console DEV para workers

**Última decisão do responsável — 08/10/2026 (DEV).** Este documento
substitui as propostas anteriores de botão individual `Iniciar contínuo`,
`Desligar processo` e toggle por worker. **Implementação e aceite pendentes**:
a decisão registrada não significa que a interface já exista.

## Interface aprovada

Um **único controle global** `Supervisão automática: DESATIVADA / ATIVADA`
para os três executáveis `Jornada.Processor.Worker`,
`Jornada.Operations.Maintenance.Worker` e
`Jornada.Bronze.Maintenance.Worker`. O ambiente DEV isolado novo
inicia com supervisão DESATIVADA e modo RunOnce.

Os botões de cada worker são **somente três, empilhados VERTICALMENTE**:

```text
SUPERVISÃO AUTOMÁTICA: [ DESATIVADA | ATIVADA ]  (ÚNICO controle global)

Processor Worker                           ● STATUS REAL
[ ▶ Executar uma vez ]
[ ■ Parar processo ]
[ ▤ Status do processo ]

Operations Maintenance Worker              ● STATUS REAL
[ ▶ Executar uma vez ]
[ ■ Parar processo ]
[ ▤ Status do processo ]

Bronze Maintenance Worker                  ● STATUS REAL
[ ▶ Executar uma vez ]
[ ■ Parar processo ]
[ ▤ Status do processo ]
```

**Não criar botão `Iniciar contínuo` nem `Desligar processo`.** A
transição do supervisor global para ON **já inicia automaticamente
os três processos contínuos**. O rótulo aprovado para a ação de
interrupção abrupta é **`Parar processo`**. Não criar alternativas
`Matar processo`, `Simular falha` ou `Parar contínuo`.

## Regra de habilitação

| Ação | Supervisão OFF — RunOnce | Supervisão ON — contínuo |
|---|---|---|
| **Executar uma vez** | Habilitado nos 3 workers, conforme pré-requisitos/concorrência já existentes. Mantém comportamento e testes anteriores. | Desabilitado em UI e recusado no backend. |
| **Parar processo** | Desabilitado sem processo a parar; durante transição OFF→ON, RunOnce ativos são interrompidos com confirmação prévia. | **Habilitado SOMENTE se o respectivo processo contínuo estiver realmente ativo, PID vivo verificado**. Encerra abruptamente aquele PID; supervisor reinicia apenas esse worker. Enquanto reiniciando/inativo fica desabilitado. |
| **Status do processo** | Habilitado, consulta RunOnce, PID/saída e supervisor OFF. | Habilitado, consulta PID, uptime, heartbeat, reinícios e estados de recuperação reais. |

A habilitação de `Parar processo` no modo ON também indica visualmente
que **aquele processo realmente subiu**. Esta indicação não substitui
o status real: PID por si só não prova heartbeat saudável nem trabalho
recuperado.

## Dois níveis de recuperação (não confundir)

1. **Supervisor externo:** quando ON e o operador pressiona
   `Parar processo`, encerrar **imediatamente** somente o PID
   escolhido (falha injetada, sem shutdown gracioso); o supervisor
   observa a morte e inicia outra instância **apenas daquele worker**.
   O estado passa por `PARANDO` / `REINICIANDO` / `ATIVO`.
2. **Worker reiniciado:** recupera **seu próprio processamento** por
   mecanismos existentes de leases, heartbeat, reaquisição/fencing,
   transações e idempotência. A conclusão da recuperação de lotes
   depende de prova operacional; reinício do PID não garante
   recuperação instantânea nem que o lease já expirou.

**Não** reiniciar NODE, API, Resultado, SQL ou os outros workers.
Quando a supervisão é desligada globalmente, o próprio controlador
desabilita reinícios e encerra os três residentes; o botão individual
`Parar processo` não é a forma de desligar permanentemente um worker.

## Preservação e segurança

Preservar intactos comandos, limites, códigos de saída e suítes RunOnce
da Console. Adicionar cobertura nova para supervisão e recovery, sem
considerar o teste histórico como prova de execução contínua.

As mudanças de modo são atômicas no backend: verificação isolada de PID
é insuficiente contra cliques simultâneos. UI lê estado efetivo, nunca
força OFF cosmético ao recarregar. Permitir apenas workers conhecidos
e ambiente DEV/`JornadaE2E` **descartável e isolado**.

Ver [DT-18](DT18_Servicos_Independentes_Console_DEV.md),
[DT-20](DT20_Supervisao_Opt_In_Workers.md) e
[DT-21](DT21_Testes_Resiliencia_Workers.md).

**Não tocar JornadaLocal, IBGE original, HML/PROD, volumes comuns
ou Trilha 4/reprocessamento de RESOLVIDOS.**

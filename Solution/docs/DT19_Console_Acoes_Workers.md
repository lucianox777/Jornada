# DT-19 — botões dos workers e apresentação da Console DEV

**Decisão refinada em 08/10/2026.** Esta versão substitui os layouts
anteriores, sem alterar os mecanismos RunOnce existentes. Estado:
**PENDENTE DE IMPLEMENTAÇÃO E TESTES**.

## Interface aprovada

Um **controle global** `Supervisão: DESATIVADA / ATIVADA` muda o modo
dos três executáveis: `Jornada.Processor.Worker`,
`Jornada.Operations.Maintenance.Worker` e
`Jornada.Bronze.Maintenance.Worker`. É único, não um toggle por worker.

Para **cada worker**, apresentar os controles **EMPILHADOS na vertical**,
não dispostos lado a lado em uma linha:

```text
Processor Worker                  ● ATIVO — PID ... / heartbeat ...
[▶ Executar uma vez]             (RUN_ONCE; OFF)
[🟢 Iniciar contínuo]              (CONTÍNUO; ON)
[🔴 Desligar processo]             (CONTÍNUO; ON e PID vivo)
[📊 Status do processo]            (sempre, read-only)
```

Os labels funcionais são `Executar uma vez`, `Iniciar contínuo`,
`Desligar processo` e `Status do processo`. A ação de desligar
corresponde ao **`Parar processo` anteriormente aprovado**, isto é,
encerrar imediatamente **somente** o PID do worker para ensaiar
recuperação; com supervisão ON, o supervisor reinicia esse worker
automaticamente. Não acrescentar outro botão `Matar processo`,
`Simular falha` ou `Parar contínuo` separado. Explicar por tooltip
que `Desligar processo` não desativa o modo supervisionado.

## Habilitação por modo e estado efetivo

| Ação | Supervisor OFF (padrão) | Supervisor ON |
|---|---|---|
| **Executar uma vez (RunOnce)** | Habilitado para os três workers, respeitados os pré-requisitos individuais e um processo finito já ativo. | **Desabilitado** na UI e **recusado pela API** para os três. |
| **Iniciar contínuo** | Desabilitado. | Habilitado **somente se o worker ainda não estiver ativo e sua inicialização for permitida**. Ao ligar supervisor ON, os três são iniciados automaticamente; normalmente este botão aparecerá desabilitado, com status ATIVO. |
| **Desligar processo** | Desabilitado para residentes; RunOnce em andamento é tratado pela transição global com confirmação. | **Habilitado somente quando aquele worker efetivamente subiu e tem PID verificável**. Encerra abruptamente a instância escolhida; supervisão reinicia apenas ela. Desabilitado enquanto parado/iniciando/reiniciando. |
| **Status do processo** | Habilitado, mostra OFF e últimas execuções finitas. | Habilitado, consulta PID, horário, heartbeat, reinícios e recuperação; sem inferir saúde só de PID. |

A presença de `Desligar processo` **HABILITADO** é também indicação
visual de que o worker está executando. O status explícito, contudo,
é fonte de verdade e distingue ATIVO, INICIANDO, RECUPERANDO e ERRO.

Os controles contínuos podem aparecer no layout enquanto OFF, mas
devem estar desabilitados e identificados como indisponíveis.
Ao ligar supervisor, desbloquear suas regras de habilitação,
**não** todos indiscriminadamente: `Desligar` depende de PID real.

## Restrições e testes existentes

Preservar os comandos, limites, códigos de saída e testes atuais de
RunOnce; os testes de Console/Chromium/SQL pré-existentes continuam no
CI sem reescrita. A nova implementação **acrescenta** apenas os
controles contínuos/supervisão e seus novos testes. Não é prova de que
E2E de continuidade foi executado.

Inícios simultâneos não podem duplicar workers; bloquear por operação
atômica no backend, não por mera leitura de PID. Browser só envia
comandos allowlisted e DEV-only. Nunca encerrar API/Resultado/SQL/NODE
ao matar um worker.

Mais detalhes na [DT-20](DT20_Supervisao_Opt_In_Workers.md) e
[DT-21](DT21_Testes_Resiliencia_Workers.md).
**Trilha 4, JornadaLocal, IBGE original e HML/PROD fora de escopo.**

# DT-20 — supervisor global como seletor de modo da Console DEV

**Decisão atualizada em 08/10/2026, após discussão com o operador.**
A semântica global abaixo **substitui a proposta anterior de toggles
por worker e de supervisão que alteraria apenas a política de restart**.
Estado: **PENDENTE DE IMPLEMENTAÇÃO E TESTES**.

## Controle único do conjunto de workers

Um único controle visível, inicializado como **DESATIVADO no perfil DEV
descartável ainda não iniciado**:

**Supervisão: DESATIVADA (modo RunOnce) / ATIVADA (modo contínuo)**

Controla conjuntamente **Processor**, **Operations Maintenance** e
**Bronze Maintenance**. Cada worker continua sendo processo/serviço
independente, supervisionado separadamente quando o modo está ON.
A independência impede que uma falha do Processor reinicie a API, os
demais workers ou o NODE. O controle global não mata nem inicia
`Jornada.Api`, `Jornada.Resultado.Api`, SQL nem serviços auxiliares.

## Matriz operacional aprovada

| Evento / estado | Supervisão DESATIVADA | Supervisão ATIVADA |
|---|---|---|
| Execução normal | Nenhum residente da Console, **RunOnce habilitado** nos três cartões. | **Os três residentes iniciados automaticamente**, e RunOnce desabilitado nos três cartões. |
| Clique no controle global | OFF → ON: parar **todos os processos dos três workers**, impedir novos RunOnce, habilitar supervisão e iniciar **os três** continuamente. | ON → OFF: desligar a política de restart **antes** de parar os três residentes; habilitar RunOnce após confirmar os encerramentos. |
| `Parar processo` no cartão | Mata apenas o RunOnce daquele worker, se estiver executando; sem restart. | Mata apenas o residente daquele worker, que deve ser reiniciado automaticamente. |
| `Status do processo` | Exibe processo finito/último RunOnce e modo efetivo OFF. | Exibe residente/heartbeat/restarts e modo efetivo ON. |
| Abriu/recarregou a página | Ler modo **efetivo** do backend. Um primeiro ambiente descartável inicia OFF. | Ler modo **efetivo** do backend; recarga não pode forçar OFF nem reiniciar os três. |

A transição ON inicia o trabalho contínuo por ação do operador:
**não existe botão separado `Iniciar contínuo` por worker**. O modo
OFF permite iniciar RunOnce individualmente. Alternar ON com algum
RunOnce ainda ativo exige confirmação explícita para interrompê-lo
abruptamente; a ação deve informar quais processos serão atingidos.

## Máquina de estados e atomicidade

Estados mínimos: `RUN_ONCE`, `ATIVANDO_CONTINUO`,
`CONTINUO`, `DESATIVANDO_CONTINUO`, `ERRO`.
O backend deve bloquear operações incompatíveis durante as transições,
serializar mudanças simultâneas e reportar **estado real por worker**.
A UI não declara CONTINUO até os três residentes atingirem o critério
de saúde verificável; se um falhar, apresentar `ERRO`, nunca falso
verde ou habilitar ambos os modos ao mesmo tempo.

**ON:** bloquear RunOnce → desabilitar restart para neutralizar
instâncias antigas → encerrar workers existentes identificados
→ configurar restart por worker → iniciar os três → verificar que
estão ativos e monitorados. Não derrubar instâncias fora do ambiente
descartável. Em caso de falha parcial, manter interface fail-closed e
exigir reconciliação segura (sem apagar estado do banco).

**OFF:** bloquear novos inícios contínuos → desabilitar restart dos
três → encerrar instâncias residentes → confirmar ausência dos três
→ habilitar RunOnce. O modo OFF não é só uma alteração cosmética
de política: encerra efetivamente a execução contínua.

Um `RunOnce` nunca passa para o pool de supervisão nem sofre restart
automático após saída normal ou falha.

## Limites

A política é aplicada pelo gerenciador externo dos **três serviços
allowlisted**, mas acionada por um único comando da Console. O
backend recebe somente valores enumerados e não expõe shell ou
Docker socket ao navegador. Estado inicial OFF é garantido pelo
perfil descartável, **não** inferido de aparência padrão da página.

Nenhuma ação executará sobre `JornadaLocal`, IBGE original,
volumes compartilhados, HML/PROD ou outros processos do usuário.
Trilha 4 continua suspensa.

## Aceite

Validar a máquina de estados OFF → ON → OFF, morte e reinício isolado
sob ON, ausência de restart sob OFF, corrida de toggles e reinício da
própria Console, com evidência real em `JornadaE2E`. Ver
[DT-19](DT19_Console_Acoes_Workers.md) e
[DT-21](DT21_Testes_Resiliencia_Workers.md).

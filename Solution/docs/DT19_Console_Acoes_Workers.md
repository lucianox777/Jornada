# DT-19 — comandos operacionais e modos mutuamente exclusivos da Console DEV

**Decisão atualizada em 08/10/2026.** A decisão posterior do operador sobre
o supervisor **substitui** a proposta de quatro ações independentes por worker.
Estado: **PENDENTE DE IMPLEMENTAÇÃO E DE ACEITE**. Não confundir esta decisão
com funcionalidade já disponível no master.

## Interface final

A Console apresenta **um único controle global** `Supervisão automática`
(ATIVADA/DESATIVADA), aplicável em conjunto a estes três workers:
`Jornada.Processor.Worker`, `Jornada.Operations.Maintenance.Worker` e
`Jornada.Bronze.Maintenance.Worker`. Não abrange API, Resultado.Api,
SQL ou serviços de infraestrutura.

Cada worker mantém **três ações individuais**, conforme o modo:

| Ação por worker | Supervisão DESATIVADA | Supervisão ATIVADA |
|---|---|---|
| **Executar uma vez (RunOnce)** | **HABILITADA** para iniciar ciclo finito, sujeita somente a pré-requisitos e exclusão de instância já ativa. | **DESABILITADA**; nunca executar em paralelo com o worker residente. |
| **Parar processo** | Pode interromper um RunOnce ativo, sem reinício automático. Não há processo contínuo esperado. | Mata abruptamente **só o worker escolhido**; o supervisor reinicia apenas esse worker. |
| **Status do processo** | Mostra estado das execuções finitas, PID e saídas reais. | Mostra PID residente, uptime, heartbeat, reinícios, saúde/recuperação reais e estado da supervisão. |

**Não criar** o botão individual `Iniciar contínuo`: passou a ser
redundante, porque a transição global para supervisão ATIVADA inicia
**todos os três** workers continuamente. Também não criar os botões
`Desligar`, `Parar contínuo`, `Matar processo` ou `Simular falha`.
O nome final aprovado permanece **Parar processo**.

O controle global é o **quarto controle lógico**, além das três ações
apresentadas em cada cartão. Não existem três toggles independentes.

## Transições do modo de execução

- **Início padrão do DEV isolado: supervisão DESATIVADA**, nenhum dos
  três workers residente controlados pela Console e RunOnce habilitado
  em cada cartão (desde que pré-requisitos reais estejam satisfeitos).
- **DESATIVADA → ATIVADA:** bloquear novos RunOnce imediatamente; fazer
  a transição controlada de **todos os três processos de worker**,
  encerrando instâncias já existentes, inclusive RunOnce em andamento
  **somente após confirmação explícita do operador**. Preparar política
  de reinício e subir os **três residentes contínuos**. Confirmar o
  estado real de todos antes de apresentar ATIVADA/OPERACIONAL.
- **ATIVADA → DESATIVADA:** primeiro desabilitar reinícios automáticos
  dos três workers, depois encerrar suas instâncias residentes e
  confirmar que permaneceram paradas. Habilitar RunOnce para cada worker.
  Nenhuma API/Resultado/SQL ou worker de outro ambiente é encerrado.
- **ATIVADA, Parar processo:** SIGKILL só no PID/instância selecionada;
  o supervisor deve restaurar apenas aquele worker automaticamente.
  Nunca derrubar o NODE nem os outros workers.
- **DESATIVADA, Parar processo:** encerrar somente a execução finita
  ativa (quando existir), sem iniciar outra.

Uma transição parcialmente executada é **ERRO/TRANSIÇÃO INCOMPLETA**, não
um sucesso aparente. O backend é fonte da verdade: recarga do navegador
não pode mentir que está DESATIVADA se a supervisão real continua ATIVADA.

## Segurança e concorrência

As mudanças de modo e ações são serializadas por trava atômica
e por identidade estável do ambiente/serviço. Em modo ON, RunOnce é
recusado também no **backend** (não basta desabilitar botão). Em modo
OFF, processos contínuos não podem ser criados por rota alternativa.
Inícios repetidos/toggles concorrentes são idempotentes ou rejeitados,
nunca duplicam workers.

Matar os processos existentes antes de ativar o modo contínuo **não**
autoriza parar serviços API/Resultado ou containers compartilhados.
Configuração fail-closed somente DEV + `JornadaE2E` descartável, sem
`JornadaLocal`, referência IBGE original, HML/PROD nem Trilha 4.

## Evidência exigida

A UI Chromium, API e E2E devem provar estado OFF inicial, RunOnce por
worker, troca ON com três workers residentes e RunOnce indisponível,
Parar processo individual com reinício isolado, troca OFF e execução
finita novamente. Status deve representar estado efetivo, não resposta
fictícia à chamada HTTP. Veja [DT-20](DT20_Supervisao_Opt_In_Workers.md)
e [DT-21](DT21_Testes_Resiliencia_Workers.md).

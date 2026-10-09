# DT-20 — ativar ou desativar supervisor por worker na Console DEV

**Decisão adicional aprovada em 08/10/2026 (escopo DEV).** Estado:
**PENDENTE**, sem ativação implícita em ambiente existente.

## Controle solicitado

Além das **quatro ações** da DT-19, apresentar em cada cartão do worker
um **controle único de alternância**:

**Supervisão automática: ATIVADA / DESATIVADA**
(`Ativar supervisão` / `Desativar supervisão`).

É uma quinta interação visual (toggle), não uma substituição de
`Executar uma vez`, `Iniciar contínuo`, `Parar processo` ou
`Status do processo`. O operador deve conseguir **escolher**
se deseja ou não o reinício automático durante os ensaios de falha.

## Máquina de estados

| Supervisão | Ação | Resultado esperado |
|---|---|---|
| ATIVADA | `Parar processo` com worker CONTÍNUO ativo | Mata PID real; supervisor reinicia **somente aquele worker** automaticamente; status informa PID novo e reinícios. |
| DESATIVADA | `Parar processo` com worker CONTÍNUO ativo | Mata PID real; worker **permanece parado** até novo `Iniciar contínuo`. Demais serviços seguem ativos. |
| ATIVADA | `Executar uma vez` | RunOnce termina sem reinício: supervisão rege **somente a instância contínua**. |
| DESATIVADA | `Iniciar contínuo` | Inicia o worker para trabalhar, mas uma morte subsequente NÃO o reinicia. |
| Qualquer | `Status do processo` | Expõe estado do worker **e estado efetivo da supervisão**, não apenas desejo da UI. |

Alterar a supervisão de ATIVADA para DESATIVADA **não mata** o processo
que já esteja executando; apenas remove a política de reinício futuro.
Ativar supervisão com worker parado **não o inicia por surpresa**:
`Iniciar contínuo` continua ação explícita para isso. Ativar a
supervisão de processo já ativo passa a vigorar para mortes futuras.

No ambiente de testes, o estado inicial da política de supervisão
é **DESATIVADA até o operador escolher**, sem alterar a configuração
preexistente do cluster comum. Se o backend orquestrador tiver uma
política divergente, a Console deve reportá-la e **recusar alterações**
até identificar serviço/escopo de forma segura.

## Implementação/observabilidade

O supervisor externo continua responsável por reiniciar processos.
O toggle modifica apenas a **política de reinício da instância do
worker allowlisted**, de forma restrita a ambiente DEV descartável,
com leitura posterior do estado efetivo. Não expor Docker socket
nem comandos arbitrários ao navegador. Evitar corrida entre
`start`, `kill` e troca de supervisão: transições atômicas,
identidade estável de serviço e verificações de estado pós-ação.
Recuperação de trabalho, leases e idempotência permanecem no worker,
conforme DT-18.

Testar ambos os estados do toggle, incluindo desligar a política com
worker vivo, kill sem restart, reativação sem start implícito,
início explícito e kill com restart. **Todas as operações reais
limitadas ao JornadaE2E descartável**.

Não autoriza alteração de HML/PROD, JornadaLocal, volumes reais ou
Trilha 4.

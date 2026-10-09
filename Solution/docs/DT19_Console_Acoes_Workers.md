# DT-19 — ações de controle dos workers na Console DEV

**Decisão técnica aprovada em 08/10/2026 (escopo DEV).** Estado: **PENDENTE**,
sujeito a PRs, testes reais e merge.

## Nomes finais aprovados e semântica

Cada um dos **três workers** definidos em DT-18 terá quatro ações com estes
nomes exatos:

| Ação | Contrato |
|---|---|
| **Executar uma vez** | RunOnce finito, código de saída real e limites de tempo; não manter residente ao terminar. |
| **Iniciar contínuo** | Inicia um worker residente quando ausente, sem duplicar instâncias; vários cliques concorrentes devem produzir no máximo uma instância. |
| **Parar processo** | **Mata abruptamente** somente a instância atual do worker (sem parada graciosa), para observar resiliência. Se supervisão estiver ATIVADA, o worker deve reiniciar automaticamente; se DESATIVADA, permanecer parado. |
| **Status do processo** | Consulta observável de PID, estado atual, início/uptime, heartbeat, última falha, contagem de reinícios e sinais de recuperação que realmente existirem. Nunca rotular um processo de saudável apenas por ter PID. |

Não criar os botões `Parar contínuo`, `Desligar`, `Simular falha`
nem `Matar processo`: o nome final da ação destrutiva de teste é
**Parar processo**. A ação NÃO é uma ordem administrativa de desativar
permanentemente o serviço. Antes de matar, exibir confirmação e o
identificador do worker afetado.

**Distinção adicional:** o controle independente de supervisão, aprovado
posteriormente, fica documentado na **DT-20**: é um toggle para habilitar
ou desabilitar reinício automático, **não** altera o nome/semântica
destas quatro ações. Portanto a UI terá quatro ações mais **um toggle
de supervisão** por worker.

## Restrições de concorrência

RunOnce e worker residente conflitantes não podem operar em paralelo
no mesmo alvo. `Iniciar contínuo` é idempotente. Verificação de PID
isolada é insuficiente para dois pedidos simultâneos: serializar
decisão e transição sob trava atômica no controlador autorizado.
O `RunOnce` jamais entra em laço de reinício automático por acidente.

Ações DEV por allowlist fechada de worker, sem parâmetros livres do
navegador e sem executar comandos de shell arbitrários.

## Prova

Aceite com Chromium real para cada ação, estados habilitados/desabilitados,
erros HTTP recuperáveis, reinício/reconexão da Console, tentativas
concorrentes, proteção de RunOnce e leitura real de status. A interface
não deve afirmar êxito com base só na resposta HTTP de início.

**Trilha 4 proibida; não tocar JornadaLocal, referência IBGE ou HML/PROD.**

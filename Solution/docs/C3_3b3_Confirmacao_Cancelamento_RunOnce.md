# C3.3b3 — Interromper RunOnce ativo com confirmação e preservar a supervisão

**Estado:** preparação técnica, sem PR ou aceite operacional. A implementação
deve ser baseada na *master que contiver a PR #853*; não usar esta branch
para trocar o HEAD da PR #853 enquanto seus workflows estão rodando.

## Condições de entrada

C3.3b1 já permite ON↔OFF somente no Compose GitHub-hosted descartável;
C3.3b2 (PR #853) introduz RunOnce isolado para Processor,
OperationsMaintenance e BronzeMaintenance. Esta etapa não pode considerar
o término de uma chamada HTTP como prova de término de processo: existe
uma camada Python e um contêiner Docker Compose `oneoff=True` real.

Operações somente no projeto
`jornada-workers-e2e-ci<GITHUB_RUN_ID><GITHUB_RUN_ATTEMPT>`,
com labels de projeto/serviço/oneoff validadas, modo DEV,
`JornadaE2E`, APIs e SQL privados, sem host ports e sem bind mounts.
Nenhuma ação atinge Docker de usuário, NODE canônico, `JornadaLocal`,
IBGE original, HML/PROD ou volumes/contêineres preexistentes.

## Contrato transacional do controlador

1. O backend associa a cada RunOnce um **ID intransferível de execução**,
   `worker`, horário de início, PID/container ID real e um
   `CancellationTokenSource` distinto do cancelamento da requisição
   HTTP. Uma desconexão do navegador **não pode** liberar a exclusão
   e deixar o contêiner one-off em execução sem rastreamento.
2. `SemaphoreSlim` único de admissão serializa: novo RunOnce, OFF↔ON e
   solicitação de cancelamento. Um único RunOnce pode estar ativo por
   projeto. Não confiar apenas no dicionário da memória para permitir
   ON: também confirmar **zero** one-offs pelas labels Docker reais.
3. ON durante RunOnce ativo deve retornar **409** contendo a necessidade
   de confirmação e o ID opaco da execução. Não iniciar workers
   residentes, não encerrar RunOnce e não alterar a política de restart
   automaticamente.
4. Somente ação **separada e explicitamente confirmada pelo operador**
   pode solicitar a interrupção. O token/ID precisa corresponder à
   execução ativa do projeto privado, ser de uso único e expirar.
   Não adotar `cancel=true` implícito no mesmo POST ON.
5. Cancelamento: sinalizar encerramento cooperativo primeiro e aguardar
   prazo limitado. Se o processo não sair, encerrar **somente o
   contêiner one-off identificado e validado pelas labels exatas**,
   nunca o serviço residente ou infraestrutura. Registrar PID, ID,
   `exitCode`, evento de encerramento, tentativas e motivo
   `CANCELLED`/ `INCOMPLETE`; não forjar status `CONCLUIDO`.
   Um `docker compose run --rm` ainda em curso exige coleta de
   saída/remoção comprovada mesmo após interrupção do cliente.
6. Enquanto houver one-off vivo, desligando, órfão ou com proveniência
   ambígua, o modo global continua **OFF** ou **ERRO**, jamais ON. A
   reativação só pode ocorrer depois de demonstrados **zero one-offs**,
   zero RunOnce em memória e três residentes efetivamente parados.
7. Somente após a confirmação explícita e encerramento realmente
   observado pode-se oferecer ON como **segunda ação**. Preservar
   PID/RestartCount/health de SQL, API e ResultadoApi em todas as etapas.

## Cenários E2E obrigatórios no mesmo runner

- Desencadear RunOnce de teste com duração determinística limitada
  dentro do `JornadaE2E` sem alterar o comportamento do produto fora
  do Compose privado.
- Concorrência: duas solicitações RunOnce simultâneas → uma admitida,
  a outra HTTP 409; ON → 409/sem efeitos; comando GET mostra
  `RUN_ONCE` do trabalhador e modo global OFF.
- Desconectar o cliente HTTP durante a execução → backend mantém
  identificação e bloqueia ON; nenhum processo sem acompanhamento.
- Requisitar cancelamento sem confirmação ou com token incorreto,
  expirado, de outra execução → 409, zero efeitos.
- Confirmar interrupção com token válido → registrar encerramento real,
  nenhuma duplicação de interrupção, ausência de one-off, consulta SQL
  do heartbeat/estado pertinente, persistência de logs; nenhuma
  promessa falsa de trabalho concluído.
- Reativar ON em segunda ação somente após desligamento confirmado,
  três PIDs/heartbeats residentes renovados, SQL/API/Resultado estáveis.
- Testar falha de remoção one-off → ON negado, sem `docker prune`,
  `down -v` ou afrouxamento do gate. Segurança falha-fechada.

## Limites

Esta especificação **não é** implantação, teste executado, merge,
nem prova de cancelamento. C3.3c `Parar processo` de worker residente,
C3.4 UI Chromium com dois botões verticais por trabalhador,
e DT-10 separação da CI continuam pendentes. Trilha 4 suspensa.

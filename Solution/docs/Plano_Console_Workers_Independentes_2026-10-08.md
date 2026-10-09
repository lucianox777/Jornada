# Plano Console DEV — supervisor global, workers independentes e RunOnce preservado

**Decisão vigente de 08/10/2026.** O operador confirmou **dois níveis
distintos de recuperação**: o supervisor deve **reiniciar
automaticamente o executável morto**, e o worker reiniciado deve
**recuperar por si próprio o processamento elegível**. Não há botão
separado de início contínuo: ligar a supervisão já inicia os três
workers. Nenhum mecanismo RunOnce ou teste existente será removido.

## Decisões formalizadas

- [DT-18 — serviços independentes](DT18_Servicos_Independentes_Console_DEV.md):
  a morte de um worker não reinicia NODE/API/Resultado/demais workers.
- [DT-19 — botões e layout](DT19_Console_Acoes_Workers.md):
  **três botões individuais empilhados verticalmente** por worker.
- [DT-20 — supervisor global](DT20_Supervisao_Opt_In_Workers.md):
  ON inicia os três automaticamente; Parar processo com ON
  provoca **reinício automático só daquele worker**.
- [DT-21 — testes de aceitação](DT21_Testes_Resiliencia_Workers.md):
  prova de reinício + recuperação de lote, sem duplicação.

## Interface e máquina de estados

```text
[ SUPERVISÃO AUTOMÁTICA: DESATIVADA | ATIVADA ]  (GLOBAL)

Processor Worker                   Estado/PID/heartbeat reais
  [Executar uma vez]
  [Parar processo]
  [Status do processo]

Operations Maintenance Worker      Estado/PID/heartbeat reais
  [Executar uma vez]
  [Parar processo]
  [Status do processo]

Bronze Maintenance Worker          Estado/PID/heartbeat reais
  [Executar uma vez]
  [Parar processo]
  [Status do processo]
```

**OFF inicial no DEV descartável:** RunOnce dos três disponível,
conforme seus pré-requisitos atuais; nenhum residente controlado pela
Console; Parar processo desabilitado sem PID contínuo; Status sempre
consultável. Preservar **todas as rotas/testes atuais de RunOnce**.

**OFF→ON:** bloquear novos RunOnce na UI e no backend, avisar/confirmar
caso estejam ativos, **matar/encerrar RunOnce em andamento**, aguardar
ausência, habilitar restart para cada worker e iniciar **automaticamente
os três residentes**. Marcar ON como operacional somente após
comprovar saúde dos três. O botão individual `Parar processo` é
**habilitado somente se aquele worker realmente estiver rodando**
(PID vivo), servindo de indicação adicional de processo ativo.

**ON, Parar processo:** matar imediatamente **somente aquele PID**.
O supervisor externo detecta a saída e reinicia **aquele mesmo worker
com novo PID sem outro clique**. O worker reiniciado retoma trabalho
elegível por lease/heartbeat/rollback/idempotência (a recuperação
pode levar até o lease vencer). Status distingue **REINICIADO** de
**RECUPERAÇÃO CONCLUÍDA**, sem assumir que PID novo significa lote
completo. Outros workers e APIs não devem reiniciar.

**ON→OFF:** desarmar primeiro os três restarts, encerrar residentes,
validar ausência e reabilitar RunOnce dos três. Transições
concorrentes/falhas parciais não permitem dois modos simultâneos;
mostrar `ERRO` quando não houver estado consistente. Reload da
Console consulta estado efetivo no backend, não inventa OFF.

Não criar botões `Iniciar contínuo`, `Desligar processo`,
`Matar processo`, `Simular falha` ou `Parar contínuo`.
**O único botão que mata um worker é `Parar processo`**. Quando
ON, é injeção de falha, não desligamento permanente.

## Ordem de implementação / merges

1. **C3.1 / PR #841:** entrypoint de worker individual e guardas
   de ambiente DEV `JornadaE2E`, sem ativação do cluster legado;
   documentação DT-18–21 e testes não destrutivos.
2. **C3.2:** três serviços independentes no projeto Compose
   **descartável**, isolados de volumes/rede/banco do cluster normal,
   com restart individual sob supervisor externo.
3. **C3.3:** backend da Console controla modo global OFF↔ON,
   preserva RunOnce e acrescenta Parar/Status individuais,
   com concorrência atômica e validação efetiva de PID.
4. **C3.4:** interface vertical e Chromium/E2E das transições,
   SIGKILL e recuperação de dados reais **sintéticos**.

Merge somente com todos os gates obrigatórios completed/success no
HEAD exato e PR mergeable, com checagem horária configurada. Não
reiniciar CI por lentidão. Nenhuma operação sobre `JornadaLocal`,
IBGE original, HML/PROD, volumes compartilhados nem Trilha 4
(reprocessamento automático de RESOLVIDOS permanece proibido).

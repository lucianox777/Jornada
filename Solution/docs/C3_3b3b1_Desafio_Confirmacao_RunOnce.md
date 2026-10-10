# C3.3b3b1 — Identidade efêmera e desafio de confirmação do RunOnce

**Estado do incremento:** implementação submetida a PR/CI; **não** é
cancelamento funcional concluído. A conclusão da
[C3.3b3 (contrato completo)](C3_3b3_Confirmacao_Cancelamento_RunOnce.md)
exige depois uma operação separada de interrupção **explicitamente
confirmada**, prova do exit real, zero Docker one-offs e testes E2E.
Esta etapa evita criar uma operação de interrupção que poderia matar
um contêiner errado ou ser invocada implicitamente pelo toggle ON.

## Implementação delimitada

- A admissão serializada de `RunOnceAsync` registra um
  `runId` aleatório imutável (UUID) e um
  `confirmationNonce` criptograficamente aleatório de 256 bits,
  formato base64url de 43 caracteres, com validade de **2 minutos**.
  Identidade e token existem **apenas em memória do processo da
  Console DEV transitória**, não em SQL, logs ou dados de cidadãos.
- Somente enquanto o `worker` permitido estiver marcado como
  `RUN_ONCE`, com modo real OFF comprovado por Docker e SQL,
  `GET /api/workers/{worker}/run-once/cancel-challenge`
  devolve `runId`, `worker`, `confirmationNonce`,
  `expiresAtUtc`. A rota é restrita a **GitHub Actions DEV
  descartável** e HTTP **loopback**, com `Cache-Control: no-store`.
- Worker diferente, finalização do processo, expiração do desafio
  ou modo não comprovadamente OFF → **409 sem efeitos**. A etapa
  `RunOnceAsync` invalida o registro assim que o trabalho termina.
  A verificação pós-leitura evita retornar um desafio se a execução
  tiver terminado **durante** a inspeção Docker/SQL.
- Nenhum código deste incremento **consome** o nonce ou executa
  `docker stop`, `docker kill`, mudança de restart policy ou
  confirmação automática. O POST ON existente **continua recusando**
  durante RunOnce; fechar navegador **não o cancela**.

## Aceite incremental

O E2E operacional **já existente** C3.3b3a no projeto Compose
`jornada-workers-e2e-ci<run><attempt>` continua iniciando
Processor **real** em oneoff=True, prova PID e estado `RUN_ONCE`,
recebe desafio válido **durante** a execução finita e verifica que:
o mesmo desafio não é concedido para outro worker;
desconexão HTTP não desarma a supervisão; ON retorna 409;
o one-off termina com `--rm`; desafio da execução terminada
retorna 409; novo ON somente após conclusão real. **O token
não é gravado em summary, artifact nem stdout.**

O contrato offline é validado no job Unit já existente em
`scripts/test-console-runonce-disconnect-contract.py`, cobrindo
geração, expiração, escopo, invalidacão, isolamento GitHub e
ausência de rota de cancelamento mutável.

**Próximo incremento C3.3b3b2:** introduzir confirmação voluntária
em chamada HTTP separada com igualdade de ID/nonce, consumo
único e expiração; encerrar cooperativamente o **one-off exato**
com labels de projeto+serviço+oneoff validadas, observar saída
Docker real e ausência de órfãos, registrar CANCELADO/INCOMPLETO
sem fabricar `CONCLUIDO`, só então permitir segunda chamada
ON. Exige novo E2E com dados sintéticos e aprovação dos gates
da HEAD exata. Nenhum mecanismo atual deve interpretar o desafio
somente como autorização de encerramento.

**Isolamento absoluto:** nunca `JornadaLocal`, IBGE original,
NODE/Compose canônico, HML/PROD, volumes ou contêineres reais
do usuário. Somente GitHub Actions efêmero `JornadaE2E`.
A DT-22 de reavaliação de RESOLVIDOS permanece **por último**.

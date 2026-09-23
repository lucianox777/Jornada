# Ensaio de cluster no banco original — verificação não destrutiva

O banco `JornadaLocal` é adequado para testar NODE1/NODE2, concorrência
e a Bronze NAS. O ensaio completo de três ondas ainda usa Bronze
temporária local e executa uma limpeza operacional destrutiva.
**Não aponte `local-synthetic-calibration.ps1` para o original.**

## Preflight sem alterar registros

Com o cluster já iniciado, na pasta `Solution`:

```powershell
.\scripts\local-cluster-readiness.ps1
```

Por padrão, lê `.env` original e ignora `JORNADA_LOCAL_ENV_FILE`
remanescente da sessão sintética. Valida DB=JornadaLocal, marcador
Development, schema, bundle, heartbeats de API/Processor e readiness.
NODE1 cria marcador efêmero fora da árvore `sha256/` do volume NAS;
NODE2 lê e atualiza, NODE1 confere, e o arquivo é removido em finally.
Não cria Entregas, altera tabelas, ativa modelos nem exclui a
quarentena histórica. Um FAIL de infraestrutura não mede linkage.

## Etapas que ainda não estão comprovadas

1. Concluir o ensaio isolado de calibração e três ondas, sem afrouxar
   o holdout TEST. O último ensaio isolado já chegou à calibração,
   mas o candidato falhou no gate de falsos positivos.
2. Fazer canário de ingestão **não destrutivo** no cluster original:
   ZIP único em Bronze NAS, envio pela API NODE1, captura de
   lease_owner/processador que o consumiu, verificação de hash
   e estado terminal, sem limpeza da base existente.
3. Implementar universo sintético segregado por run_id para
   geração de RASCUNHO e avaliação por ondas, sem contaminar
   parâmetros ou métricas com dados não sintéticos do original.
4. Medir idempotência em Silver, Gold e serving por fingerprint
   e identificador de origem, verificando escrita apenas por mudança.

O comando `local-cluster.ps1 calibrate` não faz parte do ensaio:
ele pode VALIDAR/ATIVAR modelo, enquanto a avaliação sintética deve
permanecer RASCUNHO com MODEL_VALIDATION sem publicação.

## Compatibilidade PowerShell 5.1

O preflight agora inspeciona as conexões efetivas dos dois contêineres com `docker inspect` e verifica o compartilhamento NAS bidirecional com `docker cp`, sem comandos `sh -c` passados pelo Windows. Os testes de regressão executam um Docker simulado no Windows PowerShell 5.1; a prova com contêineres reais continua sendo o comando local acima. Não há alteração SQL nem execução de cargas sintéticas neste preflight.

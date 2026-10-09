# C3.3b3a — RunOnce continua pertencendo ao servidor após desconexão HTTP

**Escopo deste incremento:** corrigir o risco de o navegador abandonar a
requisição e o backend liberar a exclusão enquanto um contêiner finito ainda
está executando. Esta etapa **não implementa ainda o cancelamento confirmado**;
isso permanece C3.3b3b.

## Mudança no backend

A rota `POST /api/workers/{worker}/run-once` continua aceitando somente os
três trabalhadores em `OFF`, no projeto Compose privado validado.
Anteriormente passava o token de cancelamento da **requisição HTTP** para
o processo da Console: fechar a conexão podia matar o subprocesso controlador
e liberar a marcação `RUN_ONCE` cedo demais, deixando um contêiner Docker
one-off sem acompanhamento.

Agora o processo finito recebe o `ApplicationStopping` da Console (não o
`RequestAborted`): abandonar a página **não cancela** o trabalho, não remove
a exclusão antecipadamente e não ativa os residentes. A conclusão continua
dependendo do exit code real do .NET, das labels Docker, da remoção do
one-off e do estado SQL e APIs. Em desligamento da aplicação, permanece
a proteção fail-closed: uma execução órfã detectada em Docker impede ON.

Nenhum comando externo de parada, limpeza ou reparação automática é
adicionado. A Console continua restrita ao GitHub-hosted CI descartável
com `JornadaE2E` e chamadas loopback.

## Prova E2E real e isolada

Depois dos testes C3.3b2 existentes, o **mesmo** E2E:

1. Começa em ON e usa a própria Console temporária para pedir OFF.
2. Envia POST de RunOnce ao Processor por um socket HTTP real.
3. O worker finito criado por `docker compose run --rm` contém um
   `sleep 8` temporário **somente com opt-in CI** e antes do
   `exec dotnet`, permitindo comprovar simultaneamente a linha
   `RUN_ONCE` no GET e o contêiner verdadeiro
   `com.docker.compose.oneoff=True`, com PID vivo e política
   `restart=no`. Não altera o worker em produção nem o residente.
4. Encerra a conexão TCP **durante** esse intervalo.
5. Novo GET deve continuar mostrando `RUN_ONCE` e modo global OFF.
   POST ON deve retornar HTTP 409 **sem efeito**.
6. Aguarda saída real do RunOnce e remoção do one-off (sem forçar
   parada), confirma três residentes ainda PARADO.
7. Somente então faz POST ON, confirma três residentes ATIVO,
   PID/heartbeat SQL e API/Resultado/SQL disponíveis.

Artefatos: `Solution/.local/e2e/c3-3b3a-disconnect/summary.json`
e log da Console transitória. O teste não usa novos builds ou jobs.
Contratos negativos baratos no job Unit continuam recusando execução
fora do GitHub DEV efêmero.

## Ainda falta para C3.3b3

O operador ainda não consegue pedir explicitamente a interrupção com
token de confirmação, nem existe tentativa cooperativa de finalizar
one-off interrompido. C3.3b3b exigirá esse mecanismo, com ID de
execução, token descartável de confirmação, eventos Docker e prova de
zero processos órfãos antes de rearmar ON. C3.3c implementará
`Parar processo` individual de residente; C3.4 implementará a UI
Chromium com o toggle global e dois botões verticais por worker.

**Segurança:** nunca usar o banco `JornadaLocal`, dados IBGE originais,
NODE Compose canônico ou qualquer contêiner/volume de usuário; somente
GitHub Actions efêmero `JornadaE2E`. Não há `docker prune`,
`down -v`, host ports, bind mounts nem comandos externos sem allowlist.
DT-10 divisão CI por último; Trilha 4 suspensa.

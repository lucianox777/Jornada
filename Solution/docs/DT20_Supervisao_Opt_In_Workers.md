# DT-20 — supervisor GLOBAL, modo RunOnce versus processos contínuos

**Decisão refinada em 08/10/2026 pelo operador.** Estado
**PENDENTE**: esta especificação não alega implementação/aceite.
Prevalece sobre versões anteriores do toggle por worker ou sem
botões de processo contínuo.

## Comportamento do supervisor

Um único toggle global `Supervisão: DESATIVADA / ATIVADA` controla
os três workers independentes (Processor, Operations Maintenance,
Bronze Maintenance). Ele inicia em **DESATIVADA** no novo ambiente
DEV `JornadaE2E` descartável.

### OFF — RunOnce

- **Todos os botões `Executar uma vez` habilitados** nos três
  workers, sujeitos aos pré-requisitos reais do comando.
- Nenhum dos três processos residentes contínuos gerenciados fica
  ativo. `Iniciar contínuo` e `Desligar processo` ficam
  desabilitados. `Status do processo` continua disponível.
- O fluxo e os testes RunOnce existentes permanecem intactos.

### OFF → ON — supervisor ativado

1. Bloquear imediatamente novos RunOnce **na UI e no backend**.
2. Localizar todas as instâncias ativas de RunOnce dos três workers,
   informar quais serão interrompidas e exigir confirmação explícita
   caso existam.
3. **Matar/encerrar esses RunOnce** antes de iniciar residentes e
   confirmar ausência. Não matar processos API, Resultado, SQL,
   serviço de outro perfil nem contêiner NODE inteiro.
4. Habilitar política de reinício **independente por worker** e
   iniciar **automaticamente os três processos contínuos**.
5. Liberar os controles de modo contínuo nos cartões dos três
   workers. `Iniciar contínuo` fica habilitável somente se algum
   serviço estiver efetivamente parado; `Desligar processo` fica
   **habilitado somente depois que o respectivo processo subir**,
   tendo PID/estado verificado. A habilitação de Desligar funciona
   como indicação visual complementar de execução.
6. Só marcar supervisor `ATIVADA / OPERACIONAL` após confirmar a
   subida dos três. Falha parcial = `ERRO`, não sucesso.

### ON — supervisão contínua

- `Executar uma vez` permanece desabilitado nos três cartões.
- Cada worker tem seu processo independente. `Desligar processo`
  encerra **imediatamente apenas aquele processo** (falha injetada,
  sem parada graciosa); o supervisor reinicia **só ele**.
  Durante reinício, Desligar fica desabilitado e o status informa
  INICIANDO/RECUPERANDO.
- `Iniciar contínuo` pode ser usado se houver instância ausente
  que não esteja reiniciando e cujo estado permita intervenção;
  cliques repetidos não duplicam instância.
- `Status do processo` consulta PID real, uptime, heartbeat,
  reinícios, última falha e sinais de recuperação, sem inventar
  contagens de lotes para outros workers.

### ON → OFF — supervisor desativado

1. Bloquear ações concorrentes e **desarmar primeiro** as três
   políticas de reinício.
2. Encerrar os três workers residentes e confirmar que não subiram
   novamente.
3. Desabilitar botões contínuos e reabilitar `Executar uma vez`
   para os três. `Status do processo` permanece disponível.

## Princípios de implementação

**O toggle é global, mas a supervisão/restart é individual.** O
gerenciador externo (Docker/serviço) cuida da retomada do executável;
o próprio worker cuida de trabalho, leases, heartbeat e idempotência.
A Console não se torna um novo supervisor de processos.

Estado efetivo é consultado no backend ao abrir/recarregar a página:
se a Console reiniciar e os serviços estiverem ON, não apresentar
OFF falso. Guardas atômicas impedem RunOnce em ON, start contínuo
em OFF, duplicação por cliques e transições simultâneas. Estado
intermediário/falha parcial é explícito e **não** libera os dois
modos em paralelo.

`Desligar processo` no modo ON é a ação de falha/recuperação
anteriormente chamada `Parar processo`; **não é desativação
administrativa permanente**. Para parar normalmente todos os três
residentes e voltar ao modo manual, desligar supervisor global.

Somente no perfil DEV `JornadaE2E` descartável, com allowlist
de processos e validação de ambiente/recurso. Proibido atingir
`JornadaLocal`, IBGE original, HML/PROD ou Trilha 4.

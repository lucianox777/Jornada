# DT-20 — supervisão GLOBAL e reinício automático por worker

**Revisão de estado — 09/10/2026:** a arquitetura foi implementada
**no ambiente DEV/CI isolado**, com estado efetivo GET (#851), toggle
global ON↔OFF e desarme de restart (#852), três RunOnce (#853),
sobrevivência à desconexão do navegador (#854), parada individual
(#855) e painel (#856). `C3.2f2c` (#850) comprovou a retomada do lote
sintético no SQL `JornadaE2E`. A transição **ON com RunOnce ativo
não o mata silenciosamente**: o backend recusa o comando; a etapa de
**cancelamento explicitamente confirmado** ainda está PENDENTE
([contrato](C3_3b3_Confirmacao_Cancelamento_RunOnce.md)).
O procedimento desejado abaixo para matar finitos *após confirmação*
é **critério futuro**, não função já aceita. O estado de UI não
substitui validação de PID/heartbeat no backend.
HML/PROD e NODE canônico fora do escopo.
Ver [guia atual](Console_DEV_Supervisao_Atual.md).

## Um único toggle — modo dos três workers

`Supervisão automática: DESATIVADA / ATIVADA`.

O toggle é **GLOBAL para Processor, Operations Maintenance e Bronze
Maintenance**. No ambiente DEV **descartável novo**, começa
**DESATIVADA**, preservando o RunOnce individual de cada worker.

| | Supervisão OFF | Supervisão ON |
|---|---|---|
| Execução | RunOnce dos três workers habilitados, respeitados pré-requisitos. Nenhum residente supervisionado. | Três workers contínuos sob supervisão, automaticamente iniciados ao ativar. RunOnce bloqueado em UI e API. |
| Parar processo individual | Sem residentes a parar; RunOnce ativos somente serão encerrados na troca de modo com confirmação. | Encerra imediatamente o PID ativo daquele worker; **o supervisor reinicia automaticamente somente ele**, sem clique em iniciar. |
| Indicador automático + log existente | Cartão mostra RUN_ONCE/PARADO; detalhes da execução no log. | Cartão mostra estado/PID observados; eventos de start, kill, restart, heartbeat e recuperação no log. |

### Transição OFF → ON

1. Impedir **atomicamente** novos RunOnce em UI e backend.
2. Identificar RunOnce ativos e pedir confirmação explícita antes de
   interrompê-los; não iniciar residentes enquanto a operação finita
   estiver ativa.
3. **Matar/encerrar todos os RunOnce ativos dos três workers**,
   confirmando a ausência de instâncias conflitantes.
4. Habilitar restart independente nos **três serviços de worker** e
   **iniciar automaticamente os três residentes contínuos**. Não
   oferecer comando individual de start contínuo.
5. Confirmar processo vivo/PID/saúde de cada serviço; somente então
   apresentar supervisor `ATIVADA` como operacional. `Parar processo`
   de cada worker fica habilitado **apenas quando seu PID está ativo**.
   Durante `INICIANDO`/`REINICIANDO`, permanecer desabilitado.

### Supervisor ON — injeção de falha

Quando `Parar processo` é clicado em um worker **ativo** (PID real consultado pelo backend):
- Encerra **somente a instância atual** desse worker, em modo
  abrupto (SIGKILL/equivalente) e confirma o término.
- A política externa de supervisão detecta a saída e **reinicia
  automaticamente o mesmo serviço de worker**, com novo PID. Não
  precisa de clique adicional. Os outros dois workers e APIs não
  são encerrados nem reiniciados.
- O worker recém-iniciado **recupera por conta própria seu trabalho
  interrompido**: transações interrompidas fazem rollback quando
  aplicável; leases e heartbeat são examinados/recuperados segundo
  sua validade; reprocessamento elegível continua com idempotência.
  A retomada pode exigir **expiração do lease**. Não confundir
  reinício imediato do executável com conclusão imediata do lote.
- O **indicador automático no cartão** distingue ATIVO, REINICIANDO,
  RECUPERANDO e ERRO, alimentado pela consulta real de processo/heartbeat;
  não exige botão `Status do processo`.
- O **Log da sessão existente** apresenta eventos e detalhes de
  supervisão, PID antigo/novo e recuperação, vinculados ao worker;
  obter histórico externo quando a Console não estava aberta, porque
  o log atual é de sessão e não substitui a leitura real de estado.
- Distinguir **PROCESSO REINICIADO** de **TRABALHO RECUPERADO**, com
  evidências reais de ambos. Não fabricar contadores de recuperação
  para Maintenance Workers.

### Transição ON → OFF

1. Impedir novas operações conflitantes e **desativar primeiro** a
   política automática de restart dos três workers.
2. Encerrar e conferir ausência dos três residentes, sem afetar
   API, Resultado, SQL ou supervisor de infraestrutura de outros
   serviços. A desativação global é a única parada administrativa
   normal do conjunto.
3. Reabilitar os RunOnce dos três workers; botões `Parar processo`
   ficam indisponíveis sem PID contínuo.

## Responsabilidades e segurança

**Supervisor externo:** mantém executáveis vivos no modo ON, inclusive
após SIGKILL; supervisiona cada worker independentemente.
**Worker:** mantém/repara seus próprios ciclos, leases, heartbeat,
dados e garantias de idempotência.
**Console:** alterna modo global e exibe/comanda as ações permitidas;
**não** se torna um supervisor próprio de todos os PIDs.

A transição é serializada/atômica; falha parcial entra em `ERRO` e
não libera RunOnce junto de residentes. Reload da página deve consultar
modo **efetivo**, sem reativar/suspender processos por aparência do
toggle. Ambiente/serviço identifiáveis por allowlist e perfil DEV
`JornadaE2E` isolado; nenhuma operação de processo sobre o cluster
existente. O primeiro estado OFF só é afirmado após confirmação de
perfil descartável novo.

Referências: [DT-18](DT18_Servicos_Independentes_Console_DEV.md),
[DT-19](DT19_Console_Acoes_Workers.md),
[DT-21](DT21_Testes_Resiliencia_Workers.md).

**Não tocar JornadaLocal, IBGE original, HML/PROD, volumes
compartilhados, dados reais, Trilha 4 ou RESOLVIDOS automáticos.**

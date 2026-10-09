# DT-21 — testes de modos, supervisão e recuperação da Console DEV

**Decisão atualizada em 08/10/2026, escopo DEV.** Estado:
**PENDENTE**. A aprovação do contrato não é evidência de execução.

## O que precisa ser comprovado

A supervisão é **global e inicialmente DESATIVADA** no perfil
`JornadaE2E` descartável. OFF = três `RunOnce` individuais
disponíveis, sem residentes sob controle da Console; ON = os **três
workers contínuos iniciados automaticamente** e RunOnce desabilitado
na UI **e na API**. O processo de cada worker e sua recuperação são
independentes: matar um não reinicia NODE, API nem os outros workers.

A recuperação após interrupção inclui apenas lotes não concluídos,
leases expirados e trabalho próprio de cada worker. **Não** implantar
reprocessamento automático de RESOLVIDOS ou Trilha 4.

## Matriz mínima de aceite obrigatório

| Caso | Prova exigida |
|---|---|
| **TC-SV01 Estado inicial** | Novo ambiente descartável sobe em `RUN_ONCE`, supervisão OFF, **todos** os RunOnce habilitados; nenhum worker residente gerenciado; status verdadeiro. |
| **TC-SV02 RunOnce OFF** | Executar cada worker individualmente em modo finito; exit code 0 apenas para ciclo concluído; erro/incomplete preservado; nenhuma autorreinicialização. |
| **TC-SV03 Habilitar supervisor** | Um toggle OFF→ON bloqueia novos RunOnce e mata/encerra os processos de workers existentes (confirmação se RunOnce ativo), sobe **os três** residentes sob supervisão. API/Resultado não mudam de PID. Não marcar ON/saudável antes dos três. |
| **TC-SV04 Bloqueio ON** | Os três botões RunOnce aparecem desabilitados e as rotas rejeitam tentativas diretas. Dois toggles concorrentes não duplicam nenhuma instância. |
| **TC-SV05 Parar processo ON** | SIGKILL no Processor reinicia **apenas Processor** com novo PID; Operations, Bronze, API e Resultado preservam identidade e disponibilidade. Repetir isolamento para Operations e Bronze. |
| **TC-SV06 Recuperação** | Processamento interrompido em lote sintético ativo; heartbeat, lease, vencimento, fencing, reaquisição e ausência de duplicação são verificados em `JornadaE2E`; PID novo isolado NÃO comprova recuperação. |
| **TC-SV07 Desabilitar supervisor** | Toggle ON→OFF primeiro impede reinício, para **os três residentes**, confirma ausência e reabilita **todos** os RunOnce. Mortes sob OFF não geram restart. |
| **TC-SV08 Estado da Console** | Recarregar/reiniciar Console preserva leitura do estado efetivo (sem OFF falso), não reinicia workers nem duplica processos. |
| **TC-SV09 Corridas e falhas** | Requisições concorrentes de modo/start/kill/status; falha no 2º dos 3 starts; API retorna estado `ERRO` e não habilita ON+RunOnce simultaneamente. Reconciliação sem destruir banco. |
| **TC-SV10 Segurança** | Recusar ambientes fora de DEV/`JornadaE2E`, nomes não allowlisted e recursos compartilhados. Somente processos dos três workers podem ser atingidos; confirmar interrupção de RunOnce ativo. |

## Plano de implementação e gates

- **C3.1:** entrypoint de worker individual, allowlist e guardas de
  ambiente sem ativação. Gate fail-closed sem iniciar processo.
- **C3.2:** serviços individuais no Compose de teste **descartável**,
  API/Resultado preservados, política restart controlável por um
  comando global sem tocar Compose padrão.
- **C3.3:** API da Console DEV com orquestração da **transição global**
  OFF→ON→OFF, RunOnce/Parar/Status individuais e atomicidade. Não
  expor shell arbitrário.
- **C3.4:** UI de um toggle global e três ações por worker, Chromium
  real e prova E2E de TC-SV01–10.

Cada PR precisa de CI e demais workflows aplicáveis `completed/success`
na **HEAD exata**, com jobs obrigatórios realmente executados antes
de marcar ready/squash merge. Checagem horária não substitui os gates.
Skipped opcional não é prova positiva do respectivo ensaio.

**Proibido:** reset/alteração de `JornadaLocal`, IBGE original,
volumes compartilhados, HML/PROD, dados reais e Trilha 4.

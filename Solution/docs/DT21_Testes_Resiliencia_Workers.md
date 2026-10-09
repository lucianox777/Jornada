# DT-21 — provas de modos, botões e recuperação da Console DEV

**Decisão refinada em 08/10/2026.** Estado: **PENDENTE**.
Os testes RunOnce e de Console existentes serão **mantidos**, não
reescritos em razão da interface adicional. Os casos novos abaixo são
gates incrementais; não declarados executados apenas por sua previsão.

## Contrato observado

Toggle único do supervisor, **OFF inicialmente** em DEV descartável.
OFF: três `Executar uma vez` disponíveis e sem residentes; ON:
cancelar/matar RunOnce ativos, iniciar automaticamente os **três**
workers contínuos, desligar RunOnce e liberar controles contínuos.
Cada cartão terá botões **empilhados verticalmente**: Executar uma
vez, Iniciar contínuo, Desligar processo, Status do processo.
`Desligar` só é clicável depois de confirmado que **aquele PID**
está vivo. Com supervisor ON, desligar o PID provoca restart somente
desse worker. Com OFF, não há processo residente para desligar.
Matar um worker não derruba API/Resultado/SQL/NODE nem os demais.

## Matriz mínima nova de aceite

| Caso | Prova real exigida |
|---|---|
| **TC-SV01 Estado inicial** | Console DEV descartável abre com supervisor OFF, 3 RunOnce habilitados (salvo pré-requisitos legítimos), botões contínuos desabilitados, `Status do processo` acessível, nenhum residente controlado. |
| **TC-SV02 Regressão RunOnce** | Suítes anteriores unit/HTTP/Chromium/E2E de RunOnce passam **sem remoção ou bypass**. Execução finita mantém limites, exit codes e ausência de restart. |
| **TC-SV03 Ativar supervisor** | ON impede imediatamente novos RunOnce e, mediante confirmação para ativos, mata todos os RunOnce dos três workers; prova não haver concorrência com residentes. Sobe automaticamente os três serviços contínuos, cada qual com PID e supervisão própria. |
| **TC-SV04 Botões por estado** | Quatro botões por worker em **coluna vertical**. ON: RunOnce desabilitado na UI/API; `Iniciar contínuo` disponibilizado no modo correto mas desabilitado se já ativo; **`Desligar processo` habilitado só quando PID real estiver ativo**, nunca somente por toggle ON. `Status` sempre disponível. |
| **TC-SV05 Desligar processo ON** | Desligar (falha abrupta) só no worker escolhido; restart automático com PID novo; API, Resultado e outros dois workers não reiniciam. Durante reinício, botão Desligar desabilitado e status não mente. Repetir nos 3 workers. |
| **TC-SV06 Recuperar lote** | Interromper Processor em lote sintético ativo; provar heartbeat, lease expirado, fencing, reaquisição e zero duplicações no `JornadaE2E`. PID novo não é prova suficiente. |
| **TC-SV07 Voltar OFF** | Desarmar reinícios, encerrar três residentes, verificar ausência, reabilitar RunOnce e desabilitar controles contínuos. Nunca reiniciar após OFF. |
| **TC-SV08 Corridas e reinício Console** | Toggles concorrentes e cliques repetidos não duplicam instâncias; RunOnce nunca começa durante ON. Reabrir Console lê estado efetivo (não força OFF visual). |
| **TC-SV09 Falha parcial** | Falha provocada no segundo start não exibe supervisor ATIVO/saudável e não libera simultaneamente RunOnce e residentes; recuperação de modo explícita. |
| **TC-SV10 Escopo seguro** | Endpoints aceitam só workers allowlisted DEV/`JornadaE2E`; sem PID arbitrário, sem shell exposto, sem matar API/NODE/serviços compartilhados. |

## Etapas e gates

- **C3.1:** entrypoint independente com allowlist, CI em modo
  `--check` sem processos/DB; documentação destas DTs.
- **C3.2:** supervisor real com restart individual e contêineres/volumes
  **exclusivos** do CI descartável; não modificar cluster comum.
- **C3.3:** backend de modos globais ON/OFF e ações por worker,
  exclusão atômica, confirmação de RunOnce ativo, PID/estado reais.
- **C3.4:** interface com botões empilhados + Chromium E2E
  TC-SV01–10 e testes de recuperação.

Merge somente após workflows e gates obrigatórios `completed/success`
no HEAD exato. `Skipped` não comprova execução; acompanhamento horário
não substitui gate real.

**Fora de escopo:** Trilha 4, RESOLVIDOS automáticos, `JornadaLocal`,
IBGE original, volumes compartilhados, HML/PROD, dados reais.

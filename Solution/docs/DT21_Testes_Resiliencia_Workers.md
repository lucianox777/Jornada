# DT-21 — evidências de supervisão e autorrecuperação dos workers

**Decisão final refinada em 08/10/2026.** Estado: **PENDENTE DE
IMPLEMENTAÇÃO, EXECUÇÃO E ACEITE**. Os testes RunOnce/Console existentes
devem continuar íntegros; a cobertura nova é **aditiva**.

## Contrato observado

Um supervisor **global**, inicialmente DESATIVADO em DEV descartável:
OFF = todos os RunOnce permitidos e nenhum residente gerenciado;
ON = matar RunOnce ativos após confirmação, iniciar automaticamente
**os três workers contínuos**, desabilitar RunOnce. Controles de
cada cartão **empilhados na vertical**:

```text
Worker · ● estado real/PID/último heartbeat  (indicador sem clique)
[Executar uma vez]
[Parar processo]
```

**NÃO** incluir `Iniciar contínuo` ou `Desligar processo`.
`Parar processo` só fica habilitado em ON se PID **realmente vivo**.
**Sem botão `Status do processo`:** o cartão mostra um indicador
atualizado automaticamente por consulta ao backend, e os detalhes
de PID/heartbeat/reinícios/recuperação são publicados no **Log da
sessão existente** (com fonte externa para eventos ocorridos com
Console fechada). A recuperação de lotes/trabalho é responsabilidade
do worker reiniciado, possivelmente após vencimento de lease.
Matar um worker não deve derrubar NODE, API, Resultado ou outros
workers. Não confundir processo novamente ativo com lote já recuperado.

## Matriz mínima de aceite

| ID | Prova |
|---|---|
| TC-SV01 — OFF inicial | Perfil descartável isolado começa OFF, sem residentes; os RunOnce dos 3 workers estão disponíveis (sob pré-requisitos). UI com **2 botões verticais** por worker, indicador automático e Log da sessão existente. |
| TC-SV02 — regressão RunOnce | Testes preexistentes de RunOnce/Console em .NET, HTTP, Chromium e E2E permanecem sem skips/bypasses novos; exit code e limitação de ciclo preservados. |
| TC-SV02b — dois RunOnce de manutenção | O catálogo da Console hoje possui o RunOnce Silver/Processor, mas **não** os dois botões específicos de RunOnce de Operations e Bronze Maintenance; expor esses modos suportados pelos executáveis, sem modificar o RunOnce Silver existente. |
| TC-SV03 — ON global | Bloquear RunOnce em UI/backend; pedir confirmação de kill se RunOnce ativo, matar todos RunOnce desses 3 workers, esperar ausência, iniciar automaticamente os três residentes, validar identidade e saúde de cada um. |
| TC-SV04 — controles por PID | `Parar processo` desabilitado até PID real vivo, habilitado após subir, desabilitado durante reinício; indicador automático sempre coerente com backend. **Não existe botão Status nem Iniciar contínuo**. |
| TC-SV05 — restart automático | ON: clicar Parar em Processor, provar SIGKILL de **apenas** Processor, PID novo **sem intervenção do operador**, API/Resultado/Operations/Bronze permanecem com PIDs estáveis. Repetir individualmente para cada outro worker. |
| TC-SV06 — trabalho recuperado | Parar Processor durante lote sintético ativo; observar rollback de operação não confirmada, heartbeat/lease e fencing, retomada elegível pelo worker reiniciado, consistência/idempotência e ausência de duplicações em `JornadaE2E`. Separar métrica `processo_reiniciado` da evidência `trabalho_recuperado`. |
| TC-SV07 — OFF após ON | Primeiro desarmar restart dos 3, depois encerrar residentes, confirmar ausência e reabilitar RunOnce. Nenhuma instância ressuscita. |
| TC-SV08 — robustez da Console | Reload/restart da Console reflete estado efetivo e não duplica workers; toggles simultâneos são serializados. Eventos que aconteceram com Console fechada são obtidos da fonte externa; log de sessão isolado não basta para afirmar vida do processo. |
| TC-SV09 — erro parcial | Falha ao subir worker 2/3 gera estado `ERRO`, RunOnce continua bloqueado e não há falso ON saudável; deve ser possível reconciliação segura. |
| TC-SV10 — isolamento e segurança | Todos os testes de falha reais limitados a serviço allowlisted em Compose descartável isolado; negar PID/worker arbitrário, modo PROD/HML, `JornadaLocal` e volumes compartilhados. |

## Sequência de implementação

- C3.1: entrypoint isolado não ativado, allowlist, proteção DEV/E2E
  e regressão não destrutiva (PR #841).
- C3.2: topologia e política de restart externa **independente para
  os três serviços**, sem `wait -n` compartilhado nos workers, em
  projeto Docker descartável.
- C3.3: controlador global OFF↔ON, RunOnce preexistente,
  `Parar processo` individual (SIGKILL), **consulta interna de
  estado real** para habilitação/indicador e envio de eventos ao
  Log da sessão; isolamento e transições atômicas.
- C3.4: UI com toggle global + **2 botões verticais/worker** +
  indicador automático e reutilização do Log da sessão;
  testes Chromium/E2E de TC-SV01–10 com regressões preservadas.

Cada PR tem seu HEAD e gates obrigatórios a validar antes do squash
merge. `Skipped` opcional não é evidência positiva daquele teste.
Checagem horária não substitui comprovação de restart e recuperação.

**Fora do escopo:** Trilha 4, reprocessamento automático de RESOLVIDOS,
JornadaLocal, IBGE original, volumes comuns, dados reais, HML/PROD.
